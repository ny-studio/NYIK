using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using NYIK.Estimator;
using NYIK.Tracker;

namespace NYIK.Tests
{
    public class BodyPartEstimatorRegistryTests
    {
        [Test]
        public void Resolve_RunsEstimatorsInDependencyOrder()
        {
            var trace = new List<HumanBodyBones>();
            var reg = new BodyPartEstimatorRegistry();

            // Chest depends on Hips, Hips depends on Head.
            reg.Register(new TraceEstimator(HumanBodyBones.Chest,
                new[] { HumanBodyBones.Hips }, trace));
            reg.Register(new TraceEstimator(HumanBodyBones.Hips,
                new[] { HumanBodyBones.Head }, trace));

            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Head] = BoneTarget.Tracked(Vector3.zero, Quaternion.identity),
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);
            reg.ResolveAll(ctx);

            Assert.AreEqual(new[] { HumanBodyBones.Hips, HumanBodyBones.Chest }, trace.ToArray());
        }

        [Test]
        public void Resolve_SkipsEstimatorWhenTrackerAlreadyPresent()
        {
            var trace = new List<HumanBodyBones>();
            var reg = new BodyPartEstimatorRegistry();
            reg.Register(new TraceEstimator(HumanBodyBones.Hips,
                new[] { HumanBodyBones.Head }, trace));

            // Pre-populate Hips as if a Waist tracker had filled it.
            var targets = new Dictionary<HumanBodyBones, BoneTarget>
            {
                [HumanBodyBones.Head] = BoneTarget.Tracked(Vector3.zero, Quaternion.identity),
                [HumanBodyBones.Hips] = BoneTarget.Tracked(Vector3.zero, Quaternion.identity),
            };
            var ctx = new EstimatorContext(null, null, targets, 0f);
            reg.ResolveAll(ctx);

            CollectionAssert.IsEmpty(trace,
                "Estimator must be skipped when its target bone is already tracked.");
        }

        [Test]
        public void Resolve_SkipsEstimatorIfDependencyMissing()
        {
            var trace = new List<HumanBodyBones>();
            var reg = new BodyPartEstimatorRegistry();
            reg.Register(new TraceEstimator(HumanBodyBones.Hips,
                new[] { HumanBodyBones.Head }, trace, writeOnRun: false));

            var targets = new Dictionary<HumanBodyBones, BoneTarget>(); // no Head!
            var ctx = new EstimatorContext(null, null, targets, 0f);
            reg.ResolveAll(ctx);

            // TraceEstimator only records when it sees its dependency. Without
            // a Head target, it should record nothing.
            CollectionAssert.IsEmpty(trace);
        }

        // Stub estimator: records execution order, optionally writes a target.
        sealed class TraceEstimator : IBodyPartEstimator
        {
            public HumanBodyBones TargetBone { get; }
            public HumanBodyBones[] DependsOn { get; }
            readonly List<HumanBodyBones> _trace;
            readonly bool _writeOnRun;

            public TraceEstimator(HumanBodyBones target, HumanBodyBones[] deps,
                                  List<HumanBodyBones> trace, bool writeOnRun = true)
            {
                TargetBone = target;
                DependsOn = deps;
                _trace = trace;
                _writeOnRun = writeOnRun;
            }

            public void Estimate(in EstimatorContext ctx)
            {
                // Only run if all declared deps are resolved (mimics a real estimator)
                foreach (var dep in DependsOn)
                {
                    if (!ctx.HasTarget(dep)) return;
                }
                _trace.Add(TargetBone);
                if (_writeOnRun)
                    ctx.SetTarget(TargetBone,
                        BoneTarget.Estimated(Vector3.zero, Quaternion.identity, 0.5f));
            }
        }
    }
}
