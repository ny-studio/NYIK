using NUnit.Framework;
using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Tests
{
    public class QuaternionMathTests
    {
        const float Eps = 1e-3f;

        [Test]
        public void FromToRotationStable_SameDirection_ReturnsIdentity()
        {
            var q = QuaternionMath.FromToRotationStable(Vector3.forward, Vector3.forward);
            AssertCloseToIdentity(q);
        }

        [Test]
        public void FromToRotationStable_PerpendicularDirections_Produces90Rotation()
        {
            var q = QuaternionMath.FromToRotationStable(Vector3.forward, Vector3.right);
            // Apply to forward → should give right (within tolerance)
            var v = q * Vector3.forward;
            AssertVectorsClose(Vector3.right, v);
        }

        [Test]
        public void FromToRotationStable_OppositeDirections_GivesDeterministic180()
        {
            // Antipodal case where Unity's built-in is undefined
            var q1 = QuaternionMath.FromToRotationStable(Vector3.up, Vector3.down);
            var q2 = QuaternionMath.FromToRotationStable(Vector3.up, Vector3.down);
            // Same call → same result (no random axis)
            float dot = Mathf.Abs(Quaternion.Dot(q1, q2));
            Assert.Greater(dot, 1f - Eps, "Antipodal rotation should be deterministic.");

            // And applying it should flip the input
            var v = q1 * Vector3.up;
            AssertVectorsClose(Vector3.down, v);
        }

        [Test]
        public void FromToRotationStable_ZeroInput_ReturnsIdentity()
        {
            var q = QuaternionMath.FromToRotationStable(Vector3.zero, Vector3.forward);
            AssertCloseToIdentity(q);
        }

        [Test]
        public void StablePerpendicular_AlwaysPerpendicular()
        {
            Vector3[] tests = {
                Vector3.up, Vector3.right, Vector3.forward,
                new Vector3(1f, 1f, 1f).normalized,
                new Vector3(0.99f, 0.01f, 0.01f).normalized,
            };
            foreach (var v in tests)
            {
                var perp = QuaternionMath.StablePerpendicular(v);
                Assert.AreEqual(1f, perp.magnitude, Eps, $"Perpendicular of {v} should be unit length.");
                Assert.Less(Mathf.Abs(Vector3.Dot(v, perp)), Eps,
                    $"Result must be perpendicular to {v} (dot was {Vector3.Dot(v, perp)}).");
            }
        }

        [Test]
        public void SafeSlerp_HandlesOppositeQuaternions()
        {
            var a = Quaternion.identity;
            var b = new Quaternion(-a.x, -a.y, -a.z, -a.w); // same rotation, opposite quaternion
            var mid = QuaternionMath.SafeSlerp(a, b, 0.5f);
            // Should be effectively identity (same rotation)
            AssertCloseToIdentity(mid);
        }

        static void AssertCloseToIdentity(Quaternion q)
        {
            float dot = Mathf.Abs(Quaternion.Dot(q, Quaternion.identity));
            Assert.Greater(dot, 1f - Eps, $"Expected identity-equivalent, got {q}");
        }

        static void AssertVectorsClose(Vector3 expected, Vector3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Eps, "X");
            Assert.AreEqual(expected.y, actual.y, Eps, "Y");
            Assert.AreEqual(expected.z, actual.z, Eps, "Z");
        }
    }
}
