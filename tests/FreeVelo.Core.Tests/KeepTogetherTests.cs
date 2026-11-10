using FreeVelo.Core.Multiplayer;
using FreeVelo.Core.Physics;
using FreeVelo.Core.World;

namespace FreeVelo.Core.Tests;

public class KeepTogetherTests
{
    private const double Tick = 0.25;

    private static Route Flat() => new Route("flat", new[]
    {
        new RoutePoint(0, 0, 0, 0, 0, RoadSurface.Asphalt),
        new RoutePoint(100_000, 0, 100_000, 0, 0, RoadSurface.Asphalt),
    }, isLoop: false);

    /// <summary>Runs the group for <paramref name="seconds"/>, every rider reporting their W/kg (0 = stopped).</summary>
    private static void Ride(KeepTogetherGroup group, double seconds, params (string Id, double Wkg)[] riders)
    {
        for (double t = 0; t < seconds; t += Tick)
        {
            foreach (var (id, wkg) in riders) group.Report(id, wkg, wkg > 0 ? 85 : 0);
            group.Step(Tick);
        }
    }

    private static KeepTogetherGroup Group(params string[] ids)
    {
        var group = new KeepTogetherGroup(Flat());
        foreach (var id in ids) group.Join(id);
        return group;
    }

    [Fact]
    public void EqualRidersRideSideBySideAtTheAnchor()
    {
        var group = Group("anna", "ben");
        Ride(group, 120, ("anna", 2.5), ("ben", 2.5));

        Assert.Equal(2.5, group.AverageWkg, 6);
        Assert.Equal(group.AnchorDistanceM, group.PositionOf("anna").DistanceM, 1);
        Assert.Equal(group.PositionOf("anna").DistanceM, group.PositionOf("ben").DistanceM, 1);

        // 2.5 W/kg × 75 kg on the flat: about 30 km/h, like the solo physics.
        double expected = new RiderPhysics { RiderMassKg = 75, BikeMassKg = 8 }.SteadyStateSpeed(187.5, 0, RoadSurfaces.RollingResistance(RoadSurface.Asphalt));
        Assert.Equal(expected, group.AnchorSpeedMps, 1);
    }

    [Fact]
    public void StrongerRiderRidesAheadWeakerBehindWithinTheLimit()
    {
        var group = Group("strong", "mid", "weak");
        Ride(group, 120, ("strong", 3.5), ("mid", 2.5), ("weak", 1.5));

        double strong = group.PositionOf("strong").DistanceM - group.AnchorDistanceM;
        double mid = group.PositionOf("mid").DistanceM - group.AnchorDistanceM;
        double weak = group.PositionOf("weak").DistanceM - group.AnchorDistanceM;

        Assert.Equal(15, strong, 0); // +1 W/kg above average → 15 m ahead
        Assert.Equal(0, mid, 0);
        Assert.Equal(-15, weak, 0);

        // Much stronger: capped at 25 m.
        var big = Group("pro", "friend");
        Ride(big, 120, ("pro", 6), ("friend", 1.5));
        Assert.Equal(KeepTogetherGroup.MaxOffsetM, big.PositionOf("pro").DistanceM - big.AnchorDistanceM, 0);
    }

    [Fact]
    public void RiderWhoStopsIsTowedAtTheBackAndDoesNotSlowTheGroup()
    {
        var group = Group("anna", "ben", "cara");
        Ride(group, 60, ("anna", 2.5), ("ben", 2.5), ("cara", 2.5));
        double steady = new RiderPhysics { RiderMassKg = 75, BikeMassKg = 8 }.SteadyStateSpeed(187.5, 0, RoadSurfaces.RollingResistance(RoadSurface.Asphalt));

        // Cara stops to drink: her 0 W/kg leaves the average instead of dragging it down.
        Ride(group, 60, ("anna", 2.5), ("ben", 2.5), ("cara", 0));
        var cara = group.PositionOf("cara");
        Assert.True(cara.Towed);
        Assert.False(cara.Active);
        Assert.Equal(2.5, group.AverageWkg, 6);
        Assert.Equal(steady, group.AnchorSpeedMps, 1); // the group keeps the 2.5 W/kg speed
        Assert.Equal(-KeepTogetherGroup.MaxOffsetM, cara.DistanceM - group.AnchorDistanceM, 0);
        Assert.True(cara.SpeedMps > 5); // still moving with the group

        // She pedals again and rejoins the group.
        Ride(group, 60, ("anna", 2.5), ("ben", 2.5), ("cara", 2.5));
        Assert.True(group.PositionOf("cara").Active);
        Assert.Equal(0, group.PositionOf("cara").DistanceM - group.AnchorDistanceM, 0);
    }

    [Fact]
    public void ARiderWhoseAppGoesSilentCountsAsStopped()
    {
        var group = Group("anna", "ben");
        Ride(group, 30, ("anna", 2.5), ("ben", 2.5));
        Ride(group, KeepTogetherGroup.ActiveWindowS + 1, ("anna", 2.5)); // ben sends nothing anymore
        Assert.True(group.PositionOf("ben").Towed);
    }

    [Fact]
    public void NobodyPedalingTheGroupRollsToAStop()
    {
        var group = Group("anna");
        Ride(group, 60, ("anna", 2.5));
        Ride(group, 120, ("anna", 0));
        Assert.Equal(0, group.AverageWkg);
        Assert.True(group.AnchorSpeedMps < 0.5);
    }

    [Fact]
    public void JoinerAppearsAtTheBackAndMovesIntoPlace()
    {
        var group = Group("anna");
        Ride(group, 60, ("anna", 2.5));
        group.Join("late");
        Assert.Equal(-KeepTogetherGroup.MaxOffsetM, group.PositionOf("late").DistanceM - group.AnchorDistanceM, 6);

        Ride(group, 30, ("anna", 2.5), ("late", 2.5));
        Assert.Equal(0, group.PositionOf("late").DistanceM - group.AnchorDistanceM, 0);
    }

    [Fact]
    public void ClimbSlowsTheGroup()
    {
        var climb = new Route("climb", new[]
        {
            new RoutePoint(0, 0, 0, 0, 6, RoadSurface.Asphalt),
            new RoutePoint(20_000, 0, 20_000, 1200, 6, RoadSurface.Asphalt),
        }, isLoop: false);
        var up = new KeepTogetherGroup(climb);
        up.Join("anna");
        Ride(up, 120, ("anna", 2.5));

        var flat = Group("anna");
        Ride(flat, 120, ("anna", 2.5));
        Assert.True(up.AnchorSpeedMps < flat.AnchorSpeedMps * 0.5);
    }

    [Fact]
    public void BuiltInRoutesAreSharedById()
    {
        var a = BuiltInRoutes.Get(BuiltInRoutes.DefaultId);
        Assert.Same(a, BuiltInRoutes.Get(""));
        Assert.Same(a, BuiltInRoutes.Get("unknown"));
        Assert.InRange(a.LengthM, 24_000, 31_000);
    }

    [Fact]
    public void GroupRidesRoundTheMountain()
    {
        // A Keep Together lobby on a climb route: the anchor moves on (slower uphill) and laps the loop.
        var route = BuiltInRoutes.Get(BuiltInRoutes.StelvioId);
        Assert.True(route.IsLoop);
        var group = new KeepTogetherGroup(route, KeepTogetherGroup.MaxOffsetM);
        group.Join("anna");
        group.Join("ben");
        Ride(group, 300, ("anna", 3), ("ben", 3));
        Assert.InRange(group.AnchorDistanceM, 300, 2500); // climbing at 3 W/kg: well below flat-road speed
    }
}
