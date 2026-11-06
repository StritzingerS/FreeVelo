using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Shared.Api;

namespace FreeVelo.Shared.Live
{
    /// <summary>A rider of the group ride as the app shows them right now (smoothed between snapshots).</summary>
    public struct GroupRider
    {
        public ushort Slot;
        public string RiderId;
        public string DisplayName;
        public bool IsMe;
        public double DistanceM;
        public float LaneM;
        public float SpeedMps;
        public float Wkg;
        public byte CadenceRpm;
        public RiderFlags Flags;
    }

    /// <summary>An emoji reaction that arrived (<see cref="ReceivedS"/> on the caller's clock).</summary>
    public struct ReceivedEmoji
    {
        public ushort Slot;
        public Emoji Emoji;
        public double ReceivedS;
    }

    /// <summary>
    /// What the app knows about its group ride, built from the server's messages. Thread-safe: messages arrive on
    /// the network thread, the ride logic and the 3D view read on theirs. Other riders are shown a little in the
    /// past (<see cref="RenderDelayS"/>) and interpolated between snapshots, so they move smoothly although
    /// positions arrive only 4 times a second; if snapshots stop, they are extrapolated briefly at their speed.
    /// </summary>
    public sealed class GroupRideState
    {
        /// <summary>Riders are shown this far in the past, so there are snapshots on both sides to interpolate.</summary>
        public const double RenderDelayS = 0.4;
        public const double MaxExtrapolationS = 1.0;
        private const int KeptSnapshots = 8;

        private readonly object _sync = new object();
        private readonly Dictionary<ushort, RosterEntry> _roster = new Dictionary<ushort, RosterEntry>();
        private readonly Dictionary<ushort, List<(double T, SnapshotEntry E)>> _history = new Dictionary<ushort, List<(double, SnapshotEntry)>>();
        private readonly List<ReceivedEmoji> _emojis = new List<ReceivedEmoji>();
        private int _rosterVersion;

        // Snapshot timing by server tick: tick time + offset ≈ arrival time without network jitter.
        private double _tickS;
        private double _offset;
        private bool _hasOffset;

        /// <summary>Own slot (0 until welcomed).</summary>
        public ushort MySlot { get; private set; }

        public LobbyMode Mode { get; private set; }

        /// <summary>Where the server placed this rider when they (re)connected.</summary>
        public double StartDistanceM { get; private set; }

        /// <summary>Counts welcomes, so the app notices a (re)connection.</summary>
        public int WelcomeCount { get; private set; }

        /// <summary>Changes whenever the roster changes (to rebuild avatars).</summary>
        public int RosterVersion
        {
            get
            {
                lock (_sync) return _rosterVersion;
            }
        }

        /// <summary>The suggested lane of this rider.</summary>
        public float MyHomeLaneM
        {
            get
            {
                lock (_sync) return _roster.TryGetValue(MySlot, out var e) ? e.HomeLaneM : 0f;
            }
        }

        /// <summary>Applies a message from the server; <paramref name="nowS"/> is the app's clock in seconds.</summary>
        public void Apply(LiveMessage message, double nowS)
        {
            lock (_sync)
            {
                switch (message)
                {
                    case WelcomeMessage welcome:
                        MySlot = welcome.YourSlot;
                        Mode = welcome.Mode;
                        StartDistanceM = welcome.StartDistanceM;
                        WelcomeCount++;
                        _history.Clear();
                        _tickS = welcome.TickMs / 1000.0;
                        _hasOffset = false;
                        break;
                    case RosterMessage roster:
                        _roster.Clear();
                        foreach (var entry in roster.Riders) _roster[entry.Slot] = entry;
                        foreach (ushort gone in _history.Keys.Where(k => !_roster.ContainsKey(k)).ToList()) _history.Remove(gone);
                        _rosterVersion++;
                        break;
                    case SnapshotMessage snapshot:
                        // With the server's tick length, snapshots are placed on the server's even clock: the
                        // offset follows the earliest arrivals (fast down, slowly up for clock drift), so late
                        // packets do not make riders stutter. Without it (old server, tests): arrival time.
                        double t = nowS;
                        if (_tickS > 0)
                        {
                            double tick = snapshot.Tick * _tickS;
                            double offset = nowS - tick;
                            if (!_hasOffset || offset < _offset) _offset = offset;
                            else _offset += (offset - _offset) * 0.01;
                            _hasOffset = true;
                            t = tick;
                        }

                        foreach (var entry in snapshot.Riders)
                        {
                            if (!_history.TryGetValue(entry.Slot, out var list)) _history[entry.Slot] = list = new List<(double, SnapshotEntry)>();
                            if (list.Count > 0 && t <= list[list.Count - 1].T) continue; // duplicate or out of order
                            list.Add((t, entry));
                            if (list.Count > KeptSnapshots) list.RemoveAt(0);
                        }

                        break;
                    case EmojiEventMessage emoji:
                        _emojis.Add(new ReceivedEmoji { Slot = emoji.Slot, Emoji = emoji.Emoji, ReceivedS = nowS });
                        if (_emojis.Count > 32) _emojis.RemoveAt(0);
                        break;
                }
            }
        }

        /// <summary>All riders of the ride as of <paramref name="nowS"/> (own entry included, <see cref="GroupRider.IsMe"/>).</summary>
        public List<GroupRider> Riders(double nowS)
        {
            lock (_sync)
            {
                var result = new List<GroupRider>(_roster.Count);
                foreach (var entry in _roster.Values.OrderBy(e => e.Slot))
                {
                    if (!_history.TryGetValue(entry.Slot, out var list) || list.Count == 0) continue;
                    var e = Sample(list, ToSnapshotTime(nowS) - RenderDelayS);
                    result.Add(new GroupRider
                    {
                        Slot = entry.Slot,
                        RiderId = entry.RiderId,
                        DisplayName = entry.DisplayName,
                        IsMe = entry.Slot == MySlot,
                        DistanceM = e.DistanceM,
                        LaneM = e.LaneM,
                        SpeedMps = e.SpeedMps,
                        Wkg = e.Wkg,
                        CadenceRpm = e.CadenceRpm,
                        Flags = e.Flags,
                    });
                }

                return result;
            }
        }

        /// <summary>
        /// Own position from the server (Keep Together: the server decides where the rider is). False until the
        /// first snapshot after the welcome.
        /// </summary>
        public bool TryGetMine(double nowS, out GroupRider mine)
        {
            mine = default;
            foreach (var r in Riders(nowS))
            {
                if (!r.IsMe) continue;
                mine = r;
                return true;
            }

            return false;
        }

        /// <summary>The app's clock converted to the time base of the stored snapshots.</summary>
        private double ToSnapshotTime(double nowS) => _tickS > 0 && _hasOffset ? nowS - _offset : nowS;

        public string NameOf(ushort slot)
        {
            lock (_sync) return _roster.TryGetValue(slot, out var e) ? e.DisplayName : "";
        }

        /// <summary>Emojis received since <paramref name="sinceS"/>.</summary>
        public List<ReceivedEmoji> EmojisSince(double sinceS)
        {
            lock (_sync) return _emojis.Where(e => e.ReceivedS >= sinceS).ToList();
        }

        /// <summary>Forgets everything (ride left).</summary>
        public void Reset()
        {
            lock (_sync)
            {
                _roster.Clear();
                _history.Clear();
                _emojis.Clear();
                MySlot = 0;
                WelcomeCount = 0;
                _rosterVersion++;
            }
        }

        /// <summary>Interpolated entry at time <paramref name="t"/>; extrapolated at its speed after the last one.</summary>
        internal static SnapshotEntry Sample(List<(double T, SnapshotEntry E)> list, double t)
        {
            if (t <= list[0].T) return list[0].E;
            for (int i = 1; i < list.Count; i++)
            {
                if (t > list[i].T) continue;
                var (t0, a) = list[i - 1];
                var (t1, b) = list[i];
                double f = t1 > t0 ? (t - t0) / (t1 - t0) : 1;
                var e = b;
                e.DistanceM = a.DistanceM + ((b.DistanceM - a.DistanceM) * f);
                e.LaneM = (float)(a.LaneM + ((b.LaneM - a.LaneM) * f));
                e.SpeedMps = (float)(a.SpeedMps + ((b.SpeedMps - a.SpeedMps) * f));
                return e;
            }

            var last = list[list.Count - 1];
            var ahead = last.E;
            ahead.DistanceM += ahead.SpeedMps * Math.Min(MaxExtrapolationS, t - last.T);
            return ahead;
        }
    }
}
