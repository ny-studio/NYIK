using System;
using UnityEngine;

namespace NYIK.Core
{
    /// <summary>
    /// Defines a bone chain and manages total chain length and inter-bone distances.
    /// </summary>
    [Serializable]
    public class IKChain
    {
        [SerializeField] BoneTransform[] m_Bones;

        float m_TotalLength;

        public BoneTransform[] Bones => m_Bones;
        public int BoneCount => m_Bones?.Length ?? 0;

        /// <summary>
        /// Total length of the chain (sum of all bone lengths).
        /// </summary>
        public float TotalLength => m_TotalLength;

        public BoneTransform First => m_Bones != null && m_Bones.Length > 0 ? m_Bones[0] : null;
        public BoneTransform Last => m_Bones != null && m_Bones.Length > 0 ? m_Bones[m_Bones.Length - 1] : null;

        public IKChain() { }

        public IKChain(Transform[] transforms)
        {
            m_Bones = new BoneTransform[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
                m_Bones[i] = new BoneTransform(transforms[i]);
        }

        public IKChain(BoneTransform[] bones)
        {
            m_Bones = bones;
        }

        /// <summary>
        /// Initialize all bones and calculate total chain length.
        /// </summary>
        public void Initialize()
        {
            if (m_Bones == null || m_Bones.Length == 0)
                return;

            m_TotalLength = 0f;

            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (m_Bones[i] == null) continue;
                var child = i + 1 < m_Bones.Length ? m_Bones[i + 1] : null;
                m_Bones[i].Initialize(child);
                m_TotalLength += m_Bones[i].Length;
            }
        }

        /// <summary>
        /// Validate that the chain has valid bone references.
        /// </summary>
        public bool IsValid()
        {
            if (m_Bones == null || m_Bones.Length < 2)
                return false;

            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (m_Bones[i] == null || !m_Bones[i].IsValid)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Reset all bones to their initial pose.
        /// </summary>
        public void ResetToInitialPose()
        {
            if (m_Bones == null)
                return;

            for (int i = 0; i < m_Bones.Length; i++)
            {
                if (m_Bones[i] != null)
                    m_Bones[i].ResetToInitialPose();
            }
        }
    }
}
