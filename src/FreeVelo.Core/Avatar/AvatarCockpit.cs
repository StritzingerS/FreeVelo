using System;
using System.Collections.Generic;
using System.Numerics;
using static FreeVelo.Core.Avatar.SdfShapes;

namespace FreeVelo.Core.Avatar
{
    /// <summary>
    /// Cockpit: angular stem with faceplate and bolts, headset cap, aero bar with flat tops, compact drops wrapped in
    /// bar tape (raised overlap ridges), end plugs.
    /// </summary>
    public sealed partial class AvatarModel
    {
        private static readonly Vector3 BarClamp = new Vector3(0, 0.877f, 0.482f);

        private void BuildCockpit(AvatarMaterial blackMat, AvatarMaterial tapeMat, AvatarMaterial metalMat)
        {
            var carbonMat = Mat("barcarbon", V(0.07f, 0.07f, 0.075f), 0.55f);
            BuildStem(carbonMat, metalMat);

            // Bar centre line, right half (left is mirrored). Cross-section: (forward/up axes of the ellipse).
            var points = new[]
            {
                V(0f, 0.877f, 0.482f), V(0.035f, 0.877f, 0.482f), V(0.07f, 0.8775f, 0.483f), V(0.13f, 0.8785f, 0.486f),
                V(0.168f, 0.8795f, 0.493f), V(0.188f, 0.8805f, 0.507f), V(0.199f, 0.8812f, 0.527f), V(0.204f, 0.882f, 0.552f),
                V(0.208f, 0.876f, 0.584f), V(0.211f, 0.853f, 0.605f), V(0.212f, 0.815f, 0.614f), V(0.212f, 0.775f, 0.607f),
                V(0.212f, 0.746f, 0.583f), V(0.212f, 0.732f, 0.548f), V(0.212f, 0.728f, 0.505f),
            };
            var radii = new[]
            {
                R(0.0159f, 0.0159f), R(0.0162f, 0.0145f), R(0.021f, 0.0085f), R(0.022f, 0.0078f),
                R(0.018f, 0.0095f), R(0.0128f, 0.0118f), R(0.012f, 0.012f), R(0.012f, 0.012f),
                R(0.012f, 0.012f), R(0.012f, 0.012f), R(0.012f, 0.012f), R(0.012f, 0.012f),
                R(0.012f, 0.012f), R(0.012f, 0.012f), R(0.012f, 0.012f),
            };
            const int tapeStart = 5; // tape from the start of the bend to the bar end

            var dense = Densify(points, radii, 0.0018f, out var denseRadii, out var denseIndex);
            int split = Array.FindIndex(denseIndex, i => i >= tapeStart);
            for (int side = -1; side <= 1; side += 2)
            {
                var bar = new MeshData();
                var tape = new MeshData();
                TubeAlong(bar, dense, denseRadii, 0, split, side, 0f, 18, capEnd: false);
                TubeAlong(tape, dense, denseRadii, split - 1, dense.Count - 1, side, 0.0016f, 20, capEnd: false);
                B(AvatarNode.Bike, carbonMat).Append(bar);
                B(AvatarNode.Bike, tapeMat).Append(tape);

                // End plug with a rim.
                var end = dense[dense.Count - 1];
                var dir = Vector3.Normalize(dense[dense.Count - 1] - dense[dense.Count - 4]);
                var c = new Vector3(side * end.X, end.Y, end.Z);
                var d = new Vector3(side * dir.X, dir.Y, dir.Z);
                var plug = Union(RoundCone(c - (d * 0.002f), c + (d * 0.0035f), 0.0135f, 0.0128f));
                Sculpt(AvatarNode.Bike, plug, c - new Vector3(0.025f), c + new Vector3(0.025f), 0.0009f, 260, blackMat);
            }
        }

        private void BuildStem(AvatarMaterial carbonMat, AvatarMaterial metalMat)
        {
            var steerer = new Vector3(0, 0.862f, 0.384f);
            var stemDir = Vector3.Normalize(BarClamp - steerer);
            var stemRot = Rot.LookRotation(stemDir, Vector3.Normalize(Vector3.UnitY - (Vector3.Dot(Vector3.UnitY, stemDir) * stemDir)));
            var inv = stemRot.Transposed;
            float len = Vector3.Distance(steerer, BarClamp);

            Sdf stem = p =>
            {
                // Steerer clamp (vertical cylinder), tapered square body, bar clamp (cylinder along x).
                var q = p - steerer;
                float clamp = MathF.Max(new Vector2(q.X, q.Z).Length() - 0.0205f, MathF.Abs(q.Y + 0.002f) - 0.0215f);
                var l = inv * (p - steerer);
                float t = Math.Clamp(l.Z / len, 0f, 1f);
                float body = RoundedBox2(l - new Vector3(0, 0, len / 2f), new Vector3(0.0165f - (0.003f * t), 0.0155f - (0.002f * t), len / 2f), 0.006f);
                var b = p - BarClamp;
                float barClamp = MathF.Max(new Vector2(b.Y, b.Z).Length() - 0.0215f, MathF.Abs(b.X) - 0.0225f);
                float shape = SMin(SMin(clamp, body, 0.006f), barClamp, 0.006f);

                // Faceplate seam groove, top cap.
                float cap = MathF.Max(new Vector2(q.X, q.Z).Length() - 0.019f, MathF.Abs(q.Y - 0.0225f) - 0.0022f);
                return MathF.Min(shape, cap);
            };

            var head = new Vector3(0, HeadTop.Y + 0.006f, HeadTop.Z - 0.002f);
            Sdf headset = p =>
            {
                var q = p - head;
                float cover = MathF.Max(new Vector2(q.X, q.Z).Length() - (0.0235f - (q.Y * 0.15f)), MathF.Abs(q.Y) - 0.008f); // tapered headset cover
                float spacer = MathF.Max(new Vector2(q.X, q.Z - 0.001f).Length() - 0.0205f, MathF.Abs(q.Y - 0.0135f) - 0.0055f);
                return MathF.Min(cover, spacer);
            };

            Sculpt(AvatarNode.Bike, Union(stem, headset), V(-0.04f, 0.81f, 0.34f), V(0.04f, 0.92f, 0.52f), 0.0012f, 2600, carbonMat);

            // Bolts: two on each side of the steerer clamp, four on the faceplate, one in the top cap.
            var bolts = new List<Sdf>();
            for (int s = -1; s <= 1; s += 2)
            {
                for (int k = -1; k <= 1; k += 2)
                {
                    var bolt = steerer + new Vector3(s * 0.0185f, k * 0.0095f, -0.012f);
                    bolts.Add(RoundCone(bolt, bolt + new Vector3(s * 0.0035f, 0, 0), 0.0032f, 0.0032f));
                    var face = BarClamp + new Vector3(s * 0.013f, k * 0.0125f, 0.0205f);
                    bolts.Add(RoundCone(face, face + new Vector3(0, 0, 0.003f), 0.003f, 0.003f));
                }
            }

            var top = steerer + new Vector3(0, 0.0245f, 0);
            bolts.Add(RoundCone(top, top + new Vector3(0, 0.002f, 0), 0.0045f, 0.0045f));
            Sculpt(AvatarNode.Bike, Union(bolts.ToArray()), V(-0.035f, 0.84f, 0.36f), V(0.035f, 0.9f, 0.51f), 0.0007f, 700, metalMat);
        }

        private static float RoundedBox2(Vector3 q, Vector3 half, float radius)
        {
            var d = Vector3.Abs(q) - (half - new Vector3(radius));
            return Vector3.Max(d, Vector3.Zero).Length() + MathF.Min(MathF.Max(d.X, MathF.Max(d.Y, d.Z)), 0f) - radius;
        }

        /// <summary>Catmull-Rom densified centre line with interpolated radii; index maps back to control points.</summary>
        private static List<Vector3> Densify(Vector3[] points, Vector2[] radii, float step, out List<Vector2> outRadii, out int[] controlIndex)
        {
            var result = new List<Vector3>();
            outRadii = new List<Vector2>();
            var index = new List<int>();
            int n = points.Length;
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = points[Math.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(n - 1, i + 2)];
                int steps = Math.Max(2, (int)MathF.Ceiling(Vector3.Distance(p1, p2) / step));
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    result.Add(0.5f * ((2f * p1) + ((p2 - p0) * t) + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t * t) + (((3f * p1) - p0 - (3f * p2) + p3) * t * t * t)));
                    float st = t * t * (3f - (2f * t));
                    outRadii.Add(Vector2.Lerp(radii[i], radii[i + 1], st));
                    index.Add(i);
                }
            }

            result.Add(points[n - 1]);
            outRadii.Add(radii[n - 1]);
            index.Add(n - 1);
            controlIndex = index.ToArray();
            return result;
        }

        /// <summary>
        /// Elliptical tube along a centre line with a parallel-transport frame (starting with the ellipse's first axis
        /// pointing forward). With <paramref name="tapeRidge"/> &gt; 0 the surface gets the raised overlap edges of
        /// spirally wrapped bar tape. <paramref name="side"/> = −1 mirrors to the left half.
        /// </summary>
        private static void TubeAlong(MeshData mesh, List<Vector3> path, List<Vector2> radii, int from, int to, int side, float tapeRidge, int sides, bool capEnd)
        {
            int count = to - from + 1;
            var frames = new (Vector3 T, Vector3 U, Vector3 W)[path.Count];
            var t0 = Vector3.Normalize(path[1] - path[0]);
            var u0 = Vector3.UnitZ - (Vector3.Dot(Vector3.UnitZ, t0) * t0);
            u0 = Vector3.Normalize(u0);
            for (int i = 0; i < path.Count; i++)
            {
                var t = Vector3.Normalize(path[Math.Min(path.Count - 1, i + 1)] - path[Math.Max(0, i - 1)]);
                if (i == 0)
                {
                    frames[i] = (t, u0, Vector3.Cross(t, u0));
                    continue;
                }

                // Parallel transport: rotate the previous U by the rotation between the tangents.
                var prev = frames[i - 1];
                var u = prev.U - (Vector3.Dot(prev.U, t) * t);
                u = Vector3.Normalize(u);
                frames[i] = (t, u, Vector3.Cross(t, u));
            }

            float arc = 0f;
            int start = mesh.Vertices.Count;
            for (int i = from; i <= to; i++)
            {
                if (i > from) arc += Vector3.Distance(path[i], path[i - 1]);
                var (_, u, w) = frames[i];
                var r = radii[i];
                for (int j = 0; j <= sides; j++)
                {
                    float a = MathF.PI * 2f * j / sides;
                    float grow = 0f;
                    if (tapeRidge > 0f)
                    {
                        // Tape wraps every 21 mm; the overlap edge forms a small ridge followed by a flat run.
                        float phase = (arc / 0.021f) + (a / (MathF.PI * 2f));
                        float f = phase - MathF.Floor(phase);
                        grow = tapeRidge * (f < 0.12f ? MathF.Sin(f / 0.12f * MathF.PI) * 0.7f : 0f) + (tapeRidge * 0.6f);
                    }

                    var p = path[i] + (u * (MathF.Cos(a) * (r.X + grow))) + (w * (MathF.Sin(a) * (r.Y + grow)));
                    mesh.Vertices.Add(new Vector3(side * p.X, p.Y, p.Z));
                    mesh.Normals.Add(Vector3.Zero);
                }
            }

            int row = sides + 1;
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = start + (i * row) + j;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;

                    // Mirroring flips the winding; keep Cross(b − a, c − a) pointing outwards.
                    if (side > 0)
                    {
                        mesh.Triangles.AddRange(new[] { a, c, b, b, c, d });
                    }
                    else
                    {
                        mesh.Triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
                }
            }

            // Orientation check on the first quad: flip everything if it points inwards.
            var v = mesh.Vertices;
            int ta = mesh.Triangles[mesh.Triangles.Count - (6 * sides * (count - 1))];
            int tb = mesh.Triangles[mesh.Triangles.Count - (6 * sides * (count - 1)) + 1];
            int tc = mesh.Triangles[mesh.Triangles.Count - (6 * sides * (count - 1)) + 2];
            var n = Vector3.Cross(v[tb] - v[ta], v[tc] - v[ta]);
            var centre = new Vector3(side * path[from].X, path[from].Y, path[from].Z);
            if (Vector3.Dot(n, v[ta] - centre) < 0)
            {
                int first = mesh.Triangles.Count - (6 * sides * (count - 1));
                for (int k = first; k < mesh.Triangles.Count; k += 3)
                {
                    (mesh.Triangles[k + 1], mesh.Triangles[k + 2]) = (mesh.Triangles[k + 2], mesh.Triangles[k + 1]);
                }
            }
        }

        /// <summary>
        /// STI lever after the reference photos: the hood is a side-profile extrusion (low grip on the bar rising to a
        /// tall, forward-overhanging horn, body reaching below the bar at the front); the blade hangs from a pivot in
        /// the front of the body, S-curved in front of the drop and flaring outwards; the shift paddle sits behind it.
        /// </summary>
        private void BuildShifter(int s, AvatarMaterial blackMat, AvatarMaterial leverMat)
        {
            var hoodMat = Mat("hood", V(0.085f, 0.085f, 0.09f), 0.22f);
            var bodyMat = Mat("stibody", V(0.06f, 0.06f, 0.065f), 0.7f);
            float x0 = s * 0.206f;

            // Side profile (z, y), counter-clockwise; rounded by 4.5 mm.
            var profile = new[]
            {
                new Vector2(0.535f, 0.871f), new Vector2(0.56f, 0.866f), new Vector2(0.585f, 0.858f), new Vector2(0.607f, 0.848f),
                new Vector2(0.622f, 0.852f), new Vector2(0.633f, 0.868f), new Vector2(0.635f, 0.886f), new Vector2(0.635f, 0.91f), new Vector2(0.628f, 0.928f),
                new Vector2(0.614f, 0.934f), new Vector2(0.6f, 0.926f), new Vector2(0.588f, 0.913f), new Vector2(0.565f, 0.905f),
                new Vector2(0.535f, 0.897f),
            };
            const float round = 0.0085f;
            Sdf body = p =>
            {
                float d2 = Polygon(profile, new Vector2(p.Z, p.Y)) + round;
                float t = Math.Clamp((p.Z - 0.585f) / 0.03f, 0f, 1f);
                float hw = 0.0145f - (0.003f * t * t * (3f - (2f * t))) - round;
                float dx = MathF.Abs(p.X - x0) - hw;
                return new Vector2(MathF.Max(d2, 0f), MathF.Max(dx, 0f)).Length() + MathF.Min(MathF.Max(d2, dx), 0f) - round;
            };
            var dome = Ellipsoid(V(x0, 0.927f, 0.618f), V(0.0118f, 0.0125f, 0.0165f), Rot.Euler(-20, 0, 0)); // rounded horn, overhanging forward
            Sdf hood = p => SMin(body(p), dome(p), 0.008f);
            hood = SdfShapes.SmoothSubtract(hood, Ellipsoid(V(x0 - (s * 0.017f), 0.898f, 0.565f), V(0.006f, 0.008f, 0.026f)), 0.006f); // thumb waist on the inside
            Sculpt(AvatarNode.Bike, hood, V(x0 - 0.03f, 0.845f, 0.51f), V(x0 + 0.03f, 0.95f, 0.65f), 0.001f, 1600, hoodMat, (p => 0.598f - p.Z, bodyMat));

            // Blade: from the pivot inside the body front, S-curve in front of the drop, flaring outwards at the tip.
            var path = new[]
            {
                V(x0, 0.888f, 0.628f), V(x0, 0.862f, 0.637f), V(x0 + (s * 0.001f), 0.832f, 0.645f), V(x0 + (s * 0.003f), 0.802f, 0.647f),
                V(x0 + (s * 0.006f), 0.774f, 0.643f), V(x0 + (s * 0.009f), 0.752f, 0.634f), V(x0 + (s * 0.011f), 0.741f, 0.625f),
            };
            float[] halfW = { 0.0105f, 0.0098f, 0.0088f, 0.008f, 0.0072f, 0.0064f, 0.0055f };
            float[] halfT = { 0.0085f, 0.0068f, 0.0052f, 0.0044f, 0.0039f, 0.0036f, 0.0034f };
            // Smooth the centre line (Catmull-Rom) so the blade has no facets.
            var radii = new Vector2[path.Length];
            for (int i = 0; i < path.Length; i++) radii[i] = new Vector2(halfW[i], halfT[i]);
            var dense = Densify(path, radii, 0.004f, out var denseRadii, out _);
            var parts = new List<Sdf>();
            for (int i = 0; i < dense.Count - 1; i++)
            {
                var t = Vector3.Normalize(dense[i + 1] - dense[i]);
                var depth = Vector3.Normalize(Vector3.Cross(t, Vector3.UnitX));
                float half = Vector3.Distance(dense[i], dense[i + 1]) / 2f;
                var r = (denseRadii[i] + denseRadii[i + 1]) / 2f;
                parts.Add(RoundBox((dense[i] + dense[i + 1]) / 2f, V(r.X, r.Y, half + 0.002f), MathF.Min(r.Y * 0.9f, 0.0034f), Rot.LookRotation(t, depth)));
            }

            var blade = Blend(0.003f, parts.ToArray());

            // Shift paddle behind the blade (between blade and drop), shorter.
            var paddle = RoundBox(V(x0 - (s * 0.001f), 0.83f, 0.6365f), V(0.0068f, 0.026f, 0.0022f), 0.002f, Rot.Euler(-10, 0, 0));
            Sculpt(AvatarNode.Bike, Union(blade, paddle), V(x0 - 0.03f, 0.725f, 0.6f), V(x0 + 0.04f, 0.9f, 0.665f), 0.0009f, 1400, leverMat);
        }

        /// <summary>Signed distance to a closed 2D polygon (negative inside).</summary>
        private static float Polygon(Vector2[] v, Vector2 p)
        {
            float d = Vector2.DistanceSquared(p, v[0]);
            float sign = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - (e * Math.Clamp(Vector2.Dot(w, e) / Vector2.Dot(e, e), 0f, 1f));
                d = MathF.Min(d, Vector2.Dot(b, b));
                bool c1 = p.Y >= v[i].Y, c2 = p.Y < v[j].Y, c3 = (e.X * w.Y) > (e.Y * w.X);
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
            }

            return sign * MathF.Sqrt(d);
        }
    }
}
