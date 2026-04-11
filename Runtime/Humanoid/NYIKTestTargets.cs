using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NYIK.Humanoid
{
    /// <summary>
    /// Editor-time IK testing. Drives the IK solver from target Transforms
    /// without VR hardware. Works in Edit mode via [ExecuteAlways].
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(NYIKHumanoid))]
    [AddComponentMenu("NYIK/Test Targets")]
    public class NYIKTestTargets : MonoBehaviour
    {
        [SerializeField] Transform m_HeadTarget;
        [SerializeField] Transform m_LeftHandTarget;
        [SerializeField] Transform m_RightHandTarget;
        [SerializeField] Transform m_LeftFootTarget;
        [SerializeField] Transform m_RightFootTarget;

        NYIKHumanoid m_Humanoid;

        public Transform HeadTarget => m_HeadTarget;
        public Transform LeftHandTarget => m_LeftHandTarget;
        public Transform RightHandTarget => m_RightHandTarget;
        public Transform LeftFootTarget => m_LeftFootTarget;
        public Transform RightFootTarget => m_RightFootTarget;

        void Reset()
        {
            m_Humanoid = GetComponent<NYIKHumanoid>();

            var refs = m_Humanoid != null ? m_Humanoid.References : null;
            if (refs == null) return;

            if (!refs.IsValid())
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                    refs.AutoDetect(animator);
            }

            m_HeadTarget = CreateTarget("NYIK_HeadTarget", refs.Head);
            m_LeftHandTarget = CreateTarget("NYIK_LeftHandTarget", refs.LeftHand);
            m_RightHandTarget = CreateTarget("NYIK_RightHandTarget", refs.RightHand);
            m_LeftFootTarget = CreateTarget("NYIK_LeftFootTarget", refs.LeftFoot);
            m_RightFootTarget = CreateTarget("NYIK_RightFootTarget", refs.RightFoot);

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        Transform CreateTarget(string targetName, Transform bone)
        {
            var go = new GameObject(targetName);
            go.transform.SetParent(transform);

            if (bone != null)
            {
                go.transform.position = bone.position;
                go.transform.rotation = bone.rotation;
            }

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(go, "Create IK Test Target");
#endif
            return go.transform;
        }

        void OnEnable()
        {
            m_Humanoid = GetComponent<NYIKHumanoid>();
        }

        void OnDisable()
        {
            if (m_Humanoid != null && m_Humanoid.IsInitialized)
            {
                m_Humanoid.LeftLeg.ClearCustomTarget();
                m_Humanoid.RightLeg.ClearCustomTarget();
            }
        }

        void Update()
        {
            if (m_Humanoid == null) return;
            if (Application.isPlaying) return;

            if (!m_Humanoid.IsInitialized)
                m_Humanoid.Initialize();
            if (!m_Humanoid.IsInitialized)
                return;

            if (m_HeadTarget != null)
                m_Humanoid.HeadTarget.SetDirectly(m_HeadTarget.position, m_HeadTarget.rotation);
            if (m_LeftHandTarget != null)
                m_Humanoid.LeftHandTarget.SetDirectly(m_LeftHandTarget.position, m_LeftHandTarget.rotation);
            if (m_RightHandTarget != null)
                m_Humanoid.RightHandTarget.SetDirectly(m_RightHandTarget.position, m_RightHandTarget.rotation);
            if (m_LeftFootTarget != null)
                m_Humanoid.LeftLeg.FootTargetPosition = m_LeftFootTarget.position;
            if (m_RightFootTarget != null)
                m_Humanoid.RightLeg.FootTargetPosition = m_RightFootTarget.position;

            m_Humanoid.SolveManual();
        }
    }
}
