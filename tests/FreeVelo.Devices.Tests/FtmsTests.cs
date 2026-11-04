using FreeVelo.Devices.Ftms;

namespace FreeVelo.Devices.Tests;

public class IndoorBikeDataTests
{
    [Fact]
    public void ParsesSpeedCadenceAndPower()
    {
        // flags 0x0044: speed (bit0 = 0), cadence, power
        // speed 32.50 km/h = 3250 = 0x0CB2, cadence 90 rpm = 180 = 0x00B4, power 250 W = 0x00FA
        byte[] payload = { 0x44, 0x00, 0xB2, 0x0C, 0xB4, 0x00, 0xFA, 0x00 };

        var data = IndoorBikeData.Parse(payload);

        Assert.False(data.MoreData);
        Assert.Equal(32.5, data.SpeedKmh);
        Assert.Equal(90.0, data.CadenceRpm);
        Assert.Equal(250, data.PowerW);
        Assert.Null(data.HeartRateBpm);
        Assert.Null(data.TotalDistanceM);
    }

    [Fact]
    public void MoreDataBitMeansNoSpeed()
    {
        // flags 0x0201: more data, heart rate 140
        byte[] payload = { 0x01, 0x02, 0x8C };

        var data = IndoorBikeData.Parse(payload);

        Assert.True(data.MoreData);
        Assert.Null(data.SpeedKmh);
        Assert.Equal(140, data.HeartRateBpm);
    }

    [Fact]
    public void ParsesAllFields()
    {
        // flags 0x1FFE: every field except "more data" (so speed is present too)
        byte[] payload =
        {
            0xFE, 0x1F,
            0x10, 0x27, // speed 100.00 km/h
            0xE8, 0x03, // avg speed 10.00 km/h
            0xA0, 0x00, // cadence 80 rpm
            0x96, 0x00, // avg cadence 75 rpm
            0x40, 0x42, 0x0F, // distance 1,000,000 m (uint24)
            0xFB, 0xFF, // resistance -5
            0x2C, 0x01, // power 300 W
            0xC8, 0x00, // avg power 200 W
            0x64, 0x00, 0xFF, 0xFF, 0xFF, // energy: total 100 kcal, per hour n/a, per minute n/a
            0x78, // heart rate 120
            0x2D, // MET 4.5
            0x10, 0x0E, // elapsed 3600 s
            0x2C, 0x01, // remaining 300 s
        };

        var data = IndoorBikeData.Parse(payload);

        Assert.Equal(100.0, data.SpeedKmh);
        Assert.Equal(10.0, data.AverageSpeedKmh);
        Assert.Equal(80.0, data.CadenceRpm);
        Assert.Equal(75.0, data.AverageCadenceRpm);
        Assert.Equal(1_000_000, data.TotalDistanceM);
        Assert.Equal(-5, data.ResistanceLevel);
        Assert.Equal(300, data.PowerW);
        Assert.Equal(200, data.AveragePowerW);
        Assert.Equal(100, data.TotalEnergyKcal);
        Assert.Equal(0xFFFF, data.EnergyPerHourKcal);
        Assert.Equal(0xFF, data.EnergyPerMinuteKcal);
        Assert.Equal(120, data.HeartRateBpm);
        Assert.Equal(4.5, data.MetabolicEquivalent);
        Assert.Equal(3600, data.ElapsedTimeS);
        Assert.Equal(300, data.RemainingTimeS);
    }

    [Fact]
    public void NegativePowerIsSigned()
    {
        byte[] payload = { 0x41, 0x00, 0xF6, 0xFF }; // more data + power -10 W
        Assert.Equal(-10, IndoorBikeData.Parse(payload).PowerW);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x44 })]
    [InlineData(new byte[] { 0x44, 0x00, 0xB2, 0x0C, 0xB4 })]
    public void TruncatedPayloadIsRejected(byte[] payload)
    {
        Assert.False(IndoorBikeData.TryParse(payload, out var data));
        Assert.Null(data);
        Assert.Throws<FormatException>(() => IndoorBikeData.Parse(payload));
    }
}

public class FtmsControlPointTests
{
    [Fact]
    public void SimpleCommands()
    {
        Assert.Equal(new byte[] { 0x00 }, FtmsControlPoint.RequestControl());
        Assert.Equal(new byte[] { 0x01 }, FtmsControlPoint.Reset());
        Assert.Equal(new byte[] { 0x07 }, FtmsControlPoint.StartOrResume());
        Assert.Equal(new byte[] { 0x08, 0x01 }, FtmsControlPoint.Stop());
        Assert.Equal(new byte[] { 0x08, 0x02 }, FtmsControlPoint.Pause());
        Assert.Equal(new byte[] { 0x13, 0x01 }, FtmsControlPoint.StartSpinDown());
    }

    [Theory]
    [InlineData(250, new byte[] { 0x05, 0xFA, 0x00 })]
    [InlineData(1000, new byte[] { 0x05, 0xE8, 0x03 })]
    [InlineData(-20, new byte[] { 0x05, 0x00, 0x00 })]
    [InlineData(100_000, new byte[] { 0x05, 0xFF, 0x7F })]
    public void SetTargetPower(int watts, byte[] expected)
    {
        Assert.Equal(expected, FtmsControlPoint.SetTargetPower(watts));
    }

    [Fact]
    public void SetIndoorBikeSimulationUphill()
    {
        // grade 5.00 % = 500 = 0x01F4, Crr 0.004 = 40, Cw 0.51 = 51
        byte[] expected = { 0x11, 0x00, 0x00, 0xF4, 0x01, 0x28, 0x33 };
        Assert.Equal(expected, FtmsControlPoint.SetIndoorBikeSimulation(5.0));
    }

    [Fact]
    public void SetIndoorBikeSimulationDownhillWithWind()
    {
        // wind -2.5 m/s = -2500 = 0xF63C, grade -3.5 % = -350 = 0xFEA2, Crr 0.008 = 80, Cw 0.40 = 40
        byte[] expected = { 0x11, 0x3C, 0xF6, 0xA2, 0xFE, 0x50, 0x28 };
        Assert.Equal(expected, FtmsControlPoint.SetIndoorBikeSimulation(-3.5, -2.5, 0.008, 0.40));
    }

    [Fact]
    public void SimulationValuesAreClamped()
    {
        byte[] bytes = FtmsControlPoint.SetIndoorBikeSimulation(1000.0, 0, 1.0, 10.0);
        Assert.Equal(new byte[] { 0x11, 0x00, 0x00, 0xFF, 0x7F, 0xFF, 0xFF }, bytes);
    }

    [Fact]
    public void ParsesSuccessResponse()
    {
        var response = FtmsControlPointResponse.Parse(new byte[] { 0x80, 0x05, 0x01 });

        Assert.Equal(FtmsOpCode.SetTargetPower, response.RequestOpCode);
        Assert.True(response.IsSuccess);
        Assert.Empty(response.Parameters);
    }

    [Fact]
    public void ParsesControlNotPermitted()
    {
        var response = FtmsControlPointResponse.Parse(new byte[] { 0x80, 0x11, 0x05 });

        Assert.Equal(FtmsOpCode.SetIndoorBikeSimulation, response.RequestOpCode);
        Assert.Equal(FtmsResultCode.ControlNotPermitted, response.Result);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void ResponseKeepsParameters()
    {
        var response = FtmsControlPointResponse.Parse(new byte[] { 0x80, 0x13, 0x01, 0x10, 0x0E, 0xA0, 0x0F });
        Assert.Equal(new byte[] { 0x10, 0x0E, 0xA0, 0x0F }, response.Parameters);
    }

    [Fact]
    public void RejectsNonResponse()
    {
        Assert.False(FtmsControlPointResponse.TryParse(new byte[] { 0x05, 0xFA, 0x00 }, out _));
    }
}

public class FtmsFeatureTests
{
    [Fact]
    public void ParsesFeatureBits()
    {
        // machine: cadence (bit1) + power (bit14) = 0x00004002
        // target: power (bit3) + simulation (bit13) = 0x00002008
        byte[] payload = { 0x02, 0x40, 0x00, 0x00, 0x08, 0x20, 0x00, 0x00 };

        var features = FtmsFeatures.Parse(payload);

        Assert.True(features.SupportsCadence);
        Assert.True(features.SupportsPowerMeasurement);
        Assert.False(features.SupportsHeartRate);
        Assert.True(features.SupportsPowerTarget);
        Assert.True(features.SupportsIndoorBikeSimulation);
        Assert.False(features.SupportsResistanceTarget);
        Assert.False(features.SupportsSpinDown);
    }

    [Fact]
    public void ParsesAndClampsPowerRange()
    {
        // 0..2200 W, step 5 W
        var range = SupportedPowerRange.Parse(new byte[] { 0x00, 0x00, 0x98, 0x08, 0x05, 0x00 });

        Assert.Equal(0, range.MinimumW);
        Assert.Equal(2200, range.MaximumW);
        Assert.Equal(5, range.IncrementW);
        Assert.Equal(2200, range.Clamp(3000));
        Assert.Equal(0, range.Clamp(-10));
        Assert.Equal(245, range.Clamp(247));
    }
}
