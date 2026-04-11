using System;
using UnityEngine;

namespace NYIK.VR
{
    /// <summary>
    /// Abstracts an IK target. Handles VR trackers (HMD, controllers) and
    /// future additional trackers (waist, feet) in a unified manner.
    /// </summary>
    [Serializable]
    public class VRIKTarget
    {
        [SerializeField] Transform m_Source;
        [SerializeField] Vector3 m_PositionOffset;
        [SerializeField] Vector3 m_RotationOffset;
        [SerializeField, Range(0f, 1f)] float m_PositionWeight = 1f;
        [SerializeField, Range(0f, 1f)] float m_RotationWeight = 1f;

        Vector3 m_Position;
        Quaternion m_Rotation = Quaternion.identity;
        bool m_IsTracking;

        /// <summary>
        /// Transform of the tracking source (HMD, controllers, etc.)
        /// </summary>
        public Transform Source
        {
            get => m_Source;
            set => m_Source = value;
        }

        /// <summary>
        /// Position offset from the source (local space)
        /// </summary>
        public Vector3 PositionOffset
        {
            get => m_PositionOffset;
            set => m_PositionOffset = value;
        }

        /// <summary>
        /// Rotation offset from the source (Euler angles, local space)
        /// </summary>
        public Vector3 RotationOffset
        {
            get => m_RotationOffset;
            set => m_RotationOffset = value;
        }

        /// <summary>
        /// Position influence weight
        /// </summary>
        public float PositionWeight
        {
            get => m_PositionWeight;
            set => m_PositionWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Rotation influence weight
        /// </summary>
        public float RotationWeight
        {
            get => m_RotationWeight;
            set => m_RotationWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// World position after offset is applied
        /// </summary>
        public Vector3 Position => m_Position;

        /// <summary>
        /// After offset, the world rotation
        /// </summary>
        public Quaternion Rotation => m_Rotation;

        /// <summary>
        /// Whether tracking is active
        /// </summary>
        public bool IsTracking => m_IsTracking;

        /// <summary>
        /// Reads tracking data from the source Transform and applies offsets.
        /// </summary>
        public void UpdateTracking()
        {
            if (m_Source == null)
            {
                m_IsTracking = false;
                return;
            }

            m_IsTracking = m_Source.gameObject.activeInHierarchy;
            if (!m_IsTracking)
                return;

            Quaternion sourceRotation = m_Source.rotation;
            Quaternion offsetRotation = Quaternion.Euler(m_RotationOffset);
            m_Rotation = sourceRotation * offsetRotation;

            m_Position = m_Source.position + sourceRotation * m_PositionOffset;
        }

        /// <summary>
        /// Sets tracking data directly (when not using a source Transform).
        /// </summary>
        public void SetDirectly(Vector3 position, Quaternion rotation)
        {
            m_Position = position;
            m_Rotation = rotation;
            m_IsTracking = true;
        }
    }
}
