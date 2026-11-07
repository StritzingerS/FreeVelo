using FreeVelo.Core.Physics;

namespace FreeVelo.Core.Tests;

public class VirtualGearTests
{
    private const double Mass = 83, Crr = 0.004, Cw = 0.51 * 0.5;

    [Fact]
    public void TwentyFourGearsAroundTheReference()
    {
        var gears = new VirtualGears();
        Assert.Equal(24, gears.Count);
        Assert.Equal(0.75, gears.RatioOf(0), 3);
        Assert.Equal(5.5, gears.RatioOf(23), 3);
        Assert.InRange(gears.Factor, 0.9, 1.1); // starts next to the real gear
        Assert.True(gears.Shift(+1));
        Assert.True(gears.Factor > 1);
        gears.Set(0);
        Assert.False(gears.Shift(-1));
    }

    [Fact]
    public void ReferenceGearSendsTheRealGrade()
    {
        var gears = new VirtualGears(referenceRatio: new VirtualGears().RatioOf(11));
        gears.Set(11);
        Assert.Equal(3.0, gears.EffectiveGrade(3, 8, Mass, Crr, Cw), 1);
    }

    [Fact]
    public void HarderGearMeansMoreResistanceAtTheSameTrainerSpeed()
    {
        var gears = new VirtualGears();
        double inReference = gears.EffectiveGrade(2, 8, Mass, Crr, Cw);
        gears.Shift(+4);
        double harder = gears.EffectiveGrade(2, 8, Mass, Crr, Cw);
        gears.Shift(-8);
        double easier = gears.EffectiveGrade(2, 8, Mass, Crr, Cw);
        Assert.True(harder > inReference + 1, $"{harder} vs {inReference}");
        Assert.True(easier < inReference - 1, $"{easier} vs {inReference}");

        // Power at the trainer = the road's power at the virtual speed
        gears.Set(16);
        double v = 8, k = gears.Factor;
        double trainerPower = VirtualGears.RoadForce(gears.EffectiveGrade(2, v, Mass, Crr, Cw), v, Mass, Crr, Cw) * v;
        double roadPower = VirtualGears.RoadForce(2, v * k, Mass, Crr, Cw) * v * k;
        Assert.InRange(trainerPower / roadPower, 0.97, 1.03);
    }

    [Fact]
    public void VirtualSpeedFollowsCadenceAndGear()
    {
        var gears = new VirtualGears();
        gears.Set(gears.NearestGear(50.0 / 17)); // ≈ 50×17
        Assert.InRange(gears.VirtualSpeed(90) * 3.6, 34, 36); // 90 rpm in 50×17 ≈ 35 km/h
        Assert.True(gears.VirtualSpeed(70) < gears.VirtualSpeed(90));
    }
}
