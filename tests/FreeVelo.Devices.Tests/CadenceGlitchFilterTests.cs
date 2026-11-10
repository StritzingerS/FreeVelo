using FreeVelo.Devices.Session;

namespace FreeVelo.Devices.Tests;

public class CadenceGlitchFilterTests
{
    // Ramp test on a Tacx Neo 2 (2026-11-06), seconds 855–974, 1 Hz: the trainer reported 55 / 37 rpm for
    // up to 15 s while the rider kept ~100 rpm and the power stayed; at the end the rider fails the ramp.
    private static readonly int[] Power = { 334, 328, 333, 347, 344, 340, 349, 333, 331, 341, 337, 334, 339, 349, 346, 338, 337, 345, 335, 330, 346, 353, 349, 333, 333, 340, 345, 343, 338, 339, 337, 338, 336, 345, 346, 344, 344, 337, 333, 343, 339, 336, 345, 338, 340, 356, 361, 357, 358, 358, 360, 362, 363, 356, 359, 368, 361, 361, 362, 366, 356, 352, 357, 371, 371, 351, 350, 354, 365, 371, 351, 344, 361, 393, 382, 337, 362, 361, 368, 362, 356, 350, 365, 369, 357, 362, 370, 352, 350, 366, 366, 350, 364, 362, 358, 369, 371, 363, 354, 352, 353, 360, 369, 368, 350, 361, 371, 375, 387, 378, 372, 346, 207, 65, 4, 0, 0, 0 };
    private static readonly int[] Cadence = { 101, 99, 99, 99, 98, 101, 99, 100, 101, 55, 55, 54, 55, 55, 55, 55, 55, 55, 54, 54, 54, 56, 55, 97, 97, 101, 102, 102, 102, 100, 101, 99, 100, 100, 100, 97, 37, 37, 37, 37, 97, 97, 97, 97, 97, 55, 54, 54, 54, 53, 53, 54, 54, 52, 53, 53, 95, 95, 37, 36, 36, 36, 36, 37, 37, 36, 36, 35, 36, 36, 35, 35, 36, 91, 88, 95, 90, 92, 92, 91, 91, 90, 91, 89, 90, 92, 90, 91, 91, 89, 89, 88, 87, 86, 88, 88, 89, 89, 90, 90, 88, 88, 89, 88, 87, 84, 82, 81, 76, 76, 71, 59, 59, 59, 59, 0, 0, 0 };

    [Fact]
    public void HidesNeoCadenceDropoutsButKeepsTheRealEnd()
    {
        var filter = new CadenceGlitchFilter();
        var output = new List<double>();
        for (int i = 0; i < Power.Length; i++) output.Add(filter.Next(i * 1000L, Cadence[i], Power[i]));

        // Until the ramp gets too hard (last ~10 s) the shown cadence never falls below 80 rpm.
        Assert.All(output.Take(Power.Length - 10), c => Assert.True(c >= 80, $"cadence {c}"));

        // The real slow-down at the end comes through (gradual: 89 → 59 rpm), and so does stopping.
        Assert.InRange(output[Power.Length - 4], 55, 65);
        Assert.Equal(0, filter.Next(Power.Length * 1000L, 0, 0));
    }

    [Fact]
    public void AcceptsARealDropWhenPowerFallsOrAfterTheHoldTime()
    {
        var filter = new CadenceGlitchFilter();
        filter.Next(0, 95, 250);
        Assert.Equal(95, filter.Next(1000, 50, 240)); // power kept: held
        Assert.Equal(50, filter.Next(2000, 50, 120)); // power dropped too: real

        filter.Reset();
        filter.Next(0, 95, 250);
        Assert.Equal(95, filter.Next(1000, 50, 250));
        Assert.Equal(95, filter.Next(15_000, 50, 250));
        Assert.Equal(50, filter.Next(1000 + CadenceGlitchFilter.MaxHoldMs, 50, 250)); // too long for a glitch
    }

    [Fact]
    public void DropInTwoStepsIsAlsoAGlitch()
    {
        var f = new CadenceGlitchFilter();
        long t = 0;
        for (int i = 0; i < 8; i++) f.Next(t += 250, 93, 238);
        Assert.Equal(68, f.Next(t += 250, 68, 240)); // first step: 73 % of 93, within normal
        Assert.Equal(93, f.Next(t += 250, 50, 239)); // second step
        for (int i = 0; i < 40; i++) Assert.Equal(93, f.Next(t += 250, 50, 238));
        Assert.Equal(94, f.Next(t += 250, 94, 240)); // back: never shown
    }
}
