using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeVelo.Server.Storage;
using Microsoft.Data.Sqlite;

namespace FreeVelo.Server.Accounts;

public sealed record Account(string Id, string Email, string DisplayName, string PasswordHash, bool EmailVerified);

public enum OneTimePurpose
{
    VerifyEmail,
    ResetPassword,
}

/// <summary>Result of using a refresh token.</summary>
public enum RefreshOutcome
{
    Rotated,
    Unknown,
    Expired,

    /// <summary>An already used token was presented again (likely stolen): the whole token family is revoked.</summary>
    Reused,
}

/// <summary>
/// SQL access for accounts and tokens. Tokens are random 256-bit values; only their SHA-256 hash is stored,
/// so a leaked database does not leak usable tokens.
/// </summary>
public sealed class AccountStore
{
    private readonly Database _db;
    private readonly TimeProvider _time;

    public AccountStore(Database db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Creates an account; null if the email is already registered.</summary>
    public Account? Create(string email, string displayName, string passwordHash)
    {
        var account = new Account(Guid.NewGuid().ToString("N"), NormalizeEmail(email), displayName.Trim(), passwordHash, false);
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO accounts (id, email, display_name, password_hash, created_utc) VALUES ($id, $email, $name, $hash, $now)";
        cmd.Parameters.AddWithValue("$id", account.Id);
        cmd.Parameters.AddWithValue("$email", account.Email);
        cmd.Parameters.AddWithValue("$name", account.DisplayName);
        cmd.Parameters.AddWithValue("$hash", account.PasswordHash);
        cmd.Parameters.AddWithValue("$now", Format(_time.GetUtcNow()));
        try
        {
            cmd.ExecuteNonQuery();
            return account;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) // UNIQUE constraint
        {
            return null;
        }
    }

    public Account? FindByEmail(string email) => Find("email", NormalizeEmail(email));

    public Account? FindById(string id) => Find("id", id);

    public void SetEmailVerified(string accountId) =>
        Execute("UPDATE accounts SET email_verified = 1 WHERE id = $id", ("$id", accountId));

    public void SetPasswordHash(string accountId, string hash) =>
        Execute("UPDATE accounts SET password_hash = $hash WHERE id = $id", ("$id", accountId), ("$hash", hash));

    public void SetDisplayName(string accountId, string name) =>
        Execute("UPDATE accounts SET display_name = $name WHERE id = $id", ("$id", accountId), ("$name", name.Trim()));

    /// <summary>Deletes the account; its tokens go with it (foreign keys cascade).</summary>
    public void Delete(string accountId) => Execute("DELETE FROM accounts WHERE id = $id", ("$id", accountId));

    // ---------------------------------------------------------------- one-time tokens

    public string IssueOneTimeToken(string accountId, OneTimePurpose purpose, TimeSpan lifetime)
    {
        string token = NewToken();
        // One open token per purpose: a new email invalidates the previous link.
        Execute("DELETE FROM one_time_tokens WHERE account_id = $id AND purpose = $purpose", ("$id", accountId), ("$purpose", purpose.ToString()));
        Execute(
            "INSERT INTO one_time_tokens (token_hash, account_id, purpose, expires_utc) VALUES ($hash, $id, $purpose, $expires)",
            ("$hash", HashToken(token)),
            ("$id", accountId),
            ("$purpose", purpose.ToString()),
            ("$expires", Format(_time.GetUtcNow() + lifetime)));
        return token;
    }

    /// <summary>Uses a one-time token: returns the account id, or null if unknown, expired or for another purpose.</summary>
    public string? ConsumeOneTimeToken(string token, OneTimePurpose purpose)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        string? accountId = null;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT account_id, purpose, expires_utc FROM one_time_tokens WHERE token_hash = $hash";
            cmd.Parameters.AddWithValue("$hash", HashToken(token));
            using var r = cmd.ExecuteReader();
            if (r.Read() && r.GetString(1) == purpose.ToString() && Parse(r.GetString(2)) > _time.GetUtcNow())
            {
                accountId = r.GetString(0);
            }
        }

        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM one_time_tokens WHERE token_hash = $hash";
            del.Parameters.AddWithValue("$hash", HashToken(token));
            del.ExecuteNonQuery();
        }

        tx.Commit();
        return accountId;
    }

    // ---------------------------------------------------------------- refresh tokens

    /// <summary>Issues a refresh token; a new login starts a new family, a rotation continues the old one.</summary>
    public string IssueRefreshToken(string accountId, TimeSpan lifetime, string? family = null)
    {
        string token = NewToken();
        Execute(
            "INSERT INTO refresh_tokens (token_hash, account_id, family, expires_utc) VALUES ($hash, $id, $family, $expires)",
            ("$hash", HashToken(token)),
            ("$id", accountId),
            ("$family", family ?? Guid.NewGuid().ToString("N")),
            ("$expires", Format(_time.GetUtcNow() + lifetime)));
        return token;
    }

    /// <summary>
    /// Uses a refresh token once (rotation). On success returns the account and the family to continue;
    /// presenting a used token again revokes the whole family.
    /// </summary>
    public (RefreshOutcome Outcome, string? AccountId, string? Family) UseRefreshToken(string token)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        string hash = HashToken(token);
        string accountId, family;
        bool revoked;
        DateTimeOffset expires;
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT account_id, family, expires_utc, revoked FROM refresh_tokens WHERE token_hash = $hash";
            cmd.Parameters.AddWithValue("$hash", hash);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (RefreshOutcome.Unknown, null, null);
            accountId = r.GetString(0);
            family = r.GetString(1);
            expires = Parse(r.GetString(2));
            revoked = r.GetInt64(3) != 0;
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            if (revoked)
            {
                cmd.CommandText = "UPDATE refresh_tokens SET revoked = 1 WHERE family = $family";
                cmd.Parameters.AddWithValue("$family", family);
            }
            else
            {
                cmd.CommandText = "UPDATE refresh_tokens SET revoked = 1 WHERE token_hash = $hash";
                cmd.Parameters.AddWithValue("$hash", hash);
            }

            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        if (revoked) return (RefreshOutcome.Reused, accountId, null);
        if (expires <= _time.GetUtcNow()) return (RefreshOutcome.Expired, accountId, null);
        return (RefreshOutcome.Rotated, accountId, family);
    }

    /// <summary>Logout of one device: revokes the token's family.</summary>
    public void RevokeFamilyOf(string token) =>
        Execute("UPDATE refresh_tokens SET revoked = 1 WHERE family = (SELECT family FROM refresh_tokens WHERE token_hash = $hash)", ("$hash", HashToken(token)));

    /// <summary>Logout everywhere (password changed or reset).</summary>
    public void RevokeAllRefreshTokens(string accountId) =>
        Execute("UPDATE refresh_tokens SET revoked = 1 WHERE account_id = $id", ("$id", accountId));

    // ---------------------------------------------------------------- helpers

    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Format(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private Account? Find(string column, string value)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT id, email, display_name, password_hash, email_verified FROM accounts WHERE {column} = $v";
        cmd.Parameters.AddWithValue("$v", value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new Account(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0) : null;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
