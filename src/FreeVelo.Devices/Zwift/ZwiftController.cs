using System;
using System.Collections.Generic;
using System.Text;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.Zwift
{
    /// <summary>Buttons of the Zwift Click / Click V2 / Play / Ride controllers.</summary>
    public enum ZwiftButton
    {
        Left,
        Up,
        Right,
        Down,
        A,
        B,
        Y,
        Z,

        /// <summary>"−" on the Click V2 left unit / Ride left shift-up.</summary>
        ShiftUpLeft,
        ShiftDownLeft,

        /// <summary>"+" on the Click V2 right unit / Ride right shift-up.</summary>
        ShiftUpRight,
        ShiftDownRight,
        PowerUpLeft,
        OnOffLeft,
        PowerUpRight,
        OnOffRight,

        /// <summary>Zwift Click (1st gen) "+" and "−".</summary>
        ClickPlus,
        ClickMinus,
    }

    /// <summary>What a controller reported.</summary>
    public enum ZwiftControllerState
    {
        Connecting,

        /// <summary>Handshake answered: buttons arrive.</summary>
        Ready,

        /// <summary>
        /// The unit answered with a challenge: it is locked to Zwift (the Click V2 left unit, until it has been unlocked
        /// in the Zwift app within about the last 24 hours). Its buttons stay silent.
        /// </summary>
        Locked,
    }

    /// <summary>
    /// The Zwift controller protocol as documented by the community (OpenBikeControl, qdomyos-zwift): a custom GATT
    /// service, a "RideOn" handshake written to SYNC_RX, button states as protobuf messages on ASYNC. The unencrypted
    /// variant is used. Kept separate from the rest because Zwift may change it with a firmware update.
    /// </summary>
    public static class ZwiftController
    {
        /// <summary>Zwift Click V2 / Ride / newer Play firmware (16-bit 0xFC82).</summary>
        public static readonly Guid RideService = GattUuids.FromShort(0xFC82);

        /// <summary>Zwift Click (1st gen) / Play.</summary>
        public static readonly Guid ClickService = new Guid("00000001-19ca-4651-86e5-fa29dcdd09d1");

        /// <summary>Notifications: button states, battery.</summary>
        public static readonly Guid AsyncChar = new Guid("00000002-19ca-4651-86e5-fa29dcdd09d1");

        /// <summary>Commands to the controller (handshake).</summary>
        public static readonly Guid SyncRxChar = new Guid("00000003-19ca-4651-86e5-fa29dcdd09d1");

        /// <summary>Indications: handshake answer ("RideOn…" when unlocked, 0xFF… challenge when locked).</summary>
        public static readonly Guid SyncTxChar = new Guid("00000004-19ca-4651-86e5-fa29dcdd09d1");

        public static readonly byte[] RideOn = Encoding.ASCII.GetBytes("RideOn");

        /// <summary>Start command of the Click V2: "RideOn" + 02 03 (Click 1st gen: 01 03, sent the same way).</summary>
        public static byte[] StartCommand(bool clickV2) => Concat(RideOn, clickV2 ? new byte[] { 0x02, 0x03 } : new byte[] { 0x01, 0x03 });

        /// <summary>The Click V2 right unit falls asleep after about a minute without traffic: repeat the handshake.</summary>
        public const long KeepAliveMs = 3000;

        public const byte ControllerNotification = 0x23; // Click V2 / Ride / Play (new firmware)
        public const byte ClickNotification = 0x37;      // Click 1st gen
        public const byte PlayNotification = 0x07;
        public const byte BatteryNotification = 0x19;

        /// <summary>Bit masks of the 0x23 button map; a bit is 0 while the button is pressed.</summary>
        private static readonly (uint Mask, ZwiftButton Button)[] RideButtons =
        {
            (0x00001, ZwiftButton.Left),
            (0x00002, ZwiftButton.Up),
            (0x00004, ZwiftButton.Right),
            (0x00008, ZwiftButton.Down),
            (0x00010, ZwiftButton.A),
            (0x00020, ZwiftButton.B),
            (0x00040, ZwiftButton.Y),
            (0x00100, ZwiftButton.Z),
            (0x00200, ZwiftButton.ShiftUpLeft),
            (0x00400, ZwiftButton.ShiftDownLeft),
            (0x00800, ZwiftButton.PowerUpLeft),
            (0x01000, ZwiftButton.OnOffLeft),
            (0x02000, ZwiftButton.ShiftUpRight),
            (0x04000, ZwiftButton.ShiftDownRight),
            (0x10000, ZwiftButton.PowerUpRight),
            (0x20000, ZwiftButton.OnOffRight),
        };

        public static bool IsController(IEnumerable<Guid> services)
        {
            foreach (var s in services)
            {
                if (s == RideService || s == ClickService) return true;
            }

            return false;
        }

        /// <summary>Buttons held down in a notification on <see cref="AsyncChar"/> (null if it is not a button message).</summary>
        public static HashSet<ZwiftButton>? PressedButtons(byte[] data)
        {
            if (data.Length < 2) return null;
            var fields = Varints(data, 1);
            var pressed = new HashSet<ZwiftButton>();
            switch (data[0])
            {
                case ControllerNotification:
                case PlayNotification:
                {
                    if (!fields.TryGetValue(1, out ulong map)) return null;
                    foreach (var (mask, button) in RideButtons)
                    {
                        if ((map & mask) == 0) pressed.Add(button);
                    }

                    return pressed;
                }

                case ClickNotification:
                    // Two fields: 1 = plus, 2 = minus; 0 = pressed, 1 = released.
                    if (fields.TryGetValue(1, out ulong plus) && plus == 0) pressed.Add(ZwiftButton.ClickPlus);
                    if (fields.TryGetValue(2, out ulong minus) && minus == 0) pressed.Add(ZwiftButton.ClickMinus);
                    return pressed;
                default:
                    return null;
            }
        }

        /// <summary>Battery level (%) from a 0x19 message, else null.</summary>
        public static int? Battery(byte[] data)
        {
            if (data.Length < 2 || data[0] != BatteryNotification) return null;
            var fields = Varints(data, 1);
            if (fields.TryGetValue(2, out ulong level) && level <= 100) return (int)level;
            if (fields.TryGetValue(1, out level) && level <= 100) return (int)level;
            return null;
        }

        /// <summary>Handshake answer on <see cref="SyncTxChar"/>.</summary>
        public static ZwiftControllerState? HandshakeAnswer(byte[] data)
        {
            if (data.Length == 0) return null;
            if (data[0] == (byte)'R') return ZwiftControllerState.Ready;
            if (data[0] == 0xFF) return ZwiftControllerState.Locked;
            return null;
        }

        /// <summary>Top-level varint fields of a protobuf message (other wire types are skipped).</summary>
        internal static Dictionary<int, ulong> Varints(byte[] data, int offset)
        {
            var result = new Dictionary<int, ulong>();
            int i = offset;
            while (i < data.Length)
            {
                ulong key = ReadVarint(data, ref i);
                int field = (int)(key >> 3);
                int wire = (int)(key & 7);
                switch (wire)
                {
                    case 0:
                        result[field] = ReadVarint(data, ref i);
                        break;
                    case 1:
                        i += 8;
                        break;
                    case 2:
                        i += (int)ReadVarint(data, ref i);
                        break;
                    case 5:
                        i += 4;
                        break;
                    default:
                        return result; // malformed
                }
            }

            return result;
        }

        private static ulong ReadVarint(byte[] data, ref int i)
        {
            ulong value = 0;
            for (int shift = 0; i < data.Length && shift < 64; shift += 7)
            {
                byte b = data[i++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
            }

            return value;
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
