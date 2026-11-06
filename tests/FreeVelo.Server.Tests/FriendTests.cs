using System.Net;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Tests;

/// <summary>Friends: codes kept on the phones, requests through the server, friends' private rides visible.</summary>
public sealed class FriendTests : IDisposable
{
    private readonly ServerFactory _server = new();

    public void Dispose() => _server.Dispose();

    private async Task<(FreeVeloApi Api, string Code)> Rider(string name)
    {
        var api = new FreeVeloApi(_server.CreateClient(), new TestJson()) { FriendCode = FreeVeloApi.NewFriendCode() };
        await api.CreateRiderAsync(name);
        await api.AccessTokenAsync(); // the session registers the friend code
        return (api, api.FriendCode);
    }

    [Fact]
    public async Task RequestAcceptAndBothSidesGetTheOthersCode()
    {
        var (sami, samiCode) = await Rider("Sami");
        var (anna, annaCode) = await Rider("Anna");

        await sami.RequestFriendAsync(anna.Credentials!.riderId);
        await sami.RequestFriendAsync(anna.Credentials!.riderId); // asking twice: still one request
        var inbox = await anna.FriendInboxAsync();
        var request = Assert.Single(inbox.requests);
        Assert.Equal("Sami", request.fromName);

        var newFriend = await anna.AcceptFriendAsync(request.id);
        Assert.Equal(("Sami", samiCode), (newFriend.name, newFriend.code));
        Assert.Empty((await anna.FriendInboxAsync()).requests);

        var samiInbox = await sami.FriendInboxAsync();
        var accepted = Assert.Single(samiInbox.accepted);
        Assert.Equal(("Anna", annaCode), (accepted.name, accepted.code));
        Assert.Empty((await sami.FriendInboxAsync()).accepted); // delivered once
    }

    [Fact]
    public async Task DeclinedRequestIsGone()
    {
        var (sami, _) = await Rider("Sami");
        var (anna, _) = await Rider("Anna");
        await sami.RequestFriendAsync(anna.Credentials!.riderId);
        var request = (await anna.FriendInboxAsync()).requests.Single();
        await anna.DeclineFriendAsync(request.id);
        Assert.Empty((await anna.FriendInboxAsync()).requests);
        Assert.Empty((await sami.FriendInboxAsync()).accepted);
        var error = await Assert.ThrowsAsync<ApiException>(() => anna.AcceptFriendAsync(request.id));
        Assert.Equal(HttpStatusCode.NotFound, error.Status);
    }

    [Fact]
    public async Task FriendsSeeAndJoinPrivateRidesOthersDoNot()
    {
        var (sami, samiCode) = await Rider("Sami");
        var (anna, _) = await Rider("Anna");
        var (ben, _) = await Rider("Ben");
        var ride = await sami.CreateLobbyAsync(new CreateLobbyRequest { name = "Sami's secret ride", isPrivate = true });

        // Anna has Sami's code (they are friends): she sees the ride with Sami as the friend in it, and can join.
        var visible = await anna.VisibleLobbiesAsync(new[] { samiCode });
        var entry = Assert.Single(visible.lobbies);
        Assert.True(entry.isPrivate);
        Assert.Equal(new[] { "Sami" }, entry.friends);
        var joined = await anna.JoinFromListAsync(entry.id, new[] { samiCode });
        Assert.Equal(ride.id, joined.id);

        // Ben is no friend: nothing to see, and the id alone does not get him in.
        Assert.Empty((await ben.VisibleLobbiesAsync(Array.Empty<string>())).lobbies);
        Assert.Empty((await ben.VisibleLobbiesAsync(new[] { FreeVeloApi.NewFriendCode() })).lobbies);
        var error = await Assert.ThrowsAsync<ApiException>(() => ben.JoinFromListAsync(ride.id, Array.Empty<string>()));
        Assert.Equal(HttpStatusCode.NotFound, error.Status);
        var error2 = await Assert.ThrowsAsync<ApiException>(() => ben.JoinPublicAsync(ride.id));
        Assert.Equal(HttpStatusCode.NotFound, error2.Status);
    }

    [Fact]
    public async Task PublicRidesStillShowWithoutFriends()
    {
        var (sami, _) = await Rider("Sami");
        var (anna, _) = await Rider("Anna");
        await sami.CreateLobbyAsync(new CreateLobbyRequest { name = "Open ride", isPrivate = false });
        var entry = Assert.Single((await anna.VisibleLobbiesAsync(Array.Empty<string>())).lobbies);
        Assert.False(entry.isPrivate);
        Assert.Empty(entry.friends);
    }

    [Fact]
    public async Task UnknownRiderCannotBeAsked()
    {
        var (sami, _) = await Rider("Sami");
        var error = await Assert.ThrowsAsync<ApiException>(() => sami.RequestFriendAsync("nobody"));
        Assert.Equal(ApiErrors.RiderNotFound, error.Code);
    }

    [Fact]
    public async Task AfterTheServerForgotTheRiderTheFriendCodeStillWorks()
    {
        var (sami, samiCode) = await Rider("Sami");
        var (anna, _) = await Rider("Anna");

        // A fresh server (Render restarted): Sami's app registers again with the same friend code.
        using var fresh = new ServerFactory();
        var samiAgain = new FreeVeloApi(fresh.CreateClient(), new TestJson()) { Credentials = sami.Credentials, FriendCode = samiCode };
        var ride = await samiAgain.CreateLobbyAsync(new CreateLobbyRequest { name = "After restart", isPrivate = true });
        var annaAgain = new FreeVeloApi(fresh.CreateClient(), new TestJson()) { Credentials = anna.Credentials, FriendCode = FreeVeloApi.NewFriendCode() };
        var visible = await annaAgain.VisibleLobbiesAsync(new[] { samiCode });
        Assert.Equal(ride.id, Assert.Single(visible.lobbies).id);
    }
}
