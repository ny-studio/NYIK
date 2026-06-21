using System;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.VR
{
    /// <summary>
    /// VR calibration system. Calculates the scale ratio between the user and avatar
    /// from a T-pose, and automatically adjusts IK parameters.
    /// </summary>
    [AddComponentMenu("NYIK/VR/VR Calibration")]
    public class VRCalibration : MonoBehaviour
    {
        [SerializeField] NYIKHumanoid m_NYIKHumanoid;

        [Header("Calibration Data")]
        [SerializeField] float m_AvatarScale = 1f;
        [SerializeField] float m_ArmLengthRatio = 1f;
        [SerializeField] Vector3 m_SpineOffset;
        [SerializeField] bool m_IsCalibrated;

        float m_AvatarHeadHeight;
        float m_AvatarArmSpan;

        /// <summary>
        /// Whether calibration has been performed.
        /// </summary>
        public bool IsCalibrated => m_IsCalibrated;

        /// <summary>
        /// Scale ratio of the avatar.
        /// </summary>
        public float AvatarScale => m_AvatarScale;

        void Start()
        {
            if (m_NYIKHumanoid == null)
                m_NYIKHumanoid = GetComponent<NYIKHumanoid>();

            CacheAvatarMeasurements();
        }

        /// <summary>
        /// Caches the avatar's initial measurements.
        /// </summary>
        void CacheAvatarMeasurements()
        {
            if (m_NYIKHumanoid == null || !m_NYIKHumanoid.References.IsValid())
                return;

            var refs = m_NYIKHumanoid.References;

            // Avatar head height
            if (refs.Head != null)
                m_AvatarHeadHeight = refs.Head.position.y - refs.Root.position.y;

            // Avatar arm length (measured from left arm)
            if (refs.LeftUpperArm != null && refs.LeftForearm != null && refs.LeftHand != null)
            {
                m_AvatarArmSpan = Vector3.Distance(refs.LeftUpperArm.position, refs.LeftForearm.position)
                    + Vector3.Distance(refs.LeftForearm.position, refs.LeftHand.position);
            }
        }

        /// <summary>
        /// Performs T-pose calibration.
        /// Call this while the user is standing in a T-pose.
        /// </summary>
        public void Calibrate()
        {
            if (m_NYIKHumanoid == null)
            {
                Debug.LogWarning("[NYIK Calibration] NYIKHumanoid is not assigned.", this);
                return;
            }

            var headTarget = m_NYIKHumanoid.HeadTarget;
            var leftHandTarget = m_NYIKHumanoid.LeftHandTarget;
            var rightHandTarget = m_NYIKHumanoid.RightHandTarget;

            headTarget.UpdateTracking();
            leftHandTarget.UpdateTracking();
            rightHandTarget.UpdateTracking();

            if (!headTarget.IsTracking)
            {
                Debug.LogWarning("[NYIK Calibration] Head tracking is not available.", this);
                return;
            }

            // Calculate scale ratio from user head height
            // NOTE: This uses raw headTarget.Position.y. For accurate results, the
            // XROrigin floor offset should be subtracted, but we currently have no
            // reference to XROrigin. If the XR rig is not at y=0, calibration may
            // be slightly inaccurate.
            float userHeadHeight = headTarget.Position.y;
            if (m_AvatarHeadHeight > 0f && userHeadHeight > 0f)
            {
                m_AvatarScale = userHeadHeight / m_AvatarHeadHeight;
            }

            // Arm span ratio (when both hand controllers are active). Use the
            // distance between the two hand targets as the user's arm span —
            // previously this read head→left-hand which is not arm length at
            // all and was off by ~2x for any T-pose stance.
            if (leftHandTarget.IsTracking && rightHandTarget.IsTracking && m_AvatarArmSpan > 0f)
            {
                float userArmSpan = Vector3.Distance(
                    leftHandTarget.Position,
                    rightHandTarget.Position
                );
                m_ArmLengthRatio = userArmSpan / m_AvatarArmSpan;
            }

            // Calculate offset from HMD to head bone
            var refs = m_NYIKHumanoid.References;
            if (refs.Head != null)
            {
                m_SpineOffset = refs.Head.position - headTarget.Position;
            }

            m_IsCalibrated = true;
            Debug.Log($"[NYIK Calibration] Calibrated. Scale: {m_AvatarScale:F3}, Arm ratio: {m_ArmLengthRatio:F3}", this);

            ApplyCalibration();
        }

        /// <summary>
        /// Applies the calibration results to IK parameters.
        /// </summary>
        void ApplyCalibration()
        {
            if (!m_IsCalibrated || m_NYIKHumanoid == null)
                return;

            // Apply scale ratio to avatar
            if (m_AvatarScale > 0f && m_AvatarScale != 1f)
            {
                m_NYIKHumanoid.transform.localScale = Vector3.one * m_AvatarScale;
                // Re-apply offsets with the new scale so HeadPositionOffset etc.
                // stay anatomically correct after a rescale.
                m_NYIKHumanoid.ApplyOffsets();
            }

            // Apply head position offset (spine offset is in world units; do not
            // re-scale here since it was computed at the current avatar scale).
            m_NYIKHumanoid.HeadTarget.PositionOffset = m_SpineOffset;
        }

        /// <summary>
        /// Resets the calibration.
        /// </summary>
        public void ResetCalibration()
        {
            m_AvatarScale = 1f;
            m_ArmLengthRatio = 1f;
            m_SpineOffset = Vector3.zero;
            m_IsCalibrated = false;

            if (m_NYIKHumanoid != null)
                m_NYIKHumanoid.HeadTarget.PositionOffset = Vector3.zero;

            Debug.Log("[NYIK Calibration] Reset.", this);
        }
    }
}
