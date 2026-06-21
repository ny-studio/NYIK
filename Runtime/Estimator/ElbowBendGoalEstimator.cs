using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Produces a bend-goal position for the elbow when no elbow / forearm
    /// tracker is bound. The natural human elbow points DOWN and slightly
    /// BACKWARD relative to the shoulder→hand line. This estimator publishes
    /// a position the arm IK can use as a stable bend goal — fixes the
    /// classic "elbow flips to wrong side when arms cross body" problem.
    ///
    /// Output: position-only target on LowerArm representing the bend goal
    /// (NOT the actual elbow joint position).
    /// </summary>
    public sealed class ElbowBendGoalEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone { get; }
        public HumanBodyBones[] DependsOn { get; }

        private readonly bool _isLeft;

        public ElbowBendGoalEstimator(bool isLeft)
        {
            _isLeft = isLeft;
            TargetBone = isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            DependsOn = new[]
            {
                isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder,
                isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                HumanBodyBones.Chest,
            };
        }

        public void Estimate(in EstimatorContext ctx)
        {
            var shoulderKind = _isLeft ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
            var handKind = _isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;

            if (!ctx.TryGetTarget(shoulderKind, out var shoulder)) return;
            if (!ctx.TryGetTarget(handKind, out var hand)) return;
            if (!ctx.TryGetTarget(HumanBodyBones.Chest, out var chest)) return;

            Vector3 shoulderToHand = hand.Position - shoulder.Position;
            if (shoulderToHand.sqrMagnitude < 1e-6f) return;

            // Choose the in-plane direction that points DOWN+BACK in chest space.
            // Chest local down = -Vector3.up rotated by chest, chest local back = -forward.
            Vector3 chestDown = chest.Rotation * Vector3.down;
            Vector3 chestBack = chest.Rotation * Vector3.back;
            Vector3 preferredDir = (chestDown * 0.7f + chestBack * 0.3f).normalized;

            // Project preferredDir onto the plane perpendicular to shoulder→hand,
            // so the bend goal stays useful regardless of arm orientation.
            Vector3 shoulderToHandDir = shoulderToHand.normalized;
            Vector3 inPlane = preferredDir - Vector3.Dot(preferredDir, shoulderToHandDir) * shoulderToHandDir;
            if (inPlane.sqrMagnitude < 1e-6f)
            {
                inPlane = chestDown; // graceful fallback
            }
            else
            {
                inPlane.Normalize();
            }

            // Bend goal sits roughly one upper-arm length away from the midpoint.
            Vector3 mid = (shoulder.Position + hand.Position) * 0.5f;
            float armLen = shoulderToHand.magnitude;
            Vector3 bendGoal = mid + inPlane * armLen * 0.5f;

            ctx.SetTarget(TargetBone, BoneTarget.PositionOnly(bendGoal, 0.6f));
        }
    }
}
