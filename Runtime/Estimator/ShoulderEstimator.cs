using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Estimates a shoulder position by offsetting laterally from Chest along
    /// the chest's local right axis. Rotation defaults to the chest rotation —
    /// not anatomically perfect, but good enough as a bend-goal anchor when no
    /// shoulder tracker exists.
    /// </summary>
    public sealed class ShoulderEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone { get; }
        public HumanBodyBones[] DependsOn => new[] { HumanBodyBones.Chest };

        private readonly bool _isLeft;
        public float ShoulderHalfWidth = 0.17f; // meters; left or right offset from chest

        public ShoulderEstimator(bool isLeft)
        {
            _isLeft = isLeft;
            TargetBone = isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
        }

        public void Estimate(in EstimatorContext ctx)
        {
            if (!ctx.TryGetTarget(HumanBodyBones.Chest, out var chest)) return;

            Vector3 right = chest.Rotation * Vector3.right;
            Vector3 offset = right * (_isLeft ? -ShoulderHalfWidth : ShoulderHalfWidth);
            Vector3 pos = chest.Position + offset;

            ctx.SetTarget(TargetBone, BoneTarget.Estimated(pos, chest.Rotation, 0.55f));
        }
    }
}
