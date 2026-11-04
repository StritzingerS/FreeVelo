using FreeVelo.Devices.Ble;
using FreeVelo.Devices.CyclingPower;
using FreeVelo.Devices.Logging;
using FreeVelo.Devices.Tacx;

namespace FreeVelo.Devices.Tests;

public class TacxFecTests
{
    [Fact]
    public void TargetPowerPage()
    {
        // 250 W = 1000 × 0.25 W = 0x03E8
        byte[] msg = TacxFec.SetTargetPower(250);
        Assert.Equal(new byte[] { 0xA4, 0x09, 0x4F, 0x05, 0x31, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xE8, 0x03 }, msg[..12]);
        Assert.Equal(XorAll(msg[..12]), msg[12]);
    }

    [Fact]
    public void TrackResistancePage()
    {
        // grade 5 % → (5 + 200) × 100 = 20500 = 0x5014, Crr 0.004 / 0.00005 = 80 = 0x50
        byte[] msg = TacxFec.SetTrackResistance(5.0);
        Assert.Equal(new byte[] { 0x33, 0xFF, 0xFF, 0xFF, 0xFF, 0x14, 0x50, 0x50 }, msg[4..12]);

        // −3.5 % → 19650 = 0x4CC2
        Assert.Equal(new byte[] { 0xC2, 0x4C }, TacxFec.SetTrackResistance(-3.5)[9..11]);
    }

    [Fact]
    public void UserConfigurationPage()
    {
        // rider 75 kg = 7500 = 0x1D4C, bike 9 kg = 180 = 0x0B4, wheel 0.70 m = 70 = 0x46
        byte[] msg = TacxFec.SetUserConfiguration(75);
        Assert.Equal(new byte[] { 0x37, 0x4C, 0x1D, 0xFF, 0x40, 0x0B, 0x46, 0x00 }, msg[4..12]);
    }

    [Fact]
    public void ParsesSpecificTrainerData()
    {
        // page 0x19: cadence 85, power 0x10F = 271 W (12 bit)
        byte[] page = { 0x19, 0x01, 0x55, 0x00, 0x00, 0x0F, 0x31, 0x20 };
        var data = FecData.Parse(Broadcast(page));

        Assert.Equal(0x19, data.Page);
        Assert.Equal(85, data.CadenceRpm);
        Assert.Equal(271, data.PowerW);
    }

    [Fact]
    public void ParsesGeneralFeData()
    {
        // page 0x10: speed 10000 mm/s = 36 km/h, heart rate 0xFF = none
        byte[] page = { 0x10, 0x19, 0x00, 0x00, 0x10, 0x27, 0xFF, 0x24 };
        var data = FecData.Parse(Broadcast(page));

        Assert.Equal(36.0, data.SpeedKmh!.Value, 3);
        Assert.Null(data.HeartRateBpm);
    }

    [Fact]
    public void RejectsBadChecksum()
    {
        byte[] msg = Broadcast(new byte[] { 0x19, 0, 0, 0, 0, 0, 0, 0 });
        msg[12] ^= 0xFF;
        Assert.False(FecData.TryParse(msg, out _));
    }

    private static byte[] Broadcast(byte[] page)
    {
        var msg = new byte[13];
        msg[0] = 0xA4;
        msg[1] = 0x09;
        msg[2] = 0x4E;
        msg[3] = 0x05;
        page.CopyTo(msg, 4);
        msg[12] = XorAll(msg[..12]);
        return msg;
    }

    private static byte XorAll(byte[] data) => data.Aggregate((byte)0, (a, b) => (byte)(a ^ b));
}

/// <summary>Regression tests with byte logs recorded on real devices (tests/.../Logs).</summary>
public class RecordedLogTests
{
    private static List<BleLogEntry> Load(string name) =>
        BleLogEntry.ReadAll(new StringReader(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Logs", name)))).ToList();

    [Fact]
    public void TacxNeo2CyclingPowerLog()
    {
        var cps = GattUuids.FromShort(GattUuids.CyclingPowerMeasurement);
        var entries = Load("tacx-neo2-cps-2026-11-04.csv");
        var calc = new CrankCadenceCalculator();
        var cadences = new List<double>();
        var powers = new List<int>();

        foreach (var e in entries.Where(e => e.Characteristic == cps))
        {
            var m = CyclingPowerMeasurement.Parse(e.Payload);
            powers.Add(m.PowerW);
            if (m.CumulativeCrankRevolutions is { } revs && m.LastCrankEventTime is { } time)
            {
                double? c = calc.Update(revs, time, e.TimestampMs);
                if (c.HasValue) cadences.Add(c.Value);
            }
        }

        Assert.Equal(entries.Count, powers.Count);
        Assert.All(powers.Take(17), p => Assert.InRange(p, 50, 65));   // steady ~55–60 W
        Assert.Equal(0, powers.Last());                                  // stopped pedaling
        Assert.InRange(cadences[0], 60, 70);                             // ~64 rpm
        Assert.Equal(0.0, cadences.Last());                              // cadence drops to 0
    }
}
