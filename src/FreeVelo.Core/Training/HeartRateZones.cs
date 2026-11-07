using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Training
{
    /// <summary>What the heart rate zones are based on.</summary>
    public enum HeartRateZoneBasis
    {
        /// <summary>5 zones in % of the maximum heart rate (50–60–70–80–90–100 %).</summary>
        MaxHr = 0,

        /// <summary>7 zones in % of the lactate threshold heart rate (Friel, cycling).</summary>
        Lthr = 1,
    }

    public sealed class HeartRateZone
    {
        public HeartRateZone(int number, LocalizedText name, int lowerBpm, int? upperBpm, string colorHex)
        {
            Number = number;
            Name = name;
            LowerBpm = lowerBpm;
            UpperBpm = upperBpm;
            ColorHex = colorHex;
        }

        public int Number { get; }
        public LocalizedText Name { get; }

        /// <summary>First beat of the zone.</summary>
        public int LowerBpm { get; }

        /// <summary>First beat of the next zone (null for the top zone).</summary>
        public int? UpperBpm { get; }

        public string ColorHex { get; }
    }

    /// <summary>Heart rate zones from max HR or LTHR (concept 6.3: for display and analysis).</summary>
    public sealed class HeartRateZoneModel
    {
        private HeartRateZoneModel(IReadOnlyList<HeartRateZone> zones) => Zones = zones;

        public IReadOnlyList<HeartRateZone> Zones { get; }

        public static HeartRateZoneModel Create(HeartRateZoneBasis basis, int maxHr, int lthr)
        {
            var zones = new List<HeartRateZone>();
            if (basis == HeartRateZoneBasis.Lthr)
            {
                // Friel: Z1 < 81 %, Z2 81–89 %, Z3 90–93 %, Z4 94–99 %, Z5a 100–102 %, Z5b 103–106 %, Z5c > 106 % of LTHR
                var bounds = new[] { 0, 81, 90, 94, 100, 103, 106 };
                var names = new[]
                {
                    ("Recovery", "Erholung"), ("Aerobic", "Aerob"), ("Tempo", "Tempo"), ("Sub-threshold", "Unter der Schwelle"),
                    ("Threshold", "Schwelle"), ("Aerobic capacity", "Aerobe Kapazität"), ("Anaerobic", "Anaerob"),
                };
                var colors = new[] { "#8A97A6", "#3B82F6", "#22C55E", "#EAB308", "#F97316", "#EF4444", "#A855F7" };
                for (int i = 0; i < bounds.Length; i++)
                {
                    int lower = i == 0 ? 0 : (int)Math.Round(lthr * bounds[i] / 100.0);
                    int? upper = i < bounds.Length - 1 ? (int?)Math.Round(lthr * bounds[i + 1] / 100.0) : null;
                    zones.Add(new HeartRateZone(i + 1, new LocalizedText(names[i].Item1, names[i].Item2), lower, upper, colors[i]));
                }
            }
            else
            {
                var bounds = new[] { 0, 60, 70, 80, 90 };
                var names = new[] { ("Very light", "Sehr leicht"), ("Light", "Leicht"), ("Moderate", "Moderat"), ("Hard", "Hart"), ("Maximum", "Maximal") };
                var colors = new[] { "#8A97A6", "#3B82F6", "#22C55E", "#F97316", "#EF4444" };
                for (int i = 0; i < bounds.Length; i++)
                {
                    int lower = i == 0 ? 0 : (int)Math.Round(maxHr * bounds[i] / 100.0);
                    int? upper = i < bounds.Length - 1 ? (int?)Math.Round(maxHr * bounds[i + 1] / 100.0) : null;
                    zones.Add(new HeartRateZone(i + 1, new LocalizedText(names[i].Item1, names[i].Item2), lower, upper, colors[i]));
                }
            }

            return new HeartRateZoneModel(zones);
        }

        public HeartRateZone ZoneFor(int bpm) => Zones.LastOrDefault(z => bpm >= z.LowerBpm) ?? Zones[0];

        /// <summary>Seconds per zone (zone number → seconds); samples without heart rate are not counted.</summary>
        public IReadOnlyDictionary<int, int> TimeInZones(IEnumerable<int?> heartRates)
        {
            var result = Zones.ToDictionary(z => z.Number, _ => 0);
            foreach (var hr in heartRates)
            {
                if (hr > 0) result[ZoneFor(hr.Value).Number]++;
            }

            return result;
        }

        /// <summary>A common estimate when the maximum is unknown: 220 − age is unreliable, so FreeVelo just starts at 185.</summary>
        public const int DefaultMaxHr = 185;
        public const int DefaultLthr = 165;
    }
}
