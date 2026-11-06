using System.Net;
using System.Security.Claims;
using FreeVelo.Server.Lobbies;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Riders;

public sealed class ServerOptions
{
    /// <summary>Folder for the database and the generated signing key (a Docker volume in production).</summary>
    public string DataDirectory { get; set; } = "data";
}

/// <summary>Riders: created with a display name; the app signs in with the stored rider id and secret.</summary>
public static class RiderEndpoints
{
    /// <summary>Rate limit for creating riders and sessions (per client IP).</summary>
    public const string IdentityRateLimit = "identity";

    public static void MapRiderEndpoints(this WebApplication app)
    {
        app.MapPost(ApiRoutes.Riders, (CreateRiderRequest req, RiderStore riders) =>
        {
            if (!RiderStore.IsValidDisplayName(req.displayName)) return Http.Error(ApiErrors.InvalidDisplayName);
            var (rider, secret) = riders.Create(req.displayName);
            return Results.Json(new RiderCredentials { riderId = rider.Id, secret = secret, displayName = rider.DisplayName }, statusCode: StatusCodes.Status201Created);
        }).RequireRateLimiting(IdentityRateLimit);

        app.MapPost(ApiRoutes.Session, (SessionRequest req, RiderStore riders, TokenService tokens, JwtOptions jwt) =>
        {
            var rider = riders.Authenticate(req.riderId ?? "", req.secret ?? "");
            if (rider == null) return Http.Error(ApiErrors.InvalidCredentials, HttpStatusCode.Unauthorized);
            return Results.Ok(new SessionResponse
            {
                accessToken = tokens.CreateAccessToken(rider),
                expiresIn = (int)jwt.AccessTokenLifetime.TotalSeconds,
                rider = Http.ToInfo(rider),
            });
        }).RequireRateLimiting(IdentityRateLimit);

        var me = app.MapGroup(ApiRoutes.Me).RequireAuthorization();

        me.MapGet("", (ClaimsPrincipal user, RiderStore riders) =>
            Http.CurrentRider(user, riders) is { } rider ? Results.Ok(Http.ToInfo(rider)) : Results.Unauthorized());

        me.MapPatch("", (UpdateRiderRequest req, ClaimsPrincipal user, RiderStore riders) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            if (!RiderStore.IsValidDisplayName(req.displayName)) return Http.Error(ApiErrors.InvalidDisplayName);
            riders.Rename(rider.Id, req.displayName);
            return Results.Ok(Http.ToInfo(riders.Find(rider.Id)!));
        });

        // Removes the rider from the server (and from any group ride); the app forgets its identity.
        me.MapDelete("", (ClaimsPrincipal user, RiderStore riders, LobbyRegistry lobbies) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            lobbies.Leave(rider.Id);
            riders.Delete(rider.Id);
            return Results.NoContent();
        });
    }
}

/// <summary>Small helpers shared by the endpoint groups.</summary>
internal static class Http
{
    public static IResult Error(string code, HttpStatusCode status = HttpStatusCode.BadRequest) =>
        Results.Json(new ApiError { error = code }, statusCode: (int)status);

    public static Rider? CurrentRider(ClaimsPrincipal user, RiderStore riders) =>
        TokenService.RiderId(user) is { } id ? riders.Find(id) : null;

    public static RiderInfo ToInfo(Rider rider) => new() { id = rider.Id, displayName = rider.DisplayName };
}
