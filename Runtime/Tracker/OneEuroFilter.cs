using UnityEngine;

namespace NYIK.Tracker
{
    /// <summary>
    /// One-Euro filter for adaptive smoothing of noisy signals.
    /// Strong filtering at low speeds (jitter), weak at high speeds (responsiveness).
    ///
    /// Reference: Casiez et al., "1€ Filter: A Simple Speed-based Low-pass Filter
    /// for Noisy Input in Interactive Systems" (CHI 2012).
    /// http://cristal.univ-lille.fr/~casiez/1euro/
    /// </summary>
    public sealed class OneEuroFilter
    {
        /// <summary>Minimum cutoff frequency in Hz. Higher = less filtering at rest.</summary>
        public float MinCutoff = 1.0f;

        /// <summary>Derivative cutoff frequency in Hz.</summary>
        public float DCutoff = 1.0f;

        /// <summary>Speed coefficient. Higher = follow fast motion more closely.</summary>
        public float Beta = 0.007f;

        private float _prevValue;
        private float _prevDerivative;
        private bool _hasPrev;

        public void Reset()
        {
            _hasPrev = false;
            _prevValue = 0f;
            _prevDerivative = 0f;
        }

        public float Filter(float value, float dt)
        {
            if (dt <= 0f) return value;

            if (!_hasPrev)
            {
                _prevValue = value;
                _prevDerivative = 0f;
                _hasPrev = true;
                return value;
            }

            // Estimate derivative
            float dRaw = (value - _prevValue) / dt;
            float dHat = LowPass(dRaw, _prevDerivative, DCutoff, dt);

            // Adaptive cutoff: faster signal -> higher cutoff (less smoothing)
            float cutoff = MinCutoff + Beta * Mathf.Abs(dHat);

            float result = LowPass(value, _prevValue, cutoff, dt);

            _prevValue = result;
            _prevDerivative = dHat;
            return result;
        }

        private static float LowPass(float x, float xPrev, float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            float alpha = 1f / (1f + tau / dt);
            return alpha * x + (1f - alpha) * xPrev;
        }
    }

    /// <summary>
    /// Quaternion variant of OneEuroFilter.
    /// Filters each component independently and renormalizes.
    /// For small rotation deltas this is acceptable; for large jumps consider
    /// converting to axis-angle and filtering the angle separately.
    /// </summary>
    public sealed class OneEuroQuaternionFilter
    {
        private readonly OneEuroFilter _fx = new();
        private readonly OneEuroFilter _fy = new();
        private readonly OneEuroFilter _fz = new();
        private readonly OneEuroFilter _fw = new();

        public float MinCutoff
        {
            get => _fx.MinCutoff;
            set { _fx.MinCutoff = value; _fy.MinCutoff = value; _fz.MinCutoff = value; _fw.MinCutoff = value; }
        }

        public float DCutoff
        {
            get => _fx.DCutoff;
            set { _fx.DCutoff = value; _fy.DCutoff = value; _fz.DCutoff = value; _fw.DCutoff = value; }
        }

        public float Beta
        {
            get => _fx.Beta;
            set { _fx.Beta = value; _fy.Beta = value; _fz.Beta = value; _fw.Beta = value; }
        }

        private Quaternion _prev = Quaternion.identity;

        public void Reset()
        {
            _fx.Reset(); _fy.Reset(); _fz.Reset(); _fw.Reset();
            _prev = Quaternion.identity;
        }

        public Quaternion Filter(Quaternion q, float dt)
        {
            // Ensure same-hemisphere with previous to avoid component sign flips
            if (Quaternion.Dot(q, _prev) < 0f)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }

            float x = _fx.Filter(q.x, dt);
            float y = _fy.Filter(q.y, dt);
            float z = _fz.Filter(q.z, dt);
            float w = _fw.Filter(q.w, dt);

            var result = new Quaternion(x, y, z, w);
            // Normalize because per-component filtering loses unit length
            float n = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (n > 1e-6f)
            {
                result = new Quaternion(x / n, y / n, z / n, w / n);
            }
            _prev = result;
            return result;
        }
    }
}
