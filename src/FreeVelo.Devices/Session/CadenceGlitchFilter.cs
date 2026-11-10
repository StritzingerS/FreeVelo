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
        public const long MaxHoldMs = 45_000;

        /// <summary>A drop is measured against the highest accepted cadence of this long ago (a drop can come in two steps).</summary>
        private const long ReferenceMs = 3000;
        private readonly System.Collections.Generic.Queue<(long Ms, double Rpm)> _recent = new System.Collections.Generic.Queue<(long, double)>();
        private const double DropFactor = 0.7;
        private const double MinCadenceForCheck = 60;
        private const double PowerKeptFactor = 0.6;

        private double? _good;
        private int? _goodPower;
        private long _suspectSinceMs = -1;
        private double _suspectReference;

        /// <summary>True while a suspicious drop is being held back.</summary>
        public bool Holding => _suspectSinceMs >= 0;

        /// <summary>Feeds a reported cadence (and the power at that moment); returns the cadence to use.</summary>
        public double Next(long nowMs, double cadence, int? powerW)
        {
            while (_recent.Count > 0 && nowMs - _recent.Peek().Ms > ReferenceMs) _recent.Dequeue();
            // While a drop is held, compare with the cadence from before it (the recent values age out meanwhile).
            double reference = _suspectSinceMs >= 0 ? _suspectReference : _good ?? 0;
            foreach (var (_, rpm) in _recent) reference = Math.Max(reference, rpm);
            if (_good.HasValue && reference >= MinCadenceForCheck && cadence > 0 && cadence < reference * DropFactor)
            {
                bool powerKept = !powerW.HasValue || !_goodPower.HasValue || powerW.Value >= _goodPower.Value * PowerKeptFactor;
                if (powerKept)
                {
                    if (_suspectSinceMs < 0)
                    {
                        _suspectSinceMs = nowMs;
                        _suspectReference = reference;
                    }

                    if (nowMs - _suspectSinceMs < MaxHoldMs) return reference;
                }
            }

            // Accepted: normal value, back from a glitch, or a confirmed real change.
            _suspectSinceMs = -1;
            _good = cadence;
            _goodPower = powerW;
            _recent.Enqueue((nowMs, cadence));
            return cadence;
        }

        public void Reset()
        {
            _good = null;
            _goodPower = null;
            _suspectSinceMs = -1;
            _recent.Clear();
        }
    }
}
