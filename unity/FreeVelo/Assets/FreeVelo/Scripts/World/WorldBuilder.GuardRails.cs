using System.Collections.Generic;
using FreeVelo.Core.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreeVelo.World
{
    /// <summary>
    /// Guard rails (placed by <see cref="RoadFurniture.GuardRails"/>): a galvanised W-beam on posts every 2 m along the
    /// shoulder's outer edge, following the road's height; over the last 4 m at each end the beam slopes down into the
    /// ground. Each post stands in the ground as drawn (<see cref="Terrain.SurfaceHeight"/>, 0.4 m in). Merged into one
    /// mesh per 250 m cell, culled when small on screen.
    /// </summary>
    public sealed partial class WorldBuilder
    {
        private List<GuardRail> _rails = new List<GuardRail>();

        private const float RailCell = 250f;
        private const float RailBeamHeight = 0.5f;  // beam centre above the road surface
        private const float RailEndSlope = 4f;
        private const float PostSpacing = 2f;

        /// <summary>The bridge railing's line beyond the asphalt (see <see cref="BuildBridges"/>).</summary>
        private const float BridgeRailingOffset = 0.55f;

        private Material _galvanised;

        /// <summary>Galvanised steel: guard rails and bridge railings.</summary>
        private Material Galvanised()
        {
            if (_galvanised != null) return _galvanised;
            _galvanised = new Material(Mat(new Color(0.68f, 0.7f, 0.72f), null, 0.55f)) { name = "Galvanised" };
            if (_galvanised.HasProperty("_Metallic")) _galvanised.SetFloat("_Metallic", 0.55f);
            return _galvanised;
        }

        /// <summary>A guard rail stands on this side of the edge here (the delineator posts make way).</summary>
        private bool HasRail(int edge, int side, double d)
        {
            foreach (var r in _rails)
            {
                if (r.Edge == edge && r.Side == side && d >= r.FromD - 3 && d <= r.ToD + 3) return true;
            }

            return false;
        }

        // W-beam cross-section (u: outwards from the road, v: up), centre at (0, 0): two ridges towards the road.
        private static readonly Vector2[] BeamProfile =
        {
            new Vector2(0f, -0.155f), new Vector2(-0.045f, -0.115f), new Vector2(-0.05f, -0.075f), new Vector2(-0.012f, -0.02f),
            new Vector2(-0.012f, 0.02f), new Vector2(-0.05f, 0.075f), new Vector2(-0.045f, 0.115f), new Vector2(0f, 0.155f),
        };

        private void BuildGuardRails()
        {
            if (_rails == null || _rails.Count == 0) return;
            var cells = new Dictionary<(int, int), RailMesh>();
            foreach (var rail in _rails)
            {
                var e = Map.Edges[rail.Edge];
                float half = (float)e.Width / 2;
                var mid = RoadFurniture.At(e, (rail.FromD + rail.ToD) / 2);
                var key = (Mathf.FloorToInt((float)mid.X / RailCell), Mathf.FloorToInt((float)mid.Z / RailCell));
                if (!cells.TryGetValue(key, out var mesh)) cells[key] = mesh = new RailMesh();

                int n = Mathf.Max(2, Mathf.CeilToInt((float)(rail.Length / PostSpacing)) + 1);
                var centres = new Vector3[n];
                var outward = new Vector3[n];
                var grounds = new float[n];
                for (int k = 0; k < n; k++)
                {
                    double d = rail.FromD + (rail.Length * k / (n - 1));
                    var (x, z, h, rx, rz) = RoadFurniture.At(e, d);
                    var o = new Vector3((float)rx, 0f, (float)rz) * rail.Side;
                    // Where the rail meets a bridge railing it swings in to the railing's line over the last 4 m.
                    float fromStart = (float)(d - rail.FromD), toEnd = (float)(rail.ToD - d);
                    float joinT = 1f;
                    if (rail.JoinsAtStart) joinT = Mathf.Min(joinT, Mathf.SmoothStep(0f, 1f, fromStart / RailEndSlope));
                    if (rail.JoinsAtEnd) joinT = Mathf.Min(joinT, Mathf.SmoothStep(0f, 1f, toEnd / RailEndSlope));
                    float offset = Mathf.Lerp(BridgeRailingOffset, (float)RoadFurniture.RailOffset, joinT);
                    var p = new Vector3((float)x, 0f, (float)z) + (o * (half + offset));
                    float ground = (float)_terrain.SurfaceHeight(p.x, p.z);
                    float road = (float)h + 0.05f;
                    float beam = road + RailBeamHeight;

                    // Free ends slope down until the beam's top meets the ground.
                    float fromEnd = Mathf.Min(rail.JoinsAtStart ? float.MaxValue : fromStart, rail.JoinsAtEnd ? float.MaxValue : toEnd);
                    if (fromEnd < RailEndSlope)
                    {
                        float t = Mathf.SmoothStep(0f, 1f, fromEnd / RailEndSlope);
                        beam = Mathf.Lerp(ground - 0.12f, beam, t);
                    }

                    centres[k] = new Vector3(p.x, beam, p.z);
                    outward[k] = o;
                    grounds[k] = ground;
                }

                mesh.Beam(centres, outward);
                for (int k = 1; k < n - 1; k++)
                {
                    // Posts behind the beam, from 0.4 m in the ground to just under the beam's top.
                    float top = centres[k].y + 0.13f;
                    if (top - grounds[k] < 0.3f) continue; // the sloping end is in the ground here
                    var along = (centres[k + 1] - centres[k - 1]).normalized;
                    var basePoint = new Vector3(centres[k].x, grounds[k] - 0.4f, centres[k].z) + (outward[k] * 0.11f);
                    mesh.Box(basePoint, top - (grounds[k] - 0.4f), along, outward[k], 0.11f, 0.06f);
                    // Spacer block between post and beam
                    mesh.Box(new Vector3(centres[k].x, centres[k].y - 0.06f, centres[k].z) + (outward[k] * 0.05f), 0.12f, along, outward[k], 0.1f, 0.07f);
                }
            }

            var material = Galvanised();
            foreach (var pair in cells)
            {
                var go = new GameObject("GuardRails");
                go.transform.SetParent(_root.transform, false);
                var mesh = pair.Value.ToMesh();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { renderer }) });
                lod.RecalculateBounds();
            }
        }

        // Kerb cross-section (u: outwards from the asphalt's edge, v: up from the road surface), road side chamfered.
        private static readonly Vector2[] KerbProfile =
        {
            new Vector2(0f, -0.04f), new Vector2(0f, 0.08f), new Vector2(0.025f, 0.11f), new Vector2(0.16f, 0.11f), new Vector2(0.16f, -0.36f),
        };

        /// <summary>Concrete kerbs along the village streets (<see cref="RoadFurniture.Kerbs"/>): 11 cm high at the asphalt's edge.</summary>
        private void BuildKerbs(List<GuardRail> kerbs)
        {
            if (kerbs == null || kerbs.Count == 0) return;
            var cells = new Dictionary<(int, int), RailMesh>();
            foreach (var kerb in kerbs)
            {
                var e = Map.Edges[kerb.Edge];
                float half = (float)e.Width / 2;
                var mid = RoadFurniture.At(e, (kerb.FromD + kerb.ToD) / 2);
                var key = (Mathf.FloorToInt((float)mid.X / RailCell), Mathf.FloorToInt((float)mid.Z / RailCell));
                if (!cells.TryGetValue(key, out var mesh)) cells[key] = mesh = new RailMesh();
                int n = Mathf.Max(2, Mathf.CeilToInt((float)kerb.Length) + 1);
                var edges = new Vector3[n];
                var outward = new Vector3[n];
                for (int k = 0; k < n; k++)
                {
                    double d = kerb.FromD + (kerb.Length * k / (n - 1));
                    var (x, z, h, rx, rz) = RoadFurniture.At(e, d);
                    var o = new Vector3((float)rx, 0f, (float)rz) * kerb.Side;
                    edges[k] = new Vector3((float)x, (float)h + 0.05f, (float)z) + (o * (half - 0.02f));
                    outward[k] = o;
                }

                mesh.Extrude(edges, outward, KerbProfile, capEnds: true);
            }

            // Round the junction corners: along the arc, and 3 m on along each road's edge to meet the straight kerbs.
            foreach (int index in RoadFurniture.KerbedCorners(_terrain))
            {
                var c = _terrain.Corners[index];
                var ea = Map.Edges[c.EdgeA];
                var eb = Map.Edges[c.EdgeB];
                var points = new List<Vector3>();
                var outward = new List<Vector3>();
                void AddEdgePoint(RoadEdge e, bool start, int side, double s)
                {
                    var p = JunctionCorners.Side(e, start, s, side, 0);
                    var centre = JunctionCorners.Side(e, start, s, side, e.Width / 2);
                    var o = new Vector3((float)(p.X - centre.X), 0f, (float)(p.Z - centre.Z)).normalized;
                    points.Add(KerbBase(p.X, p.Y, p.Z) - (o * 0.02f));
                    outward.Add(o);
                }

                for (double t = 3; t > 0.25; t -= 0.5) AddEdgePoint(ea, c.StartA, c.SideA, c.ReachA + t);
                for (int j = c.ArcStart; j < c.ArcStart + c.ArcCount; j++)
                {
                    var o = new Vector3((float)(c.ArcCX - c.X[j]), 0f, (float)(c.ArcCZ - c.Z[j])).normalized;
                    points.Add(KerbBase(c.X[j], c.Y[j], c.Z[j]) - (o * 0.02f));
                    outward.Add(o);
                }

                for (double t = 0.5; t <= 3.01; t += 0.5) AddEdgePoint(eb, c.StartB, c.SideB, c.ReachB + t);
                var key = (Mathf.FloorToInt((float)c.CX / RailCell), Mathf.FloorToInt((float)c.CZ / RailCell));
                if (!cells.TryGetValue(key, out var mesh)) cells[key] = mesh = new RailMesh();
                mesh.Extrude(points.ToArray(), outward.ToArray(), KerbProfile, capEnds: true);
            }

            var material = Mat(new Color(0.74f, 0.74f, 0.72f), null, 0.1f);
            foreach (var pair in cells)
            {
                var go = new GameObject("Kerbs");
                go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = pair.Value.ToMesh();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.01f, new Renderer[] { renderer }) });
                lod.RecalculateBounds();
            }
        }

        /// <summary>The kerb's foot on a corner's edge: on the paving as the app lifts it (see <see cref="BuildJunctionCorners"/>).</summary>
        private Vector3 KerbBase(double x, double y, double z) =>
            new Vector3((float)x, Mathf.Max((float)y + 0.05f, (float)_terrain.Height(x, z) + 0.06f), (float)z);

        /// <summary>Vertices and triangles of the rails in one cell.</summary>
        private sealed class RailMesh
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<int> _t = new List<int>();

            /// <summary>The beam swept along the centres; both faces (it is seen from the road and from below).</summary>
            public void Beam(Vector3[] centres, Vector3[] outward)
            {
                int m = BeamProfile.Length;
                for (int face = 0; face < 2; face++)
                {
                    int start = _v.Count;
                    for (int k = 0; k < centres.Length; k++)
                    {
                        for (int j = 0; j < m; j++)
                        {
                            var pr = BeamProfile[j];
                            _v.Add(centres[k] + (outward[k] * pr.x) + (Vector3.up * pr.y));
                            // Profile normal (perpendicular to the profile's direction in the u/v plane), towards the road on the front face.
                            var a = BeamProfile[Mathf.Max(0, j - 1)];
                            var b = BeamProfile[Mathf.Min(m - 1, j + 1)];
                            var tangent = (outward[k] * (b.x - a.x)) + (Vector3.up * (b.y - a.y));
                            var normal = Vector3.Cross(tangent, Vector3.Cross(outward[k], Vector3.up)).normalized;
                            if (Vector3.Dot(normal, outward[k]) > 0) normal = -normal;
                            _n.Add(face == 0 ? normal : -normal);
                        }
                    }

                    for (int k = 0; k < centres.Length - 1; k++)
                    {
                        for (int j = 0; j < m - 1; j++)
                        {
                            int a = start + (k * m) + j, b = a + 1, c = a + m, d = c + 1;
                            if (face == 0) _t.AddRange(new[] { a, c, b, b, c, d });
                            else _t.AddRange(new[] { a, b, c, b, d, c });
                        }
                    }
                }
            }

            /// <summary>
            /// A closed profile swept along a line, flat-shaded (each profile side its own face), with end caps. The
            /// profile runs road side first, so the faces point away from its inside.
            /// </summary>
            public void Extrude(Vector3[] line, Vector3[] outward, Vector2[] profile, bool capEnds)
            {
                int m = profile.Length;
                for (int j = 0; j < m; j++)
                {
                    var pa = profile[j];
                    var pb = profile[(j + 1) % m];
                    int start = _v.Count;
                    for (int k = 0; k < line.Length; k++)
                    {
                        var a = line[k] + (outward[k] * pa.x) + (Vector3.up * pa.y);
                        var b = line[k] + (outward[k] * pb.x) + (Vector3.up * pb.y);
                        // face normal in the profile plane: (dv, -du) rotated into the world, pointing out of the profile
                        var n = ((outward[k] * (pb.y - pa.y)) - (Vector3.up * (pb.x - pa.x))).normalized;
                        _v.Add(a);
                        _v.Add(b);
                        _n.Add(-n);
                        _n.Add(-n);
                    }

                    for (int k = 0; k < line.Length - 1; k++)
                    {
                        int a = start + (k * 2), b = a + 1, c = a + 2, d = a + 3;
                        AddQuadFacing(a, b, c, d, _n[a]);
                    }
                }

                if (!capEnds) return;
                foreach (int k in new[] { 0, line.Length - 1 })
                {
                    var along = (line[Mathf.Min(line.Length - 1, k + 1)] - line[Mathf.Max(0, k - 1)]).normalized * (k == 0 ? -1f : 1f);
                    int start = _v.Count;
                    foreach (var p in profile)
                    {
                        _v.Add(line[k] + (outward[k] * p.x) + (Vector3.up * p.y));
                        _n.Add(along);
                    }

                    for (int j = 1; j < m - 1; j++) AddTriangleFacing(start, start + j, start + j + 1, along);
                }
            }

            private void AddQuadFacing(int a, int b, int c, int d, Vector3 normal)
            {
                AddTriangleFacing(a, b, c, normal);
                AddTriangleFacing(b, d, c, normal);
            }

            private void AddTriangleFacing(int a, int b, int c, Vector3 normal)
            {
                var n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                if (Vector3.Dot(n, normal) >= 0) _t.AddRange(new[] { a, b, c });
                else _t.AddRange(new[] { a, c, b });
            }

            /// <summary>An upright box: <paramref name="basePoint"/> at the bottom centre.</summary>
            public void Box(Vector3 basePoint, float height, Vector3 along, Vector3 outward, float length, float width)
            {
                var a = along * (length / 2);
                var o = outward * (width / 2);
                var up = Vector3.up * height;
                var corners = new[] { basePoint - a - o, basePoint + a - o, basePoint + a + o, basePoint - a + o };
                for (int s = 0; s < 4; s++)
                {
                    var p0 = corners[s];
                    var p1 = corners[(s + 1) % 4];
                    var normal = Vector3.Cross(p1 - p0, Vector3.up).normalized;
                    int i = _v.Count;
                    _v.Add(p0);
                    _v.Add(p1);
                    _v.Add(p0 + up);
                    _v.Add(p1 + up);
                    for (int k = 0; k < 4; k++) _n.Add(normal);
                    // outward-facing whichever way the corners run
                    if (Vector3.Dot(normal, ((p0 + p1) / 2) - basePoint) >= 0) _t.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                    else _t.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
                }

                int top = _v.Count;
                foreach (var c in corners) _v.Add(c + up);
                for (int k = 0; k < 4; k++) _n.Add(Vector3.up);
                _t.AddRange(new[] { top, top + 2, top + 1, top, top + 3, top + 2 });
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = "GuardRails", indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
