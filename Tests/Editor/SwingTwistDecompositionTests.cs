using NUnit.Framework;
using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Tests
{
    public class SwingTwistDecompositionTests
    {
        const float Eps = 1e-3f;

        [Test]
        public void Decompose_Identity_ProducesIdentitySwingAndTwist()
        {
            SwingTwistDecomposition.Decompose(Quaternion.identity, Vector3.up, out var swing, out var twist);
            AssertQuaternionsClose(Quaternion.identity, swing);
            AssertQuaternionsClose(Quaternion.identity, twist);
        }

        [Test]
        public void Decompose_PureTwist_PutsAllRotationIntoTwist()
        {
            var q = Quaternion.AngleAxis(45f, Vector3.up);
            SwingTwistDecomposition.Decompose(q, Vector3.up, out var swing, out var twist);

            AssertQuaternionsClose(Quaternion.identity, swing, "Pure twist should leave swing identity.");
            AssertQuaternionsClose(q, twist, "Pure twist around axis should equal twist component.");
        }

        [Test]
        public void Decompose_PureSwing_PutsAllRotationIntoSwing()
        {
            var q = Quaternion.AngleAxis(30f, Vector3.right);
            SwingTwistDecomposition.Decompose(q, Vector3.up, out var swing, out var twist);

            AssertQuaternionsClose(q, swing, "Pure swing should equal swing component.");
            AssertQuaternionsClose(Quaternion.identity, twist, "Pure swing should leave twist identity.");
        }

        [Test]
        public void Decompose_Recomposes()
        {
            // For arbitrary q, check swing * twist == q (the definition).
            var q = Quaternion.Euler(15f, 25f, -10f);
            SwingTwistDecomposition.Decompose(q, Vector3.up, out var swing, out var twist);
            var recomposed = swing * twist;
            AssertQuaternionsClose(q, recomposed);
        }

        static void AssertQuaternionsClose(Quaternion expected, Quaternion actual, string msg = "")
        {
            float dot = Mathf.Abs(Quaternion.Dot(expected, actual));
            Assert.Greater(dot, 1f - Eps, $"{msg} expected {expected} got {actual}");
        }
    }
}
