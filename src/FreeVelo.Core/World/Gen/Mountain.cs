using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>What a road is part of.</summary>
    public enum RoadKind
    {
        /// <summary>Not used any more (the rolling home world with its loop).</summary>
        Home = 0,

        /// <summary>Not used any more (old access roads to the climbs).</summary>
        Access = 1,

        /// <summary>A famous climb, copied from GPX (foot → summit).</summary>
        Climb = 2,

        /// <summary>The short road between the two summits.</summary>
        Connector = 3,

        /// <summary>A road on a valley floor, between villages (nearly flat).</summary>
        Valley = 4,

        /// <summary>A mountain road with an even gradient (the Alm road, the road to the Alpe d'Huez).</summary>
        Pass = 5,
    }

    /// <summary>A climb as placed in the world.</summary>
    public sealed class PlacedClimb
    {
        internal PlacedClimb(ClimbData data, double angle, double summitX, double summitZ, double footElevation)
        {
            Data = data;
            Angle = angle;
            SummitX = summitX;
            SummitZ = summitZ;
            FootElevation = footElevation;
        }

        public ClimbData Data { get; }

        /// <summary>Rotation of the GPX track around its summit (radians).</summary>
        public double Angle { get; }

        public double SummitX { get; }

        public double SummitZ { get; }

        /// <summary>World elevation of the foot (the GPX elevations are shifted to it).</summary>
        public double FootElevation { get; }

        public double SummitElevation => FootElevation + Data.Gain;

        /// <summary>Edge id of the climb (From = foot, To = summit).</summary>
        public int Edge { get; internal set; } = -1;

        public (double X, double Z) Point(int i)
        {
            double c = Math.Cos(Angle), s = Math.Sin(Angle);
            return (SummitX + (Data.X[i] * c) - (Data.Z[i] * s), SummitZ + (Data.X[i] * s) + (Data.Z[i] * c));
        }

        public double Elevation(int i) => FootElevation + (Data.E[i] - Data.E[0]);

        public (double X, double Z) Foot => Point(0);

        /// <summary>Direction of the road leaving the foot (unit vector).</summary>
        public (double X, double Z) StartDirection
        {
            get
            {
                var a = Point(0);
                var b = Point(Math.Min(Data.X.Length - 1, 8)); // 40 m on
                return Unit(b.X - a.X, b.Z - a.Z);
            }
        }

        /// <summary>Direction of the road arriving at the summit (unit vector).</summary>
        public (double X, double Z) ArrivalDirection
        {
            get
            {
                int n = Data.X.Length;
                var a = Point(Math.Max(0, n - 9));
                var b = Point(n - 1);
                return Unit(b.X - a.X, b.Z - a.Z);
            }
        }

        internal static (double X, double Z) Unit(double x, double z)
        {
            double l = Math.Sqrt((x * x) + (z * z));
            return l < 1e-9 ? (0, 1) : (x / l, z / l);
        }
    }

    /// <summary>
    /// The mountain: Passo dello Stelvio and Alpe d'Huez as exact copies of their GPX tracks, meeting at a shared summit
    /// with a short road between them. The Stelvio starts in the main valley (Trafon); the summits are at the same height,
    /// so the Alpe d'Huez starts about 720 m higher (Valbrune, reached over the high plateau).
    /// </summary>
    public sealed class Mountain
    {
        public const double ConnectorLength = 1000;

        // Placement of the two GPX tracks (rotation around the summit, summit position). Fixed: the valleys are laid out
        // around them.
        private const double StelvioAngle = -2.841836436905407, StelvioSummitX = 8021.3189420527915, StelvioSummitZ = 4615.350648791348;
        private const double AlpeAngle = -1.4556632361605066, AlpeSummitX = 7646.3189420527915, AlpeSummitZ = 5264.869701629677;

        private Mountain(PlacedClimb stelvio, PlacedClimb alpe, Polyline connector)
        {
            Stelvio = stelvio;
            Alpe = alpe;
            ConnectorPath = connector;
        }

        public PlacedClimb Stelvio { get; }

        public PlacedClimb Alpe { get; }

        public IEnumerable<PlacedClimb> Climbs => new[] { Stelvio, Alpe };

        public double SummitElevation => Stelvio.SummitElevation;

        internal Polyline ConnectorPath { get; }

        public int ConnectorEdge { get; internal set; } = -1;

        public PlacedClimb? Find(string id) => Climbs.FirstOrDefault(c => c.Data.Id == id);

        /// <summary>Both climbs, the Stelvio's foot at <paramref name="stelvioFootElevation"/>.</summary>
        internal static Mountain Place(double stelvioFootElevation)
        {
            var stelvio = new PlacedClimb(ClimbData.Load(ClimbData.Stelvio), StelvioAngle, StelvioSummitX, StelvioSummitZ, stelvioFootElevation);
            var alpeData = ClimbData.Load(ClimbData.AlpeDHuez);
            var alpe = new PlacedClimb(alpeData, AlpeAngle, AlpeSummitX, AlpeSummitZ, stelvio.SummitElevation - alpeData.Gain);
            return new Mountain(stelvio, alpe, Connector(stelvio, alpe));
        }

        /// <summary>
        /// The road between the summits: it leaves the first summit the way the climb arrived and joins the second the
        /// way that climb arrived, so riding over the top feels like one road.
        /// </summary>
        private static Polyline Connector(PlacedClimb a, PlacedClimb b)
        {
            var (ax, az) = a.Point(a.Data.X.Length - 1);
            var (bx, bz) = b.Point(b.Data.X.Length - 1);
            var ta = a.ArrivalDirection;
            var tb = b.ArrivalDirection;
            double gap = Math.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));
            double k = Math.Max(150, gap * 0.45);
            return Polyline.Bezier(ax, az, ax + (ta.X * k), az + (ta.Z * k), bx + (tb.X * k), bz + (tb.Z * k), bx, bz, 300).Resample(WorldMap.Spacing);
        }
    }
}
