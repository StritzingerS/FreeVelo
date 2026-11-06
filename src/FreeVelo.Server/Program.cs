using System.Threading.RateLimiting;
using FreeVelo.Server.Accounts;
using FreeVelo.Server.Email;
using FreeVelo.Server.Storage;
using FreeVelo.Shared.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var server = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var smtp = builder.Configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
var argon = builder.Configuration.GetSection("Argon2").Get<Argon2Options>() ?? new Argon2Options();
int authRequestsPerMinute = builder.Configuration.GetValue("RateLimit:AuthRequestsPerMinute", 10);

Directory.CreateDirectory(server.DataDirectory);
var signingKey = TokenService.LoadKey(jwt, server.DataDirectory);

builder.Services.AddSingleton(server);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(smtp);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new PasswordHasher(argon));
builder.Services.AddSingleton(sp => new Database(Path.Combine(server.DataDirectory, "freevelo.db")));
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton(sp => new TokenService(jwt, signingKey, sp.GetRequiredService<TimeProvider>()));
if (string.IsNullOrWhiteSpace(smtp.Host)) builder.Services.AddSingleton<IEmailSender, LogEmailSender>();
else builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// The DTOs use public fields (readable by Unity's JsonUtility too).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.IncludeFields = true);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Brute-force protection for everything that checks a password or sends an email: per client IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ApiError { error = ApiErrors.TooManyRequests }, ct));
    o.AddPolicy(AccountEndpoints.AuthRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// Behind Caddy (reverse proxy on the same host): the client IP comes from X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAccountEndpoints();

// Opens the database (and runs migrations) at startup instead of on the first request.
app.Services.GetRequiredService<Database>();

app.Run();

/// <summary>Entry point; public for integration tests (WebApplicationFactory).</summary>
public partial class Program
{
}
