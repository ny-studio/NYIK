using UnityEngine;
using NYIK.Anatomy;
using NYIK.Calibration;
using NYIK.Solvers;
using NYIK.Tracker;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Full-Body Tracking pipeline.
    ///
    /// Reads tracker data from an <see cref="ITrackerSourceProvider"/>, applies
    /// calibrated rotations to the corresponding Humanoid bones, then refines
    /// the pose with twist distribution and anatomical constraint relaxation.
    ///
    /// Pipeline:
    ///   1. Tick provider (update IsTracking, apply filters)
    ///   2. DIRECT pass — write each assigned tracker's calibrated rotation
    ///      to its corresponding bone
    ///   3. TwistDistributor — propagate twist along forearm/shin chains
    ///   4. ConstraintRefiner — iterate ROM clamps and bone-length restore
    ///   5. (Optional) AnatomicalRefiner final pass
    /// </summary>
    public sealed class FBTPipeline
    {
        // Configuration
        public int RefinerIterations = 2;
        public float RefinerClampStrength = 0.8f;
        public float ForearmTwistRatio = 0.5f;
        public float ShinTwistRatio = 0.5f;
        public bool ApplyFinalROMClamp = true;
        public float FinalROMClampStrength = 0.5f;

        /// <summary>
        /// When true and a Waist tracker is assigned, drive the Hips bone
        /// position from the calibrated waist position. Enables full-body
        /// translation (squat, lean, step) instead of keeping the avatar's
        /// root locked. Off by default to preserve existing behavior; turn on
        /// once the Waist position calibration is dialed in.
        /// </summary>
        public bool EnableWaistPositionDrive = false;

        private readonly Animator _animator;
        private readonly ITrackerSourceProvider _provider;

        public FBTPipeline(Animator animator, ITrackerSourceProvider provider)
        {
            _animator = animator;
            _provider = provider;
        }

        /// <summary>
        /// Solve a single frame. Call after Animator update (LateUpdate).
        /// Assumes the provider has already been ticked this frame by the caller
        /// (NYIKHumanoid.Solve does this). Calling Tick again here would
        /// double-advance any OneEuroFilter state inside the provider.
        /// </summary>
        public void Solve(float deltaTime)
        {
            if (_animator == null || _provider == null) return;
            if (!_animator.isHuman) return;

            // Step 0: optional Waist→Hips translation (must run before rotations
            // so the spine column rotates around the correctly-positioned hips).
            if (EnableWaistPositionDrive)
            {
                ApplyWaistPosition();
            }

            // Step 1: DIRECT writes for every assigned tracker
            ApplyDirectRotations();

            // Step 2: distribute twists on chains where we lack mid-bone trackers
            DistributeTwists();

            // Step 3: constraint relaxation (ROM + bone length)
            ConstraintRefiner.Refine(_animator, RefinerIterations, RefinerClampStrength);

            // Step 4: final ROM clamp for safety
            if (ApplyFinalROMClamp)
            {
                AnatomicalRefiner.ClampAllJoints(_animator, FinalROMClampStrength);
            }
        }

        /// <summary>
        /// Write each tracker's calibrated rotation to its bone (world-space).
        /// Trust weight is used to blend with the current animator pose; trust 1
        /// means full override, lower values blend toward the existing pose.
        /// </summary>
        private void ApplyDirectRotations()
        {
            foreach (var slot in _provider.Slots)
            {
                if (!slot.IsAssigned || !slot.IsTracking) continue;
                var boneNullable = FBTCalibrator.GetBoneForSlot(slot.Kind);
                if (!boneNullable.HasValue) continue;
                var bone = _animator.GetBoneTransform(boneNullable.Value);
                if (bone == null) continue;

                var target = slot.CalibratedRotation;
                if (slot.TrustWeight >= 0.999f)
                {
                    bone.rotation = target;
                }
                else
                {
                    bone.rotation = Quaternion.Slerp(bone.rotation, target, slot.TrustWeight);
                }

            }
        }

        /// <summary>
        /// Move the Hips bone to the waist tracker's calibrated position.
        /// Uses TrackerSlot.CalibratedPosition so the captured T-pose offset
        /// is respected.
        /// </summary>
        private void ApplyWaistPosition()
        {
            var waist = _provider.GetSlot(TrackerSlotKind.Waist);
            if (waist == null || !waist.IsAssigned || !waist.IsTracking) return;

            var hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            Vector3 target = waist.CalibratedPosition;
            if (waist.TrustWeight >= 0.999f)
            {
                hips.position = target;
            }
            else
            {
                hips.position = Vector3.Lerp(hips.position, target, waist.TrustWeight);
            }
        }

        /// <summary>
        /// Distribute twist to mid-bones (lower arm, lower leg, spine middle)
        /// based on parent/grandchild orientation differences.
        /// </summary>
        private void DistributeTwists()
        {
            var leftShoulder = GetBone(HumanBodyBones.LeftUpperArm);
            var leftElbow = GetBone(HumanBodyBones.LeftLowerArm);
            var leftWrist = GetBone(HumanBodyBones.LeftHand);
            TwistDistributor.DistributeForearm(leftShoulder, leftElbow, leftWrist, ForearmTwistRatio);

            var rightShoulder = GetBone(HumanBodyBones.RightUpperArm);
            var rightElbow = GetBone(HumanBodyBones.RightLowerArm);
            var rightWrist = GetBone(HumanBodyBones.RightHand);
            TwistDistributor.DistributeForearm(rightShoulder, rightElbow, rightWrist, ForearmTwistRatio);

            var leftHip = GetBone(HumanBodyBones.LeftUpperLeg);
            var leftKnee = GetBone(HumanBodyBones.LeftLowerLeg);
            var leftAnkle = GetBone(HumanBodyBones.LeftFoot);
            TwistDistributor.DistributeShin(leftHip, leftKnee, leftAnkle, ShinTwistRatio);

            var rightHip = GetBone(HumanBodyBones.RightUpperLeg);
            var rightKnee = GetBone(HumanBodyBones.RightLowerLeg);
            var rightAnkle = GetBone(HumanBodyBones.RightFoot);
            TwistDistributor.DistributeShin(rightHip, rightKnee, rightAnkle, ShinTwistRatio);
        }

        private Transform GetBone(HumanBodyBones b) => _animator.GetBoneTransform(b);
    }
}
