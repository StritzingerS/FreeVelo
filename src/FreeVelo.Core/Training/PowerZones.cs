using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Training
{
    /// <summary>A power zone; the lower bound is inclusive, the upper bound exclusive (null = open).</summary>
    public sealed class PowerZone
    {
        public PowerZone(int number, LocalizedText name, PowerTarget lower, PowerTarget? upper, string colorHex)
        {
            Number = number;
            Name = name;
            Lower = lower;
            Upper = upper;
            ColorHex = colorHex;
        }

        public int Number { get; }

        public LocalizedText Name { get; set; }

        /// <summary>Lower bound, as % FTP or absolute watts (user setting).</summary>
        public PowerTarget Lower { get; set; }

        public PowerTarget? Upper { get; set; }

        public string ColorHex { get; set; }

        public bool Contains(double watts, int ftpW) =>
            watts >= Lower.ToWatts(ftpW) && (Upper == null || watts < Upper.Value.ToWatts(ftpW));
    }

    /// <summary>Power zone definition (editable in settings).</summary>
    public sealed class PowerZoneModel
    {
        public PowerZoneModel(IEnumerable<PowerZone> zones)
        {
            Zones = zones.OrderBy(z => z.Number).ToList();
        }

        public IReadOnlyList<PowerZone> Zones { get; }

        /// <summary>Default: Coggan's 7 power zones.</summary>
        public static PowerZoneModel Coggan() => new PowerZoneModel(new[]
        {
            Zone(1, "Active Recovery", "Regeneration", 0, 55, "#7F8C8D"),
            Zone(2, "Endurance", "Grundlage", 55, 75, "#2E86DE"),
            Zone(3, "Tempo", "Tempo", 75, 90, "#27AE60"),
            Zone(4, "Threshold", "Schwelle", 90, 105, "#F1C40F"),
            Zone(5, "VO2max", "VO2max", 105, 120, "#E67E22"),
            Zone(6, "Anaerobic", "Anaerob", 120, 150, "#E74C3C"),
            Zone(7, "Neuromuscular", "Neuromuskulär", 150, null, "#8E44AD"),
        });

        /// <summary>Zone for a power value; values below zone 1 map to zone 1.</summary>
        public PowerZone ZoneFor(double watts, int ftpW) =>
            Zones.LastOrDefault(z => watts >= z.Lower.ToWatts(ftpW)) ?? Zones[0];

        private static PowerZone Zone(int n, string en, string de, double lowerPct, double? upperPct, string color) =>
            new PowerZone(
                n,
                new LocalizedText(en, de),
                PowerTarget.Percent(lowerPct),
                upperPct.HasValue ? PowerTarget.Percent(upperPct.Value) : (PowerTarget?)null,
                color);
    }
}
