using System.Threading.RateLimiting;
using FreeVelo.Server.Lobbies;
using FreeVelo.Server.Riders;
using FreeVelo.Server.Storage;
using FreeVelo.Shared.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var server = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
int identityPerMinute = builder.Configuration.GetValue("RateLimit:IdentityRequestsPerMinute", 20);
int codePerMinute = builder.Configuration.GetValue("RateLimit:CodeJoinsPerMinute", 10);

Directory.CreateDirectory(server.DataDirectory);
var signingKey = TokenService.LoadKey(jwt, server.DataDirectory);

builder.Services.AddSingleton(server);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new Database(Path.Combine(server.DataDirectory, "freevelo.db")));
builder.Services.AddSingleton<RiderStore>();
builder.Services.AddSingleton<LobbyRegistry>();
builder.Services.AddSingleton(sp => new TokenService(jwt, signingKey, sp.GetRequiredService<TimeProvider>()));

// The messages use public fields (readable by Unity's JsonUtility too).
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

// Protection against floods and code guessing, per client IP.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(new ApiError { error = ApiErrors.TooManyRequests }, ct));
    o.AddPolicy(RiderEndpoints.IdentityRateLimit, http => PerIp(http, identityPerMinute));
    o.AddPolicy(LobbyEndpoints.CodeRateLimit, http => PerIp(http, codePerMinute));
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
app.MapRiderEndpoints();
app.MapLobbyEndpoints();

// Opens the database (and runs migrations) at startup instead of on the first request.
app.Services.GetRequiredService<Database>();

app.Run();

static RateLimitPartition<string> PerIp(HttpContext http, int perMinute) => RateLimitPartition.GetFixedWindowLimiter(
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });

/// <summary>Entry point; public for integration tests (WebApplicationFactory).</summary>
public partial class Program
{
}
