using NUnit.Framework;
using UnityEngine;
using NYIK.Anatomy;

namespace NYIK.Tests
{
    public class JointROMLimitsTests
    {
        [TearDown]
        public void TearDown()
        {
            // Always reset so other tests get default behavior
            JointROMLimits.SetReference(null);
        }

        [Test]
        public void DefaultElbow_RespectsAAOSFlexion150()
        {
            var limit = JointROMLimits.Get(HumanBodyBones.LeftLowerArm);
            // X is flexion axis per package convention
            Assert.GreaterOrEqual(limit.Max.x, 140f, "AAOS elbow flexion ~150°");
            Assert.LessOrEqual(limit.Min.x, 5f, "AAOS elbow hyperextension 0-5°");
        }

        [Test]
        public void DefaultUpperArm_ReturnsSwingTwist()
        {
            var sw = JointROMLimits.GetSwingTwist(HumanBodyBones.LeftUpperArm);
            Assert.IsTrue(sw.HasValue);
            Assert.GreaterOrEqual(sw.Value.SwingMaxDeg, 90f, "Shoulder swing cone is wide.");
        }

        [Test]
        public void DefaultElbow_ReturnsNullSwingTwist()
        {
            // Hinge joints don't use swing-twist
            var sw = JointROMLimits.GetSwingTwist(HumanBodyBones.LeftLowerArm);
            Assert.IsFalse(sw.HasValue, "Elbow should use Euler, not swing-twist.");
        }

        [Test]
        public void Reference_OverridesEulerForListedBones()
        {
            var asset = ScriptableObject.CreateInstance<JointROMReference>();
            asset.Entries.Add(new JointROMReference.JointEntry
            {
                Bone = HumanBodyBones.LeftLowerArm,
                UseSwingTwist = false,
                EulerMin = new Vector3(-99f, -99f, -99f),
                EulerMax = new Vector3(99f, 99f, 99f),
                Citation = "Test override.",
            });

            JointROMLimits.SetReference(asset);
            try
            {
                var limit = JointROMLimits.Get(HumanBodyBones.LeftLowerArm);
                Assert.AreEqual(-99f, limit.Min.x, 1e-4f);
                Assert.AreEqual(99f, limit.Max.x, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Reference_DoesNotAffectBonesItDoesntList()
        {
            var asset = ScriptableObject.CreateInstance<JointROMReference>();
            // Only override LeftLowerArm
            asset.Entries.Add(new JointROMReference.JointEntry
            {
                Bone = HumanBodyBones.LeftLowerArm,
                UseSwingTwist = false,
                EulerMin = Vector3.zero,
                EulerMax = Vector3.zero,
            });

            JointROMLimits.SetReference(asset);
            try
            {
                // RightLowerArm should still use the AAOS default (~150° flexion)
                var rightLimit = JointROMLimits.Get(HumanBodyBones.RightLowerArm);
                Assert.GreaterOrEqual(rightLimit.Max.x, 140f);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Reference_OverridesSwingTwistWhenFlagged()
        {
            var asset = ScriptableObject.CreateInstance<JointROMReference>();
            asset.Entries.Add(new JointROMReference.JointEntry
            {
                Bone = HumanBodyBones.LeftUpperArm,
                UseSwingTwist = true,
                TwistAxis = Vector3.up,
                TwistMinDeg = -30f,
                TwistMaxDeg = 30f,
                SwingMaxDeg = 45f,
            });

            JointROMLimits.SetReference(asset);
            try
            {
                var sw = JointROMLimits.GetSwingTwist(HumanBodyBones.LeftUpperArm);
                Assert.IsTrue(sw.HasValue);
                Assert.AreEqual(45f, sw.Value.SwingMaxDeg, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
