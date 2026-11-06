using System.Net;
using System.Security.Claims;
using FreeVelo.Server.Riders;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Friends;

/// <summary>Friend requests between apps; the friends themselves are stored on the phones.</summary>
public static class FriendEndpoints
{
    public static void MapFriendEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("").RequireAuthorization();

        group.MapPost(ApiRoutes.FriendRequests, (FriendRequestCreate req, ClaimsPrincipal user, RiderStore riders, FriendService friends) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            if (riders.Find(req.riderId ?? "") == null || !friends.Request(rider.Id, rider.DisplayName, req.riderId!))
            {
                return Http.Error(ApiErrors.RiderNotFound, HttpStatusCode.NotFound);
            }

            return Results.Accepted();
        }).RequireRateLimiting(RiderEndpoints.IdentityRateLimit);

        group.MapGet(ApiRoutes.FriendInbox, (ClaimsPrincipal user, FriendService friends) =>
            TokenService.RiderId(user) is { } id ? Results.Ok(friends.Inbox(id)) : Results.Unauthorized());

        group.MapPost("/api/v1/friends/requests/{id}/accept", (string id, ClaimsPrincipal user, RiderStore riders, FriendService friends) =>
        {
            if (Http.CurrentRider(user, riders) is not { } rider) return Results.Unauthorized();
            return friends.Accept(id, rider.Id, rider.DisplayName) is { } friend ? Results.Ok(friend) : Http.Error(ApiErrors.RequestNotFound, HttpStatusCode.NotFound);
        });

        group.MapPost("/api/v1/friends/requests/{id}/decline", (string id, ClaimsPrincipal user, FriendService friends) =>
        {
            if (TokenService.RiderId(user) is not { } riderId) return Results.Unauthorized();
            return friends.Decline(id, riderId) ? Results.NoContent() : Http.Error(ApiErrors.RequestNotFound, HttpStatusCode.NotFound);
        });
    }
}
