using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.VR
{
    /// <summary>
    /// Optional component. Used only when you want to override NYIKHumanoid's
    /// VR offsets or tracking sources from an external source.
    /// Normally not required, as NYIKHumanoid handles automatic connection on its own.
    /// </summary>
    [AddComponentMenu("NYIK/VR/VRIK Setup (Optional Override)")]
    [DefaultExecutionOrder(100)]
    public class VRIKSetup : MonoBehaviour
    {
        [SerializeField] NYIKHumanoid m_NYIKHumanoid;

        [Header("Override Tracking Sources")]
        [SerializeField] Transform m_HeadSource;
        [SerializeField] Transform m_LeftHandSource;
        [SerializeField] Transform m_RightHandSource;

        [Header("Override Offsets")]
        [SerializeField] Vector3 m_HeadPositionOffset;
        [SerializeField] Vector3 m_HeadRotationOffset;
        [SerializeField] Vector3 m_LeftHandPositionOffset;
        [SerializeField] Vector3 m_LeftHandRotationOffset;
        [SerializeField] Vector3 m_RightHandPositionOffset;
        [SerializeField] Vector3 m_RightHandRotationOffset;

        public NYIKHumanoid NYIKHumanoid
        {
            get => m_NYIKHumanoid;
            set => m_NYIKHumanoid = value;
        }

        void Start()
        {
            if (m_NYIKHumanoid == null)
                m_NYIKHumanoid = FindAnyObjectByType<NYIKHumanoid>();

            if (m_NYIKHumanoid == null)
                return;

            // Override with specified sources
            if (m_HeadSource != null)
            {
                m_NYIKHumanoid.HeadTarget.Source = m_HeadSource;
                m_NYIKHumanoid.HeadTarget.PositionOffset = m_HeadPositionOffset;
                m_NYIKHumanoid.HeadTarget.RotationOffset = m_HeadRotationOffset;
            }
            if (m_LeftHandSource != null)
            {
                m_NYIKHumanoid.LeftHandTarget.Source = m_LeftHandSource;
                m_NYIKHumanoid.LeftHandTarget.PositionOffset = m_LeftHandPositionOffset;
                m_NYIKHumanoid.LeftHandTarget.RotationOffset = m_LeftHandRotationOffset;
            }
            if (m_RightHandSource != null)
            {
                m_NYIKHumanoid.RightHandTarget.Source = m_RightHandSource;
                m_NYIKHumanoid.RightHandTarget.PositionOffset = m_RightHandPositionOffset;
                m_NYIKHumanoid.RightHandTarget.RotationOffset = m_RightHandRotationOffset;
            }
        }
    }
}
