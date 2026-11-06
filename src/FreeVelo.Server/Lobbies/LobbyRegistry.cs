using System.Security.Cryptography;
using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Lobbies;

public sealed class Lobby
{
    private readonly List<string> _riderIds = new();

    public Lobby(string id, string code, CreateLobbyRequest settings, string hostRiderId, DateTimeOffset createdUtc)
    {
        Id = id;
        Code = code;
        Name = settings.name.Trim();
        Mode = settings.mode;
        IsPrivate = settings.isPrivate;
        MaxRiders = settings.maxRiders;
        RouteId = settings.routeId ?? "";
        WorkoutId = settings.workoutId ?? "";
        WorkoutName = settings.workoutName ?? "";
        HostRiderId = hostRiderId;
        LastActivityUtc = createdUtc;
    }

    public string Id { get; }

    public string Code { get; }

    public string Name { get; }

    public LobbyMode Mode { get; }

    public bool IsPrivate { get; }

    public int MaxRiders { get; }

    public string RouteId { get; }

    public string WorkoutId { get; }

    public string WorkoutName { get; }

    /// <summary>Creator; passes to the longest-present rider when the host leaves.</summary>
    public string HostRiderId { get; internal set; }

    public IReadOnlyList<string> RiderIds => _riderIds;

    public DateTimeOffset LastActivityUtc { get; internal set; }

    internal void Add(string riderId) => _riderIds.Add(riderId);

    internal void Remove(string riderId)
    {
        _riderIds.Remove(riderId);
        if (HostRiderId == riderId && _riderIds.Count > 0) HostRiderId = _riderIds[0];
    }
}

public enum JoinResult
{
    Joined,
    NotFound,
    Full,
}

/// <summary>
/// Group rides, in memory only (concept: nothing about rides is stored). Every ride gets a 4-letter code, unique
/// among the open rides; private rides are joinable only with it, public ones are also listed. A rider is in at
/// most one ride; a ride closes when its last rider leaves, or when nobody was active in it for a while
/// (a client that disappeared without leaving).
/// </summary>
public sealed class LobbyRegistry
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A rider without any activity (no live connection, no API calls – the waiting room asks every few seconds) for
    /// this long has left: the app was closed. Long enough to switch to a messenger and share the code.
    /// </summary>
    public static readonly TimeSpan RiderTimeout = TimeSpan.FromMinutes(3);

    private readonly Dictionary<string, DateTimeOffset> _lastSeen = new();

    private readonly object _sync = new();
    private readonly Dictionary<string, Lobby> _byId = new();
    private readonly Dictionary<string, Lobby> _byCode = new();
    private readonly Dictionary<string, Lobby> _byRider = new();
    private readonly TimeProvider _time;

    public LobbyRegistry(TimeProvider time) => _time = time;

    public static bool IsValid(CreateLobbyRequest settings) =>
        settings.name != null
        && settings.name.Trim().Length >= LobbyRules.MinNameLength
        && settings.name.Trim().Length <= LobbyRules.MaxNameLength
        && !settings.name.Any(char.IsControl)
        && settings.maxRiders >= LobbyRules.MinRiders
        && settings.maxRiders <= LobbyRules.MaxRiders
        && Enum.IsDefined(settings.mode);

    /// <summary>Creates a ride with the host as its first rider (leaving any ride the host was in).</summary>
    public Lobby Create(CreateLobbyRequest settings, string hostRiderId)
    {
        lock (_sync)
        {
            RemoveIdle();
            LeaveLocked(hostRiderId);
            var lobby = new Lobby(Guid.NewGuid().ToString("N"), NewCode(), settings, hostRiderId, _time.GetUtcNow());
            _byId[lobby.Id] = lobby;
            _byCode[lobby.Code] = lobby;
            AddLocked(lobby, hostRiderId);
            return lobby;
        }
    }

    public (JoinResult Result, Lobby? Lobby) JoinByCode(string code, string riderId)
    {
        lock (_sync)
        {
            RemoveIdle();
            return _byCode.TryGetValue(LobbyRules.NormalizeCode(code), out var lobby) ? JoinLocked(lobby, riderId) : (JoinResult.NotFound, null);
        }
    }

    /// <summary>Joins a public ride from the list (private rides need the code).</summary>
    public (JoinResult Result, Lobby? Lobby) JoinPublic(string lobbyId, string riderId, Func<Lobby, bool>? allowPrivate = null)
    {
        lock (_sync)
        {
            RemoveIdle();
            if (!_byId.TryGetValue(lobbyId, out var lobby)) return (JoinResult.NotFound, null);
            if (lobby.IsPrivate && (allowPrivate == null || !allowPrivate(lobby))) return (JoinResult.NotFound, null);
            return JoinLocked(lobby, riderId);
        }
    }

    /// <summary>All open rides (public and private), for filtering by friends.</summary>
    public IReadOnlyList<Lobby> AllLobbies()
    {
        lock (_sync)
        {
            RemoveIdle();
            return _byId.Values.OrderByDescending(l => l.RiderIds.Count).ThenBy(l => l.Name).ToList();
        }
    }

    public void Leave(string riderId)
    {
        lock (_sync) LeaveLocked(riderId);
    }

    /// <summary>The open ride with this id, or null once it closed.</summary>
    public Lobby? Get(string lobbyId)
    {
        lock (_sync)
        {
            RemoveIdle();
            return _byId.TryGetValue(lobbyId, out var lobby) ? lobby : null;
        }
    }

    public Lobby? Of(string riderId)
    {
        lock (_sync)
        {
            RemoveIdle();
            return _byRider.TryGetValue(riderId, out var lobby) ? lobby : null;
        }
    }

    /// <summary>Keeps the rider's ride open (called by realtime traffic and API use).</summary>
    public void Touch(string riderId)
    {
        lock (_sync)
        {
            if (!_byRider.TryGetValue(riderId, out var lobby)) return;
            var now = _time.GetUtcNow();
            lobby.LastActivityUtc = now;
            _lastSeen[riderId] = now;
        }
    }

    public IReadOnlyList<Lobby> PublicLobbies()
    {
        lock (_sync)
        {
            RemoveIdle();
            return _byId.Values.Where(l => !l.IsPrivate).OrderByDescending(l => l.RiderIds.Count).ThenBy(l => l.Name).ToList();
        }
    }

    /// <summary>Snapshot of a ride's riders and host (safe to read outside the lock).</summary>
    public (string HostRiderId, string[] RiderIds) Members(Lobby lobby)
    {
        lock (_sync) return (lobby.HostRiderId, lobby.RiderIds.ToArray());
    }

    private (JoinResult, Lobby?) JoinLocked(Lobby lobby, string riderId)
    {
        if (_byRider.TryGetValue(riderId, out var current) && current == lobby)
        {
            lobby.LastActivityUtc = _time.GetUtcNow();
            return (JoinResult.Joined, lobby);
        }

        if (lobby.RiderIds.Count >= lobby.MaxRiders) return (JoinResult.Full, null);
        LeaveLocked(riderId);
        AddLocked(lobby, riderId);
        return (JoinResult.Joined, lobby);
    }

    private void AddLocked(Lobby lobby, string riderId)
    {
        lobby.Add(riderId);
        lobby.LastActivityUtc = _time.GetUtcNow();
        _byRider[riderId] = lobby;
        _lastSeen[riderId] = lobby.LastActivityUtc;
    }

    private void LeaveLocked(string riderId)
    {
        _lastSeen.Remove(riderId);
        if (!_byRider.Remove(riderId, out var lobby)) return;
        lobby.Remove(riderId);
        if (lobby.RiderIds.Count == 0) Close(lobby);
    }

    private void Close(Lobby lobby)
    {
        _byId.Remove(lobby.Id);
        _byCode.Remove(lobby.Code);
        foreach (string riderId in lobby.RiderIds)
        {
            _byRider.Remove(riderId);
            _lastSeen.Remove(riderId);
        }
    }

    private void RemoveIdle()
    {
        var now = _time.GetUtcNow();
        foreach (var riderId in _lastSeen.Where(p => now - p.Value > RiderTimeout).Select(p => p.Key).ToList()) LeaveLocked(riderId);
        foreach (var lobby in _byId.Values.Where(l => now - l.LastActivityUtc > IdleTimeout).ToList()) Close(lobby);
    }

    /// <summary>A random code not used by an open ride (23^4 ≈ 280 000 codes; joining by code is rate-limited).</summary>
    private string NewCode()
    {
        while (true)
        {
            var chars = new char[LobbyRules.CodeLength];
            for (int i = 0; i < chars.Length; i++) chars[i] = LobbyRules.CodeAlphabet[RandomNumberGenerator.GetInt32(LobbyRules.CodeAlphabet.Length)];
            string code = new(chars);
            if (!_byCode.ContainsKey(code)) return code;
        }
    }
}
