using System;
using UnityEngine;

namespace NYIK.Constraints
{
    /// <summary>
    /// Hinge rotation limit. For single-axis joints like elbows and knees.
    /// </summary>
    [Serializable]
    public class RotationLimitHinge
    {
        [SerializeField] float m_MinAngle;
        [SerializeField] float m_MaxAngle = 150f;
        [SerializeField] Vector3 m_Axis = Vector3.right;

        Quaternion m_InitialLocalRotation;
        Transform m_Transform;
        bool m_Initialized;

        /// <summary>
        /// Minimum rotation angle (degrees).
        /// </summary>
        public float MinAngle
        {
            get => m_MinAngle;
            set => m_MinAngle = value;
        }

        /// <summary>
        /// Maximum rotation angle (degrees).
        /// </summary>
        public float MaxAngle
        {
            get => m_MaxAngle;
            set => m_MaxAngle = value;
        }

        /// <summary>
        /// Rotation axis (local space).
        /// </summary>
        public Vector3 Axis
        {
            get => m_Axis;
            set => m_Axis = value.normalized;
        }

        public void Initialize(Transform transform)
        {
            m_Transform = transform;
            m_InitialLocalRotation = transform.localRotation;
            m_Initialized = true;
        }

        /// <summary>
        /// Clamp the current rotation to the hinge limits.
        /// </summary>
        public void Apply()
        {
            if (!m_Initialized || m_Transform == null)
                return;

            Quaternion localRot = m_Transform.localRotation;
            Quaternion delta = Quaternion.Inverse(m_InitialLocalRotation) * localRot;

            delta.ToAngleAxis(out float angle, out Vector3 axis);

            // When angle is near zero, axis is undefined — skip
            if (Mathf.Abs(angle) < 0.001f)
                return;

            if (angle > 180f)
                angle -= 360f;

            // Project rotation axis onto hinge axis
            float projection = Vector3.Dot(axis.normalized, m_Axis);
            if (projection < 0f)
            {
                angle = -angle;
                projection = -projection;
            }

            // Remove rotation components not on the hinge axis
            float clampedAngle = Mathf.Clamp(angle * projection, m_MinAngle, m_MaxAngle);

            m_Transform.localRotation = m_InitialLocalRotation * Quaternion.AngleAxis(clampedAngle, m_Axis);
        }
    }
}
