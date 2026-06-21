using UnityEngine;

namespace NYIK.Anatomy
{
    /// <summary>
    /// Per-joint anatomical Range of Motion (ROM) limits.
    ///
    /// Two limit families:
    /// - <see cref="EulerLimit"/>: independent X/Y/Z axis clamps. Cheap and
    ///   adequate for hinge-like joints (elbows, knees, ankles, spine).
    /// - <see cref="SwingTwistLimit"/>: swing cone + twist range around the
    ///   bone's long axis. Used for ball joints where Euler clamping
    ///   gimbal-locks (shoulders, upper arms, upper legs).
    ///
    /// Default values are baked from published anatomical references:
    ///   • American Academy of Orthopaedic Surgeons (AAOS),
    ///     *Joint Motion: Method of Measuring and Recording*, 1965.
    ///   • Norkin CC, White DJ. *Measurement of Joint Motion: A Guide to
    ///     Goniometry*, 5th ed., F.A. Davis, 2016.
    ///   • Wu G et al. *ISB recommendation on definitions of joint
    ///     coordinate systems...*, J Biomech 35(4):543-548 (2002, 2005).
    ///
    /// Values are slightly conservative vs. AAOS maxima to leave headroom
    /// for tracker error without pinning at the limit. Per-avatar override
    /// is supported via <see cref="JointROMReference"/> ScriptableObject
    /// and <see cref="SetReference"/>.
    /// </summary>
    public static class JointROMLimits
    {
        public struct EulerLimit
        {
            public Vector3 Min;
            public Vector3 Max;

            public EulerLimit(Vector3 min, Vector3 max)
            {
                Min = min;
                Max = max;
            }

            public EulerLimit(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
            {
                Min = new Vector3(minX, minY, minZ);
                Max = new Vector3(maxX, maxY, maxZ);
            }
        }

        public struct SwingTwistLimit
        {
            public Vector3 TwistAxis;
            public float TwistMinDeg;
            public float TwistMaxDeg;
            public float SwingMaxDeg;
        }

        /// <summary>
        /// Effectively unconstrained limit returned for bones without an
        /// anatomically meaningful ROM (toes, fingers).
        /// </summary>
        public static readonly EulerLimit Unlimited = new EulerLimit(
            new Vector3(-360f, -360f, -360f),
            new Vector3( 360f,  360f,  360f));

        private static JointROMReference s_activeReference;

        /// <summary>
        /// Override the static defaults with values from a reference asset.
        /// Pass null to revert to defaults. Reference is global (static),
        /// so the last-set reference wins.
        /// </summary>
        public static void SetReference(JointROMReference reference)
        {
            s_activeReference = reference;
        }

        public static JointROMReference ActiveReference => s_activeReference;

        /// <summary>Returns the Euler ROM limit for the given Humanoid bone.</summary>
        public static EulerLimit Get(HumanBodyBones bone)
        {
            if (s_activeReference != null
                && s_activeReference.TryGetEntry(bone, out var entry)
                && !entry.UseSwingTwist)
            {
                return entry.ToEulerLimit();
            }
            return DefaultEuler(bone);
        }

        /// <summary>
        /// Returns a swing-twist limit when one is defined; otherwise null.
        /// Callers should fall back to <see cref="Get"/> in that case.
        /// </summary>
        public static SwingTwistLimit? GetSwingTwist(HumanBodyBones bone)
        {
            if (s_activeReference != null
                && s_activeReference.TryGetEntry(bone, out var entry)
                && entry.UseSwingTwist)
            {
                return entry.ToSwingTwistLimit();
            }
            return DefaultSwingTwist(bone);
        }

        // ------------------------------------------------------------------
        // AAOS-based defaults. Comments cite the anatomical degree ranges;
        // numerical values may be slightly tightened from the max to leave
        // tracker-noise headroom.
        // ------------------------------------------------------------------
        private static EulerLimit DefaultEuler(HumanBodyBones bone)
        {
            switch (bone)
            {
                // Shoulder girdle (clavicle elevation / protraction).
                // AAOS: elevation ~45°, depression 5°, protraction 30°, retraction 20°.
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.RightShoulder:
                    return new EulerLimit(-45f, 90f, -30f, 30f, -30f, 60f);

                // Upper arm glenohumeral joint — handled by SwingTwist; Euler
                // value retained for non-swing-twist consumers.
                // AAOS: flexion 180, extension 60, abduction 180, ext-rot 90,
                // int-rot 70.
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    return new EulerLimit(-90f, 180f, -90f, 90f, -45f, 135f);

                // Elbow — pure hinge in X; forearm pronation/supination in Z.
                // AAOS: flexion 150, hyperext 0-5, pronation 80, supination 80.
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                    return new EulerLimit(0f, 150f, 0f, 0f, -80f, 80f);

                // Wrist. AAOS: flex 80, ext 70, radial dev 20, ulnar dev 30.
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                    return new EulerLimit(-70f, 80f, -25f, 25f, -20f, 30f);

                // Hip — handled by SwingTwist; Euler value retained.
                // AAOS: flex 125, ext 30, abd 45, add 30, ext-rot 45, int-rot 40.
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                    return new EulerLimit(-30f, 125f, -45f, 45f, -30f, 30f);

                // Knee — pure hinge in X; small rotational play at 90° flexion (Norkin).
                // AAOS: flex 135, hyperext 0-5.
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                    return new EulerLimit(0f, 135f, -10f, 10f, 0f, 0f);

                // Ankle. AAOS: dorsiflex 20, plantar 50, inversion 30, eversion 20.
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                    return new EulerLimit(-50f, 20f, -30f, 30f, -20f, 30f);

                // Spinal column. ISB / Norkin lumbar+thoracic combined:
                // flex 80, ext 25, lat-flex 35, rotation 45.
                // Distributed across Spine/Chest/UpperChest segments.
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                    return new EulerLimit(-25f, 40f, -22f, 22f, -22f, 22f);

                // Cervical spine. AAOS: flex 45, ext 50, lat-flex 45 each side,
                // rotation 60 each side. Split between Neck and Head.
                case HumanBodyBones.Neck:
                    return new EulerLimit(-25f, 25f, -35f, 35f, -25f, 25f);

                case HumanBodyBones.Head:
                    return new EulerLimit(-25f, 25f, -35f, 35f, -25f, 25f);

                default:
                    return Unlimited;
            }
        }

        private static SwingTwistLimit? DefaultSwingTwist(HumanBodyBones bone)
        {
            // TwistAxis = local +Y, assumes Unity Humanoid convention
            // (bone long axis aligned with local Y). Verify per avatar.
            switch (bone)
            {
                // Glenohumeral. AAOS: combined flex/abd cone ≈ 180°, ext-rot 90, int-rot 70.
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    return new SwingTwistLimit
                    {
                        TwistAxis = Vector3.up,
                        TwistMinDeg = -70f, TwistMaxDeg = 90f,
                        SwingMaxDeg = 150f,
                    };

                // Coxofemoral. AAOS: flex 125, abd 45 — combined cone ~110°.
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                    return new SwingTwistLimit
                    {
                        TwistAxis = Vector3.up,
                        TwistMinDeg = -40f, TwistMaxDeg = 45f,
                        SwingMaxDeg = 110f,
                    };

                // Shoulder girdle. Tighter; mostly elevation + protraction.
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.RightShoulder:
                    return new SwingTwistLimit
                    {
                        TwistAxis = Vector3.up,
                        TwistMinDeg = -20f, TwistMaxDeg = 20f,
                        SwingMaxDeg = 60f,
                    };

                default:
                    return null;
            }
        }
    }
}
