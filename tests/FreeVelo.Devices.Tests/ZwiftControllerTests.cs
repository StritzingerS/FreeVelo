using FreeVelo.Devices.Ble;
using FreeVelo.Devices.Session;
using FreeVelo.Devices.Zwift;

namespace FreeVelo.Devices.Tests;

public class ZwiftControllerTests
{
    private sealed class FakeWriter : IBleWriter
    {
        public List<(string Address, Guid Characteristic, byte[] Data)> Writes { get; } = new();

        public bool Write(string address, Guid characteristic, byte[] data)
        {
            Writes.Add((address, characteristic, data));
            return true;
        }
    }

    // 0x23 + field 1 (ButtonMap) as varint. All bits set = nothing pressed; a cleared bit = pressed.
    private static byte[] Buttons(uint map)
    {
        var bytes = new List<byte> { 0x23, 0x08 };
        do
        {
            byte b = (byte)(map & 0x7F);
            map >>= 7;
            bytes.Add(map != 0 ? (byte)(b | 0x80) : b);
        }
        while (map != 0);
        return bytes.ToArray();
    }

    private const uint Released = 0x3FFFF;

    [Fact]
    public void DecodesClickV2Buttons()
    {
        Assert.Empty(ZwiftController.PressedButtons(Buttons(Released))!);
        Assert.Equal(new[] { ZwiftButton.ShiftUpRight }, ZwiftController.PressedButtons(Buttons(Released & ~0x2000u))!);
        Assert.Equal(new[] { ZwiftButton.ShiftUpLeft }, ZwiftController.PressedButtons(Buttons(Released & ~0x200u))!);
        Assert.Equal(new[] { ZwiftButton.A, ZwiftButton.B }.OrderBy(b => b), ZwiftController.PressedButtons(Buttons(Released & ~0x30u))!.OrderBy(b => b));
        Assert.Null(ZwiftController.PressedButtons(new byte[] { 0x15, 0x00 })); // "empty" message
    }

    [Fact]
    public void DecodesClickFirstGenAndBattery()
    {
        Assert.Equal(new[] { ZwiftButton.ClickPlus }, ZwiftController.PressedButtons(new byte[] { 0x37, 0x08, 0x00, 0x10, 0x01 })!);
        Assert.Equal(new[] { ZwiftButton.ClickMinus }, ZwiftController.PressedButtons(new byte[] { 0x37, 0x08, 0x01, 0x10, 0x00 })!);
        Assert.Equal(87, ZwiftController.Battery(new byte[] { 0x19, 0x08, 0x00, 0x10, 87 }));
    }

    [Fact]
    public void HandshakeKeepAliveAndPressesThroughTheHub()
    {
        long now = 1_000_000;
        var writer = new FakeWriter();
        var hub = new DeviceHub(writer, () => now);
        var pressed = new List<ZwiftButton>();
        hub.ControllerButton += pressed.Add;

        hub.OnReady("RIGHT", new[] { ZwiftController.RideService, GattUuids.FromShort(GattUuids.BatteryService) });
        Assert.True(hub.IsController("RIGHT"));
        Assert.Null(hub.TrainerAddress);

        hub.Tick();
        var handshake = Assert.Single(writer.Writes);
        Assert.Equal(ZwiftController.SyncRxChar, handshake.Characteristic);
        Assert.Equal("RideOn"u8.ToArray().Concat(new byte[] { 0x02, 0x03 }), handshake.Data);

        hub.OnData("RIGHT", ZwiftController.SyncTxChar, "RideOn"u8.ToArray().Concat(new byte[] { 0x02, 0x03 }).ToArray());
        Assert.Equal(ZwiftControllerState.Ready, hub.Controllers.Single().State);

        // Keep-alive every 3 s
        now += 1000; hub.Tick();
        Assert.Single(writer.Writes);
        now += 2000; hub.Tick();
        Assert.Equal(2, writer.Writes.Count);

        // One event per press, not per notification
        hub.OnData("RIGHT", ZwiftController.AsyncChar, Buttons(Released & ~0x2000u));
        hub.OnData("RIGHT", ZwiftController.AsyncChar, Buttons(Released & ~0x2000u));
        hub.OnData("RIGHT", ZwiftController.AsyncChar, Buttons(Released));
        hub.OnData("RIGHT", ZwiftController.AsyncChar, Buttons(Released & ~0x2000u));
        Assert.Equal(new[] { ZwiftButton.ShiftUpRight, ZwiftButton.ShiftUpRight }, pressed);
    }

    [Fact]
    public void LockedLeftUnitIsReportedAndLeftAlone()
    {
        long now = 1_000_000;
        var writer = new FakeWriter();
        var hub = new DeviceHub(writer, () => now);
        hub.OnReady("LEFT", new[] { ZwiftController.RideService });
        hub.Tick();
        hub.OnData("LEFT", ZwiftController.SyncTxChar, new byte[] { 0xFF, 0x05, 0x00, 0xEA, 0x05 });
        Assert.Equal(ZwiftControllerState.Locked, hub.Controllers.Single().State);
        now += 10_000; hub.Tick();
        Assert.Single(writer.Writes); // no more handshakes to a locked unit
    }
}
