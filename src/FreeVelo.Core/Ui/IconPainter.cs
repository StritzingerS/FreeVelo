using System;
using System.Collections.Generic;
using System.Numerics;

namespace FreeVelo.Core.Ui
{
    public enum Icon
    {
        Play,
        Pause,
        Stop,
        Plus,
        Close,
        Check,
        ChevronLeft,
        ChevronRight,
        Edit,
        Trash,
        Share,
        Undo,
        Redo,
        Reset,
        Bluetooth,
        Heart,
        Bolt,
        Cadence,
        Speed,
        Gear,
        List,
        Bike,
        Download,
        Import,
        Copy,
        Timer,
        Target,
        Route,
        Signal,
        Search,
        Skip,
        Trainer,
        Group,
        ThumbsUp,
        Fire,
        Drop,
        Hand,
        TurnLeft,
        TurnRight,
        Straight,
        Tow,
        Minus,

        /// <summary>Gradient (uphill): a wedge rising to the right, like a road gradient sign.</summary>
        GradeUp,

        /// <summary>Gradient (downhill): the wedge falling to the right.</summary>
        GradeDown,

        /// <summary>Elevation gained: a mountain with an arrow up.</summary>
        Ascent,

        /// <summary>Distance: a double arrow between two end marks (a measured length).</summary>
        Distance,
    }

    /// <summary>
    /// Procedural line icons on a 24-unit grid (like common icon sets), rasterised with anti-aliasing into an
    /// alpha mask. Engine independent: Unity turns the mask into a texture and tints it.
    /// </summary>
    public static class IconPainter
    {
        private const float Stroke = 2.1f;

        /// <summary>Alpha coverage (row-major, top row first) of an icon at <paramref name="size"/>×<paramref name="size"/> pixels.</summary>
        public static float[] Render(Icon icon, int size)
        {
            var shapes = Shapes(icon);
            var alpha = new float[size * size];
            float unit = size / 24f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Pixel centre in icon units.
                    var p = new Vector2((x + 0.5f) / unit, (y + 0.5f) / unit);
                    float d = float.MaxValue;
                    float cut = float.MaxValue;
                    foreach (var s in shapes)
                    {
                        if (s.Cut) cut = MathF.Min(cut, s.Distance(p));
                        else d = MathF.Min(d, s.Distance(p));
                    }

                    d = MathF.Max(d, -cut);
                    alpha[(y * size) + x] = Math.Clamp(0.5f - (d * unit), 0f, 1f);
                }
            }

            return alpha;
        }

        /// <summary>
        /// Alpha mask of a rounded rectangle (<paramref name="size"/> square, corner radius in pixels) with an
        /// optional soft outer shadow: <paramref name="blur"/> &gt; 0 gives a blurred edge for drop shadows.
        /// </summary>
        public static float[] RoundedRect(int size, float radius, float blur = 0f, float inset = 0f)
        {
            var alpha = new float[size * size];
            float half = (size / 2f) - inset;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var q = new Vector2(MathF.Abs(x + 0.5f - (size / 2f)), MathF.Abs(y + 0.5f - (size / 2f))) - new Vector2(half - radius);
                    float d = new Vector2(MathF.Max(q.X, 0), MathF.Max(q.Y, 0)).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) - radius;
                    alpha[(y * size) + x] = blur > 0
                        ? MathF.Pow(Math.Clamp(0.5f - (d / blur), 0f, 1f), 1.6f)
                        : Math.Clamp(0.5f - d, 0f, 1f);
                }
            }

            return alpha;
        }

        // ------------------------------------------------------------------ icon shapes

        private abstract class Shape
        {
            public bool Cut { get; set; }

            public abstract float Distance(Vector2 p);
        }

        private sealed class Polyline : Shape
        {
            private readonly Vector2[] _points;
            private readonly float _halfWidth;

            public Polyline(float width, params Vector2[] points)
            {
                _points = points;
                _halfWidth = width / 2f;
            }

            public override float Distance(Vector2 p)
            {
                float d = float.MaxValue;
                for (int i = 0; i < _points.Length - 1; i++) d = MathF.Min(d, Segment(p, _points[i], _points[i + 1]));
                if (_points.Length == 1) d = Vector2.Distance(p, _points[0]);
                return d - _halfWidth;
            }
        }

        private sealed class Polygon : Shape
        {
            private readonly Vector2[] _v;

            public Polygon(params Vector2[] v) => _v = v;

            public override float Distance(Vector2 p)
            {
                float d = float.MaxValue;
                bool inside = false;
                for (int i = 0, j = _v.Length - 1; i < _v.Length; j = i, i++)
                {
                    d = MathF.Min(d, Segment(p, _v[i], _v[j]));
                    if ((_v[i].Y > p.Y) != (_v[j].Y > p.Y) && p.X < ((_v[j].X - _v[i].X) * (p.Y - _v[i].Y) / (_v[j].Y - _v[i].Y)) + _v[i].X) inside = !inside;
                }

                return inside ? -d : d;
            }
        }

        private sealed class Disc : Shape
        {
            private readonly Vector2 _c;
            private readonly float _r;

            public Disc(Vector2 c, float r)
            {
                _c = c;
                _r = r;
            }

            public override float Distance(Vector2 p) => Vector2.Distance(p, _c) - _r;
        }

        private sealed class Ring : Shape
        {
            private readonly Vector2 _c;
            private readonly float _r;
            private readonly float _w;

            public Ring(Vector2 c, float r, float w)
            {
                _c = c;
                _r = r;
                _w = w;
            }

            public override float Distance(Vector2 p) => MathF.Abs(Vector2.Distance(p, _c) - _r) - (_w / 2f);
        }

        private sealed class Box : Shape
        {
            private readonly Vector2 _min;
            private readonly Vector2 _max;
            private readonly float _r;
            private readonly float _outline;

            public Box(Vector2 min, Vector2 max, float radius, float outline = 0f)
            {
                _min = min;
                _max = max;
                _r = radius;
                _outline = outline;
            }

            public override float Distance(Vector2 p)
            {
                var c = (_min + _max) / 2f;
                var h = ((_max - _min) / 2f) - new Vector2(_r);
                var q = new Vector2(MathF.Abs(p.X - c.X), MathF.Abs(p.Y - c.Y)) - h;
                float d = new Vector2(MathF.Max(q.X, 0), MathF.Max(q.Y, 0)).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0) - _r;
                return _outline > 0 ? MathF.Abs(d) - (_outline / 2f) : d;
            }
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.LengthSquared();
            float t = l2 < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / l2, 0f, 1f);
            return Vector2.Distance(p, a + (ab * t));
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Shape Line(params Vector2[] points) => new Polyline(Stroke, points);

        private static Vector2[] Arc(Vector2 c, float r, float fromDeg, float toDeg, int steps = 24)
        {
            var pts = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float a = (fromDeg + ((toDeg - fromDeg) * i / steps)) * MathF.PI / 180f;
                pts[i] = c + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
            }

            return pts;
        }

        /// <summary>Closed outline of a rounded bar at 45° (a chain link).</summary>
        private static Vector2[] Stadium(Vector2 c, float length, float radius)
        {
            var axis = Vector2.Normalize(V(1, -1)) * (length / 2 - radius);
            var pts = new List<Vector2>();
            pts.AddRange(Arc(c + axis, radius, -135, 45, 10));
            pts.AddRange(Arc(c - axis, radius, 45, 225, 10));
            pts.Add(pts[0]);
            return pts.ToArray();
        }

        private static List<Shape> Shapes(Icon icon)
        {
            var s = new List<Shape>();
            switch (icon)
            {
                case Icon.Play:
                    s.Add(new Polygon(V(8, 5), V(19.5f, 12), V(8, 19)));
                    break;
                case Icon.Pause:
                    s.Add(new Box(V(6.5f, 5), V(10.5f, 19), 1.2f));
                    s.Add(new Box(V(13.5f, 5), V(17.5f, 19), 1.2f));
                    break;
                case Icon.Stop:
                    s.Add(new Box(V(6, 6), V(18, 18), 2.2f));
                    break;
                case Icon.Plus:
                    s.Add(new Polyline(2.4f, V(12, 5), V(12, 19)));
                    s.Add(new Polyline(2.4f, V(5, 12), V(19, 12)));
                    break;
                case Icon.Minus:
                    s.Add(new Polyline(2.4f, V(5, 12), V(19, 12)));
                    break;
                case Icon.Distance:
                    s.Add(Line(V(3, 7), V(3, 17)));
                    s.Add(Line(V(21, 7), V(21, 17)));
                    s.Add(Line(V(8, 12), V(16, 12)));
                    s.Add(new Polygon(V(5.2f, 12), V(10, 7.8f), V(10, 16.2f)));
                    s.Add(new Polygon(V(18.8f, 12), V(14, 7.8f), V(14, 16.2f)));
                    break;
                case Icon.Ascent:
                    s.Add(new Polygon(V(1.5f, 21), V(9, 9.5f), V(12.5f, 14.5f), V(15.5f, 11), V(22.5f, 21)));
                    s.Add(Line(V(19, 9), V(19, 2.8f)));
                    s.Add(new Polygon(V(19, 1), V(22.2f, 4.8f), V(15.8f, 4.8f)));
                    break;
                case Icon.GradeUp:
                case Icon.GradeDown:
                {
                    // Road gradient sign: a filled wedge on the road line, the high side up.
                    bool up = icon == Icon.GradeUp;
                    s.Add(new Polygon(V(3, 19.5f), V(21, 19.5f), up ? V(21, 7) : V(3, 7)));
                    break;
                }
                case Icon.Close:
                    s.Add(new Polyline(2.4f, V(6.5f, 6.5f), V(17.5f, 17.5f)));
                    s.Add(new Polyline(2.4f, V(17.5f, 6.5f), V(6.5f, 17.5f)));
                    break;
                case Icon.Check:
                    s.Add(new Polyline(2.5f, V(5, 12.5f), V(9.8f, 17.2f), V(19, 7.2f)));
                    break;
                case Icon.ChevronLeft:
                    s.Add(new Polyline(2.4f, V(15, 5.5f), V(8.5f, 12), V(15, 18.5f)));
                    break;
                case Icon.ChevronRight:
                    s.Add(new Polyline(2.4f, V(9, 5.5f), V(15.5f, 12), V(9, 18.5f)));
                    break;
                case Icon.Edit:
                    s.Add(new Polyline(3.2f, V(7.5f, 16.5f), V(16.2f, 7.8f)));
                    s.Add(new Polygon(V(4.6f, 19.4f), V(5.6f, 15.2f), V(8.8f, 18.4f)));
                    s.Add(new Polyline(3.2f, V(17.4f, 6.6f), V(18.3f, 5.7f)));
                    break;
                case Icon.Trash:
                    s.Add(Line(V(4.5f, 7), V(19.5f, 7)));
                    s.Add(Line(V(9.5f, 7), V(10, 4.5f), V(14, 4.5f), V(14.5f, 7)));
                    s.Add(Line(V(6.8f, 7.5f), V(7.8f, 19.5f), V(16.2f, 19.5f), V(17.2f, 7.5f)));
                    s.Add(Line(V(10.3f, 10.5f), V(10.5f, 16.5f)));
                    s.Add(Line(V(13.7f, 10.5f), V(13.5f, 16.5f)));
                    break;
                case Icon.Share:
                    s.Add(new Disc(V(6.5f, 12), 2.6f));
                    s.Add(new Disc(V(17.5f, 6), 2.6f));
                    s.Add(new Disc(V(17.5f, 18), 2.6f));
                    s.Add(new Polyline(1.9f, V(6.5f, 12), V(17.5f, 6)));
                    s.Add(new Polyline(1.9f, V(6.5f, 12), V(17.5f, 18)));
                    break;
                case Icon.Undo:
                case Icon.Redo:
                {
                    bool redo = icon == Icon.Redo;
                    Vector2 M(Vector2 p) => redo ? new Vector2(24 - p.X, p.Y) : p;
                    var arc = Arc(V(13, 14), 6.5f, 180, 360 + 30);
                    for (int i = 0; i < arc.Length; i++) arc[i] = M(arc[i]);
                    s.Add(Line(arc));
                    s.Add(new Polygon(M(V(2.5f, 13.5f)), M(V(10.5f, 13.5f)), M(V(6.5f, 19))));
                    break;
                }

                case Icon.Reset:
                    s.Add(Line(Arc(V(12, 12), 7, -60, 230)));
                    s.Add(new Polygon(V(14.8f, 2.5f), V(19.6f, 8.2f), V(12.6f, 8.8f)));
                    break;
                case Icon.Bluetooth:
                    s.Add(new Polyline(2f, V(7, 7.5f), V(17, 16), V(12, 20.5f), V(12, 3.5f), V(17, 8), V(7, 16.5f)));
                    break;
                case Icon.Heart:
                    s.Add(new Disc(V(8.6f, 9.3f), 4.4f));
                    s.Add(new Disc(V(15.4f, 9.3f), 4.4f));
                    s.Add(new Polygon(V(4.6f, 11.4f), V(19.4f, 11.4f), V(12, 20.5f)));
                    break;
                case Icon.Bolt:
                    s.Add(new Polygon(V(13.5f, 2.5f), V(5.5f, 13.5f), V(11.2f, 13.5f), V(10.2f, 21.5f), V(18.5f, 10), V(12.6f, 10)));
                    break;
                case Icon.Cadence:
                    s.Add(new Ring(V(12, 12), 8, Stroke));
                    s.Add(new Polyline(Stroke, V(12, 12), V(16.5f, 7.5f)));
                    s.Add(new Disc(V(12, 12), 2.2f));
                    break;
                case Icon.Speed:
                    s.Add(Line(Arc(V(12, 15), 8.5f, 180, 360)));
                    s.Add(new Polyline(2.4f, V(12, 15), V(16.5f, 9)));
                    s.Add(new Disc(V(12, 15), 2f));
                    break;
                case Icon.Gear:
                {
                    s.Add(new Ring(V(12, 12), 3.2f, 2.2f));
                    var body = new Ring(V(12, 12), 6.3f, 2.4f);
                    s.Add(body);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * MathF.PI / 4f;
                        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                        s.Add(new Polyline(3.2f, V(12, 12) + (dir * 6.8f), V(12, 12) + (dir * 9.2f)));
                    }

                    break;
                }

                case Icon.List:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 6.5f + (i * 5.5f);
                        s.Add(new Disc(V(5.5f, y), 1.5f));
                        s.Add(Line(V(9.5f, y), V(19.5f, y)));
                    }

                    break;
                case Icon.Bike:
                    s.Add(new Ring(V(5.8f, 15.5f), 3.8f, 1.9f));
                    s.Add(new Ring(V(18.2f, 15.5f), 3.8f, 1.9f));
                    s.Add(new Polyline(1.9f, V(5.8f, 15.5f), V(10, 8.5f), V(16, 8.5f), V(18.2f, 15.5f)));
                    s.Add(new Polyline(1.9f, V(10, 8.5f), V(12.5f, 15.5f), V(16, 8.5f)));
                    s.Add(new Polyline(1.9f, V(8.5f, 6), V(11.5f, 6)));
                    s.Add(new Polyline(1.9f, V(16, 8.5f), V(15.2f, 5.5f), V(17.5f, 5.5f)));
                    break;
                case Icon.Download:
                case Icon.Import:
                {
                    bool up = icon == Icon.Import;
                    s.Add(Line(V(12, up ? 15 : 4), V(12, up ? 4 : 15)));
                    s.Add(Line(V(7, up ? 9 : 10), V(12, up ? 4 : 15), V(17, up ? 9 : 10)));
                    s.Add(Line(V(5, 15.5f), V(5, 19.5f), V(19, 19.5f), V(19, 15.5f)));
                    break;
                }

                case Icon.Copy:
                    s.Add(new Box(V(8.5f, 8.5f), V(19.5f, 19.5f), 2, Stroke));
                    s.Add(Line(V(15.5f, 5.5f), V(15.5f, 4.5f), V(4.5f, 4.5f), V(4.5f, 15.5f), V(5.5f, 15.5f)));
                    break;
                case Icon.Timer:
                    s.Add(new Ring(V(12, 13), 7.5f, Stroke));
                    s.Add(Line(V(12, 13), V(12, 8.5f)));
                    s.Add(Line(V(9.5f, 3), V(14.5f, 3)));
                    break;
                case Icon.Target:
                    s.Add(new Ring(V(12, 12), 8.5f, Stroke));
                    s.Add(new Ring(V(12, 12), 4.5f, Stroke));
                    s.Add(new Disc(V(12, 12), 1.6f));
                    break;
                case Icon.Route:
                    s.Add(new Polyline(Stroke, V(3, 19), V(8, 11), V(12, 15), V(17, 6), V(21, 12)));
                    break;
                case Icon.Signal:
                    for (int i = 0; i < 4; i++) s.Add(new Box(V(4 + (i * 4.4f), 18 - (i * 4)), V(6.8f + (i * 4.4f), 20), 0.8f));
                    break;
                case Icon.Search:
                    s.Add(new Ring(V(10.5f, 10.5f), 6, Stroke));
                    s.Add(new Polyline(2.6f, V(15, 15), V(20, 20)));
                    break;
                case Icon.Skip:
                    s.Add(new Polygon(V(5, 5.5f), V(14, 12), V(5, 18.5f)));
                    s.Add(new Box(V(15.5f, 5.5f), V(18.5f, 18.5f), 1));
                    break;
                case Icon.Trainer:
                    s.Add(new Ring(V(15, 13), 5.5f, Stroke));
                    s.Add(new Disc(V(15, 13), 1.6f));
                    s.Add(Line(V(3.5f, 20), V(20.5f, 20)));
                    s.Add(Line(V(7, 20), V(10.5f, 9), V(15, 13)));
                    s.Add(Line(V(13, 20), V(10.5f, 9)));
                    break;
                case Icon.Group:
                    // Two riders: heads and shoulders, the front one larger.
                    s.Add(new Ring(V(9, 8), 3.4f, Stroke));
                    s.Add(new Polyline(Stroke, Arc(V(9, 21), 6.5f, 200, 340)));
                    s.Add(new Ring(V(16.5f, 9), 2.8f, Stroke));
                    s.Add(new Polyline(Stroke, Arc(V(17.5f, 20.5f), 5, 230, 330)));
                    break;
                case Icon.ThumbsUp:
                    // Cuff, fist and the thumb pointing up.
                    s.Add(new Box(V(3.5f, 10.5f), V(7, 20.5f), 1));
                    s.Add(new Box(V(8.5f, 10.5f), V(19.5f, 20.5f), 2.6f));
                    s.Add(new Polygon(V(8.5f, 11.5f), V(11.5f, 3.2f), V(14.4f, 4.2f), V(13.6f, 11.5f)));
                    s.Add(new Polyline(1.1f, V(10, 14.2f), V(18.5f, 14.2f)) { Cut = true });
                    s.Add(new Polyline(1.1f, V(10, 17.4f), V(18.5f, 17.4f)) { Cut = true });
                    break;
                case Icon.Fire:
                    s.Add(new Polygon(
                        V(12.5f, 2), V(15.5f, 6.5f), V(18.5f, 11), V(19, 15.5f), V(17, 19.5f), V(13.5f, 21.8f), V(10, 21.8f),
                        V(6.5f, 19.5f), V(5, 15.5f), V(5.8f, 11.5f), V(8.2f, 8.5f), V(9.3f, 12), V(11, 7.5f)));
                    s.Add(new Polygon(V(12, 12), V(14.6f, 16), V(14.3f, 18.8f), V(12, 20.2f), V(9.8f, 18.8f), V(9.6f, 16)) { Cut = true });
                    break;
                case Icon.Drop:
                    s.Add(new Disc(V(12, 15), 5.6f));
                    s.Add(new Polygon(V(12, 2.5f), V(16.9f, 12.3f), V(7.1f, 12.3f)));
                    break;
                case Icon.Tow:
                    // Two interlocked chain links (towed along by the group).
                    s.Add(new Polyline(Stroke, Stadium(V(8.7f, 15.3f), 8.6f, 3.3f)));
                    s.Add(new Polyline(Stroke, Stadium(V(15.3f, 8.7f), 8.6f, 3.3f)));
                    break;
                case Icon.Straight:
                    s.Add(Line(V(12, 21), V(12, 5)));
                    s.Add(new Polygon(V(12, 2.5f), V(17.5f, 9), V(6.5f, 9)));
                    break;
                case Icon.TurnLeft:
                case Icon.TurnRight:
                {
                    // Up from the bottom, then a bend towards the side, arrowhead pointing sideways.
                    float d = icon == Icon.TurnRight ? 1 : -1;
                    var bend = new List<Vector2> { V(12 - (d * 3), 21), V(12 - (d * 3), 13) };
                    bend.AddRange(Arc(V(12 + (d * 2), 13), 5, d > 0 ? 180 : 0, d > 0 ? 270 : -90, 10));
                    bend.Add(V(12 + (d * 5.5f), 8));
                    s.Add(new Polyline(Stroke * 1.15f, bend.ToArray()));
                    s.Add(new Polygon(V(12 + (d * 9.5f), 8), V(12 + (d * 4.5f), 2.5f), V(12 + (d * 4.5f), 13.5f)));
                    break;
                }

                case Icon.Hand:
                    // Waving hand: palm and four fingers, thumb to the side.
                    s.Add(new Disc(V(12.5f, 15.5f), 5.2f));
                    s.Add(new Polyline(2.6f, V(9, 13), V(8, 5.5f)));
                    s.Add(new Polyline(2.6f, V(11.6f, 12), V(11.6f, 3.8f)));
                    s.Add(new Polyline(2.6f, V(14.3f, 12), V(15, 4.5f)));
                    s.Add(new Polyline(2.6f, V(16.8f, 13.5f), V(18.6f, 7.5f)));
                    s.Add(new Polyline(2.6f, V(8.5f, 17.5f), V(4.5f, 13.5f)));
                    break;
            }

            return s;
        }
    }
}
