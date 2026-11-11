using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Workouts;

namespace FreeVelo.Core.Training
{
    public enum FtpTestKind
    {
        /// <summary>+20 W per minute from 100 W until failure; FTP = 75 % of best 1-min power.</summary>
        Ramp,

        /// <summary>20 min all-out; FTP = 95 % of the 20-min average.</summary>
        TwentyMinute,

        /// <summary>2 × 8 min all-out; FTP = 90 % of the average of both efforts.</summary>
        TwoByEight,
    }

    /// <summary>FTP test workouts and their evaluation.</summary>
    public static class FtpTests
    {
        public static Workout Create(FtpTestKind kind) => kind switch
        {
            FtpTestKind.Ramp => RampTest(),
            FtpTestKind.TwentyMinute => TwentyMinuteTest(),
            FtpTestKind.TwoByEight => TwoByEightTest(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        /// <summary>
        /// Estimates FTP from 1-second power samples recorded during the test workout.
        /// Returns null if the test was not completed far enough.
        /// </summary>
        public static int? EstimateFtp(FtpTestKind kind, IReadOnlyList<double> watts, Workout testWorkout)
        {
            switch (kind)
            {
                case FtpTestKind.Ramp:
                {
                    double? best = PowerMetrics.MeanMax(watts, 60);
                    return best.HasValue ? (int?)Math.Round(best.Value * 0.75) : null;
                }

                case FtpTestKind.TwentyMinute:
                case FtpTestKind.TwoByEight:
                {
                    var efforts = testWorkout.Flatten()
                        .Where(s => s.Block is FreeRideBlock f && f.IsMaxEffort)
                        .ToList();
                    // 20-minute test: only the 20 minutes count (the 5-min blow-out before is all-out too).
                    if (kind == FtpTestKind.TwentyMinute && efforts.Count > 0) efforts = new List<WorkoutSegment> { efforts.Last() };
                    if (efforts.Count == 0 || watts.Count < efforts.Last().EndS) return null;

                    double avg = efforts.Average(s => Enumerable.Range(s.StartS, s.DurationS).Average(i => watts[i]));
                    double factor = kind == FtpTestKind.TwentyMinute ? 0.95 : 0.90;
                    return (int)Math.Round(avg * factor);
                }

                default:
                    return null;
            }
        }

        private static Workout RampTest()
        {
            var w = new Workout("ftp-ramp", new LocalizedText("FTP Ramp Test", "FTP-Rampentest"))
            {
                Category = WorkoutCategory.FtpTest,
                Author = "FreeVelo",
                Description = new LocalizedText(
                    "Warm-up, then +20 W every minute starting at 100 W. Ride until you cannot hold the power any more – then stop. FTP = 75 % of your best minute.",
                    "Aufwärmen, dann jede Minute +20 W ab 100 W. Fahre, bis du die Leistung nicht mehr halten kannst – dann aufhören. FTP = 75 % deiner besten Minute."),
            };
            w.Blocks.Add(new RampBlock(300, PowerTarget.Watts(80), PowerTarget.Watts(120)));
            w.Blocks.Add(new SteadyBlock(180, PowerTarget.Watts(90)));
            for (int watts = 100; watts <= 700; watts += 20)
            {
                var step = new SteadyBlock(60, PowerTarget.Watts(watts));
                if (watts == 100) step.Cues.Add(new TextCue(0, new LocalizedText("Ramp starts – +20 W every minute", "Rampe startet – jede Minute +20 W")));
                w.Blocks.Add(step);
            }

            return w;
        }

        private static Workout TwentyMinuteTest()
        {
            var w = new Workout("ftp-20min", new LocalizedText("FTP 20-Minute Test", "FTP-20-Minuten-Test"))
            {
                Category = WorkoutCategory.FtpTest,
                Author = "FreeVelo",
                Description = new LocalizedText(
                    "The protocol of Allen & Coggan: warm-up with fast pedalling, 5 min all-out, " +
                    "10 min easy, then 20 minutes as hard as you can sustain. FTP = 95 % of the 20-min average. Repeat every 6–8 weeks under the same conditions.",
                    "Das Protokoll von Allen & Coggan: Aufwärmen mit schnellem Treten, 5 min maximal, " +
                    "10 min locker, dann 20 Minuten so hart, wie du durchhalten kannst. FTP = 95 % des 20-min-Schnitts. Alle 6–8 Wochen unter gleichen Bedingungen wiederholen."),
            };

            // 15 min warm-up at endurance pace (~65 %), always the same so tests compare
            w.Blocks.Add(BuiltInWorkouts.Ramp(300, 50, 65));
            w.Blocks.Add(BuiltInWorkouts.Steady(600, 65, "Warm-up at endurance pace", "Aufwärmen im Grundlagentempo"));

            // 3 × 1 min fast pedalling (100 rpm), 1 min easy in between, then 3 min easy
            w.Blocks.Add(new RepeatBlock(3, new WorkoutBlock[]
            {
                BuiltInWorkouts.Steady(60, 75, "Fast pedalling – 100 rpm", "Schnell treten – 100 U/min", new CadenceRange(100, 110)),
                BuiltInWorkouts.Steady(60, 60),
            }) { SkipLastBlock = true });
            w.Blocks.Add(BuiltInWorkouts.Steady(180, 60, "Easy", "Locker"));

            // 5 min all-out: opens the legs, measures 5-min power, uses up the anaerobic reserve before the 20 minutes
            w.Blocks.Add(new FreeRideBlock(300, isMaxEffort: true)
            {
                Cues =
                {
                    new TextCue(0, new LocalizedText("5 min all-out – start hard, but keep something for the last minute",
                        "5 min maximal – hart anfangen, aber etwas für die letzte Minute übrig lassen")),
                    new TextCue(240, new LocalizedText("Last minute – kick to the finish!", "Letzte Minute – zum Schluss alles!")),
                },
            });
            w.Blocks.Add(BuiltInWorkouts.Steady(600, 60, "10 min easy – recover for the 20 minutes", "10 min locker – erholen für die 20 Minuten"));
            w.Blocks.Add(new FreeRideBlock(1200, isMaxEffort: true)
            {
                Cues =
                {
                    new TextCue(0, new LocalizedText("20 min all-out – pace evenly!", "20 min maximal – gleichmäßig einteilen!")),
                    new TextCue(600, new LocalizedText("Halfway – hold on!", "Halbzeit – dranbleiben!")),
                    new TextCue(1140, new LocalizedText("Last minute – give everything!", "Letzte Minute – alles geben!")),
                },
            });
            w.Blocks.Add(BuiltInWorkouts.CoolDown());
            return w;
        }

        private static Workout TwoByEightTest()
        {
            var w = new Workout("ftp-2x8", new LocalizedText("FTP 2×8-Minute Test", "FTP-2×8-Minuten-Test"))
            {
                Category = WorkoutCategory.FtpTest,
                Author = "FreeVelo",
                Description = new LocalizedText(
                    "Two 8-minute all-out efforts with 10 min recovery. FTP = 90 % of the average of both efforts.",
                    "Zwei maximale 8-Minuten-Blöcke mit 10 min Erholung. FTP = 90 % des Schnitts beider Blöcke."),
            };
            w.Blocks.AddRange(BuiltInWorkouts.WarmUp(15));
            w.Blocks.Add(new FreeRideBlock(480, isMaxEffort: true) { Cues = { new TextCue(0, new LocalizedText("8 min all-out #1", "8 min maximal #1")) } });
            w.Blocks.Add(BuiltInWorkouts.Steady(600, 50, "Recover", "Erholen"));
            w.Blocks.Add(new FreeRideBlock(480, isMaxEffort: true) { Cues = { new TextCue(0, new LocalizedText("8 min all-out #2", "8 min maximal #2")) } });
            w.Blocks.Add(BuiltInWorkouts.CoolDown());
            return w;
        }
    }
}
