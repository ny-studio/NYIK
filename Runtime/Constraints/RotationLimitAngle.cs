using System;
using UnityEngine;

namespace NYIK.Constraints
{
    /// <summary>
    /// Swing + twist rotation limit. Used for joints with multi-axis rotation such as shoulders and hips.
    /// </summary>
    [Serializable]
    public class RotationLimitAngle
    {
        [SerializeField] float m_SwingLimit = 90f;
        [SerializeField] float m_TwistMinAngle = -90f;
        [SerializeField] float m_TwistMaxAngle = 90f;
        [SerializeField] Vector3 m_TwistAxis = Vector3.forward;

        Quaternion m_InitialLocalRotation;
        Transform m_Transform;
        bool m_Initialized;

        /// <summary>
        /// Swing limit angle (degrees). Maximum deviation from the twist axis.
        /// </summary>
        public float SwingLimit
        {
            get => m_SwingLimit;
            set => m_SwingLimit = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Twist minimum angle (degrees).
        /// </summary>
        public float TwistMinAngle
        {
            get => m_TwistMinAngle;
            set => m_TwistMinAngle = value;
        }

        /// <summary>
        /// Twist maximum angle (degrees).
        /// </summary>
        public float TwistMaxAngle
        {
            get => m_TwistMaxAngle;
            set => m_TwistMaxAngle = value;
        }

        public void Initialize(Transform transform)
        {
            m_Transform = transform;
            m_InitialLocalRotation = transform.localRotation;
            m_Initialized = true;
        }

        /// <summary>
        /// Clamps the current rotation within swing + twist limits.
        /// </summary>
        public void Apply()
        {
            if (!m_Initialized || m_Transform == null)
                return;

            Quaternion localRot = m_Transform.localRotation;
            Quaternion delta = Quaternion.Inverse(m_InitialLocalRotation) * localRot;

            // Decompose into swing + twist
            DecomposeSwingTwist(delta, m_TwistAxis, out Quaternion swing, out Quaternion twist);

            // Swing limit
            swing.ToAngleAxis(out float swingAngle, out Vector3 swingAxis);
            if (swingAngle > 180f) swingAngle -= 360f;
            if (Mathf.Abs(swingAngle) > m_SwingLimit)
            {
                swingAngle = Mathf.Clamp(swingAngle, -m_SwingLimit, m_SwingLimit);
                swing = Quaternion.AngleAxis(swingAngle, swingAxis);
            }

            // Twist limit
            twist.ToAngleAxis(out float twistAngle, out Vector3 twistAxis);
            if (twistAngle > 180f) twistAngle -= 360f;
            float twistSign = Vector3.Dot(twistAxis, m_TwistAxis) >= 0f ? 1f : -1f;
            twistAngle *= twistSign;
            twistAngle = Mathf.Clamp(twistAngle, m_TwistMinAngle, m_TwistMaxAngle);
            twist = Quaternion.AngleAxis(twistAngle * twistSign, m_TwistAxis);

            m_Transform.localRotation = m_InitialLocalRotation * swing * twist;
        }

        /// <summary>
        /// Decomposes a quaternion into swing and twist components.
        /// </summary>
        static void DecomposeSwingTwist(Quaternion rotation, Vector3 twistAxis,
            out Quaternion swing, out Quaternion twist)
        {
            Vector3 rotationAxis = new Vector3(rotation.x, rotation.y, rotation.z);
            float projection = Vector3.Dot(rotationAxis, twistAxis);

            twist = new Quaternion(
                twistAxis.x * projection,
                twistAxis.y * projection,
                twistAxis.z * projection,
                rotation.w
            );

            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag > 0.0001f)
            {
                twist.x /= mag;
                twist.y /= mag;
                twist.z /= mag;
                twist.w /= mag;
            }
            else
            {
                twist = Quaternion.identity;
            }

            swing = rotation * Quaternion.Inverse(twist);
        }
    }
}
