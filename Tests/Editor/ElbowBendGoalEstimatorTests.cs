using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Estimator;

namespace NYIK.Tests
{
    public class ElbowBendGoalEstimatorTests
    {
        [Test]
        public void Estimate_BendGoalPointsDownAndBackFromArmLine()
        {
            // Setup: standing avatar, left arm extended straight forward.
            //   chest at origin, facing +Z
            //   left shoulder at (-0.17, 0, 0)
            //   left hand at (-0.17, 0, 0.5) — straight forward
            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Chest] = BoneTarget.Tracked(Vector3.zero, Quaternion.identity),
                [HumanBodyBones.LeftShoulder] = BoneTarget.Tracked(new Vector3(-0.17f, 0, 0), Quaternion.identity),
                [HumanBodyBones.LeftHand] = BoneTarget.Tracked(new Vector3(-0.17f, 0, 0.5f), Quaternion.identity),
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);

            var est = new ElbowBendGoalEstimator(isLeft: true);
            est.Estimate(ctx);

            Assert.IsTrue(targets.ContainsKey(HumanBodyBones.LeftLowerArm));
            var bend = targets[HumanBodyBones.LeftLowerArm];
            Assert.IsTrue(bend.HasPosition);
            // Bend goal should be below (Y < 0) the arm line — chest local down dominates
            Assert.Less(bend.Position.y, 0f, "Elbow bend goal should be below the arm line.");
        }

        [Test]
        public void Estimate_SkipsWhenAnyDependencyMissing()
        {
            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.LeftShoulder] = BoneTarget.Tracked(Vector3.zero, Quaternion.identity),
                // Hand and chest missing
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);
            new ElbowBendGoalEstimator(isLeft: true).Estimate(ctx);
            Assert.IsFalse(targets.ContainsKey(HumanBodyBones.LeftLowerArm));
        }
    }
}
