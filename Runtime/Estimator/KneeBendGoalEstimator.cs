using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Bend-goal estimator for the knee. The natural human knee points
    /// FORWARD relative to the hip. This estimator publishes a forward-of-hip
    /// position as the bend goal so the leg IK doesn't flex the knee
    /// backward when the foot moves behind the body.
    ///
    /// Output: position-only target on LowerLeg (the bend goal).
    /// </summary>
    public sealed class KneeBendGoalEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone { get; }
        public HumanBodyBones[] DependsOn { get; }

        private readonly bool _isLeft;

        public KneeBendGoalEstimator(bool isLeft)
        {
            _isLeft = isLeft;
            TargetBone = isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
            DependsOn = new[]
            {
                isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                HumanBodyBones.Hips,
            };
        }

        public void Estimate(in EstimatorContext ctx)
        {
            var hipBoneKind = _isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            if (!ctx.TryGetTarget(HumanBodyBones.Hips, out var hips)) return;

            // UpperLeg target may not be set; fall back to the bone's current world position
            Vector3 hipJoint;
            if (ctx.TryGetTarget(hipBoneKind, out var upper))
            {
                hipJoint = upper.Position;
            }
            else
            {
                var bone = ctx.GetBoneTransform(hipBoneKind);
                if (bone == null) return;
                hipJoint = bone.position;
            }

            // Bend goal: ~30 cm in front of the hip joint at hip height.
            Vector3 forward = hips.Rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            else forward.Normalize();

            Vector3 bendGoal = hipJoint + forward * 0.30f;
            ctx.SetTarget(TargetBone, BoneTarget.PositionOnly(bendGoal, 0.55f));
        }
    }
}
