using FreeVelo.Devices.Ble;
using FreeVelo.Devices.CyclingPower;
using FreeVelo.Devices.HeartRate;
using FreeVelo.Devices.Logging;

namespace FreeVelo.Devices.Tests;

public class CyclingPowerMeasurementTests
{
    [Fact]
    public void ParsesPowerAndCrankData()
    {
        // flags 0x0020 (crank data), power 200 W, crank revs 1000, crank time 2048/1024 s
        byte[] payload = { 0x20, 0x00, 0xC8, 0x00, 0xE8, 0x03, 0x00, 0x08 };

        var m = CyclingPowerMeasurement.Parse(payload);

        Assert.Equal(200, m.PowerW);
        Assert.Equal((ushort)1000, m.CumulativeCrankRevolutions);
        Assert.Equal((ushort)2048, m.LastCrankEventTime);
        Assert.Null(m.CumulativeWheelRevolutions);
        Assert.Null(m.PedalPowerBalancePercent);
    }

    [Fact]
    public void ParsesBalanceWheelAndCrankData()
    {
        // flags 0x0033: balance + balance reference (left) + wheel + crank
        byte[] payload =
        {
            0x33, 0x00,
            0x2C, 0x01, // 300 W
            0x66, // balance 51 %
            0x10, 0x27, 0x00, 0x00, // wheel revs 10000
            0x00, 0x10, // wheel time 4096/2048 s
            0x05, 0x00, // crank revs 5
            0x00, 0x04, // crank time 1024
        };

        var m = CyclingPowerMeasurement.Parse(payload);

        Assert.Equal(300, m.PowerW);
        Assert.Equal(51.0, m.PedalPowerBalancePercent);
        Assert.True(m.PedalPowerBalanceIsLeft);
        Assert.Equal(10000u, m.CumulativeWheelRevolutions);
        Assert.Equal((ushort)4096, m.LastWheelEventTime);
        Assert.Equal((ushort)5, m.CumulativeCrankRevolutions);
        Assert.Equal((ushort)1024, m.LastCrankEventTime);
    }

    [Fact]
    public void SkipsOptionalFieldsBeforeAccumulatedEnergy()
    {
        // flags 0x0FC0: extreme force, torque, angles, TDC, BDC, accumulated energy
        byte[] payload =
        {
            0xC0, 0x0F, 0x64, 0x00,
            0, 0, 0, 0, // force
            0, 0, 0, 0, // torque
            0, 0, 0, // angles
            0, 0, // top dead spot
            0, 0, // bottom dead spot
            0x2A, 0x00, // 42 kJ
        };

        var m = CyclingPowerMeasurement.Parse(payload);

        Assert.Equal(100, m.PowerW);
        Assert.Equal(42, m.AccumulatedEnergyKj);
    }

    [Fact]
    public void TruncatedPayloadIsRejected()
    {
        Assert.False(CyclingPowerMeasurement.TryParse(new byte[] { 0x20, 0x00, 0xC8, 0x00, 0xE8 }, out _));
    }
}

public class CrankCadenceCalculatorTests
{
    [Fact]
    public void FirstSampleHasNoCadence()
    {
        var calc = new CrankCadenceCalculator();
        Assert.Null(calc.Update(100, 1000, 0));
    }

    [Fact]
    public void ComputesCadence()
    {
        var calc = new CrankCadenceCalculator();
        calc.Update(100, 0, 0);

        // 3 revolutions in 2 s (2048 ticks) = 90 rpm
        Assert.Equal(90.0, calc.Update(103, 2048, 2000));
    }

    [Fact]
    public void HandlesRollover()
    {
        var calc = new CrankCadenceCalculator();
        calc.Update(65535, 65000, 0);

        // 2 revolutions, 1365 ticks later (time rolled over to 829)
        double? cadence = calc.Update(1, 829, 1333);

        Assert.NotNull(cadence);
        Assert.Equal(90.0, cadence!.Value, 1);
    }

    [Fact]
    public void KeepsCadenceForRepeatedEventsThenDropsToZero()
    {
        var calc = new CrankCadenceCalculator(stopTimeoutMs: 3000);
        calc.Update(10, 0, 0);
        calc.Update(11, 683, 700); // ~90 rpm

        Assert.Equal(90.0, calc.Update(11, 683, 1500)!.Value, 0); // same event repeated
        Assert.Equal(0.0, calc.Update(11, 683, 3700));
    }

    [Fact]
    public void FirstStrokeAfterStandstillIsNotACadence()
    {
        // Assioma log: revs 0 → 1 after 15.33 s (first stroke), then 1 rev per ~1.7 s
        var calc = new CrankCadenceCalculator();
        calc.Update(0, 0, 0);

        Assert.Null(calc.Update(1, 15700, 1000));
        Assert.Equal(26.0, calc.Update(2, 18045, 4000)!.Value, 0);
    }

    [Fact]
    public void IgnoresImplausibleJumps()
    {
        var calc = new CrankCadenceCalculator();
        calc.Update(10, 0, 0);
        calc.Update(11, 683, 700);

        double? before = calc.CadenceRpm;
        calc.Update(500, 1366, 1400);

        Assert.Equal(before, calc.CadenceRpm);
    }
}

public class HeartRateMeasurementTests
{
    [Fact]
    public void ParsesUInt8HeartRate()
    {
        var m = HeartRateMeasurement.Parse(new byte[] { 0x00, 0x48 });

        Assert.Equal(72, m.Bpm);
        Assert.Equal(SensorContact.NotSupported, m.Contact);
        Assert.Empty(m.RrIntervalsMs);
    }

    [Fact]
    public void ParsesUInt16WithContactEnergyAndRrIntervals()
    {
        // flags 0x1F: uint16, contact detected (0b11), energy, RR
        byte[] payload = { 0x1F, 0x8C, 0x00, 0x0A, 0x00, 0x00, 0x04, 0x00, 0x03 };

        var m = HeartRateMeasurement.Parse(payload);

        Assert.Equal(140, m.Bpm);
        Assert.Equal(SensorContact.Detected, m.Contact);
        Assert.Equal(10, m.EnergyExpendedKj);
        Assert.Equal(new[] { 1000.0, 750.0 }, m.RrIntervalsMs);
    }

    [Fact]
    public void ContactNotDetected()
    {
        Assert.Equal(SensorContact.NotDetected, HeartRateMeasurement.Parse(new byte[] { 0x04, 0x50 }).Contact);
    }

    [Fact]
    public void EmptyPayloadIsRejected()
    {
        Assert.False(HeartRateMeasurement.TryParse(Array.Empty<byte>(), out _));
    }
}

public class GattUuidTests
{
    [Fact]
    public void ExpandsShortUuid()
    {
        Assert.Equal(
            Guid.Parse("00001826-0000-1000-8000-00805f9b34fb"),
            GattUuids.FromShort(GattUuids.FitnessMachineService));
    }

    [Fact]
    public void ExtractsShortUuid()
    {
        Assert.True(GattUuids.TryGetShort(Guid.Parse("00002AD2-0000-1000-8000-00805F9B34FB"), out ushort value));
        Assert.Equal(GattUuids.IndoorBikeData, value);
        Assert.False(GattUuids.TryGetShort(Guid.Parse("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e"), out _));
    }
}

public class BleLogEntryTests
{
    private const string SampleLog = """
        # FreeVelo BLE log v1
        1730700000000,C1:22:33:44:55:66,00002ad2-0000-1000-8000-00805f9b34fb,N,4400b20cb400fa00

        1730700000250,C1:22:33:44:55:66,00002ad9-0000-1000-8000-00805f9b34fb,W,05fa00
        1730700000300,C1:22:33:44:55:66,00002ad9-0000-1000-8000-00805f9b34fb,N,800501
        """;

    [Fact]
    public void ReadsLogAndSkipsHeaderAndBlankLines()
    {
        var entries = BleLogEntry.ReadAll(new StringReader(SampleLog)).ToList();

        Assert.Equal(3, entries.Count);
        Assert.Equal(BleLogDirection.Notify, entries[0].Direction);
        Assert.Equal(GattUuids.FromShort(GattUuids.IndoorBikeData), entries[0].Characteristic);
        Assert.Equal(250, Ftms.IndoorBikeData.Parse(entries[0].Payload).PowerW);
        Assert.Equal(BleLogDirection.Write, entries[1].Direction);
        Assert.Equal(new byte[] { 0x05, 0xFA, 0x00 }, entries[1].Payload);
    }

    [Fact]
    public void RoundTripsLine()
    {
        const string line = "42,AA:BB:CC:DD:EE:FF,00002a37-0000-1000-8000-00805f9b34fb,R,0048";
        Assert.Equal(line, BleLogEntry.ParseLine(line).ToLine());
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("1,AA,00002a37-0000-1000-8000-00805f9b34fb,X,00")]
    [InlineData("1,AA,00002a37-0000-1000-8000-00805f9b34fb,N,0")]
    public void RejectsMalformedLines(string line)
    {
        Assert.Throws<FormatException>(() => BleLogEntry.ParseLine(line));
    }
}
