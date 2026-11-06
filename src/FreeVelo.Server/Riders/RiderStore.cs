using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeVelo.Server.Storage;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Riders;

public sealed record Rider(string Id, string DisplayName);

/// <summary>
/// Riders: a display name and a secret the server generates for the device. The secret is 256 random bits, so a
/// plain SHA-256 hash is enough (unlike human passwords, it cannot be guessed); only the hash is stored.
/// </summary>
public sealed class RiderStore
{
    private readonly Database _db;
    private readonly TimeProvider _time;

    public RiderStore(Database db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>Creates a rider; returns it with the secret (shown only this once).</summary>
    public (Rider Rider, string Secret) Create(string displayName)
    {
        var rider = new Rider(Guid.NewGuid().ToString("N"), displayName.Trim());
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string now = Now();
        Execute(
            "INSERT INTO riders (id, secret_hash, display_name, created_utc, last_seen_utc) VALUES ($id, $hash, $name, $now, $now)",
            ("$id", rider.Id), ("$hash", Hash(secret)), ("$name", rider.DisplayName), ("$now", now));
        return (rider, secret);
    }

    /// <summary>The rider if the secret matches (and marks them as seen), otherwise null.</summary>
    public Rider? Authenticate(string riderId, string secret)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT secret_hash, display_name FROM riders WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", riderId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        byte[] expected = Convert.FromHexString(r.GetString(0));
        byte[] actual = Convert.FromHexString(Hash(secret));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
        var rider = new Rider(riderId, r.GetString(1));
        r.Close();
        Execute("UPDATE riders SET last_seen_utc = $now WHERE id = $id", ("$id", riderId), ("$now", Now()));
        return rider;
    }

    public Rider? Find(string riderId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT display_name FROM riders WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", riderId);
        return cmd.ExecuteScalar() is string name ? new Rider(riderId, name) : null;
    }

    public void Rename(string riderId, string displayName) =>
        Execute("UPDATE riders SET display_name = $name WHERE id = $id", ("$id", riderId), ("$name", displayName.Trim()));

    public void Delete(string riderId) => Execute("DELETE FROM riders WHERE id = $id", ("$id", riderId));

    public static bool IsValidDisplayName(string? name)
    {
        if (name == null) return false;
        string trimmed = name.Trim();
        return trimmed.Length >= RiderRules.MinDisplayNameLength
            && trimmed.Length <= RiderRules.MaxDisplayNameLength
            && !trimmed.Any(char.IsControl);
    }

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private string Now() => _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
