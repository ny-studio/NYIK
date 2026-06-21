using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Estimator;

namespace NYIK.Tests
{
    public class ChestEstimatorTests
    {
        [Test]
        public void Estimate_SetsChestOnLineBetweenHipsAndHead()
        {
            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Hips] = BoneTarget.Tracked(new Vector3(0, 1.0f, 0), Quaternion.identity),
                [HumanBodyBones.Head] = BoneTarget.Tracked(new Vector3(0, 1.7f, 0), Quaternion.identity),
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);

            var est = new ChestEstimator { ChestHeightRatio = 0.55f, RotationBlend = 0.5f };
            est.Estimate(ctx);

            Assert.IsTrue(targets.ContainsKey(HumanBodyBones.Chest));
            var chest = targets[HumanBodyBones.Chest];
            // ChestHeightRatio 0.55 between Y=1.0 and Y=1.7 → 1.0 + 0.55*0.7 = 1.385
            Assert.AreEqual(1.385f, chest.Position.y, 1e-4f);
            Assert.AreEqual(0f, chest.Position.x, 1e-4f);
            Assert.AreEqual(0f, chest.Position.z, 1e-4f);
            Assert.Less(chest.Confidence, 1f, "Estimated targets should have <1 confidence.");
        }

        [Test]
        public void Estimate_SkipsWhenDependenciesMissing()
        {
            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Head] = BoneTarget.Tracked(Vector3.up, Quaternion.identity),
                // No Hips
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);
            new ChestEstimator().Estimate(ctx);
            Assert.IsFalse(targets.ContainsKey(HumanBodyBones.Chest));
        }
    }
}
