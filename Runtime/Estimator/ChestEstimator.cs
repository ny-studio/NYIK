using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Estimates Chest position + rotation from Hips and Head. The chest sits
    /// on the line between hips and head; rotation interpolates the two so
    /// the torso bends naturally between them.
    ///
    /// Skipped automatically when an Upper-Chest or Chest tracker is bound.
    /// </summary>
    public sealed class ChestEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone => HumanBodyBones.Chest;
        public HumanBodyBones[] DependsOn => new[]
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Head,
        };

        /// <summary>
        /// Fraction of the hips→head distance where the chest sits. 0 = at
        /// hips, 1 = at head. Anatomical default ~0.55.
        /// </summary>
        public float ChestHeightRatio = 0.55f;

        /// <summary>
        /// Rotation blend between hips and head. 0 = follow hips, 1 = follow head.
        /// </summary>
        public float RotationBlend = 0.5f;

        public void Estimate(in EstimatorContext ctx)
        {
            if (!ctx.TryGetTarget(HumanBodyBones.Hips, out var hips)) return;
            if (!ctx.TryGetTarget(HumanBodyBones.Head, out var head)) return;

            Vector3 pos = Vector3.Lerp(hips.Position, head.Position, ChestHeightRatio);
            Quaternion rot = Quaternion.Slerp(hips.Rotation, head.Rotation, RotationBlend);

            ctx.SetTarget(HumanBodyBones.Chest, BoneTarget.Estimated(pos, rot, 0.65f));
        }
    }
}
