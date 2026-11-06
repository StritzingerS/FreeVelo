using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using FreeVelo.Server.Email;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Accounts;

public sealed class ServerOptions
{
    /// <summary>Public HTTPS address of the server, used in email links (e.g. https://freevelo.duckdns.org).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Folder for the database and the generated signing key (a Docker volume in production).</summary>
    public string DataDirectory { get; set; } = "data";
}

/// <summary>
/// Accounts over REST: register, verify email, login, refresh (rotation), logout, password reset, change name or
/// password, delete. Responses never reveal whether an email address is registered.
/// </summary>
public static class AccountEndpoints
{
    public const string AuthRateLimit = "auth";
    private static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(48);
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("").RequireRateLimiting(AuthRateLimit);

        auth.MapPost(ApiRoutes.Register, async (RegisterRequest req, AccountStore store, PasswordHasher hasher, IEmailSender mail, ServerOptions server) =>
        {
            if (!IsValidEmail(req.email)) return Error(ApiErrors.InvalidEmail);
            if (!IsValidDisplayName(req.displayName)) return Error(ApiErrors.InvalidDisplayName);
            if (!IsValidPassword(req.password)) return Error(ApiErrors.WeakPassword);

            var account = store.Create(req.email, req.displayName, hasher.Hash(req.password));
            if (account == null)
            {
                var existing = store.FindByEmail(req.email)!;
                await mail.SendAsync(AccountEmails.AlreadyRegistered(existing.Email, existing.DisplayName, req.language));
            }
            else
            {
                await SendVerification(account, store, mail, server, req.language);
            }

            return Results.Accepted();
        });

        auth.MapPost(ApiRoutes.ResendVerification, async (EmailRequest req, AccountStore store, IEmailSender mail, ServerOptions server) =>
        {
            var account = store.FindByEmail(req.email ?? "");
            if (account != null && !account.EmailVerified) await SendVerification(account, store, mail, server, req.language);
            return Results.Accepted();
        });

        // Link from the email: opened in the browser, so it answers with a small page.
        app.MapGet("/verify", (string? token, AccountStore store) =>
        {
            string? accountId = string.IsNullOrEmpty(token) ? null : store.ConsumeOneTimeToken(token, OneTimePurpose.VerifyEmail);
            if (accountId != null) store.SetEmailVerified(accountId);
            return Page(accountId != null
                ? ("E-Mail bestätigt · Email confirmed", "Du kannst dich jetzt in der FreeVelo-App anmelden.<br>You can now sign in to the FreeVelo app.")
                : ("Link ungültig · Link invalid", "Der Link ist abgelaufen oder wurde schon benutzt. Fordere in der App eine neue E-Mail an.<br>The link has expired or was already used. Request a new email in the app."));
        });

        auth.MapPost(ApiRoutes.Login, (LoginRequest req, AccountStore store, PasswordHasher hasher, TokenService tokens, JwtOptions jwt) =>
        {
            var account = store.FindByEmail(req.email ?? "");
            if (account == null)
            {
                hasher.Hash(req.password ?? ""); // same work as a real check: no timing hint that the email is unknown
                return Error(ApiErrors.InvalidCredentials, HttpStatusCode.Unauthorized);
            }

            if (!hasher.Verify(req.password ?? "", account.PasswordHash)) return Error(ApiErrors.InvalidCredentials, HttpStatusCode.Unauthorized);
            if (!account.EmailVerified) return Error(ApiErrors.EmailNotVerified, HttpStatusCode.Forbidden);
            if (hasher.NeedsRehash(account.PasswordHash)) store.SetPasswordHash(account.Id, hasher.Hash(req.password!));

            return Results.Ok(Session(account, store.IssueRefreshToken(account.Id, jwt.RefreshTokenLifetime), tokens, jwt));
        });

        auth.MapPost(ApiRoutes.Refresh, (RefreshRequest req, AccountStore store, TokenService tokens, JwtOptions jwt) =>
        {
            var (outcome, accountId, family) = store.UseRefreshToken(req.refreshToken ?? "");
            var account = outcome == RefreshOutcome.Rotated ? store.FindById(accountId!) : null;
            if (account == null) return Error(ApiErrors.InvalidToken, HttpStatusCode.Unauthorized);
            return Results.Ok(Session(account, store.IssueRefreshToken(account.Id, jwt.RefreshTokenLifetime, family), tokens, jwt));
        });

        auth.MapPost(ApiRoutes.Logout, (RefreshRequest req, AccountStore store) =>
        {
            store.RevokeFamilyOf(req.refreshToken ?? "");
            return Results.NoContent();
        });

        auth.MapPost(ApiRoutes.ForgotPassword, async (EmailRequest req, AccountStore store, IEmailSender mail, ServerOptions server) =>
        {
            var account = store.FindByEmail(req.email ?? "");
            if (account != null)
            {
                string token = store.IssueOneTimeToken(account.Id, OneTimePurpose.ResetPassword, ResetLifetime);
                string link = $"{server.PublicBaseUrl.TrimEnd('/')}/reset?token={Uri.EscapeDataString(token)}";
                await mail.SendAsync(AccountEmails.ResetPassword(account.Email, account.DisplayName, link, req.language));
            }

            return Results.Accepted();
        });

        // Link from the email: a form for the new password (the token in the form is the authorization).
        app.MapGet("/reset", (string? token) => Results.Content(ResetForm(token ?? "", null), "text/html; charset=utf-8"));

        auth.MapPost("/reset", async (HttpRequest request, AccountStore store, PasswordHasher hasher) =>
        {
            var form = await request.ReadFormAsync();
            string token = form["token"].ToString();
            string password = form["password"].ToString();
            if (!IsValidPassword(password))
            {
                return Results.Content(ResetForm(token, $"Mindestens {AccountRules.MinPasswordLength} Zeichen · At least {AccountRules.MinPasswordLength} characters"), "text/html; charset=utf-8");
            }

            string? accountId = store.ConsumeOneTimeToken(token, OneTimePurpose.ResetPassword);
            if (accountId == null)
            {
                return Page(("Link ungültig · Link invalid", "Der Link ist abgelaufen oder wurde schon benutzt. Fordere in der App eine neue E-Mail an.<br>The link has expired or was already used. Request a new email in the app."));
            }

            store.SetPasswordHash(accountId, hasher.Hash(password));
            store.SetEmailVerified(accountId); // the reset link proves the address too
            store.RevokeAllRefreshTokens(accountId);
            return Page(("Passwort geändert · Password changed", "Melde dich in der FreeVelo-App mit dem neuen Passwort an.<br>Sign in to the FreeVelo app with your new password."));
        });

        // ------------------------------------------------------------ signed in

        var me = app.MapGroup("").RequireAuthorization();

        me.MapGet(ApiRoutes.Me, (ClaimsPrincipal user, AccountStore store) =>
            CurrentAccount(user, store) is { } account ? Results.Ok(ToResponse(account)) : Results.Unauthorized());

        me.MapPatch(ApiRoutes.Me, (UpdateAccountRequest req, ClaimsPrincipal user, AccountStore store) =>
        {
            if (CurrentAccount(user, store) is not { } account) return Results.Unauthorized();
            if (!IsValidDisplayName(req.displayName)) return Error(ApiErrors.InvalidDisplayName);
            store.SetDisplayName(account.Id, req.displayName);
            return Results.Ok(ToResponse(store.FindById(account.Id)!));
        });

        me.MapPost(ApiRoutes.ChangePassword, (ChangePasswordRequest req, ClaimsPrincipal user, AccountStore store, PasswordHasher hasher) =>
        {
            if (CurrentAccount(user, store) is not { } account) return Results.Unauthorized();
            if (!hasher.Verify(req.currentPassword ?? "", account.PasswordHash)) return Error(ApiErrors.InvalidCredentials, HttpStatusCode.Unauthorized);
            if (!IsValidPassword(req.newPassword)) return Error(ApiErrors.WeakPassword);
            store.SetPasswordHash(account.Id, hasher.Hash(req.newPassword));
            store.RevokeAllRefreshTokens(account.Id); // other devices sign in again
            return Results.NoContent();
        }).RequireRateLimiting(AuthRateLimit);

        me.MapPost(ApiRoutes.DeleteAccount, (DeleteAccountRequest req, ClaimsPrincipal user, AccountStore store, PasswordHasher hasher) =>
        {
            if (CurrentAccount(user, store) is not { } account) return Results.Unauthorized();
            if (!hasher.Verify(req.password ?? "", account.PasswordHash)) return Error(ApiErrors.InvalidCredentials, HttpStatusCode.Unauthorized);
            store.Delete(account.Id);
            return Results.NoContent();
        }).RequireRateLimiting(AuthRateLimit);
    }

    // ---------------------------------------------------------------- validation

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        try
        {
            var parsed = new MailAddress(email.Trim());
            return parsed.Address == email.Trim() && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsValidDisplayName(string? name)
    {
        if (name == null) return false;
        string trimmed = name.Trim();
        return trimmed.Length >= AccountRules.MinDisplayNameLength && trimmed.Length <= AccountRules.MaxDisplayNameLength && !trimmed.Any(char.IsControl);
    }

    public static bool IsValidPassword(string? password) =>
        password != null && password.Length >= AccountRules.MinPasswordLength && password.Length <= AccountRules.MaxPasswordLength;

    // ---------------------------------------------------------------- helpers

    private static async Task SendVerification(Account account, AccountStore store, IEmailSender mail, ServerOptions server, string? language)
    {
        string token = store.IssueOneTimeToken(account.Id, OneTimePurpose.VerifyEmail, VerifyLifetime);
        string link = $"{server.PublicBaseUrl.TrimEnd('/')}/verify?token={Uri.EscapeDataString(token)}";
        await mail.SendAsync(AccountEmails.Verify(account.Email, account.DisplayName, link, language));
    }

    private static Account? CurrentAccount(ClaimsPrincipal user, AccountStore store)
    {
        string? id = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return id == null ? null : store.FindById(id);
    }

    private static SessionResponse Session(Account account, string refreshToken, TokenService tokens, JwtOptions jwt) => new()
    {
        accessToken = tokens.CreateAccessToken(account),
        refreshToken = refreshToken,
        expiresIn = (int)jwt.AccessTokenLifetime.TotalSeconds,
        account = ToResponse(account),
    };

    private static AccountResponse ToResponse(Account account) => new()
    {
        id = account.Id,
        email = account.Email,
        displayName = account.DisplayName,
    };

    private static IResult Error(string code, HttpStatusCode status = HttpStatusCode.BadRequest) =>
        Results.Json(new ApiError { error = code }, statusCode: (int)status);

    private static IResult Page((string Title, string Html) content) =>
        Results.Content(Html(content.Title, $"<p>{content.Html}</p>"), "text/html; charset=utf-8");

    private static string ResetForm(string token, string? error) => Html(
        "Neues Passwort · New password",
        $"""
        {(error != null ? $"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>" : "")}
        <form method="post" action="/reset">
          <input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}">
          <input type="password" name="password" minlength="{AccountRules.MinPasswordLength}" required autocomplete="new-password" placeholder="Neues Passwort · New password">
          <button type="submit">Speichern · Save</button>
        </form>
        """);

    private static string Html(string title, string body) => $$"""
        <!doctype html>
        <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>FreeVelo</title>
        <style>
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #11161c; color: #e8edf2; font-family: system-ui, sans-serif; }
          main { max-width: 420px; padding: 32px; margin: 16px; background: #1a2027; border-radius: 20px; }
          h1 { font-size: 22px; margin: 0 0 12px; } p { line-height: 1.5; color: #b3beca; } .error { color: #ff6b6b; }
          input, button { width: 100%; box-sizing: border-box; font-size: 16px; padding: 14px; border-radius: 12px; border: 0; margin-top: 12px; }
          input { background: #242b33; color: #e8edf2; } button { background: #f0703c; color: white; font-weight: 600; }
        </style></head>
        <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1>{{body}}</main></body></html>
        """;
}
