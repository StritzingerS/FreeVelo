using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World;

namespace FreeVelo.Core.Multiplayer
{
    /// <summary>Where a rider of a Keep Together group is, as sent to everyone.</summary>
    public readonly struct GroupPosition
    {
        public GroupPosition(double distanceM, double speedMps, bool active, bool towed)
        {
            DistanceM = distanceM;
            SpeedMps = speedMps;
            Active = active;
            Towed = towed;
        }

        public double DistanceM { get; }

        public double SpeedMps { get; }

        /// <summary>Counts for the group speed (pedaling with at least <see cref="KeepTogetherGroup.MinActiveWkg"/>).</summary>
        public bool Active { get; }

        /// <summary>Stopped pedaling: pulled along at the back of the group.</summary>
        public bool Towed { get; }
    }

    /// <summary>
    /// Keep Together (concept section 9.4): the server simulates one virtual reference rider, the anchor, pedaling the
    /// group's average W/kg on the real route. Every rider is placed relative to the anchor: who pushes more W/kg
    /// rides a little ahead, who pushes less a little behind (at most <see cref="MaxOffsetM"/>), smoothed so positions
    /// change gently. Riders who stop pedaling (drink, toilet) do not slow the group: they leave the average and are
    /// towed along at the back until they pedal again.
    /// </summary>
    public sealed class KeepTogetherGroup
    {
        /// <summary>Metres ahead per W/kg above the group average.</summary>
        public const double MetresPerWkg = 15;

        public const double MaxOffsetM = 25;

        /// <summary>Time constant of the offset smoothing.</summary>
        public const double OffsetTauS = 5;

        public const double MinActiveWkg = 0.5;

        /// <summary>Without a fresh "pedaling" update for this long a rider counts as stopped.</summary>
        public const double ActiveWindowS = 3;

        /// <summary>The anchor: 75 kg rider (W/kg × 75 = its power), 8 kg bike, CdA 0.32.</summary>
        public const double ReferenceRiderKg = 75;

        private readonly Route _route;
        private readonly RiderPhysics _anchor = new RiderPhysics { RiderMassKg = ReferenceRiderKg, BikeMassKg = 8, CdA = 0.32 };
        private readonly Dictionary<string, Member> _members = new Dictionary<string, Member>();
        private double _clockS;

        public KeepTogetherGroup(Route route, double startDistanceM = 0)
        {
            _route = route;
            AnchorDistanceM = startDistanceM;
        }

        public double AnchorDistanceM { get; private set; }

        public double AnchorSpeedMps => _anchor.SpeedMps;

        /// <summary>Average W/kg of the active riders in the last step (0 when nobody pedals).</summary>
        public double AverageWkg { get; private set; }

        public IReadOnlyCollection<string> RiderIds => _members.Keys;

        /// <summary>Adds a rider; they appear at the back of the group and move to their place within seconds.</summary>
        public void Join(string riderId)
        {
            if (_members.ContainsKey(riderId)) return;
            _members[riderId] = new Member { OffsetM = -MaxOffsetM, LastActiveS = double.NegativeInfinity };
        }

        public void Leave(string riderId) => _members.Remove(riderId);

        /// <summary>Latest values from a rider's app (about 4 per second).</summary>
        public void Report(string riderId, double wkg, double cadenceRpm)
        {
            if (!_members.TryGetValue(riderId, out var member)) return;
            member.Wkg = Math.Max(0, wkg);
            if (wkg >= MinActiveWkg && cadenceRpm > 0) member.LastActiveS = _clockS;
        }

        /// <summary>Advances the group by <paramref name="dtSeconds"/> (the server calls this 4 times a second).</summary>
        public void Step(double dtSeconds)
        {
            if (dtSeconds <= 0) return;
            _clockS += dtSeconds;

            var active = _members.Values.Where(IsActive).ToList();
            AverageWkg = active.Count > 0 ? active.Average(m => m.Wkg) : 0;

            // Anchor: the average rider on the real road (nobody pedaling → it rolls out to a stop).
            var here = _route.Sample(AnchorDistanceM);
            _anchor.Step(AverageWkg * ReferenceRiderKg, here.GradePercent, RoadSurfaces.RollingResistance(here.Surface), dtSeconds);
            // Brake for bends: every rider of the group (up to MaxOffsetM behind or ahead of the anchor) must take
            // them slowly enough.
            double limit = Cornering.StepLimit(_route, AnchorDistanceM, _anchor.SpeedMps, dtSeconds, MaxOffsetM, MaxOffsetM);
            Cornering.Brake(_anchor, limit, dtSeconds);
            AnchorDistanceM += _anchor.SpeedMps * dtSeconds;

            double alpha = 1 - Math.Exp(-dtSeconds / OffsetTauS);
            foreach (var member in _members.Values)
            {
                bool isActive = IsActive(member);
                double target = isActive
                    ? Math.Max(-MaxOffsetM, Math.Min(MaxOffsetM, MetresPerWkg * (member.Wkg - AverageWkg)))
                    : -MaxOffsetM;
                double before = member.OffsetM;
                member.OffsetM += (target - before) * alpha;
                member.OffsetRateMps = (member.OffsetM - before) / dtSeconds;
                member.Active = isActive;
            }
        }

        public bool Contains(string riderId) => _members.ContainsKey(riderId);

        public GroupPosition PositionOf(string riderId)
        {
            var m = _members[riderId];
            return new GroupPosition(AnchorDistanceM + m.OffsetM, Math.Max(0, _anchor.SpeedMps + m.OffsetRateMps), m.Active, !m.Active);
        }

        private bool IsActive(Member member) => _clockS - member.LastActiveS <= ActiveWindowS && member.Wkg >= MinActiveWkg;

        private sealed class Member
        {
            public double Wkg;
            public double LastActiveS;
            public double OffsetM;
            public double OffsetRateMps;
            public bool Active;
        }
    }
}
