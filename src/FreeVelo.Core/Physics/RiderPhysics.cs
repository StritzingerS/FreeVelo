using System;

namespace FreeVelo.Core.Physics
{
    /// <summary>
    /// Road cycling physics: converts power into speed on a given grade and surface.
    /// <code>
    /// F = m·g·sin θ + m·g·Crr·cos θ + ½·ρ·CdA·(v + v_wind)²
    /// m·dv/dt = P·η / v − F
    /// </code>
    /// </summary>
    public sealed class RiderPhysics
    {
        public const double Gravity = 9.80665;
        public const double DefaultAirDensity = 1.225;

        // Below this speed the P/v term is evaluated at this speed (avoids division by zero when starting).
        private const double MinSpeedForPower = 1.0;

        /// <summary>Rider weight in kg.</summary>
        public double RiderMassKg { get; set; } = 75.0;

        /// <summary>Bike weight in kg.</summary>
        public double BikeMassKg { get; set; } = 8.0;

        /// <summary>Drag area in m².</summary>
        public double CdA { get; set; } = 0.32;

        /// <summary>Drivetrain efficiency (0–1).</summary>
        public double DrivetrainEfficiency { get; set; } = 0.975;

        /// <summary>Air density in kg/m³.</summary>
        public double AirDensity { get; set; } = DefaultAirDensity;

        /// <summary>Current speed in m/s.</summary>
        public double SpeedMps { get; set; }

        public double TotalMassKg => RiderMassKg + BikeMassKg;

        /// <summary>Wind resistance coefficient ½·ρ·CdA in kg/m (as used by FTMS and FE-C).</summary>
        public double WindResistanceCoefficient => 0.5 * AirDensity * CdA;

        /// <summary>Resisting force in N at <paramref name="speedMps"/>.</summary>
        public double ResistanceForce(double speedMps, double gradePercent, double rollingResistance, double headwindMps = 0.0)
        {
            double theta = Math.Atan(gradePercent / 100.0);
            double m = TotalMassKg;
            double airSpeed = speedMps + headwindMps;
            double aero = 0.5 * AirDensity * CdA * airSpeed * Math.Abs(airSpeed);
            double rolling = speedMps > 0 ? m * Gravity * rollingResistance * Math.Cos(theta) : 0.0;
            return (m * Gravity * Math.Sin(theta)) + rolling + aero;
        }

        /// <summary>Advances the simulation by <paramref name="dtSeconds"/> and returns the new speed.</summary>
        public double Step(double powerW, double gradePercent, double rollingResistance, double dtSeconds, double headwindMps = 0.0)
        {
            if (dtSeconds <= 0) return SpeedMps;

            // Sub-steps keep the explicit integration stable for long frames.
            int steps = Math.Max(1, (int)Math.Ceiling(dtSeconds / 0.05));
            double h = dtSeconds / steps;
            for (int i = 0; i < steps; i++)
            {
                double v = SpeedMps;
                double drive = Math.Max(0.0, powerW) * DrivetrainEfficiency / Math.Max(v, MinSpeedForPower);
                double force = drive - ResistanceForce(v, gradePercent, rollingResistance, headwindMps);
                v += force / TotalMassKg * h;
                SpeedMps = Math.Max(0.0, v);
            }

            return SpeedMps;
        }

        /// <summary>Power needed to hold a constant speed.</summary>
        public double PowerForSpeed(double speedMps, double gradePercent, double rollingResistance, double headwindMps = 0.0) =>
            Math.Max(0.0, ResistanceForce(speedMps, gradePercent, rollingResistance, headwindMps) * speedMps / DrivetrainEfficiency);

        /// <summary>Steady-state speed for a constant power (bisection).</summary>
        public double SteadyStateSpeed(double powerW, double gradePercent, double rollingResistance, double headwindMps = 0.0)
        {
            double lo = 0.0, hi = 40.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (PowerForSpeed(mid, gradePercent, rollingResistance, headwindMps) > powerW) hi = mid;
                else lo = mid;
            }

            return lo;
        }
    }

    /// <summary>Rolling resistance by road surface (used by physics and sent to the trainer).</summary>
    public enum RoadSurface
    {
        Asphalt,
        CompactedGravel,
        Gravel,
        Dirt,
        Grass,
    }

    public static class RoadSurfaces
    {
        public static double RollingResistance(RoadSurface surface) => surface switch
        {
            RoadSurface.Asphalt => 0.004,
            RoadSurface.CompactedGravel => 0.006,
            RoadSurface.Gravel => 0.008,
            RoadSurface.Dirt => 0.010,
            RoadSurface.Grass => 0.015,
            _ => 0.004,
        };
    }
}
