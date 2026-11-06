using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.Workouts
{
    /// <summary>Workouts shipped with FreeVelo, based on common training recommendations.</summary>
    public static class BuiltInWorkouts
    {
        public static IReadOnlyList<Workout> All { get; } = new[]
        {
            Recovery(),
            Endurance(60),
            Endurance(90),
            Tempo2x20(),
            SweetSpot3x15(),
            Threshold2x20(),
            OverUnders(),
            Vo2Max4x4(),
            Vo2Max5x5(),
            FortyTwenties(),
            Sprints(),
        };

        public static Workout? Find(string id) => All.FirstOrDefault(w => w.Id == id);

        // ------------------------------------------------------------------ helpers

        internal static SteadyBlock Steady(int seconds, double pct, string? cueEn = null, string? cueDe = null, CadenceRange? cadence = null)
        {
            var block = new SteadyBlock(seconds, PowerTarget.Percent(pct)) { Cadence = cadence };
            if (cueEn != null) block.Cues.Add(new TextCue(0, new LocalizedText(cueEn, cueDe)));
            return block;
        }

        internal static RampBlock Ramp(int seconds, double fromPct, double toPct) =>
            new RampBlock(seconds, PowerTarget.Percent(fromPct), PowerTarget.Percent(toPct));

        internal static RepeatBlock Repeat(int count, params WorkoutBlock[] blocks) => new RepeatBlock(count, blocks);

        internal static WorkoutBlock[] WarmUp(int minutes = 10) => new WorkoutBlock[]
        {
            Ramp((minutes - 3) * 60, 45, 70),
            Steady(60, 90, "Short activation", "Kurze Aktivierung"),
            Steady(120, 55),
        };

        internal static WorkoutBlock CoolDown(int minutes = 10) => Ramp(minutes * 60, 60, 40);

        private static Workout Create(string id, string en, string de, WorkoutCategory category, string descEn, string descDe, params WorkoutBlock[] blocks)
        {
            var w = new Workout(id, new LocalizedText(en, de))
            {
                Category = category,
                Description = new LocalizedText(descEn, descDe),
                Author = "FreeVelo",
            };
            w.Blocks.AddRange(blocks);
            return w;
        }

        private static WorkoutBlock[] Concat(params IEnumerable<WorkoutBlock>[] parts) => parts.SelectMany(p => p).ToArray();

        // ------------------------------------------------------------------ workouts

        public static Workout Recovery() => Create(
            "recovery-40", "Recovery 40", "Regeneration 40", WorkoutCategory.Recovery,
            "40 minutes very easy spinning to recover.", "40 Minuten sehr locker kurbeln zur Erholung.",
            Ramp(300, 40, 50),
            Steady(30 * 60, 50, "Easy spin, high cadence", "Locker, hohe Trittfrequenz", new CadenceRange(85, 100)),
            Ramp(300, 50, 40));

        public static Workout Endurance(int minutes) => Create(
            $"endurance-{minutes}", $"Endurance {minutes}", $"Grundlage {minutes}", WorkoutCategory.Endurance,
            $"{minutes} minutes in zone 2 to build aerobic base.", $"{minutes} Minuten in Zone 2 für die Grundlagenausdauer.",
            Ramp(600, 45, 65),
            Steady((minutes - 15) * 60, 68),
            Ramp(300, 60, 45));

        public static Workout Tempo2x20() => Create(
            "tempo-2x20", "Tempo 2×20", "Tempo 2×20", WorkoutCategory.Tempo,
            "2 × 20 min at 80 % FTP.", "2 × 20 min bei 80 % FTP.",
            Concat(WarmUp(), new WorkoutBlock[]
            {
                Repeat(2, Steady(20 * 60, 80, "Tempo – steady and controlled", "Tempo – gleichmäßig und kontrolliert"), Steady(5 * 60, 55)),
                CoolDown(),
            }));

        public static Workout SweetSpot3x15() => Create(
            "sweetspot-3x15", "Sweet Spot 3×15", "Sweet Spot 3×15", WorkoutCategory.SweetSpot,
            "3 × 15 min at 90 % FTP – high training effect, manageable fatigue.",
            "3 × 15 min bei 90 % FTP – hoher Trainingseffekt bei moderater Ermüdung.",
            Concat(WarmUp(), new WorkoutBlock[]
            {
                Repeat(3, Steady(15 * 60, 90, "Sweet spot", "Sweet Spot"), Steady(5 * 60, 55)),
                CoolDown(),
            }));

        public static Workout Threshold2x20() => Create(
            "threshold-2x20", "Threshold 2×20", "Schwelle 2×20", WorkoutCategory.Threshold,
            "The classic: 2 × 20 min at 95–100 % FTP.", "Der Klassiker: 2 × 20 min bei 95–100 % FTP.",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(2, Steady(20 * 60, 97, "Threshold – find your rhythm", "Schwelle – finde deinen Rhythmus"), Steady(5 * 60, 50)),
                CoolDown(),
            }));

        public static Workout OverUnders() => Create(
            "over-unders-3x10", "Over-Unders 3×10", "Over-Unders 3×10", WorkoutCategory.Threshold,
            "3 × 10 min alternating 2 min at 95 % and 1 min at 105 % FTP.",
            "3 × 10 min im Wechsel 2 min bei 95 % und 1 min bei 105 % FTP.",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(
                    3,
                    Steady(120, 95, "Under", "Unter"),
                    Steady(60, 105, "Over!", "Über!"),
                    Steady(120, 95),
                    Steady(60, 105),
                    Steady(120, 95),
                    Steady(60, 105),
                    Steady(60, 95),
                    Steady(5 * 60, 50)),
                CoolDown(),
            }));

        public static Workout Vo2Max4x4() => Create(
            "vo2max-4x4", "4×4 VO2max", "4×4 VO2max", WorkoutCategory.Vo2Max,
            "4 × 4 min at 110 % FTP with 3 min recovery – the well-known 4×4 protocol.",
            "4 × 4 min bei 110 % FTP mit 3 min Erholung – das bekannte 4×4-Protokoll.",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(4, Steady(240, 110, "Go hard!", "Jetzt hart!", new CadenceRange(90, 105)), Steady(180, 50, "Recover", "Erholen")),
                CoolDown(),
            }));

        public static Workout Vo2Max5x5() => Create(
            "vo2max-5x5", "VO2max 5×5", "VO2max 5×5", WorkoutCategory.Vo2Max,
            "5 × 5 min at 108 % FTP with 5 min recovery.", "5 × 5 min bei 108 % FTP mit 5 min Erholung.",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(5, Steady(300, 108, "VO2max", "VO2max", new CadenceRange(90, 105)), Steady(300, 50)),
                CoolDown(),
            }));

        public static Workout FortyTwenties() => Create(
            "40-20s", "40/20s", "40/20s", WorkoutCategory.Vo2Max,
            "3 sets of 10 × (40 s at 120 % / 20 s at 50 % FTP), 5 min between sets.",
            "3 Sätze à 10 × (40 s bei 120 % / 20 s bei 50 % FTP), 5 min zwischen den Sätzen.",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(
                    3,
                    Repeat(10, Steady(40, 120, "40 on", "40 an"), Steady(20, 50, "20 off", "20 aus")),
                    Steady(5 * 60, 50)),
                CoolDown(),
            }));

        public static Workout Sprints() => Create(
            "sprints-8x15", "Sprints 8×15 s", "Sprints 8×15 s", WorkoutCategory.Sprint,
            "8 all-out sprints of 15 s with long recovery (free ride: sprint as hard as you can).",
            "8 maximale Sprints à 15 s mit langer Erholung (freie Fahrt: so hart du kannst).",
            Concat(WarmUp(15), new WorkoutBlock[]
            {
                Repeat(8, SprintBlock(), Steady(285, 50)),
                CoolDown(),
            }));

        private static FreeRideBlock SprintBlock()
        {
            var block = new FreeRideBlock(15, isMaxEffort: true);
            block.Cues.Add(new TextCue(0, new LocalizedText("SPRINT – all out!", "SPRINT – alles geben!")));
            return block;
        }
    }
}
