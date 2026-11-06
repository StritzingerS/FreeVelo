using System.Net;
using System.Net.Http.Headers;
using FreeVelo.Server.Accounts;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Tests;

public sealed class AccountTests : IDisposable
{
    private const string Email = "anna@example.com";
    private const string Password = "correct horse battery";
    private readonly ServerFactory _server = new();
    private readonly HttpClient _client;

    public AccountTests() => _client = _server.CreateClient();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task Health()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegisterVerifyLoginAndReadAccount()
    {
        var register = await _client.Post(ApiRoutes.Register, new RegisterRequest { email = " Anna@Example.com ", displayName = "Anna", password = Password, language = "de" });
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);
        var mail = Assert.Single(_server.Emails.Sent);
        Assert.Equal(Email, mail.To);
        Assert.Contains("bestätige", mail.Body); // German, as requested
        Assert.Contains("https://freevelo.test/verify?token=", mail.Body);

        // Not verified yet: the password is right, but login is refused with a clear code.
        var early = await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal(ApiErrors.EmailNotVerified, (await early.Read<ApiError>()).error);

        var verify = await _client.GetAsync("/verify?token=" + Uri.EscapeDataString(_server.Emails.LastToken(Email, "/verify")));
        Assert.Contains("Email confirmed", await verify.Content.ReadAsStringAsync());

        var session = await Login();
        Assert.Equal("Anna", session.account.displayName);
        Assert.Equal(900, session.expiresIn);

        var me = await Authorized(HttpMethod.Get, ApiRoutes.Me, session.accessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(Email, (await me.Read<AccountResponse>()).email);
    }

    [Fact]
    public async Task VerificationLinkWorksOnlyOnce()
    {
        await Register();
        string token = _server.Emails.LastToken(Email, "/verify");
        Assert.Contains("Email confirmed", await (await _client.GetAsync("/verify?token=" + Uri.EscapeDataString(token))).Content.ReadAsStringAsync());
        Assert.Contains("Link invalid", await (await _client.GetAsync("/verify?token=" + Uri.EscapeDataString(token))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VerificationLinkExpires()
    {
        await Register();
        _server.Time.Advance(TimeSpan.FromHours(49));
        var page = await _client.GetAsync("/verify?token=" + Uri.EscapeDataString(_server.Emails.LastToken(Email, "/verify")));
        Assert.Contains("Link invalid", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RegisteringAnExistingEmailRevealsNothingButTellsTheOwner()
    {
        await RegisterAndVerify();
        var again = await _client.Post(ApiRoutes.Register, new RegisterRequest { email = Email, displayName = "Mallory", password = "another password" });
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Contains("already", _server.Emails.Sent.Last().Subject);

        // The original password still works; the attacker's does not.
        await Login();
        var attacker = await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = "another password" });
        Assert.Equal(HttpStatusCode.Unauthorized, attacker.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "Anna", Password, ApiErrors.InvalidEmail)]
    [InlineData("anna@localhost", "Anna", Password, ApiErrors.InvalidEmail)]
    [InlineData(Email, "A", Password, ApiErrors.InvalidDisplayName)]
    [InlineData(Email, "Anna", "short", ApiErrors.WeakPassword)]
    public async Task RegisterValidatesInput(string email, string name, string password, string error)
    {
        var response = await _client.Post(ApiRoutes.Register, new RegisterRequest { email = email, displayName = name, password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(error, (await response.Read<ApiError>()).error);
        Assert.Empty(_server.Emails.Sent);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownEmailLookTheSame()
    {
        await RegisterAndVerify();
        var wrong = await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = "wrong password!" });
        var unknown = await _client.Post(ApiRoutes.Login, new LoginRequest { email = "nobody@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(ApiErrors.InvalidCredentials, (await wrong.Read<ApiError>()).error);
        Assert.Equal(ApiErrors.InvalidCredentials, (await unknown.Read<ApiError>()).error);
    }

    [Fact]
    public async Task RefreshRotatesTheToken()
    {
        await RegisterAndVerify();
        var first = await Login();
        var second = await Refresh(first.refreshToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var rotated = await second.Read<SessionResponse>();
        Assert.NotEqual(first.refreshToken, rotated.refreshToken);
        Assert.Equal(HttpStatusCode.OK, (await Authorized(HttpMethod.Get, ApiRoutes.Me, rotated.accessToken)).StatusCode);
    }

    [Fact]
    public async Task ReusingARefreshTokenRevokesTheWholeFamily()
    {
        await RegisterAndVerify();
        var first = await Login();
        var rotated = await (await Refresh(first.refreshToken)).Read<SessionResponse>();

        // Someone replays the old token (stolen): refused, and the legitimate newer token stops working too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(first.refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(rotated.refreshToken)).StatusCode);
    }

    [Fact]
    public async Task OtherDevicesStaySignedInWhenOneIsRevoked()
    {
        await RegisterAndVerify();
        var phone = await Login();
        var tablet = await Login();
        var rotatedPhone = await (await Refresh(phone.refreshToken)).Read<SessionResponse>();
        await Refresh(phone.refreshToken); // replay on the phone's family

        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(rotatedPhone.refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Refresh(tablet.refreshToken)).StatusCode);
    }

    [Fact]
    public async Task RefreshTokenExpires()
    {
        await RegisterAndVerify();
        var session = await Login();
        _server.Time.Advance(TimeSpan.FromDays(91));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(session.refreshToken)).StatusCode);
    }

    [Fact]
    public async Task LogoutEndsThatSession()
    {
        await RegisterAndVerify();
        var session = await Login();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.Post(ApiRoutes.Logout, new RefreshRequest { refreshToken = session.refreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(session.refreshToken)).StatusCode);
    }

    [Fact]
    public async Task ApiNeedsAValidAccessToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(ApiRoutes.Me)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Authorized(HttpMethod.Get, ApiRoutes.Me, "not.a.token")).StatusCode);
    }

    [Fact]
    public async Task PasswordResetThroughTheEmailLink()
    {
        await RegisterAndVerify();
        var oldSession = await Login();
        Assert.Equal(HttpStatusCode.Accepted, (await _client.Post(ApiRoutes.ForgotPassword, new EmailRequest { email = Email })).StatusCode);
        string token = _server.Emails.LastToken(Email, "/reset");

        var form = await _client.GetAsync("/reset?token=" + Uri.EscapeDataString(token));
        Assert.Contains("name=\"password\"", await form.Content.ReadAsStringAsync());

        var tooShort = await PostResetForm(token, "short");
        Assert.Contains("At least", await tooShort.Content.ReadAsStringAsync());

        var done = await PostResetForm(token, "a brand new password");
        Assert.Contains("Password changed", await done.Content.ReadAsStringAsync());

        // Old password and old sessions are gone; the new password works; the link is used up.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(oldSession.refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = "a brand new password" })).StatusCode);
        Assert.Contains("Link invalid", await (await PostResetForm(token, "yet another password")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ForgotPasswordForUnknownEmailSendsNothing()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await _client.Post(ApiRoutes.ForgotPassword, new EmailRequest { email = "nobody@example.com" })).StatusCode);
        Assert.Empty(_server.Emails.Sent);
    }

    [Fact]
    public async Task ChangeDisplayNameAndPassword()
    {
        await RegisterAndVerify();
        var session = await Login();

        var rename = await Authorized(HttpMethod.Patch, ApiRoutes.Me, session.accessToken, new UpdateAccountRequest { displayName = "  Anna B.  " });
        Assert.Equal("Anna B.", (await rename.Read<AccountResponse>()).displayName);

        var wrongCurrent = await Authorized(HttpMethod.Post, ApiRoutes.ChangePassword, session.accessToken, new ChangePasswordRequest { currentPassword = "wrong one!!", newPassword = "new password 123" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCurrent.StatusCode);

        var change = await Authorized(HttpMethod.Post, ApiRoutes.ChangePassword, session.accessToken, new ChangePasswordRequest { currentPassword = Password, newPassword = "new password 123" });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(session.refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = "new password 123" })).StatusCode);
    }

    [Fact]
    public async Task DeleteAccountNeedsThePasswordAndRemovesEverything()
    {
        await RegisterAndVerify();
        var session = await Login();

        var refused = await Authorized(HttpMethod.Post, ApiRoutes.DeleteAccount, session.accessToken, new DeleteAccountRequest { password = "wrong password" });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var deleted = await Authorized(HttpMethod.Post, ApiRoutes.DeleteAccount, session.accessToken, new DeleteAccountRequest { password = Password });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(session.refreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Authorized(HttpMethod.Get, ApiRoutes.Me, session.accessToken)).StatusCode);

        // The address is free again.
        await Register();
        Assert.Contains("confirm", _server.Emails.Sent.Last().Subject);
    }

    [Fact]
    public async Task LoginIsRateLimitedPerClient()
    {
        using var limited = new ServerFactory(authRequestsPerMinute: 3);
        var client = limited.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
        {
            codes.Add((await client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = "guess number " + i })).StatusCode);
        }

        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(c => c == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public void PasswordHashesUseArgon2idAndVerify()
    {
        var hasher = new PasswordHasher(new Argon2Options { MemoryKiB = 1024, Iterations = 1 });
        string hash = hasher.Hash(Password);
        Assert.StartsWith("$argon2id$v=19$m=1024,t=1,p=1$", hash);
        Assert.True(hasher.Verify(Password, hash));
        Assert.False(hasher.Verify(Password + "x", hash));
        Assert.NotEqual(hash, hasher.Hash(Password)); // salted
        Assert.False(hasher.NeedsRehash(hash));
        Assert.True(new PasswordHasher(new Argon2Options()).NeedsRehash(hash));
        Assert.False(hasher.Verify(Password, "garbage"));
    }

    // ---------------------------------------------------------------- helpers

    private Task<HttpResponseMessage> Register() =>
        _client.Post(ApiRoutes.Register, new RegisterRequest { email = Email, displayName = "Anna", password = Password });

    private async Task RegisterAndVerify()
    {
        await Register();
        await _client.GetAsync("/verify?token=" + Uri.EscapeDataString(_server.Emails.LastToken(Email, "/verify")));
    }

    private async Task<SessionResponse> Login()
    {
        var response = await _client.Post(ApiRoutes.Login, new LoginRequest { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Read<SessionResponse>();
    }

    private Task<HttpResponseMessage> Refresh(string token) =>
        _client.Post(ApiRoutes.Refresh, new RefreshRequest { refreshToken = token });

    private Task<HttpResponseMessage> PostResetForm(string token, string password) =>
        _client.PostAsync("/reset", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token, ["password"] = password }));

    private Task<HttpResponseMessage> Authorized(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body != null) request.Content = System.Net.Http.Json.JsonContent.Create(body, body.GetType(), options: ServerFactory.Json);
        return _client.SendAsync(request);
    }
}
