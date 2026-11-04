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
    public void ParsesTrainerStatusAndCommandStatus()
    {
        var trainer = FecData.Parse(Broadcast(new byte[] { 0x19, 0x01, 0x42, 0x00, 0x00, 0x32, 0x40, 0x30 }));
        Assert.Equal(FecTrainerStatus.UserConfigurationRequired, trainer.TrainerStatus);
        Assert.Equal(50, trainer.PowerW);

        var status = FecData.Parse(Broadcast(new byte[] { 0x47, 0x33, 0x02, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }));
        Assert.Equal((byte)0x33, status.LastCommandPage);
        Assert.Equal((byte)2, status.CommandSequence);
        Assert.Equal(FecCommandResult.Pass, status.CommandResult);

        var none = FecData.Parse(Broadcast(new byte[] { 0x47, 0xFF, 0xFF, 0xFF, 0x16, 0x0B, 0xFF, 0x34 }));
        Assert.Null(none.LastCommandPage);
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

    [Fact]
    public void TacxNeo2FecErgAndSimLog()
    {
        var entries = Load("tacx-neo2-fec-erg-sim-2026-11-04.csv");
        var writes = entries.Where(e => e.Characteristic == TacxFec.TxCharacteristic).ToList();
        var notifications = entries.Where(e => e.Characteristic == TacxFec.RxCharacteristic).ToList();

        // The app sent exactly what the encoder produces.
        Assert.Equal(TacxFec.SetUserConfiguration(75), writes[0].Payload);
        Assert.Equal(TacxFec.SetTargetPower(160), writes[1].Payload);
        Assert.Equal(TacxFec.SetTargetPower(210), writes[2].Payload);
        Assert.Equal(TacxFec.SetTrackResistance(0), writes[3].Payload);
        Assert.Equal(TacxFec.SetTrackResistance(5), writes[4].Payload);
        Assert.Equal(TacxFec.SetTrackResistance(10), writes[5].Payload);

        var parsed = notifications.Select(e => (e.TimestampMs, Data: FecData.Parse(e.Payload))).ToList();

        // Every ERG/SIM command was confirmed with "pass" (page 0x47).
        var confirmed = parsed.Where(p => p.Data.CommandResult == FecCommandResult.Pass)
            .Select(p => (p.Data.LastCommandPage, p.Data.CommandSequence)).Distinct().ToList();
        Assert.Equal(new (byte?, byte?)[] { (0x31, 0), (0x31, 1), (0x33, 2), (0x33, 3), (0x33, 4) }, confirmed);

        // ERG 160 W: power settles around the target 10–20 s after the command.
        long erg160 = writes[1].TimestampMs;
        var power = parsed.Where(p => p.TimestampMs > erg160 + 10_000 && p.TimestampMs < erg160 + 20_000 && p.Data.PowerW.HasValue)
            .Select(p => p.Data.PowerW!.Value).ToList();
        Assert.NotEmpty(power);
        Assert.InRange(power.Average(), 150, 170);

        // After the SIM commands the trainer asked for the user configuration again.
        Assert.Contains(parsed, p => p.TimestampMs > writes[3].TimestampMs
            && p.Data.TrainerStatus.HasFlag(FecTrainerStatus.UserConfigurationRequired));
    }

    [Fact]
    public void HeartRateStrapAndPowerPedalsLog()
    {
        var entries = Load("hrmpro-assioma-2026-11-04.csv");
        var hrUuid = GattUuids.FromShort(GattUuids.HeartRateMeasurement);
        var cpsUuid = GattUuids.FromShort(GattUuids.CyclingPowerMeasurement);

        var hr = entries.Where(e => e.Characteristic == hrUuid).Select(e => HeartRate.HeartRateMeasurement.Parse(e.Payload)).ToList();
        Assert.True(hr.Count > 300);
        Assert.All(hr, m => Assert.InRange(m.Bpm, 70, 105));
        Assert.Contains(hr, m => m.RrIntervalsMs.Count > 0);
        Assert.All(hr.SelectMany(m => m.RrIntervalsMs), rr => Assert.InRange(rr, 300, 2000));

        var calc = new CrankCadenceCalculator();
        var cadences = new List<double>();
        var powers = new List<int>();
        foreach (var e in entries.Where(e => e.Characteristic == cpsUuid))
        {
            var m = CyclingPowerMeasurement.Parse(e.Payload);
            powers.Add(m.PowerW);
            double? c = calc.Update(m.CumulativeCrankRevolutions!.Value, m.LastCrankEventTime!.Value, e.TimestampMs);
            if (c is > 0) cadences.Add(c.Value);
        }

        Assert.Equal(238, powers.Max());
        Assert.NotEmpty(cadences);
        Assert.All(cadences, c => Assert.InRange(c, 20, 45)); // slow pedaling, no 4 rpm start artefact
    }
}
