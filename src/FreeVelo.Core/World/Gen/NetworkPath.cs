using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.Core.Physics;

namespace FreeVelo.Core.World.Gen
{
    /// <summary>A way to continue at a junction.</summary>
    public readonly struct TurnOption
    {
        public TurnOption(DirectedEdge edge, double turnDegrees, bool mainLoop, RoadSurface surface, double lengthM)
        {
            Edge = edge;
            TurnDegrees = turnDegrees;
            IsMainLoop = mainLoop;
            Surface = surface;
            LengthM = lengthM;
        }

        public DirectedEdge Edge { get; }

        /// <summary>Change of direction: negative = left, positive = right, about 0 = straight on.</summary>
        public double TurnDegrees { get; }

        public bool IsMainLoop { get; }

        public RoadSurface Surface { get; }

        /// <summary>Length of the road until the next junction.</summary>
        public double LengthM { get; }
    }

    /// <summary>The next junction ahead of the rider and the ways on.</summary>
    public sealed class UpcomingJunction
    {
        internal UpcomingJunction(int node, double distanceM, List<TurnOption> options, int chosen)
        {
            Node = node;
            DistanceM = distanceM;
            Options = options;
            Chosen = chosen;
        }

        public int Node { get; }

        /// <summary>Route distance of the junction (compare with the rider's distance).</summary>
        public double DistanceM { get; }

        /// <summary>Ways on, from the leftmost to the rightmost (never back the way the rider came).</summary>
        public IReadOnlyList<TurnOption> Options { get; }

        /// <summary>Index into <see cref="Options"/>: the rider's choice, or the default (straight on / main loop).</summary>
        public int Chosen { get; }
    }

    /// <summary>
    /// The rider's way through the road network: a route made of the edges ridden so far and the one ahead. At each
    /// junction the rider picks a way on (left / right / straight); without a choice it goes straight on, preferring
    /// the main loop. The route grows edge by edge (and forgets edges far behind), so the ride's distance keeps
    /// counting up without jumps.
    /// </summary>
    public sealed class NetworkPath
    {
        /// <summary>The next edge is added when the rider is this close to the junction (the choice is fixed then).</summary>
        public const double CommitDistanceM = 40;

        private const int KeptSteps = 4;

        private readonly WorldMap _map;
        private readonly List<DirectedEdge> _steps = new List<DirectedEdge>();
        private double _startDistanceM;
        private int? _choice;

        public NetworkPath(WorldMap map, DirectedEdge first, double startDistanceM)
        {
            _map = map;
            _steps.Add(first);
            _startDistanceM = startDistanceM;
            Route = Build();
        }

        /// <summary>The route ridden: from a few edges back up to the next junction.</summary>
        public Route Route { get; private set; }

        /// <summary>Route distance where the current path ends (the next junction).</summary>
        public double EndDistanceM => _startDistanceM + _steps.Sum(s => _map.Edges[s.Edge].Length);

        /// <summary>
        /// Starts on the main loop at <paramref name="loopDistanceM"/> (e.g. after a group ride), with the ride's
        /// distance <paramref name="rideDistanceM"/> at that point.
        /// </summary>
        public static NetworkPath OnMainLoop(WorldMap map, double loopDistanceM, double rideDistanceM)
        {
            double length = map.MainLoop.LengthM;
            double d = ((loopDistanceM % length) + length) % length;
            foreach (int edge in map.MainLoopEdges)
            {
                double l = map.Edges[edge].Length;
                if (d <= l) return new NetworkPath(map, new DirectedEdge(edge, true), rideDistanceM - d);
                d -= l;
            }

            return new NetworkPath(map, new DirectedEdge(map.MainLoopEdges[0], true), rideDistanceM);
        }

        /// <summary>The junction at the end of the path, with the ways on and the current choice.</summary>
        public UpcomingJunction Next()
        {
            var options = Options(_steps[_steps.Count - 1]);
            int chosen = _choice.HasValue && _choice.Value < options.Count ? _choice.Value : DefaultChoice(_steps[_steps.Count - 1], options);
            return new UpcomingJunction(EndNode(_steps[_steps.Count - 1]), EndDistanceM, options, chosen);
        }

        /// <summary>Picks the way on at the next junction (index into <see cref="UpcomingJunction.Options"/>).</summary>
        public void Choose(int option) => _choice = option;

        /// <summary>
        /// Call with the rider's distance: close to the junction the chosen edge is added and old edges are dropped.
        /// Returns true when <see cref="Route"/> changed.
        /// </summary>
        public bool Update(double distanceM)
        {
            bool changed = false;
            while (distanceM > EndDistanceM - CommitDistanceM)
            {
                var next = Next();
                _steps.Add(next.Options[next.Chosen].Edge);
                _choice = null;
                while (_steps.Count > KeptSteps && distanceM - _startDistanceM > _map.Edges[_steps[0].Edge].Length + 500)
                {
                    _startDistanceM += _map.Edges[_steps[0].Edge].Length;
                    _steps.RemoveAt(0);
                }

                changed = true;
            }

            if (changed) Route = Build();
            return changed;
        }

        /// <summary>Ways on from the end of <paramref name="arriving"/>, leftmost first, without turning back.</summary>
        public List<TurnOption> Options(DirectedEdge arriving)
        {
            int node = EndNode(arriving);
            double inHeading = Heading(arriving, atEnd: true);
            var options = new List<TurnOption>();
            foreach (int id in _map.Nodes[node].Edges)
            {
                var edge = _map.Edges[id];
                var candidates = new List<DirectedEdge>();
                if (edge.From == node) candidates.Add(new DirectedEdge(id, true));
                if (edge.To == node) candidates.Add(new DirectedEdge(id, false));
                foreach (var c in candidates)
                {
                    if (c.Edge == arriving.Edge && c.Forward != arriving.Forward) continue; // back the way we came
                    double turn = Wrap(Heading(c, atEnd: false) - inHeading) * 180 / Math.PI;
                    options.Add(new TurnOption(c, turn, edge.IsMainLoop, edge.Surface, edge.Length));
                }
            }

            return options.OrderBy(o => o.TurnDegrees).ToList();
        }

        private bool IsMainLoopEdge(DirectedEdge e) => _map.Edges[e.Edge].IsMainLoop;

        private int DefaultChoice(DirectedEdge arriving, List<TurnOption> options)
        {
            int best = 0;
            double bestScore = double.MaxValue;
            for (int i = 0; i < options.Count; i++)
            {
                // Straight on, and stay on the main loop while on it (the mountain's access roads are only taken on
                // purpose: never by default from the main loop).
                double score = Math.Abs(options[i].TurnDegrees) - (options[i].IsMainLoop ? 60 : 0);
                if (options[i].IsMainLoop && IsMainLoopEdge(arriving)) score -= 1000;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        private int EndNode(DirectedEdge step) => step.Forward ? _map.Edges[step.Edge].To : _map.Edges[step.Edge].From;

        /// <summary>Direction of travel (0 = +Z, π/2 = +X) at the start or end of a directed edge.</summary>
        private double Heading(DirectedEdge step, bool atEnd)
        {
            var e = _map.Edges[step.Edge];
            int n = e.X.Length;
            int a, b;
            if (step.Forward)
            {
                a = atEnd ? n - 4 : 0;
                b = atEnd ? n - 1 : 3;
            }
            else
            {
                // Travelling from the To end (last point) towards the From end (first point).
                a = atEnd ? 3 : n - 1;
                b = atEnd ? 0 : n - 4;
            }

            return Math.Atan2(e.X[b] - e.X[a], e.Z[b] - e.Z[a]);
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private Route Build() => _map.BuildRoute("Network", _steps, isLoop: false, _startDistanceM);
    }
}
