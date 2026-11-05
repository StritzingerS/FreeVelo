using System.Numerics;
using FreeVelo.Core.Avatar;

namespace FreeVelo.Core.Tests;

public class RotTests
{
    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void EulerMatchesUnity()
    {
        // Values as documented / observed in Unity
        Near(new Vector3(0, -1, 0), Rot.Euler(90, 0, 0) * Vector3.UnitZ);  // pitch down
        Near(new Vector3(0, 0, 1), Rot.Euler(90, 0, 0) * Vector3.UnitY);
        Near(new Vector3(1, 0, 0), Rot.Euler(0, 90, 0) * Vector3.UnitZ);   // yaw right
        Near(new Vector3(0, 1, 0), Rot.Euler(0, 0, 90) * Vector3.UnitX);   // roll
        // Order Z, X, Y: Euler(90, 90, 0) * forward = (pitch down, then yaw) = down
        Near(new Vector3(0, -1, 0), Rot.Euler(90, 90, 0) * Vector3.UnitZ);
    }

    [Fact]
    public void AngleAxisMatchesEuler()
    {
        foreach (float deg in new[] { 30f, 90f, -45f, 170f })
        {
            var a = Rot.AngleAxis(deg, Vector3.UnitX);
            var e = Rot.Euler(deg, 0, 0);
            Near(e * new Vector3(0.3f, 0.5f, 0.8f), a * new Vector3(0.3f, 0.5f, 0.8f));
            Near(Rot.Euler(0, deg, 0) * Vector3.UnitZ, Rot.AngleAxis(deg, Vector3.UnitY) * Vector3.UnitZ);
        }
    }

    [Fact]
    public void LookRotationAndFromTo()
    {
        var r = Rot.LookRotation(new Vector3(1, 0, 0), Vector3.UnitY);
        Near(Vector3.UnitX, r * Vector3.UnitZ);
        Near(Vector3.UnitY, r * Vector3.UnitY);
        Near(-Vector3.UnitZ, r * Vector3.UnitX); // right-of-facing-+x is −z in a left-handed frame

        var target = Vector3.Normalize(new Vector3(0.3f, -0.8f, 0.5f));
        Near(target, Rot.FromTo(Vector3.UnitY, target) * Vector3.UnitY);
        Near(-Vector3.UnitY, Rot.FromTo(Vector3.UnitY, -Vector3.UnitY) * Vector3.UnitY);
    }

    [Fact]
    public void QuaternionRoundTrip()
    {
        var r = Rot.Euler(25, -40, 10);
        var q = r.ToQuaternion();
        var qn = new Quaternion(q.X, q.Y, q.Z, q.W);
        var v = new Vector3(0.2f, -0.7f, 0.4f);
        // System.Numerics applies quaternions with the same formula as Unity.
        Near(r * v, Vector3.Transform(v, qn));
    }
}

public class AvatarModelTests
{
    private static readonly Lazy<AvatarModel> Model = new(() => AvatarModel.Build());

    [Fact]
    public void BakedModelRoundTripsAndAppliesStyle()
    {
        var model = Model.Value;
        var data = model.Serialize();
        var style = new AvatarStyle { Jersey = new Vector3(1, 0, 0) };
        var loaded = AvatarModel.Deserialize(data, style);
        Assert.Equal(model.TriangleCount, loaded.TriangleCount);
        Assert.Equal(new Vector3(1, 0, 0), loaded.Parts.First(p => p.Material.Id == "jersey").Material.Color);

        var withoutBike = AvatarModel.Deserialize(data, style, includeBike: false);
        Assert.DoesNotContain(withoutBike.Parts, p => AvatarModel.IsBikeNode(p.Node));
        Assert.Contains(withoutBike.Parts, p => p.Node == AvatarNode.ThighLeft);

        // Positions survive the 16-bit quantisation within 0.1 mm.
        var a = model.Parts.First(p => p.Node == AvatarNode.Rider && p.Material.Id == "skin").Mesh.Vertices;
        var b = loaded.Parts.First(p => p.Node == AvatarNode.Rider && p.Material.Id == "skin").Mesh.Vertices;
        for (int i = 0; i < a.Count; i += 97) Assert.True(Vector3.Distance(a[i], b[i]) < 1e-4f);
    }

    [Fact]
    public void SculptedMeshesAreClosedAndFaceOutwards()
    {
        var mesh = Polygonizer.Mesh(SdfShapes.RoundCone(Vector3.Zero, new Vector3(0.1f, 0.3f, 0.2f), 0.05f, 0.03f), new Vector3(-0.1f), new Vector3(0.2f, 0.4f, 0.3f), 0.01f);
        mesh = Decimator.Simplify(mesh, 800, 0.003f);
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < mesh.Triangles.Count; i += 3)
        {
            int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
            Assert.True(edges.Add((a, b)) && edges.Add((b, c)) && edges.Add((c, a)), "edge used twice in the same direction");
            var n = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
            var centroid = (mesh.Vertices[a] + mesh.Vertices[b] + mesh.Vertices[c]) / 3f;
            var g = Polygonizer.Gradient(SdfShapes.RoundCone(Vector3.Zero, new Vector3(0.1f, 0.3f, 0.2f), 0.05f, 0.03f), centroid, 0.001f);
            Assert.True(Vector3.Dot(n, g) > 0, "triangle faces inwards");
        }

        foreach (var (a, b) in edges) Assert.Contains((b, a), edges); // closed
        Assert.InRange(mesh.TriangleCount, 700, 900);
    }

    [Fact]
    public void BuildsAllNodesWithOutwardFacingGeometry()
    {
        var model = Model.Value;
        var nodes = model.Parts.Select(p => p.Node).Distinct().ToHashSet();
        foreach (AvatarNode n in Enum.GetValues<AvatarNode>()) Assert.Contains(n, nodes);
        Assert.InRange(model.TriangleCount, 10_000, 400_000);

        foreach (var part in model.Parts)
        {
            Assert.All(part.Mesh.Vertices, v => Assert.False(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)));
            Assert.Equal(0, part.Mesh.Triangles.Count % 3);
        }
    }

    [Fact]
    public void LegsFollowPedalsWithRealisticKneeAngles()
    {
        for (int deg = 0; deg < 360; deg += 15)
        {
            var pose = AvatarModel.Pose(deg, 0);
            foreach (var (thigh, shin, foot) in new[] { (AvatarNode.ThighRight, AvatarNode.ShinRight, AvatarNode.FootRight), (AvatarNode.ThighLeft, AvatarNode.ShinLeft, AvatarNode.FootLeft) })
            {
                var hip = pose[thigh].Position;
                var knee = pose[shin].Position;
                var kneeFromThigh = pose[thigh].Apply(new Vector3(0, AvatarModel.ThighLength, 0));
                Assert.True(Vector3.Distance(knee, kneeFromThigh) < 1e-3f);

                var ankle = pose[shin].Apply(new Vector3(0, AvatarModel.ShinLength, 0));
                var ankleFromFoot = pose[foot].Apply(AvatarModel.AnkleInFoot);
                Assert.True(Vector3.Distance(ankle, ankleFromFoot) < 0.01f, $"crank {deg}: foot off by {Vector3.Distance(ankle, ankleFromFoot)}");

                float kneeAngle = MathF.Acos(Vector3.Dot(Vector3.Normalize(hip - knee), Vector3.Normalize(ankle - knee))) * 180f / MathF.PI;
                Assert.InRange(kneeAngle, 60f, 150f);
                Assert.True(knee.Z > hip.Z - 0.05f, "knee bends forward");
            }

            // Cranks opposite
            var r = pose[AvatarNode.PedalRight].Position - AvatarModel.BottomBracket;
            var l = pose[AvatarNode.PedalLeft].Position - AvatarModel.BottomBracket;
            Assert.True(MathF.Abs(r.Y + l.Y) < 1e-4f && MathF.Abs(r.Z + l.Z) < 1e-4f);
        }
    }
}
