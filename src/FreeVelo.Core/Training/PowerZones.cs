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

        /// <summary>True if the bounds are given in watts (otherwise % FTP).</summary>
        public bool UsesWatts => Zones.Count > 1 && Zones[1].Lower.Unit == TargetUnit.Watts;

        /// <summary>The 6 boundaries between the 7 zones (upper bound of zone n = lower bound of zone n+1).</summary>
        public IReadOnlyList<double> Boundaries => Zones.Skip(1).Select(z => z.Lower.Value).ToList();

        /// <summary>
        /// Moves the boundary between zone <paramref name="index"/>+1 and +2 (0-based boundary index), keeping
        /// at least 1 unit to the neighbouring boundaries. Returns the value applied.
        /// </summary>
        public double SetBoundary(int index, double value)
        {
            if (index < 0 || index >= Zones.Count - 1) return 0;
            double min = index == 0 ? 1 : Zones[index].Lower.Value + 1;
            double max = index + 2 < Zones.Count ? Zones[index + 2].Lower.Value - 1 : (UsesWatts ? 3000 : 400);
            value = System.Math.Round(System.Math.Max(min, System.Math.Min(max, value)));
            var unit = Zones[index + 1].Lower.Unit;
            var bound = new PowerTarget(value, unit);
            Zones[index].Upper = bound;
            Zones[index + 1].Lower = bound;
            return value;
        }

        /// <summary>Switches all bounds to watts or % FTP (rounded), keeping the zones at <paramref name="ftpW"/>.</summary>
        public void SetUnit(bool watts, int ftpW)
        {
            if (watts == UsesWatts) return;
            foreach (var z in Zones)
            {
                z.Lower = Convert(z.Lower);
                if (z.Upper.HasValue) z.Upper = Convert(z.Upper.Value);
            }

            PowerTarget Convert(PowerTarget t) => watts
                ? PowerTarget.Watts(System.Math.Round(t.ToWatts(ftpW)))
                : PowerTarget.Percent(System.Math.Round(t.ToPercent(ftpW)));
        }

        /// <summary>Typical intensity of a zone in % FTP (used to add blocks in the editor): its middle, for zone 1 just below its top.</summary>
        public double TypicalPercent(int zoneIndex, int ftpW)
        {
            var z = Zones[zoneIndex];
            double lower = z.Lower.ToPercent(ftpW);
            double upper = z.Upper?.ToPercent(ftpW) ?? (lower * 1.2);
            return System.Math.Round(zoneIndex == 0 ? upper - 5 : (lower + upper) / 2);
        }

        /// <summary>Compact text form for settings storage: "%;55;75;90;105;120;150" or "W;…".</summary>
        public string Serialize() =>
            (UsesWatts ? "W;" : "%;") + string.Join(";", Boundaries.Select(b => b.ToString("0", System.Globalization.CultureInfo.InvariantCulture)));

        /// <summary>Reads <see cref="Serialize"/> output; invalid text gives the Coggan default.</summary>
        public static PowerZoneModel Parse(string? text)
        {
            var model = Coggan();
            if (string.IsNullOrWhiteSpace(text)) return model;
            var parts = text!.Split(';');
            if (parts.Length != model.Zones.Count || (parts[0] != "%" && parts[0] != "W")) return model;
            var values = new List<double>();
            for (int i = 1; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return model;
                if (values.Count > 0 && v <= values[values.Count - 1]) return model;
                if (v <= 0) return model;
                values.Add(v);
            }

            var unit = parts[0] == "W" ? TargetUnit.Watts : TargetUnit.PercentFtp;
            for (int i = 0; i < values.Count; i++)
            {
                var bound = new PowerTarget(values[i], unit);
                model.Zones[i].Upper = bound;
                model.Zones[i + 1].Lower = bound;
            }

            if (unit == TargetUnit.Watts) model.Zones[0].Lower = PowerTarget.Watts(0);
            return model;
        }

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
