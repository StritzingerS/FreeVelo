using System.Net;
using System.Security.Claims;
using FreeVelo.Server.Riders;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Lobbies;

/// <summary>Group rides: list public ones, create (private with a 4-letter code by default), join, leave.</summary>
public static class LobbyEndpoints
{
    /// <summary>Rate limit for joining with a code (per client IP), so codes cannot be guessed by trying them all.</summary>
    public const string CodeRateLimit = "code";

    public static void MapLobbyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("").RequireAuthorization();

        group.MapGet(ApiRoutes.Lobbies, (LobbyRegistry lobbies, RiderStore riders) => Results.Ok(new LobbyList
        {
            lobbies = lobbies.PublicLobbies().Select(l =>
            {
                var (host, members) = lobbies.Members(l);
                return new LobbySummary
                {
                    id = l.Id,
                    name = l.Name,
                    mode = l.Mode,
                    maxRiders = l.MaxRiders,
                    riderCount = members.Length,
                    routeId = l.RouteId,
                    workoutName = l.WorkoutName,
                    hostName = riders.Find(host)?.DisplayName ?? "",
                };
            }).ToArray(),
        }));

        group.MapPost(ApiRoutes.Lobbies, (CreateLobbyRequest req, ClaimsPrincipal user, LobbyRegistry lobbies, RiderStore riders) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            if (!LobbyRegistry.IsValid(req)) return Http.Error(ApiErrors.InvalidLobby);
            return Results.Json(Details(lobbies.Create(req, rider.Id), lobbies, riders), statusCode: StatusCodes.Status201Created);
        });

        group.MapGet(ApiRoutes.CurrentLobby, (ClaimsPrincipal user, LobbyRegistry lobbies, RiderStore riders) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            lobbies.Touch(rider.Id);
            return lobbies.Of(rider.Id) is { } lobby ? Results.Ok(Details(lobby, lobbies, riders)) : Results.NoContent();
        });

        group.MapPost(ApiRoutes.JoinByCode, (JoinByCodeRequest req, ClaimsPrincipal user, LobbyRegistry lobbies, RiderStore riders) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            return Joined(lobbies.JoinByCode(req.code ?? "", rider.Id), lobbies, riders);
        }).RequireRateLimiting(CodeRateLimit);

        group.MapPost("/api/v1/lobbies/{id}/join", (string id, ClaimsPrincipal user, LobbyRegistry lobbies, RiderStore riders) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            return Joined(lobbies.JoinPublic(id, rider.Id), lobbies, riders);
        });

        group.MapPost(ApiRoutes.LeaveLobby, (ClaimsPrincipal user, LobbyRegistry lobbies) =>
        {
            if (TokenService.RiderId(user) is not { } riderId) return Results.Unauthorized();
            lobbies.Leave(riderId);
            return Results.NoContent();
        });
    }

    private static IResult Joined((JoinResult Result, Lobby? Lobby) join, LobbyRegistry lobbies, RiderStore riders) => join.Result switch
    {
        JoinResult.Joined => Results.Ok(Details(join.Lobby!, lobbies, riders)),
        JoinResult.Full => Http.Error(ApiErrors.LobbyFull, HttpStatusCode.Conflict),
        _ => Http.Error(ApiErrors.LobbyNotFound, HttpStatusCode.NotFound),
    };

    private static LobbyDetails Details(Lobby lobby, LobbyRegistry lobbies, RiderStore riders)
    {
        var (host, members) = lobbies.Members(lobby);
        return new LobbyDetails
        {
            id = lobby.Id,
            code = lobby.Code,
            name = lobby.Name,
            mode = lobby.Mode,
            isPrivate = lobby.IsPrivate,
            maxRiders = lobby.MaxRiders,
            routeId = lobby.RouteId,
            workoutId = lobby.WorkoutId,
            workoutName = lobby.WorkoutName,
            hostRiderId = host,
            riders = members.Select(id => riders.Find(id) is { } r ? Http.ToInfo(r) : new RiderInfo { id = id }).ToArray(),
        };
    }
}
