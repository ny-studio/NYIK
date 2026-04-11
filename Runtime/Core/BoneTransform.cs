using System;
using UnityEngine;

namespace NYIK.Core
{
    /// <summary>
    /// Holds a bone's Transform reference and cached initial state.
    /// </summary>
    [Serializable]
    public class BoneTransform
    {
        [SerializeField] Transform m_Transform;

        Vector3 m_InitialLocalPosition;
        Quaternion m_InitialLocalRotation;
        float m_Length;

        public Transform Transform => m_Transform;
        public Vector3 Position
        {
            get => m_Transform != null ? m_Transform.position : Vector3.zero;
            set { if (m_Transform != null) m_Transform.position = value; }
        }
        public Quaternion Rotation
        {
            get => m_Transform != null ? m_Transform.rotation : Quaternion.identity;
            set { if (m_Transform != null) m_Transform.rotation = value; }
        }
        public Vector3 LocalPosition => m_Transform != null ? m_Transform.localPosition : Vector3.zero;
        public Quaternion LocalRotation => m_Transform != null ? m_Transform.localRotation : Quaternion.identity;
        public Vector3 InitialLocalPosition => m_InitialLocalPosition;
        public Quaternion InitialLocalRotation => m_InitialLocalRotation;

        /// <summary>
        /// Length from this bone to the child bone (calculated during Initialize).
        /// </summary>
        public float Length => m_Length;

        public bool IsValid => m_Transform != null;

        public BoneTransform() { }

        public BoneTransform(Transform transform)
        {
            m_Transform = transform;
        }

        /// <summary>
        /// Caches the initial state and calculates the distance to the child bone.
        /// </summary>
        /// <param name="childBone">The next bone in the chain (null for the end bone).</param>
        public void Initialize(BoneTransform childBone = null)
        {
            if (m_Transform == null)
                return;

            m_InitialLocalPosition = m_Transform.localPosition;
            m_InitialLocalRotation = m_Transform.localRotation;

            if (childBone != null && childBone.IsValid)
                m_Length = Vector3.Distance(m_Transform.position, childBone.Position);
            else
                m_Length = 0f;
        }

        /// <summary>
        /// Resets the bone to its initial local pose.
        /// </summary>
        public void ResetToInitialPose()
        {
            if (m_Transform == null)
                return;

            m_Transform.localPosition = m_InitialLocalPosition;
            m_Transform.localRotation = m_InitialLocalRotation;
        }
    }
}
