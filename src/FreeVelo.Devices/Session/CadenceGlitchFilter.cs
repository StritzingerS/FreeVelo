using System;

namespace FreeVelo.Devices.Session
{
    /// <summary>
    /// Hides a known trainer glitch (seen on the Tacx Neo 2, also in other apps): at high power the reported
    /// cadence suddenly jumps from ~100 rpm to a fixed ~55 or ~37 rpm for several seconds and then back, while
    /// the power stays the same. A real rider can't lose 30 % cadence within one update without the power
    /// dropping too, so such a drop is held back until it is confirmed:
    /// - the value comes back near the last good cadence → it was a glitch (never shown),
    /// - the power drops as well, the cadence goes to 0, or it lasts longer than <see cref="MaxHoldMs"/> → it is
    ///   real and accepted.
    /// </summary>
    public sealed class CadenceGlitchFilter
    {
        public const long MaxHoldMs = 20_000;
        private const double DropFactor = 0.7;
        private const double MinCadenceForCheck = 60;
        private const double PowerKeptFactor = 0.6;

        private double? _good;
        private int? _goodPower;
        private long _suspectSinceMs = -1;

        /// <summary>True while a suspicious drop is being held back.</summary>
        public bool Holding => _suspectSinceMs >= 0;

        /// <summary>Feeds a reported cadence (and the power at that moment); returns the cadence to use.</summary>
        public double Next(long nowMs, double cadence, int? powerW)
        {
            if (_good.HasValue && _good.Value >= MinCadenceForCheck && cadence > 0 && cadence < _good.Value * DropFactor)
            {
                bool powerKept = !powerW.HasValue || !_goodPower.HasValue || powerW.Value >= _goodPower.Value * PowerKeptFactor;
                if (powerKept)
                {
                    if (_suspectSinceMs < 0) _suspectSinceMs = nowMs;
                    if (nowMs - _suspectSinceMs < MaxHoldMs) return _good.Value;
                }
            }

            // Accepted: normal value, back from a glitch, or a confirmed real change.
            _suspectSinceMs = -1;
            _good = cadence;
            _goodPower = powerW;
            return cadence;
        }

        public void Reset()
        {
            _good = null;
            _goodPower = null;
            _suspectSinceMs = -1;
        }
    }
}
