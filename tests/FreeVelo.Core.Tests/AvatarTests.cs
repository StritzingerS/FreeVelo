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
    [Fact]
    public void BuildsAllNodesWithOutwardFacingGeometry()
    {
        var model = AvatarModel.Build();
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
