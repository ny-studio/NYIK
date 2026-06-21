using NUnit.Framework;
using UnityEngine;
using NYIK.Tracker;

namespace NYIK.Tests
{
    /// <summary>
    /// Unit tests for OneEuroFilter behavior. These don't require an Animator
    /// or VR setup — pure numerical tests that catch filter regressions.
    /// </summary>
    public class OneEuroFilterTests
    {
        [Test]
        public void Filter_FirstSample_ReturnsInputUnchanged()
        {
            var f = new OneEuroFilter { MinCutoff = 1f, Beta = 0.007f };
            float result = f.Filter(5f, 1f / 90f);
            Assert.AreEqual(5f, result, 1e-6f);
        }

        [Test]
        public void Filter_SteadyInput_Converges()
        {
            var f = new OneEuroFilter { MinCutoff = 1f, Beta = 0.007f };
            float dt = 1f / 90f;
            for (int i = 0; i < 200; i++) f.Filter(10f, dt);
            float result = f.Filter(10f, dt);
            Assert.AreEqual(10f, result, 1e-4f, "Filter should converge to a constant input.");
        }

        [Test]
        public void Filter_SmoothsHighFrequencyNoise()
        {
            var f = new OneEuroFilter { MinCutoff = 0.5f, Beta = 0.001f };
            float dt = 1f / 90f;
            // Inject zero-mean noise around 0
            float lastFiltered = 0f;
            float maxAbsOutput = 0f;
            for (int i = 0; i < 200; i++)
            {
                float noise = Mathf.Sin(i * 17.3f) * 1.0f; // pseudo-noise
                lastFiltered = f.Filter(noise, dt);
                if (i > 50) maxAbsOutput = Mathf.Max(maxAbsOutput, Mathf.Abs(lastFiltered));
            }
            Assert.Less(maxAbsOutput, 0.6f, "Filter should attenuate fast oscillation around zero.");
        }

        [Test]
        public void QuaternionFilter_HemisphereFlip_HandledCorrectly()
        {
            var f = new OneEuroQuaternionFilter { MinCutoff = 1f, Beta = 0.007f };
            float dt = 1f / 90f;

            var q = Quaternion.Euler(0f, 30f, 0f);
            for (int i = 0; i < 20; i++) f.Filter(q, dt);

            // Same rotation but with negated quaternion (opposite hemisphere)
            var qFlipped = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            var filtered = f.Filter(qFlipped, dt);

            // Should still be close to q (or -q which is the same rotation)
            float dot = Mathf.Abs(Quaternion.Dot(filtered, q));
            Assert.Greater(dot, 0.99f, "Quaternion filter should handle hemisphere flips.");
        }
    }
}
