using FreeVelo.Devices.Ble;
using FreeVelo.Devices.Session;
using FreeVelo.Devices.Tacx;
using FreeVelo.Devices.Zwift;

namespace FreeVelo.Devices.Tests;

public class ZwiftTrainerTests
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

    // Tacx Neo 2 with current firmware (services as in Logs/tacx-neo2-fec-erg-sim-2026-11-04.csv): FE-C + Zwift FC82.
    private static readonly Guid[] Neo2 = { Cps, GattUuids.FromShort(GattUuids.CyclingSpeedCadenceService), TacxFec.Service, ZwiftController.RideService };

    private long _now = 1_000_000;
    private readonly FakeWriter _writer = new();
    private readonly DeviceHub _hub;

    public ZwiftTrainerTests() => _hub = new DeviceHub(_writer, () => _now);

    private void Advance(int ms)
    {
        for (int t = 0; t < ms; t += 250)
        {
            _now += 250;
            _hub.Tick();
        }
    }

    [Fact]
    public void MessagesMatchWhatZwiftSends()
    {
        // Captured from Zwift (published by the qdomyos-zwift project): road 1.73 %, CdA 0.51, Crr 0.004 …
        Assert.Equal(new byte[] { 0x04, 0x22, 0x0b, 0x08, 0x00, 0x10, 0xda, 0x02, 0x18, 0xec, 0x27, 0x20, 0x90, 0x03 }, ZwiftTrainer.Simulation(1.73, 0.004, 0.51));
        // … gear ratio 2.4 with bike 8.31 kg and rider 85 kg, and the gear alone.
        Assert.Equal(new byte[] { 0x04, 0x2a, 0x0a, 0x10, 0xc0, 0xbb, 0x01, 0x20, 0xbf, 0x06, 0x28, 0xb4, 0x42 }, ZwiftTrainer.Physical(24000, 8.31, 85));
        Assert.Equal(new byte[] { 0x04, 0x2a, 0x04, 0x10, 0xc0, 0xbb, 0x01 }, ZwiftTrainer.Physical(24000, null, null));
        Assert.Equal(new byte[] { 0x52, 0x69, 0x64, 0x65, 0x4f, 0x6e, 0x02, 0x01 }, ZwiftTrainer.Handshake);
        // Downhill: negative grades are zig-zag encoded (−5 % → −500 → 999).
        Assert.Equal(new byte[] { 0x10, 0xe7, 0x07 }, ZwiftTrainer.Simulation(-5, 0.004, 0.51)[5..8]);
        // Gear factor 1 = the bike's own gear = 42×14 for the trainer.
        Assert.Equal(ZwiftTrainer.Physical(30000, null, null), ZwiftTrainer.Gear(1.0));
    }

    [Fact]
    public void TrainerWithTheZwiftServiceIsATrainerNotAController()
    {
        _hub.OnReady("NEO", Neo2);
        Assert.Equal("NEO", _hub.TrainerAddress);
        Assert.False(_hub.IsController("NEO"));
        Assert.True(_hub.TrainerSupportsShifting);

        // A Click V2 (Zwift service only) is still a controller.
        _hub.OnReady("CLICK", new[] { ZwiftController.RideService });
        Assert.True(_hub.IsController("CLICK"));

        // A plain trainer has no built-in shifting.
        var other = new DeviceHub(new FakeWriter(), () => _now);
        other.OnReady("KICKR", new[] { Ftms, Cps });
        Assert.False(other.TrainerSupportsShifting);
    }

    [Fact]
    public void VirtualGearsGoToTheTrainerWhenItCanShift()
    {
        _hub.OnReady("NEO", Neo2);
        _hub.SetSimulation(4.0);
        _hub.SetVirtualGear(1.0);
        Advance(500);

        var zwift = _writer.To(ZwiftController.SyncRxChar);
        Assert.Equal(ZwiftTrainer.Handshake, zwift[0]);
        Assert.Contains(zwift, m => m.SequenceEqual(ZwiftTrainer.Simulation(4.0, 0.004, 0.51)));
        Assert.Contains(zwift, m => m.SequenceEqual(ZwiftTrainer.Gear(1.0)));
        Assert.True(_hub.UsesTrainerShifting);
        // The road is not sent over FE-C at the same time (the trainer would get two conflicting commands).
        Assert.DoesNotContain(_writer.To(TacxFec.TxCharacteristic), m => m.SequenceEqual(TacxFec.SetTrackResistance(4.0, 0.004)));

        // A shift goes out right away, followed by the confirmation request.
        int before = _writer.Writes.Count;
        _hub.SetVirtualGear(1.25);
        Advance(250);
        var sent = _writer.Writes.Skip(before).Select(w => w.Data).ToList();
        Assert.Equal(ZwiftTrainer.Gear(1.25), sent[0]);
        Assert.Equal(ZwiftTrainer.ConfirmGear, sent[1]);
    }

    [Fact]
    public void WithoutVirtualGearsTheRoadGoesOverTheTrainerProtocol()
    {
        _hub.OnReady("NEO", Neo2);
        _hub.SetSimulation(4.0);
        _hub.SetVirtualGear(null);
        Advance(500);
        Assert.Empty(_writer.To(ZwiftController.SyncRxChar));
        Assert.Contains(_writer.To(TacxFec.TxCharacteristic), m => m.SequenceEqual(TacxFec.SetTrackResistance(4.0, 0.004)));

        // Gears on, then off again: the trainer's gear is set back to neutral.
        _hub.SetVirtualGear(1.4);
        Advance(1500);
        _hub.SetVirtualGear(null);
        Advance(1500);
        Assert.Equal(ZwiftTrainer.Gear(1.0), _writer.To(ZwiftController.SyncRxChar).Last());
    }

    [Fact]
    public void ErgStillUsesTheTrainerProtocol()
    {
        _hub.OnReady("NEO", Neo2);
        _hub.SetVirtualGear(1.0);
        _hub.SetErgTarget(200);
        Advance(500);
        Assert.False(_hub.UsesTrainerShifting);
        Assert.Contains(_writer.To(TacxFec.TxCharacteristic), m => m.SequenceEqual(TacxFec.SetTargetPower(200)));
    }
}
