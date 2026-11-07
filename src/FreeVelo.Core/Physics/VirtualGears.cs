using System;

namespace FreeVelo.Core.Physics
{
    /// <summary>
    /// 24 virtual gears (ratio chainring/cog). The rider leaves the bike in one middle gear (about 50×23, ratio
    /// ≈ 2.2 = <see cref="ReferenceRatio"/>) and shifts virtually.
    /// </summary>
    public sealed class VirtualGears
    {
        public const int DefaultCount = 24;
        public const double LowestRatio = 0.75;
        public const double HighestRatio = 5.5;
        public const double WheelCircumferenceM = 2.105; // 700×25c
        public const double MinGrade = -10;
        public const double MaxGrade = 20;

        private readonly double[] _ratios;

        public VirtualGears(int count = DefaultCount, double referenceRatio = 2.2)
        {
            count = Math.Max(2, count);
            _ratios = new double[count];
            for (int i = 0; i < count; i++) _ratios[i] = LowestRatio * Math.Pow(HighestRatio / LowestRatio, i / (double)(count - 1));
            ReferenceRatio = referenceRatio;
            Gear = NearestGear(referenceRatio);
        }

        public int Count => _ratios.Length;

        /// <summary>Ratio of the real gear the bike stays in.</summary>
        public double ReferenceRatio { get; }

        /// <summary>Current gear, 0 = easiest.</summary>
        public int Gear { get; private set; }

        public double Ratio => _ratios[Gear];

        /// <summary>Virtual speed / trainer speed.</summary>
        public double Factor => Ratio / ReferenceRatio;

        public double RatioOf(int gear) => _ratios[Math.Max(0, Math.Min(Count - 1, gear))];

        /// <summary>Shifts by <paramref name="steps"/> (positive = harder); returns true if the gear changed.</summary>
        public bool Shift(int steps)
        {
            int g = Math.Max(0, Math.Min(Count - 1, Gear + steps));
            if (g == Gear) return false;
            Gear = g;
            return true;
        }

        public void Set(int gear) => Gear = Math.Max(0, Math.Min(Count - 1, gear));

        public int NearestGear(double ratio)
        {
            int best = 0;
            for (int i = 1; i < Count; i++)
            {
                if (Math.Abs(_ratios[i] - ratio) < Math.Abs(_ratios[best] - ratio)) best = i;
            }

            return best;
        }

        /// <summary>Road resistance force (N) at a speed: slope + rolling + air. <paramref name="cw"/> = ½·ρ·CdA (kg/m).</summary>
        public static double RoadForce(double gradePercent, double speedMps, double massKg, double crr, double cw)
        {
            double theta = Math.Atan(gradePercent / 100.0);
            return (massKg * RiderPhysics.Gravity * (Math.Sin(theta) + (crr * Math.Cos(theta)))) + (cw * speedMps * speedMps);
        }

        /// <summary>
        /// For trainers without built-in virtual shifting: the grade to send so that the trainer, which itself computes slope + rolling
        /// + air at its own speed, brakes like the road at the virtual speed (force × factor, since the virtual gear moves
        /// the bike factor times as far per pedal turn).
        /// </summary>
        public double EffectiveGrade(double gradePercent, double trainerSpeedMps, double massKg, double crr, double cw)
        {
            double k = Factor;
            double v = Math.Max(0, trainerSpeedMps);
            double wanted = k * RoadForce(gradePercent, v * k, massKg, crr, cw);
            double slopeAndRolling = (wanted - (cw * v * v)) / (massKg * RiderPhysics.Gravity);
            double grade = (slopeAndRolling - crr) * 100.0; // small-angle: sin θ ≈ tan θ
            return Math.Max(MinGrade, Math.Min(MaxGrade, Math.Round(grade, 1)));
        }

        /// <summary>Virtual speed for a cadence in the current gear.</summary>
        public double VirtualSpeed(double cadenceRpm) => Math.Max(0, cadenceRpm) / 60.0 * Ratio * WheelCircumferenceM;
    }
}
