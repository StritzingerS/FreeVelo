using System.Threading.RateLimiting;
using FreeVelo.Server.Live;
using FreeVelo.Server.Lobbies;
using FreeVelo.Server.Riders;
using FreeVelo.Server.Storage;
using FreeVelo.Shared.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Hosting platforms (Render, Koyeb, …) say through PORT where to listen; otherwise ASPNETCORE_HTTP_PORTS / --urls.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out int platformPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{platformPort}");
}

var server = builder.Configuration.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var live = builder.Configuration.GetSection("Live").Get<LiveOptions>() ?? new LiveOptions();
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
builder.Services.AddSingleton(live);
builder.Services.AddSingleton<LiveHub>();
builder.Services.AddHostedService<LiveTicker>();

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

        // WebSocket clients that cannot set headers may pass the token as ?access_token=… (live endpoint only).
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path == ApiRoutes.Live && context.Request.Query.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
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

// Behind Caddy: the client IP comes from X-Forwarded-For (rate limits are per client). By default only a proxy
// on localhost is trusted; with Server:BehindProxy (Docker Compose, where Caddy is another container and port
// 8080 is not published) the proxy in front is trusted whatever its address.
bool behindProxy = builder.Configuration.GetValue("Server:BehindProxy", false);
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (behindProxy)
    {
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapRiderEndpoints();
app.MapLobbyEndpoints();

// Live group ride: one WebSocket per app (binary messages, see FreeVelo.Shared.Live).
app.Map(ApiRoutes.Live, async (HttpContext context, LiveHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest || TokenService.RiderId(context.User) is not { } riderId)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.Run(socket, riderId, context.RequestAborted);
}).RequireAuthorization();

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
