using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Anatomy
{
    /// <summary>
    /// Post-IK pass that clamps each Humanoid bone's local rotation into the
    /// anatomical Range of Motion defined by <see cref="JointROMLimits"/>.
    /// This is the final safety net after FABRIK / SQP — it guarantees no
    /// bone exits its plausible orientation envelope due to tracker noise.
    ///
    /// Joint families:
    /// - Shoulders / upper arms / upper legs use SWING-TWIST clamping (avoids
    ///   the Euler-axis gimbal lock that bites at wide arm raises etc).
    /// - All other joints use independent Euler axis clamping (cheap and
    ///   adequate for narrow-ROM hinges like elbows, knees, ankles).
    /// </summary>
    public static class AnatomicalRefiner
    {
        /// <summary>
        /// Iterates every Humanoid bone on <paramref name="animator"/> and clamps
        /// its local rotation into the corresponding ROM.
        /// </summary>
        public static void ClampAllJoints(Animator animator, float strength = 1f)
        {
            if (animator == null)
            {
                Debug.LogWarning("[AnatomicalRefiner] ClampAllJoints called with null animator.");
                return;
            }

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones kind = (HumanBodyBones)i;
                Transform bone = animator.GetBoneTransform(kind);
                if (bone == null)
                    continue;

                ClampJoint(bone, kind, strength);
            }
        }

        /// <summary>
        /// Same as <see cref="ClampAllJoints(Animator, float)"/> but skips the
        /// per-bone Animator.GetBoneTransform lookup by accepting a precomputed
        /// cache (indexed by (int)HumanBodyBones). Used by ConstraintRefiner
        /// inside its iteration loop to avoid ~110 lookups per frame.
        /// </summary>
        public static void ClampAllJoints(Transform[] cachedBones, float strength = 1f)
        {
            if (cachedBones == null) return;
            int count = Mathf.Min(cachedBones.Length, (int)HumanBodyBones.LastBone);
            for (int i = 0; i < count; i++)
            {
                var bone = cachedBones[i];
                if (bone == null) continue;
                ClampJoint(bone, (HumanBodyBones)i, strength);
            }
        }

        /// <summary>
        /// Clamps a single bone's local rotation. Picks swing-twist or Euler
        /// based on the bone kind. Strength blends between the original (0)
        /// and the clamped result (1).
        /// </summary>
        public static void ClampJoint(Transform bone, HumanBodyBones kind, float strength = 1f)
        {
            if (bone == null)
            {
                Debug.LogWarning($"[AnatomicalRefiner] ClampJoint called with null bone (kind={kind}).");
                return;
            }

            var swingTwist = JointROMLimits.GetSwingTwist(kind);
            if (swingTwist.HasValue)
            {
                ClampSwingTwist(bone, swingTwist.Value, strength);
            }
            else
            {
                ClampEuler(bone, JointROMLimits.Get(kind), strength);
            }
        }

        private static void ClampSwingTwist(Transform bone, JointROMLimits.SwingTwistLimit limit, float strength)
        {
            Quaternion original = bone.localRotation;
            SwingTwistDecomposition.Decompose(original, limit.TwistAxis, out var swing, out var twist);

            // Clamp twist into [TwistMin, TwistMax]
            twist.ToAngleAxis(out float twistAngle, out Vector3 twistAxisOut);
            if (twistAxisOut.sqrMagnitude > 1e-6f &&
                Vector3.Dot(twistAxisOut, limit.TwistAxis) < 0f)
            {
                twistAngle = -twistAngle;
            }
            // Convert AngleAxis's [0, 360] range to symmetric [-180, 180]
            if (twistAngle > 180f) twistAngle -= 360f;
            twistAngle = Mathf.Clamp(twistAngle, limit.TwistMinDeg, limit.TwistMaxDeg);
            var clampedTwist = Quaternion.AngleAxis(twistAngle, limit.TwistAxis);

            // Clamp swing into the cone (max half-angle)
            swing.ToAngleAxis(out float swingAngle, out Vector3 swingAxis);
            if (swingAngle > 180f) swingAngle -= 360f;
            float swingMag = Mathf.Abs(swingAngle);
            if (swingMag > limit.SwingMaxDeg)
            {
                swingAngle = Mathf.Sign(swingAngle) * limit.SwingMaxDeg;
            }
            var clampedSwing = Quaternion.AngleAxis(swingAngle, swingAxis);

            Quaternion clamped = clampedSwing * clampedTwist;
            ApplyStrength(bone, original, clamped, strength);
        }

        private static void ClampEuler(Transform bone, JointROMLimits.EulerLimit limit, float strength)
        {
            Quaternion original = bone.localRotation;
            Vector3 euler = original.eulerAngles;

            float x = Mathf.Clamp(NormalizeAngle(euler.x), limit.Min.x, limit.Max.x);
            float y = Mathf.Clamp(NormalizeAngle(euler.y), limit.Min.y, limit.Max.y);
            float z = Mathf.Clamp(NormalizeAngle(euler.z), limit.Min.z, limit.Max.z);

            Quaternion clamped = Quaternion.Euler(x, y, z);
            ApplyStrength(bone, original, clamped, strength);
        }

        private static void ApplyStrength(Transform bone, Quaternion original, Quaternion clamped, float strength)
        {
            if (strength >= 1f) bone.localRotation = clamped;
            else if (strength <= 0f) return;
            else bone.localRotation = Quaternion.Slerp(original, clamped, strength);
        }

        /// <summary>
        /// Wraps an Euler angle expressed in [0, 360) (Unity's convention) back into
        /// the symmetric range (-180, 180]. Required because <c>eulerAngles</c>
        /// returns positive-only values that would never satisfy negative ROM bounds.
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            else if (angle <= -180f) angle += 360f;
            return angle;
        }
    }
}
