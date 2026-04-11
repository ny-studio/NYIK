using System.Collections.Generic;
using UnityEngine;

using Unity.XR.CoreUtils;
using NYIK.Validation;
using NYIK.VR;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Main component of NYIK. Simply attach to a Humanoid avatar to enable
    /// automatic bone detection, VR tracking connection, and full-body IK solving.
    /// </summary>
    [AddComponentMenu("NYIK/Humanoid IK")]
    [RequireComponent(typeof(Animator))]
    public class NYIKHumanoid : MonoBehaviour
    {
        [Header("Bone References (auto-detected from Animator if empty)")]
        [SerializeField] NYIKReferences m_References = new NYIKReferences();

        [Header("VR Tracking Sources (auto-detected from XROrigin if empty)")]
        [SerializeField] Transform m_HeadSource;
        [SerializeField] Transform m_LeftHandSource;
        [SerializeField] Transform m_RightHandSource;

        [Header("VR Offsets")]
        [SerializeField] Vector3 m_HeadPositionOffset = new Vector3(0f, -0.1f, -0.05f);
        [SerializeField] Vector3 m_HeadRotationOffset;
        [SerializeField] Vector3 m_LeftHandPositionOffset = new Vector3(0f, -0.03f, -0.07f);
        [SerializeField] Vector3 m_LeftHandRotationOffset;
        [SerializeField] Vector3 m_RightHandPositionOffset = new Vector3(0f, -0.03f, -0.07f);
        [SerializeField] Vector3 m_RightHandRotationOffset;

        [Header("Spine")]
        [SerializeField] SpineSolver m_Spine = new SpineSolver();

        [Header("Arms")]
        [SerializeField] ArmSolver m_LeftArm = new ArmSolver();
        [SerializeField] ArmSolver m_RightArm = new ArmSolver();

        [Header("Legs")]
        [SerializeField] LegSolver m_LeftLeg = new LegSolver();
        [SerializeField] LegSolver m_RightLeg = new LegSolver();

        [Header("General")]
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;

        VRIKTarget m_HeadTarget = new VRIKTarget();
        VRIKTarget m_LeftHandTarget = new VRIKTarget();
        VRIKTarget m_RightHandTarget = new VRIKTarget();
        bool m_Initialized;

        public NYIKReferences References => m_References;
        public VRIKTarget HeadTarget => m_HeadTarget;
        public VRIKTarget LeftHandTarget => m_LeftHandTarget;
        public VRIKTarget RightHandTarget => m_RightHandTarget;
        public SpineSolver Spine => m_Spine;
        public ArmSolver LeftArm => m_LeftArm;
        public ArmSolver RightArm => m_RightArm;
        public LegSolver LeftLeg => m_LeftLeg;
        public LegSolver RightLeg => m_RightLeg;
        public bool IsInitialized => m_Initialized;

        /// <summary>
        /// Overall IK weight.
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Fully automatic initialization:
        /// 1. Auto-detect bones from Animator
        /// 2. Auto-detect tracking sources from XROrigin
        /// 3. Set up all sub-solvers
        /// </summary>
        public void Initialize()
        {
            if (m_Initialized)
                return;

            // 1. Auto-detect bones
            if (!m_References.IsValid())
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman)
                    m_References.AutoDetect(animator);
            }

            if (!m_References.IsValid())
            {
                Debug.LogWarning("[NYIK] Bone references are not valid. Humanoid Animator required.", this);
                return;
            }

            // 2. Auto-detect VR tracking sources
            AutoDetectTrackingSources();
            ApplyOffsets();

            // 3. Validation
            var warnings = IKValidator.Validate(this);
            foreach (var warning in warnings)
                Debug.LogWarning($"[NYIK] {warning}", this);

            // 4. Set up sub-solvers
            SetupSolvers();

            m_Initialized = true;
        }

        #region VR Auto Detection

        void AutoDetectTrackingSources()
        {
            var xrOrigin = FindAnyObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogWarning("[NYIK] XROrigin not found. VR tracking disabled.", this);
                return;
            }

            // HMD
            if (m_HeadSource == null)
                m_HeadSource = xrOrigin.Camera?.transform;

            // Left/right controllers
            if (m_LeftHandSource == null || m_RightHandSource == null)
            {
                DetectControllers(xrOrigin);
            }

            // Connect detection results to VRIKTarget
            m_HeadTarget.Source = m_HeadSource;
            m_LeftHandTarget.Source = m_LeftHandSource;
            m_RightHandTarget.Source = m_RightHandSource;

            if (m_HeadSource == null)
                Debug.LogWarning("[NYIK] Head tracking source not found.", this);
            if (m_LeftHandSource == null)
                Debug.LogWarning("[NYIK] Left hand tracking source not found.", this);
            if (m_RightHandSource == null)
                Debug.LogWarning("[NYIK] Right hand tracking source not found.", this);
        }

        void DetectControllers(XROrigin xrOrigin)
        {
            // Detect from interactor components
            var interactors = xrOrigin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(true);
            foreach (var interactor in interactors)
            {
                var t = interactor.transform;
                bool isLeft = ContainsInHierarchy(t, "left");
                bool isRight = ContainsInHierarchy(t, "right");

                if (isLeft && m_LeftHandSource == null)
                    m_LeftHandSource = FindControllerRoot(t, xrOrigin);
                else if (isRight && m_RightHandSource == null)
                    m_RightHandSource = FindControllerRoot(t, xrOrigin);
            }

            // Fallback: name-based detection
            if (m_LeftHandSource == null || m_RightHandSource == null)
            {
                Transform searchRoot = xrOrigin.CameraFloorOffsetObject?.transform ?? xrOrigin.transform;
                foreach (Transform child in searchRoot)
                {
                    string name = child.name.ToLowerInvariant();
                    if (m_LeftHandSource == null && name.Contains("left") &&
                        (name.Contains("controller") || name.Contains("hand")))
                        m_LeftHandSource = child;
                    else if (m_RightHandSource == null && name.Contains("right") &&
                        (name.Contains("controller") || name.Contains("hand")))
                        m_RightHandSource = child;
                }
            }
        }

        static Transform FindControllerRoot(Transform interactor, XROrigin xrOrigin)
        {
            Transform current = interactor;
            while (current.parent != null)
            {
                string parentName = current.parent.name.ToLowerInvariant();
                if (parentName.Contains("controller") || parentName.Contains("hand"))
                    return current.parent;
                if (current.parent == xrOrigin.transform)
                    break;
                current = current.parent;
            }
            return interactor;
        }

        static bool ContainsInHierarchy(Transform t, string keyword)
        {
            Transform current = t;
            for (int depth = 0; current != null && depth < 5; depth++)
            {
                if (current.name.ToLowerInvariant().Contains(keyword))
                    return true;
                current = current.parent;
            }
            return false;
        }

        void ApplyOffsets()
        {
            m_HeadTarget.PositionOffset = m_HeadPositionOffset;
            m_HeadTarget.RotationOffset = m_HeadRotationOffset;
            m_LeftHandTarget.PositionOffset = m_LeftHandPositionOffset;
            m_LeftHandTarget.RotationOffset = m_LeftHandRotationOffset;
            m_RightHandTarget.PositionOffset = m_RightHandPositionOffset;
            m_RightHandTarget.RotationOffset = m_RightHandRotationOffset;
        }

        #endregion

        #region Solver Setup

        void SetupSolvers()
        {
            // Spine
            m_Spine.Setup(m_References.Pelvis, m_References.SpineBones, m_References.Head);
            m_Spine.Initialize(transform);

            // Left arm
            m_LeftArm.Setup(m_References.LeftShoulder, m_References.LeftUpperArm,
                m_References.LeftForearm, m_References.LeftHand, isLeft: true);
            m_LeftArm.Initialize(transform);

            // Right arm
            m_RightArm.Setup(m_References.RightShoulder, m_References.RightUpperArm,
                m_References.RightForearm, m_References.RightHand, isLeft: false);
            m_RightArm.Initialize(transform);

            // Left leg
            m_LeftLeg.Setup(m_References.LeftThigh, m_References.LeftCalf,
                m_References.LeftFoot, isLeft: true);
            m_LeftLeg.Initialize(transform);

            // Right leg
            m_RightLeg.Setup(m_References.RightThigh, m_References.RightCalf,
                m_References.RightFoot, isLeft: false);
            m_RightLeg.Initialize(transform);
        }

        #endregion

        void LateUpdate()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            Solve();
        }

        /// <summary>
        /// Reads VR tracking data and then solves IK.
        /// </summary>
        void Solve()
        {
            m_HeadTarget.UpdateTracking();
            m_LeftHandTarget.UpdateTracking();
            m_RightHandTarget.UpdateTracking();

            SolveIK();
        }

        /// <summary>
        /// Manually triggers IK solving using current target data.
        /// Set target data via VRIKTarget.SetDirectly() before calling.
        /// Intended for editor-time testing without VR hardware.
        /// </summary>
        public void SolveManual()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            SolveIK();
        }

        /// <summary>
        /// Core IK solve logic.
        /// Order: Spine → Arms → Legs.
        /// </summary>
        void SolveIK()
        {
            // Spine
            if (m_HeadTarget.IsTracking && m_Spine.IsValid())
            {
                m_Spine.Weight = m_HeadTarget.PositionWeight * m_Weight;
                m_Spine.Solve(m_HeadTarget.Position, m_HeadTarget.Rotation);
            }

            // Body rotation from spine solver (pelvis yaw applied)
            Quaternion bodyRotation = m_Spine.PelvisYawRotation;

            // Arms
            if (m_LeftHandTarget.IsTracking)
            {
                m_LeftArm.TargetPosition = m_LeftHandTarget.Position;
                m_LeftArm.TargetRotation = m_LeftHandTarget.Rotation;
                m_LeftArm.Weight = m_LeftHandTarget.PositionWeight * m_Weight;
                m_LeftArm.BodyRotation = bodyRotation;
                m_LeftArm.Solve();
            }
            if (m_RightHandTarget.IsTracking)
            {
                m_RightArm.TargetPosition = m_RightHandTarget.Position;
                m_RightArm.TargetRotation = m_RightHandTarget.Rotation;
                m_RightArm.Weight = m_RightHandTarget.PositionWeight * m_Weight;
                m_RightArm.BodyRotation = bodyRotation;
                m_RightArm.Solve();
            }

            // Legs
            Vector3 pelvisPos = m_References.Pelvis != null
                ? m_References.Pelvis.position : transform.position;
            m_LeftLeg.BodyRotation = bodyRotation;
            m_LeftLeg.Weight = m_Weight;
            m_LeftLeg.Solve(pelvisPos);
            m_RightLeg.BodyRotation = bodyRotation;
            m_RightLeg.Weight = m_Weight;
            m_RightLeg.Solve(pelvisPos);
        }

        /// <summary>
        /// Resets all calibration and recalculates on the next frame.
        /// Accuracy improves when called while in a correct pose such as T-pose.
        /// </summary>
        public void Recalibrate()
        {
            m_Spine.RecalibrateHead();
            m_LeftArm.ResetHandCalibration();
            m_RightArm.ResetHandCalibration();
        }

        /// <summary>
        /// Resets hand rotation calibration and recalculates on the next frame.
        /// </summary>
        public void RecalibrateHands()
        {
            m_LeftArm.ResetHandCalibration();
            m_RightArm.ResetHandCalibration();
        }

        [ContextMenu("Auto Detect Bones")]
        public void AutoDetectBones()
        {
            var animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                m_References.AutoDetect(animator);
                Debug.Log("[NYIK] Bones auto-detected from Humanoid Animator.", this);
            }
            else
            {
                Debug.LogWarning("[NYIK] No Humanoid Animator found.", this);
            }
        }

        public List<string> GetWarnings() => IKValidator.Validate(this);

        void OnValidate()
        {
            if (Application.isPlaying && m_Initialized)
            {
                m_Initialized = false;
                Initialize();
            }
        }
    }
}
