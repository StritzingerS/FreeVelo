using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FreeVelo.Shared.Api;

namespace FreeVelo.Shared.Live
{
    // Realtime messages of a group ride over a WebSocket (binary frames). Hand-written little-endian encoding:
    // small (a snapshot of 8 riders is about 220 bytes), no external library, works with Unity IL2CPP.
    // Each frame: 1 byte message type, then the fields below in order.

    public enum LiveMessageType : byte
    {
        // app → server
        RiderState = 1,
        Emoji = 2,

        // server → app
        Welcome = 10,
        Roster = 11,
        Snapshot = 12,
        EmojiEvent = 13,
        Bye = 14,
    }

    /// <summary>Emoji reactions (shown above the avatar and in a feed).</summary>
    public enum Emoji : byte
    {
        ThumbsUp = 1,
        Fire = 2,
        Heart = 3,
        Bolt = 4,
        Sweat = 5,
        Wave = 6,
    }

    [Flags]
    public enum RiderFlags : byte
    {
        None = 0,

        /// <summary>Pedaling with at least 0.5 W/kg: counts for the group speed.</summary>
        Active = 1,

        /// <summary>Stopped pedaling: towed along at the back (Keep Together).</summary>
        Towed = 2,

        /// <summary>The rider's app is connected right now.</summary>
        Connected = 4,
    }

    public abstract class LiveMessage
    {
        public abstract LiveMessageType Type { get; }
    }

    /// <summary>App → server, about 4 times a second.</summary>
    public sealed class RiderStateMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.RiderState;

        /// <summary>Own position on the route (used in Free mode; Keep Together positions come from the server).</summary>
        public double DistanceM;

        /// <summary>Sideways position on the road, metres from the centre (right is positive).</summary>
        public float LaneM;

        public float SpeedMps;
        public float Wkg;
        public byte CadenceRpm;
    }

    /// <summary>App → server: an emoji reaction.</summary>
    public sealed class EmojiMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.Emoji;

        public Emoji Emoji;
    }

    /// <summary>Server → app, once after connecting.</summary>
    public sealed class WelcomeMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.Welcome;

        /// <summary>This rider's short number in rosters and snapshots.</summary>
        public ushort YourSlot;

        public LobbyMode Mode;

        /// <summary>Where to start (the group's current position, for joining mid-ride).</summary>
        public double StartDistanceM;
    }

    public sealed class RosterEntry
    {
        public ushort Slot;
        public string RiderId = "";
        public string DisplayName = "";

        /// <summary>Suggested lane, so riders do not overlap.</summary>
        public float HomeLaneM;
    }

    /// <summary>Server → app: who is in the ride (sent when it changes).</summary>
    public sealed class RosterMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.Roster;

        public List<RosterEntry> Riders = new List<RosterEntry>();
    }

    public struct SnapshotEntry
    {
        public ushort Slot;
        public double DistanceM;
        public float LaneM;
        public float SpeedMps;
        public float Wkg;
        public byte CadenceRpm;
        public RiderFlags Flags;
    }

    /// <summary>Server → app, 4 times a second: everyone's position.</summary>
    public sealed class SnapshotMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.Snapshot;

        /// <summary>Increases by one per server tick (the app interpolates between snapshots).</summary>
        public uint Tick;

        /// <summary>Keep Together: the anchor (group centre); Free mode: 0.</summary>
        public double AnchorDistanceM;

        public float AnchorSpeedMps;

        public List<SnapshotEntry> Riders = new List<SnapshotEntry>();
    }

    /// <summary>Server → app: a rider reacted.</summary>
    public sealed class EmojiEventMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.EmojiEvent;

        public ushort Slot;
        public Emoji Emoji;
    }

    /// <summary>
    /// Server → app, right before the server ends the connection: why (the same codes as the WebSocket close status,
    /// e.g. 4410 = left the ride). Sent as a message too, because some proxies drop or delay close frames.
    /// </summary>
    public sealed class ByeMessage : LiveMessage
    {
        public override LiveMessageType Type => LiveMessageType.Bye;

        public ushort Code;
    }

    /// <summary>Encodes and decodes live messages (binary, little-endian).</summary>
    public static class LiveCodec
    {
        /// <summary>Larger frames are rejected (a full 20-rider roster is far below this).</summary>
        public const int MaxFrameBytes = 4096;

        public static byte[] Encode(LiveMessage message)
        {
            using var stream = new MemoryStream(64);
            using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)message.Type);
                switch (message)
                {
                    case RiderStateMessage m:
                        w.Write(m.DistanceM);
                        w.Write(m.LaneM);
                        w.Write(m.SpeedMps);
                        w.Write(m.Wkg);
                        w.Write(m.CadenceRpm);
                        break;
                    case EmojiMessage m:
                        w.Write((byte)m.Emoji);
                        break;
                    case WelcomeMessage m:
                        w.Write(m.YourSlot);
                        w.Write((byte)m.Mode);
                        w.Write(m.StartDistanceM);
                        break;
                    case RosterMessage m:
                        w.Write((byte)m.Riders.Count);
                        foreach (var r in m.Riders)
                        {
                            w.Write(r.Slot);
                            w.Write(r.RiderId);
                            w.Write(r.DisplayName);
                            w.Write(r.HomeLaneM);
                        }

                        break;
                    case SnapshotMessage m:
                        w.Write(m.Tick);
                        w.Write(m.AnchorDistanceM);
                        w.Write(m.AnchorSpeedMps);
                        w.Write((byte)m.Riders.Count);
                        foreach (var r in m.Riders)
                        {
                            w.Write(r.Slot);
                            w.Write(r.DistanceM);
                            w.Write(r.LaneM);
                            w.Write(r.SpeedMps);
                            w.Write(r.Wkg);
                            w.Write(r.CadenceRpm);
                            w.Write((byte)r.Flags);
                        }

                        break;
                    case EmojiEventMessage m:
                        w.Write(m.Slot);
                        w.Write((byte)m.Emoji);
                        break;
                    case ByeMessage m:
                        w.Write(m.Code);
                        break;
                    default:
                        throw new ArgumentException("Unknown message " + message.GetType().Name);
                }
            }

            return stream.ToArray();
        }

        /// <summary>Decodes one frame; throws <see cref="FormatException"/> for broken or unknown frames.</summary>
        public static LiveMessage Decode(byte[] frame, int count)
        {
            if (count < 1 || count > MaxFrameBytes || count > frame.Length) throw new FormatException("Bad frame length");
            try
            {
                using var r = new BinaryReader(new MemoryStream(frame, 0, count, writable: false), Encoding.UTF8);
                var type = (LiveMessageType)r.ReadByte();
                LiveMessage message;
                switch (type)
                {
                    case LiveMessageType.RiderState:
                        message = new RiderStateMessage
                        {
                            DistanceM = Finite(r.ReadDouble()),
                            LaneM = Finite(r.ReadSingle()),
                            SpeedMps = Finite(r.ReadSingle()),
                            Wkg = Finite(r.ReadSingle()),
                            CadenceRpm = r.ReadByte(),
                        };
                        break;
                    case LiveMessageType.Emoji:
                        message = new EmojiMessage { Emoji = (Emoji)r.ReadByte() };
                        break;
                    case LiveMessageType.Welcome:
                        message = new WelcomeMessage { YourSlot = r.ReadUInt16(), Mode = (LobbyMode)r.ReadByte(), StartDistanceM = Finite(r.ReadDouble()) };
                        break;
                    case LiveMessageType.Roster:
                        var roster = new RosterMessage();
                        int riders = r.ReadByte();
                        for (int i = 0; i < riders; i++)
                        {
                            roster.Riders.Add(new RosterEntry { Slot = r.ReadUInt16(), RiderId = r.ReadString(), DisplayName = r.ReadString(), HomeLaneM = Finite(r.ReadSingle()) });
                        }

                        message = roster;
                        break;
                    case LiveMessageType.Snapshot:
                        var snapshot = new SnapshotMessage { Tick = r.ReadUInt32(), AnchorDistanceM = Finite(r.ReadDouble()), AnchorSpeedMps = Finite(r.ReadSingle()) };
                        int count2 = r.ReadByte();
                        for (int i = 0; i < count2; i++)
                        {
                            snapshot.Riders.Add(new SnapshotEntry
                            {
                                Slot = r.ReadUInt16(),
                                DistanceM = Finite(r.ReadDouble()),
                                LaneM = Finite(r.ReadSingle()),
                                SpeedMps = Finite(r.ReadSingle()),
                                Wkg = Finite(r.ReadSingle()),
                                CadenceRpm = r.ReadByte(),
                                Flags = (RiderFlags)r.ReadByte(),
                            });
                        }

                        message = snapshot;
                        break;
                    case LiveMessageType.EmojiEvent:
                        message = new EmojiEventMessage { Slot = r.ReadUInt16(), Emoji = (Emoji)r.ReadByte() };
                        break;
                    case LiveMessageType.Bye:
                        message = new ByeMessage { Code = r.ReadUInt16() };
                        break;
                    default:
                        throw new FormatException("Unknown message type " + (int)type);
                }

                if (r.BaseStream.Position != count) throw new FormatException("Trailing bytes");
                return message;
            }
            catch (EndOfStreamException)
            {
                throw new FormatException("Truncated frame");
            }
        }

        public static LiveMessage Decode(byte[] frame) => Decode(frame, frame.Length);

        private static double Finite(double v) => double.IsNaN(v) || double.IsInfinity(v) ? throw new FormatException("Not a number") : v;

        private static float Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? throw new FormatException("Not a number") : v;
    }
}
