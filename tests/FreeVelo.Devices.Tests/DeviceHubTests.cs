using FreeVelo.Devices.Ble;
using FreeVelo.Devices.Ftms;
using FreeVelo.Devices.Logging;
using FreeVelo.Devices.Session;
using FreeVelo.Devices.Tacx;

namespace FreeVelo.Devices.Tests;

public class DeviceHubTests
{
    private sealed class FakeWriter : IBleWriter
    {
        public List<(string Address, Guid Characteristic, byte[] Data)> Writes { get; } = new();

        public bool Write(string address, Guid characteristic, byte[] data)
        {
            Writes.Add((address, characteristic, data));
            return true;
        }

        public List<byte[]> To(Guid characteristic) => Writes.Where(w => w.Characteristic == characteristic).Select(w => w.Data).ToList();
    }

    private static readonly Guid Ftms = GattUuids.FromShort(GattUuids.FitnessMachineService);
    private static readonly Guid Cps = GattUuids.FromShort(GattUuids.CyclingPowerService);
    private static readonly Guid Hrs = GattUuids.FromShort(GattUuids.HeartRateService);
    private static readonly Guid ControlPoint = GattUuids.FromShort(GattUuids.FitnessMachineControlPoint);
    private static readonly Guid IndoorBike = GattUuids.FromShort(GattUuids.IndoorBikeData);

    private long _now = 1_000_000;
    private readonly FakeWriter _writer = new();
    private readonly DeviceHub _hub;

    public DeviceHubTests()
    {
        _hub = new DeviceHub(_writer, () => _now);
    }

    private void Advance(int ms, int tickMs = 250)
    {
        for (int t = 0; t < ms; t += tickMs)
        {
            _now += tickMs;
            _hub.Tick();
        }
    }

    private void ConnectNeo() => _hub.OnReady("NEO", new[] { Cps, GattUuids.FromShort(GattUuids.CyclingSpeedCadenceService), TacxFec.Service });

    [Fact]
    public void ErgScheduleForTheBackgroundUsesTheTrainerProtocol()
    {
        ConnectNeo();
        Assert.Empty(_hub.BuildErgSchedule(new (double, int?)[] { (0, 200) })); // not in ERG mode yet

        _hub.SetErgTarget(150);
        Advance(500);
        var schedule = _hub.BuildErgSchedule(new (double, int?)[] { (0, 150), (12.5, 300), (60, null), (90, 120) });
        Assert.Equal(new long[] { 0, 12500, 90000 }, schedule.Select(w => w.OffsetMs).ToArray());
        Assert.All(schedule, w => Assert.Equal("NEO", w.Address));
        Assert.All(schedule, w => Assert.Equal(TacxFec.TxCharacteristic, w.Characteristic));
        Assert.Equal(TacxFec.SetTargetPower(300), schedule[1].Data);
    }

    private void ConnectFtmsTrainer()
    {
        _hub.OnReady("KICKR", new[] { Ftms, Cps });
        _hub.Tick();
        _hub.OnData("KICKR", ControlPoint, new byte[] { 0x80, 0x00, 0x01 }); // control granted
    }

    private static byte[] Ibd(int power, int cadenceRpm) =>
        new byte[] { 0x44, 0x00, 0xB2, 0x0C, (byte)(cadenceRpm * 2), (byte)(cadenceRpm * 2 >> 8), (byte)power, (byte)(power >> 8) };

    [Fact]
    public void AssignsRoles()
    {
        ConnectNeo();
        _hub.OnReady("HRM", new[] { Hrs });
        _hub.OnReady("PEDALS", new[] { Cps });

        Assert.Equal("NEO", _hub.TrainerAddress);
        Assert.Equal(TrainerProtocol.TacxFec, _hub.TrainerProtocol);
        Assert.Equal("HRM", _hub.HeartRateAddress);
        Assert.Equal("PEDALS", _hub.PowerMeterAddress);
    }

    [Fact]
    public void FtmsIsPreferredOverFec()
    {
        _hub.OnReady("BOTH", new[] { Ftms, TacxFec.Service });
        Assert.Equal(TrainerProtocol.Ftms, _hub.TrainerProtocol);
    }

    [Fact]
    public void FecErgSendsTargetAndRampsLargeJumps()
    {
        ConnectNeo();
        _hub.SetErgTarget(100);
        Advance(250);

        var commands = _writer.To(TacxFec.TxCharacteristic);
        Assert.Equal(TacxFec.SetUserConfiguration(75), commands[0]); // config on connect
        Assert.Equal(TacxFec.SetTargetPower(100), commands[1]);

        _hub.SetErgTarget(300);
        Advance(1000);
        Assert.InRange(_hub.SentErgTargetW!.Value, 140, 160); // 50 W/s
        Advance(4000);
        Assert.Equal(300, _hub.SentErgTargetW);

        // No duplicate commands while the target is constant
        int count = _writer.Writes.Count;
        Advance(2000);
        Assert.Equal(count, _writer.Writes.Count);
    }

    [Fact]
    public void FtmsWaitsForControlBeforeSendingTargets()
    {
        _hub.OnReady("KICKR", new[] { Ftms });
        _hub.SetErgTarget(200);
        Advance(250);

        Assert.Equal(new[] { FtmsControlPoint.RequestControl(), FtmsControlPoint.StartOrResume() }, _writer.To(ControlPoint));

        _hub.OnData("KICKR", ControlPoint, new byte[] { 0x80, 0x00, 0x01 });
        Advance(250);
        Assert.Equal(FtmsControlPoint.SetTargetPower(200), _writer.To(ControlPoint).Last());
    }

    [Fact]
    public void FtmsRequestsControlAgainWhenNotPermitted()
    {
        ConnectFtmsTrainer();
        _hub.SetErgTarget(200);
        Advance(250);

        _hub.OnData("KICKR", ControlPoint, new byte[] { 0x80, 0x05, 0x05 }); // control not permitted
        Advance(250);
        Assert.Equal(FtmsControlPoint.StartOrResume(), _writer.To(ControlPoint).Last());

        _hub.OnData("KICKR", ControlPoint, new byte[] { 0x80, 0x00, 0x01 });
        Advance(250);
        Assert.Equal(FtmsControlPoint.SetTargetPower(200), _writer.To(ControlPoint).Last());
    }

    [Fact]
    public void LowCadenceReducesErgTarget()
    {
        ConnectFtmsTrainer();
        _hub.SetErgTarget(300);
        Advance(250);
        Assert.Equal(300, _hub.SentErgTargetW);

        for (int i = 0; i < 20; i++)
        {
            _hub.OnData("KICKR", IndoorBike, Ibd(250, 40));
            Advance(250);
        }

        Assert.True(_hub.LowCadenceProtectionActive);
        Assert.Equal(210, _hub.SentErgTargetW); // 70 %

        for (int i = 0; i < 20; i++)
        {
            _hub.OnData("KICKR", IndoorBike, Ibd(220, 80));
            Advance(250);
        }

        Assert.False(_hub.LowCadenceProtectionActive);
        Assert.Equal(300, _hub.SentErgTargetW);
    }

    [Fact]
    public void LowCadenceProtectionCanBeDisabled()
    {
        ConnectFtmsTrainer();
        _hub.LowCadenceProtectionEnabled = false;
        _hub.SetErgTarget(300);
        for (int i = 0; i < 20; i++)
        {
            _hub.OnData("KICKR", IndoorBike, Ibd(250, 40));
            Advance(250);
        }

        Assert.False(_hub.LowCadenceProtectionActive);
        Assert.Equal(300, _hub.SentErgTargetW);
    }

    [Fact]
    public void FecSimSendsGradeWithUserConfigAndRespectsDifficulty()
    {
        ConnectNeo();
        _hub.TrainerDifficulty = 0.5;
        _hub.SetSimulation(8.0);
        Advance(250);

        var commands = _writer.To(TacxFec.TxCharacteristic);
        Assert.Contains(commands, c => c.SequenceEqual(TacxFec.SetTrackResistance(4.0)));
        Assert.Equal(TacxFec.SetUserConfiguration(75), commands.Last());

        // Small changes are throttled to 1/s
        int count = _writer.Writes.Count;
        _hub.SetSimulation(8.6);
        Advance(250);
        Assert.Equal(count, _writer.Writes.Count);
        Advance(1000);
        Assert.Contains(_writer.To(TacxFec.TxCharacteristic), c => c.SequenceEqual(TacxFec.SetTrackResistance(4.3)));
    }

    [Fact]
    public void ResendsTargetAfterReconnect()
    {
        ConnectNeo();
        _hub.SetErgTarget(180);
        Advance(1000);
        int before = _writer.To(TacxFec.TxCharacteristic).Count(c => c.SequenceEqual(TacxFec.SetTargetPower(180)));

        _hub.OnDisconnected("NEO");
        ConnectNeo();
        Advance(250);

        int after = _writer.To(TacxFec.TxCharacteristic).Count(c => c.SequenceEqual(TacxFec.SetTargetPower(180)));
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void PowerSourcePreference()
    {
        ConnectFtmsTrainer();
        _hub.OnReady("PEDALS", new[] { Cps });
        _hub.OnData("KICKR", IndoorBike, Ibd(200, 90));
        _hub.OnData("PEDALS", GattUuids.FromShort(GattUuids.CyclingPowerMeasurement), new byte[] { 0x00, 0x00, 0xD2, 0x00 }); // 210 W

        Assert.Equal(200, _hub.Live.PowerW);
        _hub.PowerSource = PowerSource.PowerMeter;
        _hub.Tick();
        Assert.Equal(210, _hub.Live.PowerW);
        Assert.Equal(200, _hub.Live.TrainerPowerW);
    }

    [Fact]
    public void StaleValuesExpire()
    {
        ConnectFtmsTrainer();
        _hub.OnData("KICKR", IndoorBike, Ibd(200, 90));
        Assert.Equal(200, _hub.Live.PowerW);

        Advance(3500);
        Assert.Null(_hub.Live.PowerW);
        Assert.Null(_hub.Live.CadenceRpm);
    }

    [Fact]
    public void ReplaysRecordedNeoAndSensorLogs()
    {
        var fecLog = Load("tacx-neo2-fec-erg-sim-2026-11-04.csv");
        var sensorLog = Load("hrmpro-assioma-2026-11-04.csv");

        var hub = new DeviceHub(_writer, () => _now);
        hub.OnReady("00:00:00:00:00:01", new[] { Cps, TacxFec.Service });
        hub.OnReady("00:00:00:00:00:02", new[] { Hrs });
        hub.OnReady("00:00:00:00:00:03", new[] { Cps });

        var powers = new List<int>();
        long erg160 = fecLog.First(e => e.Direction == BleLogDirection.Write && e.Payload[4] == 0x31).TimestampMs;
        foreach (var e in fecLog.Where(e => e.Direction == BleLogDirection.Notify))
        {
            _now = e.TimestampMs;
            hub.OnData(e.DeviceAddress, e.Characteristic, e.Payload);
            if (e.TimestampMs > erg160 + 10_000 && e.TimestampMs < erg160 + 20_000) powers.Add(hub.Live.PowerW!.Value);
        }

        Assert.InRange(powers.Average(), 150, 170);

        var heartRates = new List<int>();
        foreach (var e in sensorLog)
        {
            _now = e.TimestampMs;
            hub.OnData(e.DeviceAddress, e.Characteristic, e.Payload);
            if (hub.Live.HeartRateBpm.HasValue) heartRates.Add(hub.Live.HeartRateBpm.Value);
        }

        Assert.InRange(heartRates.Max(), 95, 110);
        Assert.Equal(0, hub.Live.PowerMeterPowerW); // pedals at rest at the end
    }

    [Fact]
    public void ErgTooFastWhenPowerStaysAboveTarget()
    {
        ConnectFtmsTrainer();
        _hub.SetErgTarget(150);
        for (int i = 0; i < 40; i++) // 10 s at 32.5 km/h, 250 W instead of 150 W
        {
            _hub.OnData("KICKR", IndoorBike, Ibd(250, 90));
            Advance(250);
            if (i < 28) Assert.False(_hub.ErgSpeedTooHigh); // not before 8 s
        }

        Assert.True(_hub.ErgSpeedTooHigh);
        Assert.Equal(19, _hub.ErgMaxSpeedKmh); // 32.5 km/h × 150 / 250

        for (int i = 0; i < 20; i++)
        {
            _hub.OnData("KICKR", IndoorBike, Ibd(152, 90));
            Advance(250);
        }

        Assert.False(_hub.ErgSpeedTooHigh);
        Assert.Null(_hub.ErgMaxSpeedKmh);
    }

    [Fact]
    public void ReplayedNeroLogReportsErgTooFast()
    {
        // Elite Nero rollers: every ERG target is acknowledged, but at 18–25 km/h even minimum resistance gives more
        // than the 110–130 W warm-up targets, and the trainer reports "speed too high" (training status 0x0A).
        var log = Load("elite-nero-ftms-erg-2026-11-06.csv");
        string nero = log[0].DeviceAddress;
        var hub = new DeviceHub(_writer, () => _now);
        hub.OnReady(nero, new[] { Ftms, Cps });

        var tooFast = new List<(long Ms, double? MaxKmh)>();
        long lastTick = log[0].TimestampMs;
        foreach (var e in log)
        {
            while (lastTick + 250 <= e.TimestampMs)
            {
                lastTick += 250;
                _now = lastTick;
                hub.Tick();
                if (hub.ErgSpeedTooHigh) tooFast.Add((_now, hub.ErgMaxSpeedKmh));
            }

            _now = e.TimestampMs;
            if (e.Direction == BleLogDirection.Write && e.Characteristic == ControlPoint && e.Payload[0] == 0x05)
                hub.SetErgTarget(e.Payload[1] | e.Payload[2] << 8);
            else if (e.Direction == BleLogDirection.Notify)
                hub.OnData(e.DeviceAddress, e.Characteristic, e.Payload);
        }

        long start = log[0].TimestampMs;
        Assert.Contains(tooFast, t => t.Ms - start is > 50_000 and < 60_000); // 18 km/h, 129 W for 119 W
        Assert.Contains(tooFast, t => t.Ms - start is > 80_000 and < 90_000); // 24 km/h, 175 W for 125 W
        Assert.All(tooFast.Where(t => t.MaxKmh.HasValue), t => Assert.InRange(t.MaxKmh!.Value, 10, 19));

        // Stopped at the end: no hint any more
        Assert.False(hub.ErgSpeedTooHigh);
    }

    private static List<BleLogEntry> Load(string name) =>
        BleLogEntry.ReadAll(new StringReader(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Logs", name)))).ToList();
}
