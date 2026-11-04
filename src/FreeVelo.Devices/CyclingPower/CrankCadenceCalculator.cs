namespace FreeVelo.Devices.CyclingPower
{
    /// <summary>
    /// Derives cadence from cumulative crank revolutions and the last crank event time
    /// (1/1024 s), as sent by Cycling Power and Cycling Speed &amp; Cadence sensors.
    /// Handles 16-bit rollover of both values.
    /// </summary>
    public sealed class CrankCadenceCalculator
    {
        private const double TicksPerSecond = 1024.0;
        private const int MaxPlausibleRevolutionsPerEvent = 20;

        // Longer than 6 s per revolution (< 10 rpm) means the cranks stood still in between,
        // e.g. the first pedal stroke after a stop – not a real cadence.
        private const int MaxTicksPerRevolution = 6 * 1024;

        private readonly long _stopTimeoutMs;
        private ushort? _lastRevolutions;
        private ushort _lastEventTime;
        private long _lastChangeMs;

        /// <param name="stopTimeoutMs">Cadence drops to 0 when no new crank event arrives for this long.</param>
        public CrankCadenceCalculator(long stopTimeoutMs = 3000)
        {
            _stopTimeoutMs = stopTimeoutMs;
        }

        /// <summary>Current cadence in rpm, or null until two samples were seen.</summary>
        public double? CadenceRpm { get; private set; }

        /// <summary>Feeds a new sample and returns the current cadence.</summary>
        /// <param name="cumulativeRevolutions">Cumulative crank revolutions.</param>
        /// <param name="lastEventTime">Time of the last crank event in 1/1024 s.</param>
        /// <param name="nowMs">Monotonic local time in milliseconds.</param>
        public double? Update(ushort cumulativeRevolutions, ushort lastEventTime, long nowMs)
        {
            if (_lastRevolutions == null)
            {
                Remember(cumulativeRevolutions, lastEventTime, nowMs);
                return CadenceRpm;
            }

            int deltaRevolutions = (ushort)(cumulativeRevolutions - _lastRevolutions.Value);
            int deltaTicks = (ushort)(lastEventTime - _lastEventTime);

            if (deltaRevolutions == 0 || deltaTicks == 0)
            {
                if (nowMs - _lastChangeMs >= _stopTimeoutMs)
                {
                    CadenceRpm = 0;
                }

                return CadenceRpm;
            }

            if (deltaRevolutions <= MaxPlausibleRevolutionsPerEvent
                && deltaTicks <= deltaRevolutions * MaxTicksPerRevolution)
            {
                CadenceRpm = deltaRevolutions * 60.0 * TicksPerSecond / deltaTicks;
            }

            Remember(cumulativeRevolutions, lastEventTime, nowMs);
            return CadenceRpm;
        }

        /// <summary>Forgets all state, e.g. after a reconnect.</summary>
        public void Reset()
        {
            _lastRevolutions = null;
            CadenceRpm = null;
        }

        private void Remember(ushort revolutions, ushort eventTime, long nowMs)
        {
            _lastRevolutions = revolutions;
            _lastEventTime = eventTime;
            _lastChangeMs = nowMs;
        }
    }
}
