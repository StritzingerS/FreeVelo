using System.Net;
using FreeVelo.Server.Riders;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Strava;

/// <summary>
/// Connecting Strava: start (app) → Strava login → callback (browser) → claim (app); refresh (app, every few hours).
/// No rider identity needed – the random state proves the login belongs to the app; limited per client IP.
/// </summary>
public static class StravaEndpoints
{
    public static void MapStravaEndpoints(this WebApplication app)
    {
        app.MapPost(ApiRoutes.StravaStart, (StravaStartRequest req, HttpRequest http, StravaService strava) =>
        {
            if (!strava.Configured) return Http.Error(ApiErrors.StravaNotConfigured, HttpStatusCode.ServiceUnavailable);
            if (!StravaService.ValidState(req.state)) return Http.Error(ApiErrors.InvalidState);
            return Results.Ok(new StravaStartResponse { authorizeUrl = strava.Start(req.state, strava.RedirectUri(http)) });
        }).RequireRateLimiting(RiderEndpoints.IdentityRateLimit);

        app.MapGet(ApiRoutes.StravaCallback, async (string? state, string? code, string? scope, string? error, StravaService strava, CancellationToken ct) =>
        {
            bool known = StravaService.ValidState(state) && await strava.Complete(state!, code, scope, error, ct);
            bool ok = known && string.IsNullOrEmpty(error) && (scope ?? "").Split(',').Contains(StravaService.RequiredScope);
            return Results.Content(Page(known, ok), "text/html; charset=utf-8");
        }).RequireRateLimiting(RiderEndpoints.IdentityRateLimit);

        app.MapPost(ApiRoutes.StravaClaim, (StravaClaimRequest req, StravaService strava) =>
        {
            if (!StravaService.ValidState(req.state)) return Http.Error(ApiErrors.InvalidState);
            return strava.Claim(req.state, out var tokens, out var error) switch
            {
                StravaService.ClaimResult.Done => Results.Ok(tokens),
                StravaService.ClaimResult.Waiting => Results.Accepted(),
                StravaService.ClaimResult.Failed => Http.Error(error ?? ApiErrors.StravaFailed, HttpStatusCode.Forbidden),
                _ => Http.Error(ApiErrors.InvalidState, HttpStatusCode.NotFound),
            };
        });

        app.MapPost(ApiRoutes.StravaRefresh, async (StravaRefreshRequest req, StravaService strava, CancellationToken ct) =>
        {
            if (!strava.Configured) return Http.Error(ApiErrors.StravaNotConfigured, HttpStatusCode.ServiceUnavailable);
            if (string.IsNullOrEmpty(req.refreshToken)) return Http.Error(ApiErrors.StravaDenied, HttpStatusCode.Forbidden);
            try
            {
                return await strava.Refresh(req.refreshToken, ct) is { } tokens
                    ? Results.Ok(tokens)
                    : Http.Error(ApiErrors.StravaDenied, HttpStatusCode.Forbidden); // revoked on Strava: connect again
            }
            catch (HttpRequestException)
            {
                return Http.Error(ApiErrors.StravaFailed, HttpStatusCode.BadGateway);
            }
        }).RequireRateLimiting(RiderEndpoints.IdentityRateLimit);
    }

    /// <summary>What the rider sees in the browser after the Strava login (German and English).</summary>
    private static string Page(bool known, bool ok)
    {
        (string icon, string de, string en) = !known
            ? ("⚠", "Diese Anmeldung ist abgelaufen. Bitte in FreeVelo nochmal „Mit Strava verbinden“ tippen.",
                "This login has expired. Please tap “Connect with Strava” in FreeVelo again.")
            : ok
                ? ("✓", "Strava ist verbunden. Du kannst zur FreeVelo-App zurückkehren.",
                    "Strava is connected. You can go back to the FreeVelo app.")
                : ("✕", "Strava wurde nicht verbunden (abgelehnt oder „Aktivitäten hochladen“ nicht erlaubt).",
                    "Strava was not connected (declined, or “upload activities” not allowed).");
        string color = ok ? "#35C97D" : "#FF6A2B";
        return $$"""
            <!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>FreeVelo · Strava</title>
            <style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#0C1015;
            color:#E8EDF2;font-family:system-ui,sans-serif;padding:24px;box-sizing:border-box}
            .c{max-width:440px;text-align:center}.i{font-size:56px;color:{{color}}}p{font-size:18px;line-height:1.5}
            .en{color:#8A97A6;font-size:15px}</style></head>
            <body><div class="c"><div class="i">{{icon}}</div><p>{{de}}</p><p class="en">{{en}}</p></div></body></html>
            """;
    }
}
