using FreeVelo.Core.World;

namespace FreeVelo.Core.Tests;

public class RouteTests
{
    [Theory]
    [InlineData(1, 10_000, 0.5)]
    [InlineData(42, 5_000, 1.0)]
    [InlineData(7, 20_000, 0.0)]
    public void GeneratedLoopIsClosedAndSmooth(int seed, double length, double hilliness)
    {
        var route = ProceduralRoutes.Generate(seed, length, hilliness);
        var first = route.Points[0];
        var last = route.Points[^1];

        Assert.True(route.IsLoop);
        Assert.Equal(length, route.LengthM, 6);
        Assert.InRange(Math.Sqrt(Math.Pow(first.X - last.X, 2) + Math.Pow(first.Z - last.Z, 2)), 0, 0.5);
        Assert.InRange(Math.Abs(first.ElevationM - last.ElevationM), 0, 0.5);
        Assert.All(route.Points, p => Assert.InRange(p.GradePercent, -8.5 * hilliness - 0.1, 8.5 * hilliness + 0.1));
        Assert.All(route.Points.Zip(route.Points.Skip(1)), p =>
            Assert.InRange(Math.Sqrt(Math.Pow(p.Second.X - p.First.X, 2) + Math.Pow(p.Second.Z - p.First.Z, 2)), 2.5, 7.5));
    }

    [Fact]
    public void SameSeedSameRoute()
    {
        var a = ProceduralRoutes.Generate(3);
        var b = ProceduralRoutes.Generate(3);
        Assert.Equal(a.Points[500].X, b.Points[500].X);
        Assert.NotEqual(a.Points[500].X, ProceduralRoutes.Generate(4).Points[500].X);
    }

    [Fact]
    public void HillyRouteHasAscent()
    {
        Assert.True(ProceduralRoutes.Generate(1, 10_000, 1.0).TotalAscentM > 50);
        Assert.Equal(0, ProceduralRoutes.Generate(1, 10_000, 0.0).TotalAscentM, 6);
    }

    [Fact]
    public void SampleInterpolatesAndWrapsOnLoops()
    {
        var route = ProceduralRoutes.Generate(1, 1000, 0.5);
        var p0 = route.Points[0];
        var p1 = route.Points[1];

        var mid = route.Sample((p0.DistanceM + p1.DistanceM) / 2);
        Assert.Equal((p0.X + p1.X) / 2, mid.X, 6);
        Assert.Equal((p0.ElevationM + p1.ElevationM) / 2, mid.Y, 6);

        var wrapped = route.Sample(route.LengthM + 2.5);
        Assert.Equal(mid.X, wrapped.X, 6);
        Assert.Equal(route.Sample(-2.5).X, route.Sample(route.LengthM - 2.5).X, 6);
    }

    [Fact]
    public void OpenRouteIsClamped()
    {
        var points = new[]
        {
            new RoutePoint(0, 0, 0, 100, 0, Physics.RoadSurface.Asphalt),
            new RoutePoint(100, 0, 100, 105, 5, Physics.RoadSurface.Gravel),
        };
        var route = new Route("line", points, isLoop: false);

        Assert.Equal(105, route.Sample(500).Y);
        Assert.Equal(100, route.Sample(-10).Y);
        Assert.Equal(0, route.Sample(50).HeadingRad, 6); // heading +Z
        Assert.Equal(Physics.RoadSurface.Gravel, route.Sample(80).Surface);
    }

    [Fact]
    public void LeanAngleIsRealistic()
    {
        // 40 km/h through a 50 m radius bend: tan(lean) = v²/(gR) ≈ 0.25 → about 14°.
        double v = 40 / 3.6;
        Assert.InRange(Route.LeanAngleDeg(v, 1.0 / 50), 13.5, 14.5);
        Assert.InRange(Route.LeanAngleDeg(v, -1.0 / 50), -14.5, -13.5);
        Assert.Equal(0, Route.LeanAngleDeg(0, 1.0 / 10));
        Assert.Equal(35, Route.LeanAngleDeg(20, 1.0 / 5)); // limited
    }

    [Fact]
    public void CurvatureOfACircleIsOneOverRadius()
    {
        const double r = 100;
        var points = new List<RoutePoint>();
        for (int i = 0; i <= 720; i++)
        {
            double a = i * Math.PI * 2 / 720;
            points.Add(new RoutePoint(r * a, r * (1 - Math.Cos(a)), r * Math.Sin(a), 0, 0, default));
        }

        var route = new Route("circle", points, isLoop: false);
        Assert.InRange(route.CurvatureAt(200), 0.0097, 0.0103); // heading increases: right-hand bend
    }
}
