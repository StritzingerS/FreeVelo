using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Devices.Zwift
{
    /// <summary>
    /// Virtual shifting built into the trainer ("Zwift Hub" protocol, community-documented – Zwift has not published
    /// it). Trainers with current firmware (Zwift Hub, newer Wahoo KICKR, Elite, Tacx Neo 2/2T/3M, JetBlack, Van
    /// Rysel …) offer the Zwift service (<see cref="ZwiftController.RideService"/> FC82, or the older
    /// <see cref="ZwiftController.ClickService"/>) next to their trainer service. FreeVelo then sends the road and
    /// the virtual gear ratio; the trainer works out the resistance itself.
    /// <code>
    /// handshake  "RideOn" 02 01                        → SyncRx (00000003)
    /// command    04 + HubCommand (protobuf)           → SyncRx
    ///            HubCommand  4: Simulation { 1 wind m/s×100 (sint), 2 grade %×100 (sint), 3 CdA×10000, 4 Crr×100000 }
    ///                        5: Physical   { 2 gear ratio×10000, 4 bike kg×100, 5 rider kg×100 }
    /// confirm    00 + HubRequest { 1: 520 } = 00 08 88 04   after a gear change
    /// </code>
    /// Byte patterns match those captured from Zwift (see the tests). Gear commands only take effect after a grade has
    /// been sent once. ERG targets keep going over FTMS / FE-C.
    /// </summary>
    public static class ZwiftTrainer
    {
        /// <summary>The trainer's own gear ratio when the bike is in <see cref="ReferenceRatio"/>: 42×14 (as Zwift assumes).</summary>
        public const double ReferenceRatio = 42.0 / 14.0;

        public static readonly byte[] Handshake = ZwiftController.RideOn.Concat(new byte[] { 0x02, 0x01 }).ToArray();

        /// <summary>Asks the trainer to confirm the gear ratio (sent after each gear change).</summary>
        public static readonly byte[] ConfirmGear = { 0x00, 0x08, 0x88, 0x04 };

        /// <summary>True for a trainer (FTMS or Tacx FE-C) that also offers the Zwift service.</summary>
        public static bool SupportsShifting(ICollection<Guid> services, Guid ftmsService, Guid fecService) =>
            (services.Contains(ZwiftController.RideService) || services.Contains(ZwiftController.ClickService))
            && (services.Contains(ftmsService) || services.Contains(fecService));

        /// <summary>The road: grade (%), rolling resistance and wind resistance (½·ρ·CdA in kg/m, e.g. 0.51).</summary>
        public static byte[] Simulation(double gradePercent, double rollingResistance, double windResistanceKgPerM, double windMps = 0)
        {
            var sim = new List<byte>();
            Field(sim, 1, ZigZag((long)Math.Round(windMps * 100)));
            Field(sim, 2, ZigZag((long)Math.Round(gradePercent * 100)));
            Field(sim, 3, (ulong)Math.Round(Math.Max(0, windResistanceKgPerM) * 10000));
            Field(sim, 4, (ulong)Math.Round(Math.Max(0, rollingResistance) * 100000));
            return Command(4, sim);
        }

        /// <summary>
        /// The virtual gear: <paramref name="gearFactor"/> = virtual ratio / ratio of the gear the bike is in (1 = no
        /// change), sent as a ratio relative to <see cref="ReferenceRatio"/>.
        /// </summary>
        public static byte[] Gear(double gearFactor) => Physical((uint)Math.Round(10000 * ReferenceRatio * Math.Max(0.1, gearFactor)), null, null);

        /// <summary>Gear ratio (×10000) and, once after connecting, bike and rider weight.</summary>
        public static byte[] Physical(uint gearRatioX10000, double? bikeKg, double? riderKg)
        {
            var physical = new List<byte>();
            Field(physical, 2, gearRatioX10000);
            if (bikeKg.HasValue) Field(physical, 4, (ulong)Math.Round(bikeKg.Value * 100));
            if (riderKg.HasValue) Field(physical, 5, (ulong)Math.Round(riderKg.Value * 100));
            return Command(5, physical);
        }

        /// <summary>04 + HubCommand with one embedded message in <paramref name="field"/>.</summary>
        private static byte[] Command(int field, List<byte> message)
        {
            var bytes = new List<byte> { 0x04 };
            Varint(bytes, (ulong)((field << 3) | 2));
            Varint(bytes, (ulong)message.Count);
            bytes.AddRange(message);
            return bytes.ToArray();
        }

        private static void Field(List<byte> bytes, int field, ulong value)
        {
            Varint(bytes, (ulong)(field << 3));
            Varint(bytes, value);
        }

        private static ulong ZigZag(long v) => (ulong)((v << 1) ^ (v >> 63));

        private static void Varint(List<byte> bytes, ulong value)
        {
            while (value >= 0x80)
            {
                bytes.Add((byte)(value | 0x80));
                value >>= 7;
            }

            bytes.Add((byte)value);
        }
    }
}
