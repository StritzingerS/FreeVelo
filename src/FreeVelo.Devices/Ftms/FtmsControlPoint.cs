using System;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.Ftms
{
    /// <summary>FTMS Fitness Machine Control Point (0x2AD9) op codes.</summary>
    public enum FtmsOpCode : byte
    {
        RequestControl = 0x00,
        Reset = 0x01,
        SetTargetPower = 0x05,
        StartOrResume = 0x07,
        StopOrPause = 0x08,
        SetIndoorBikeSimulation = 0x11,
        SpinDownControl = 0x13,
        ResponseCode = 0x80,
    }

    /// <summary>FTMS control point result codes.</summary>
    public enum FtmsResultCode : byte
    {
        Success = 0x01,
        OpCodeNotSupported = 0x02,
        InvalidParameter = 0x03,
        OperationFailed = 0x04,
        ControlNotPermitted = 0x05,
    }

    /// <summary>Builds FTMS control point commands (written with response).</summary>
    public static class FtmsControlPoint
    {
        /// <summary>Must be sent (and acknowledged) before any other command.</summary>
        public static byte[] RequestControl() => new[] { (byte)FtmsOpCode.RequestControl };

        public static byte[] Reset() => new[] { (byte)FtmsOpCode.Reset };

        public static byte[] StartOrResume() => new[] { (byte)FtmsOpCode.StartOrResume };

        public static byte[] Stop() => new[] { (byte)FtmsOpCode.StopOrPause, (byte)0x01 };

        public static byte[] Pause() => new[] { (byte)FtmsOpCode.StopOrPause, (byte)0x02 };

        /// <summary>ERG mode: the trainer holds <paramref name="watts"/> regardless of cadence.</summary>
        public static byte[] SetTargetPower(int watts)
        {
            short value = (short)Math.Max(0, Math.Min(short.MaxValue, watts));
            return new[] { (byte)FtmsOpCode.SetTargetPower, (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
        }

        /// <summary>
        /// SIM mode: the trainer simulates riding on a road.
        /// </summary>
        /// <param name="gradePercent">Road grade in percent (resolution 0.01 %).</param>
        /// <param name="windSpeedMps">Head wind (+) or tail wind (−) in m/s (resolution 0.001).</param>
        /// <param name="rollingResistance">Coefficient of rolling resistance (resolution 0.0001, max 0.0255).</param>
        /// <param name="windResistanceKgPerM">Wind resistance coefficient ½·ρ·CdA in kg/m (resolution 0.01, max 2.55).</param>
        public static byte[] SetIndoorBikeSimulation(
            double gradePercent,
            double windSpeedMps = 0.0,
            double rollingResistance = 0.004,
            double windResistanceKgPerM = 0.51)
        {
            short wind = ToInt16(windSpeedMps * 1000.0);
            short grade = ToInt16(gradePercent * 100.0);
            byte crr = ToUInt8(rollingResistance * 10000.0);
            byte cw = ToUInt8(windResistanceKgPerM * 100.0);
            return new[]
            {
                (byte)FtmsOpCode.SetIndoorBikeSimulation,
                (byte)(wind & 0xFF), (byte)((wind >> 8) & 0xFF),
                (byte)(grade & 0xFF), (byte)((grade >> 8) & 0xFF),
                crr,
                cw,
            };
        }

        public static byte[] StartSpinDown() => new[] { (byte)FtmsOpCode.SpinDownControl, (byte)0x01 };

        public static byte[] IgnoreSpinDown() => new[] { (byte)FtmsOpCode.SpinDownControl, (byte)0x02 };

        private static short ToInt16(double value) =>
            (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero)));

        private static byte ToUInt8(double value) =>
            (byte)Math.Max(0, Math.Min(byte.MaxValue, Math.Round(value, MidpointRounding.AwayFromZero)));
    }

    /// <summary>Indication sent by the trainer on the control point after each command.</summary>
    public sealed class FtmsControlPointResponse
    {
        private FtmsControlPointResponse(FtmsOpCode requestOpCode, FtmsResultCode result, byte[] parameters)
        {
            RequestOpCode = requestOpCode;
            Result = result;
            Parameters = parameters;
        }

        /// <summary>The op code of the command this response belongs to.</summary>
        public FtmsOpCode RequestOpCode { get; }

        public FtmsResultCode Result { get; }

        /// <summary>Optional response parameters (e.g. spin down target speeds).</summary>
        public byte[] Parameters { get; }

        public bool IsSuccess => Result == FtmsResultCode.Success;

        /// <summary>Parses a payload; throws <see cref="FormatException"/> if it is not a response.</summary>
        public static FtmsControlPointResponse Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            byte opCode = reader.ReadUInt8();
            if (opCode != (byte)FtmsOpCode.ResponseCode)
            {
                throw new FormatException($"Not a control point response (op code 0x{opCode:X2}).");
            }

            var request = (FtmsOpCode)reader.ReadUInt8();
            var result = (FtmsResultCode)reader.ReadUInt8();
            return new FtmsControlPointResponse(request, result, reader.ReadRemaining());
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, out FtmsControlPointResponse? response)
        {
            try
            {
                response = Parse(payload);
                return true;
            }
            catch (FormatException)
            {
                response = null;
                return false;
            }
        }

        public override string ToString() => $"{RequestOpCode}: {Result}";
    }
}
