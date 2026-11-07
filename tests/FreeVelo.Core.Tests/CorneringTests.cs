using FreeVelo.Core.Multiplayer;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World;

namespace FreeVelo.Core.Tests;

public class CorneringTests
{
    [Fact]
    public void BendSpeedFollowsRadiusAndLean()
    {
        // 10 m hairpin at 30° lean: √(9.81 · tan 30° · 10) ≈ 7.5 m/s ≈ 27 km/h
        Assert.InRange(Cornering.BendSpeed(1 / 10.0) * 3.6, 26, 28);
        Assert.True(double.IsPositiveInfinity(Cornering.BendSpeed(0)));
        Assert.Equal(Cornering.BendSpeed(0.05), Cornering.BendSpeed(-0.05), 9);
    }

    [Fact]
    public void BrakesEarlyEnoughForAHairpin()
    {
        // Straight descent, then a 180° hairpin of 10 m radius.
        var points = new List<RoutePoint>();
        double d = 0;
        for (int i = 0; i <= 100; i++) points.Add(new RoutePoint(i * 5, 0, i * 5, -400 - (i * 0.4), -8, RoadSurface.Asphalt));
        d = 500;
        for (int i = 1; i <= 30; i++)
        {
            double a = Math.PI * i / 30;
            double x = 10 - (10 * Math.Cos(a)), z = 500 + (10 * Math.Sin(a));
            d += Math.PI * 10 / 30;
            points.Add(new RoutePoint(d, x, z, -440, -8, RoadSurface.Asphalt));
        }

        var route = new Route("hairpin", points, isLoop: false);
        var physics = new RiderPhysics { SpeedMps = 18 };
        double dist = 0, maxLean = 0;
        while (dist < 520)
        {
            const double dt = 1 / 60.0;
            physics.Step(0, -8, 0.004, dt);
            Cornering.Brake(physics, Cornering.SpeedLimit(route, dist), dt);
            dist += physics.SpeedMps * dt;
            maxLean = Math.Max(maxLean, Math.Abs(Route.LeanAngleDeg(physics.SpeedMps, route.CurvatureAt(dist))));
        }

        Assert.InRange(physics.SpeedMps * 3.6, 20, 29); // slowed from 65 km/h to hairpin speed
        Assert.True(maxLean <= Cornering.MaxLeanDeg + 1, $"lean {maxLean:0.0}°");
    }

    [Fact]
    public void NoBrakingOnTheHomeRoads()
    {
        var route = BuiltInRoutes.Get(BuiltInRoutes.DefaultId);
        for (double d = 0; d < route.LengthM; d += 20) Assert.True(Cornering.SpeedLimit(route, d) > 70 / 3.6);
    }

    [Fact]
    public void TheGroupBrakesToo()
    {
        // Keep Together round the Alpe d'Huez loop at 2.5 W/kg: the anchor never takes a bend faster than it is safe.
        var route = BuiltInRoutes.Get(BuiltInRoutes.AlpeId);
        var group = new KeepTogetherGroup(route);
        group.Join("anna");
        double maxLean = 0;
        for (double t = 0; t < 3 * 3600 && group.AnchorDistanceM < route.LengthM - 100; t += 0.25)
        {
            group.Report("anna", 2.5, 85);
            group.Step(0.25);
            maxLean = Math.Max(maxLean, Math.Abs(Route.LeanAngleDeg(group.AnchorSpeedMps, route.CurvatureAt(group.AnchorDistanceM))));
        }

        Assert.True(maxLean <= Cornering.MaxLeanDeg + 1, $"lean {maxLean:0.0}°");
    }
}
