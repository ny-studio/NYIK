using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Estimator
{
    /// <summary>
    /// Estimates Hips from the Head target. Adapter around the existing
    /// <see cref="PelvisEstimator"/> so its logic survives the migration
    /// into the unified estimator pipeline.
    /// </summary>
    public sealed class HipsFromHeadEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone => HumanBodyBones.Hips;
        public HumanBodyBones[] DependsOn => new[] { HumanBodyBones.Head };

        private readonly PelvisEstimator _inner;
        private readonly Transform _root;

        public HipsFromHeadEstimator(PelvisEstimator inner, Transform root)
        {
            _inner = inner;
            _root = root;
        }

        public void Estimate(in EstimatorContext ctx)
        {
            if (!ctx.TryGetTarget(HumanBodyBones.Head, out var head)) return;
            if (_inner == null) return;

            Vector3 pos = _inner.Estimate(head.Position, head.Rotation, _root);

            // Rotation: blend pelvis rest rotation with head yaw (so torso turns
            // a bit when the head turns). The full pelvis yaw is applied by
            // SpineSolver later — here we just publish a reasonable target.
            var hipsBone = ctx.GetBoneTransform(HumanBodyBones.Hips);
            Quaternion rot = hipsBone != null ? hipsBone.rotation : Quaternion.identity;

            ctx.SetTarget(HumanBodyBones.Hips, BoneTarget.Estimated(pos, rot, 0.7f));
        }
    }
}
