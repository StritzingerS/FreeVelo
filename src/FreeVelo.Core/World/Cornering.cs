using System;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World
{
    /// <summary>
    /// Automatic braking before bends, like a real rider on a descent: every bend has a safe speed (from its radius
    /// and a comfortable lean), and the rider brakes early enough to reach it. Only the virtual speed is limited;
    /// the trainer keeps simulating the road.
    /// <code>
    /// v_bend  = √(g · tan(lean) / κ)
    /// v_limit = min over the next metres x of √(v_bend(x)² + 2 · a · x)
    /// </code>
    /// </summary>
    public static class Cornering
    {
        /// <summary>Lean a rider is comfortable with in a bend (a 10 m hairpin → about 27 km/h).</summary>
        public const double MaxLeanDeg = 30;

        /// <summary>Planned deceleration before a bend (firm, controlled braking).</summary>
        public const double PlannedDecelMps2 = 4;

        /// <summary>Strongest braking (used when already too fast, e.g. after joining mid-bend).</summary>
        public const double MaxDecelMps2 = 6.5;

        public const double LookAheadM = 160;
        private const double StepM = 2;

        /// <summary>Safe speed in a bend of curvature <paramref name="curvature"/> (1/m); infinite on straights.</summary>
        public static double BendSpeed(double curvature)
        {
            double k = Math.Abs(curvature);
            if (k < 1e-5) return double.PositiveInfinity;
            return Math.Sqrt(RiderPhysics.Gravity * Math.Tan(MaxLeanDeg * Math.PI / 180) / k);
        }

        /// <summary>
        /// Highest speed at <paramref name="distanceM"/> from which every bend ahead can still be taken safely.
        /// With a spread (a group), the stretch from <paramref name="behindM"/> behind to <paramref name="aheadM"/>
        /// ahead is ridden at this speed right now (no room to brake there); the look-ahead starts at its front.
        /// </summary>
        public static double SpeedLimit(Route route, double distanceM, double behindM = 0, double aheadM = 0)
        {
            double limit = double.PositiveInfinity;
            for (double x = -behindM; x <= aheadM + LookAheadM; x += StepM)
            {
                double bend = BendSpeed(route.CurvatureAt(distanceM + x));
                if (double.IsPositiveInfinity(bend)) continue;
                double room = Math.Max(0, x - aheadM);
                limit = Math.Min(limit, Math.Sqrt((bend * bend) + (2 * PlannedDecelMps2 * room)));
            }

            return limit;
        }

        /// <summary>
        /// Speed limit for a step of <paramref name="dtSeconds"/>: the stretch covered by the step counts as already
        /// there, so long steps (the server's 0.25 s ticks, 1 s background ticks) do not carry the speed into a bend.
        /// </summary>
        public static double StepLimit(Route route, double distanceM, double speedMps, double dtSeconds, double behindM = 0, double aheadM = 0) =>
            SpeedLimit(route, distanceM, behindM, aheadM + (speedMps * dtSeconds));

        /// <summary>
        /// Brakes <paramref name="physics"/> down towards <paramref name="limit"/> after a physics step; returns true
        /// while braking.
        /// </summary>
        public static bool Brake(RiderPhysics physics, double limit, double dtSeconds)
        {
            if (physics.SpeedMps <= limit) return false;
            physics.SpeedMps = Math.Max(limit, physics.SpeedMps - (MaxDecelMps2 * dtSeconds));
            return true;
        }
    }
}
