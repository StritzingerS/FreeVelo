using System.Net.WebSockets;
using FreeVelo.Core.Multiplayer;
using FreeVelo.Core.World;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;

namespace FreeVelo.Server.Live;

/// <summary>Custom WebSocket close codes (4000–4999) the app can act on.</summary>
public static class LiveCloseCodes
{
    /// <summary>The rider is in no group ride (join one over REST first).</summary>
    public const WebSocketCloseStatus NotInRide = (WebSocketCloseStatus)4404;

    /// <summary>The same rider connected again (e.g. app restarted); the older connection ends.</summary>
    public const WebSocketCloseStatus Replaced = (WebSocketCloseStatus)4409;

    /// <summary>The rider left the ride (over REST, or was away too long).</summary>
    public const WebSocketCloseStatus LeftRide = (WebSocketCloseStatus)4410;

    /// <summary>The group ride closed.</summary>
    public const WebSocketCloseStatus RideClosed = (WebSocketCloseStatus)4411;
}

/// <summary>
/// The live state of one group ride: members with their latest state and connection, the Keep Together group
/// (or plain relaying in Free mode), and the 4 Hz snapshots. All access goes through <see cref="Sync"/>.
/// </summary>
public sealed class LobbySession
{
    /// <summary>Lanes by slot, so riders spread over the road instead of overlapping.</summary>
    private static readonly float[] Lanes = { -0.5f, 0.5f, -1.5f, 1.5f, 0f, -1f, 1f, -2f, 2f };

    private readonly Dictionary<string, Member> _members = new();
    private readonly TimeProvider _time;
    private ushort _nextSlot = 1;
    private uint _tick;
    private bool _rosterChanged;

    public LobbySession(string lobbyId, LobbyMode mode, string routeId, TimeProvider time)
    {
        LobbyId = lobbyId;
        Mode = mode;
        _time = time;
        if (mode == LobbyMode.KeepTogether) Group = new KeepTogetherGroup(BuiltInRoutes.Get(routeId));
    }

    public object Sync { get; } = new();

    public string LobbyId { get; }

    public LobbyMode Mode { get; }

    public KeepTogetherGroup? Group { get; }

    public IEnumerable<string> MemberIds => _members.Keys;

    public bool HasConnections => _members.Values.Any(m => m.Connection != null);

    /// <summary>The app connected: welcome it (replacing an older connection of the same rider).</summary>
    public void Attach(string riderId, string displayName, LiveConnection connection)
    {
        var member = AddMember(riderId, displayName);
        member.Connection?.Close(LiveCloseCodes.Replaced, "replaced");
        member.Connection = connection;
        member.DisconnectedAt = null;
        connection.Send(LiveCodec.Encode(new WelcomeMessage { YourSlot = member.Slot, Mode = Mode, StartDistanceM = StartDistance(riderId) }));
        _rosterChanged = true;
    }

    /// <summary>The app's connection ended; the rider stays in the ride for a while (reconnect after a dropout).</summary>
    public void Detach(string riderId, LiveConnection connection)
    {
        if (_members.TryGetValue(riderId, out var member) && member.Connection == connection)
        {
            member.Connection = null;
            member.DisconnectedAt = _time.GetUtcNow();
        }
    }

    public Member AddMember(string riderId, string displayName)
    {
        if (!_members.TryGetValue(riderId, out var member))
        {
            ushort slot = _nextSlot++;
            member = new Member(riderId, slot, Lanes[(slot - 1) % Lanes.Length]) { DisplayName = displayName };
            _members[riderId] = member;
            Group?.Join(riderId);
            _rosterChanged = true;
        }

        return member;
    }

    public void RemoveMember(string riderId, WebSocketCloseStatus status, string reason)
    {
        if (!_members.Remove(riderId, out var member)) return;
        member.Connection?.Close(status, reason);
        Group?.Leave(riderId);
        _rosterChanged = true;
    }

    public void CloseAll(WebSocketCloseStatus status, string reason)
    {
        foreach (var id in _members.Keys.ToList()) RemoveMember(id, status, reason);
    }

    /// <summary>Riders whose app has been gone longer than <paramref name="grace"/>.</summary>
    public IEnumerable<string> AwayLongerThan(TimeSpan grace)
    {
        var now = _time.GetUtcNow();
        return _members.Values.Where(m => m.DisconnectedAt is { } t && now - t > grace).Select(m => m.RiderId).ToList();
    }

    public void Handle(string riderId, LiveMessage message)
    {
        if (!_members.TryGetValue(riderId, out var member)) return;
        switch (message)
        {
            case RiderStateMessage state:
                state.Wkg = Math.Clamp(state.Wkg, 0, 30);
                state.SpeedMps = Math.Clamp(state.SpeedMps, 0, 40);
                state.LaneM = Math.Clamp(state.LaneM, -3, 3);
                member.State = state;
                Group?.Report(riderId, state.Wkg, state.CadenceRpm);
                break;
            case EmojiMessage emoji when Enum.IsDefined(emoji.Emoji):
                var now = _time.GetUtcNow();
                if (now - member.LastEmojiAt < TimeSpan.FromSeconds(1)) return; // no emoji spam
                member.LastEmojiAt = now;
                Broadcast(LiveCodec.Encode(new EmojiEventMessage { Slot = member.Slot, Emoji = emoji.Emoji }));
                break;
        }
    }

    /// <summary>One server tick: advance the group, send the roster if it changed, send a snapshot to everyone.</summary>
    public void Tick(double dtSeconds)
    {
        Group?.Step(dtSeconds);
        if (_rosterChanged)
        {
            _rosterChanged = false;
            Broadcast(LiveCodec.Encode(Roster()));
        }

        Broadcast(LiveCodec.Encode(Snapshot()));
    }

    public RosterMessage Roster() => new()
    {
        Riders = _members.Values.OrderBy(m => m.Slot).Select(m => new RosterEntry
        {
            Slot = m.Slot,
            RiderId = m.RiderId,
            DisplayName = m.DisplayName,
            HomeLaneM = m.HomeLaneM,
        }).ToList(),
    };

    public SnapshotMessage Snapshot()
    {
        var snapshot = new SnapshotMessage
        {
            Tick = ++_tick,
            AnchorDistanceM = Group?.AnchorDistanceM ?? 0,
            AnchorSpeedMps = (float)(Group?.AnchorSpeedMps ?? 0),
        };

        foreach (var m in _members.Values.OrderBy(m => m.Slot))
        {
            var flags = m.Connection != null ? RiderFlags.Connected : RiderFlags.None;
            double distance;
            float speed;
            if (Group != null)
            {
                var p = Group.PositionOf(m.RiderId);
                distance = p.DistanceM;
                speed = (float)p.SpeedMps;
                if (p.Active) flags |= RiderFlags.Active;
                if (p.Towed) flags |= RiderFlags.Towed;
            }
            else
            {
                distance = m.State?.DistanceM ?? 0;
                speed = m.State?.SpeedMps ?? 0;
                if (m.State is { Wkg: >= (float)KeepTogetherGroup.MinActiveWkg, CadenceRpm: > 0 }) flags |= RiderFlags.Active;
            }

            snapshot.Riders.Add(new SnapshotEntry
            {
                Slot = m.Slot,
                DistanceM = distance,
                LaneM = m.State?.LaneM ?? m.HomeLaneM,
                SpeedMps = speed,
                Wkg = m.State?.Wkg ?? 0,
                CadenceRpm = m.State?.CadenceRpm ?? 0,
                Flags = flags,
            });
        }

        return snapshot;
    }

    /// <summary>Where a (re)connecting rider starts: their group position, or next to the others in Free mode.</summary>
    private double StartDistance(string riderId)
    {
        if (Group != null) return Group.PositionOf(riderId).DistanceM;
        if (_members.TryGetValue(riderId, out var self) && self.State != null) return self.State.DistanceM;
        var others = _members.Values.Where(m => m.RiderId != riderId && m.State != null).Select(m => m.State!.DistanceM).ToList();
        return others.Count > 0 ? others.Average() : 0;
    }

    private void Broadcast(byte[] frame)
    {
        foreach (var m in _members.Values) m.Connection?.Send(frame);
    }

    public sealed class Member
    {
        public Member(string riderId, ushort slot, float homeLaneM)
        {
            RiderId = riderId;
            Slot = slot;
            HomeLaneM = homeLaneM;
        }

        public string RiderId { get; }

        public ushort Slot { get; }

        public float HomeLaneM { get; }

        public string DisplayName { get; set; } = "";

        public RiderStateMessage? State { get; set; }

        public LiveConnection? Connection { get; set; }

        public DateTimeOffset? DisconnectedAt { get; set; }

        public DateTimeOffset LastEmojiAt { get; set; } = DateTimeOffset.MinValue;
    }
}
