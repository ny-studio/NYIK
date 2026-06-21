using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Estimates foot position when no foot tracker is bound. Anchors the
    /// foot to the avatar's bind-pose foot position relative to its hip,
    /// optionally snapping to a Y=0 ground plane.
    ///
    /// Without a grounder (raycast onto colliders), this is a flat-floor
    /// approximation — fine for casual VR, weak for terrain.
    /// </summary>
    public sealed class FootEstimator : IBodyPartEstimator
    {
        public HumanBodyBones TargetBone { get; }
        public HumanBodyBones[] DependsOn => new[] { HumanBodyBones.Hips };

        private readonly bool _isLeft;

        /// <summary>Snap the foot Y to GroundPlaneY. Disable if the scene has terrain.</summary>
        public bool SnapToGround = true;

        public float GroundPlaneY = 0f;

        /// <summary>Captured at Initialize() from the bind pose.</summary>
        private Vector3 _hipsLocalFootOffset;
        private bool _hasBindOffset;

        public FootEstimator(bool isLeft)
        {
            _isLeft = isLeft;
            TargetBone = isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
        }

        /// <summary>
        /// Capture the bind-pose offset from Hips to this foot. Call once
        /// during NYIKHumanoid.Initialize after bone references are valid.
        /// </summary>
        public void CaptureBindPose(Animator animator)
        {
            if (animator == null) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var foot = animator.GetBoneTransform(TargetBone);
            if (hips == null || foot == null) return;
            _hipsLocalFootOffset = hips.InverseTransformPoint(foot.position);
            _hasBindOffset = true;
        }

        public void Estimate(in EstimatorContext ctx)
        {
            if (!ctx.TryGetTarget(HumanBodyBones.Hips, out var hips)) return;
            if (!_hasBindOffset) return;

            Vector3 worldFoot = hips.Position + hips.Rotation * _hipsLocalFootOffset;
            if (SnapToGround) worldFoot.y = GroundPlaneY;

            var bone = ctx.GetBoneTransform(TargetBone);
            Quaternion footRot = bone != null ? bone.rotation : Quaternion.identity;

            ctx.SetTarget(TargetBone, BoneTarget.Estimated(worldFoot, footRot, 0.5f));
        }
    }
}
