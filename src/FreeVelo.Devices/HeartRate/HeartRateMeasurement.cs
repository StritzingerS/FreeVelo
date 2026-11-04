using System;
using System.Collections.Generic;
using FreeVelo.Devices.Ble;

namespace FreeVelo.Devices.HeartRate
{
    /// <summary>Skin contact status reported by a heart rate sensor.</summary>
    public enum SensorContact
    {
        NotSupported,
        NotDetected,
        Detected,
    }

    /// <summary>Heart Rate Measurement characteristic (0x2A37).</summary>
    public sealed class HeartRateMeasurement
    {
        private HeartRateMeasurement(int bpm, SensorContact contact, int? energyKj, IReadOnlyList<double> rrIntervalsMs)
        {
            Bpm = bpm;
            Contact = contact;
            EnergyExpendedKj = energyKj;
            RrIntervalsMs = rrIntervalsMs;
        }

        public int Bpm { get; }

        public SensorContact Contact { get; }

        public int? EnergyExpendedKj { get; }

        /// <summary>RR intervals in milliseconds (may be empty).</summary>
        public IReadOnlyList<double> RrIntervalsMs { get; }

        public static HeartRateMeasurement Parse(ReadOnlySpan<byte> payload)
        {
            var reader = new ByteReader(payload);
            byte flags = reader.ReadUInt8();

            int bpm = (flags & 0x01) != 0 ? reader.ReadUInt16() : reader.ReadUInt8();

            SensorContact contact = ((flags >> 1) & 0x03) switch
            {
                0x02 => SensorContact.NotDetected,
                0x03 => SensorContact.Detected,
                _ => SensorContact.NotSupported,
            };

            int? energy = (flags & 0x08) != 0 ? reader.ReadUInt16() : (int?)null;

            var rr = new List<double>();
            if ((flags & 0x10) != 0)
            {
                while (reader.Remaining >= 2)
                {
                    rr.Add(reader.ReadUInt16() * 1000.0 / 1024.0);
                }
            }

            return new HeartRateMeasurement(bpm, contact, energy, rr);
        }

        public static bool TryParse(ReadOnlySpan<byte> payload, out HeartRateMeasurement? measurement)
        {
            try
            {
                measurement = Parse(payload);
                return true;
            }
            catch (FormatException)
            {
                measurement = null;
                return false;
            }
        }
    }
}
