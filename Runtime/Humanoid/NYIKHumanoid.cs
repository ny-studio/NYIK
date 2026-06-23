using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using Unity.XR.CoreUtils;
using NYIK.Anatomy;
using NYIK.Calibration;
using NYIK.Estimator;
using NYIK.Solvers;
using NYIK.Tracker;
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
        [Tooltip("Reference to the scene's XR Origin. When set, AutoDetectTrackingSources " +
                 "uses its Camera + interactors. When null, no scene scan is performed " +
                 "(NYIK never uses FindAnyObjectByType — wire this explicitly).")]
        [SerializeField] XROrigin m_XROrigin;
        [SerializeField] Transform m_HeadSource;
        [SerializeField] Transform m_LeftHandSource;
        [SerializeField] Transform m_RightHandSource;

        [Header("VR Offsets (avatar-local meters, scale-aware)")]
        [Tooltip("Offset from HMD to head bone IK target. e.g. (0, -0.05, -0.15) puts the head bone " +
                 "5cm below + 15cm behind the HMD — i.e. HMD is at the brow, head bone is at the back of skull.")]
        [SerializeField] Vector3 m_HeadPositionOffset = new Vector3(0f, -0.1f, -0.05f);
        [SerializeField] Vector3 m_HeadRotationOffset;
        [SerializeField] Vector3 m_LeftHandPositionOffset = new Vector3(0f, -0.03f, -0.07f);
        [SerializeField] Vector3 m_LeftHandRotationOffset;
        [SerializeField] Vector3 m_RightHandPositionOffset = new Vector3(0f, -0.03f, -0.07f);
        [SerializeField] Vector3 m_RightHandRotationOffset;

        [Header("First-Person View")]
        [Tooltip("Scale the head bone down to ~0 while the first-person camera renders, hiding the " +
                 "head mesh from the user's own POV without affecting mirrors / other cameras. " +
                 "Standard VRChat trick.")]
        [SerializeField] bool m_HideHeadInFirstPerson = true;
        [Tooltip("Camera identified as the user's first-person view. Typically the VR HMD's Main Camera. " +
                 "Falls back to Camera.main at runtime if left null.")]
        [SerializeField] Camera m_FirstPersonCamera;
        [Tooltip("Scale to apply to the head bone during first-person rendering (default ~0.001 makes the head invisibly tiny).")]
        [SerializeField, Range(0f, 0.05f)]
        float m_HeadScaleWhileHidden = 0.001f;

        [Tooltip("一人称憑依: 起動時に XR Origin を一度だけアバター頭ボーンへ整列し、HMD/向きをアバターに重ねる。" +
                 "NYIK は HMD/コントローラの world 座標をそのまま IK ターゲットにするため、アバター root が " +
                 "リグから離れて置かれていると全身がリグ位置へ引っ張られて捻れる。これを ON にすると、" +
                 "アバターをシーンのどこに置いてもプレイヤーがその場で憑依できる(=鏡に自分が映る)。" +
                 "\n\n⚠️ NPC(他者)を駆動する NYIKHumanoid では OFF のまま。プレイヤーが憑依する 1 体だけ ON。")]
        [SerializeField] bool m_AlignXROriginToAvatarOnStart;

        // Set once TryAlignRigToAvatar() has run (waits for live HMD tracking).
        bool m_RigAligned;

        [Header("Spine")]
        [SerializeField] SpineSolver m_Spine = new SpineSolver();

        [Header("Arms")]
        [SerializeField] ArmSolver m_LeftArm = new ArmSolver();
        [SerializeField] ArmSolver m_RightArm = new ArmSolver();

        [Header("Legs")]
        [SerializeField] LegSolver m_LeftLeg = new LegSolver();
        [SerializeField] LegSolver m_RightLeg = new LegSolver();

        [Header("Anatomy")]
        [Tooltip("Optional per-avatar Joint ROM reference. When set, overrides the AAOS-based static defaults in JointROMLimits. Create via NYIK > Create AAOS Default ROM Reference, then customize per avatar.")]
        [SerializeField] JointROMReference m_ROMReference;

        [Header("General")]
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        [Tooltip("腰トラッカー位置で骨盤を動かす重み。1=トラッカーに完全追従(従来=ハード上書き)、" +
                 "<1 で前フレーム位置とブレンドし腰トラッカーの揺れ/ポップを減衰(VRChat の " +
                 "pelvisPositionWeight 相当)。既定 1.0 は挙動不変。ポップが出たらヘッドセットで下げる。")]
        [SerializeField, Range(0f, 1f)] float m_PelvisPositionWeight = 1f;

        [Header("User Scale (philosophy B: shrink targets, avatar fixed)")]
        [Tooltip("ユーザー→アバターのターゲット再マップ倍率 (avatar/user)。1.0=従来挙動(完全 no-op)。" +
                 "estimator 後の body ターゲットを床-腰ピボットまわりで縮め、performer↔avatar の身長差を吸収。" +
                 "VRCalibration の localScale 書き込み除去とセットで有効化する(現状は既定1.0で無回帰)。")]
        [SerializeField, Range(0.3f, 3f)] float m_UserScale = 1f;

        VRIKTarget m_HeadTarget = new VRIKTarget();
        VRIKTarget m_LeftHandTarget = new VRIKTarget();
        VRIKTarget m_RightHandTarget = new VRIKTarget();
        bool m_Initialized;

        // Full-body tracking integration. When TrackerProvider is set and has
        // full-body trackers, NYIK switches from the 3-point solver pipeline
        // to FBTPipeline (direct rotation writes + constraint refinement).
        ITrackerSourceProvider m_TrackerProvider;
        FBTPipeline m_FbtPipeline;

        // Unified pipeline (graceful degradation: tracked → tracker data,
        // un-tracked → estimator fills in). Built in SetupSolvers().
        BodyPartEstimatorRegistry m_EstimatorRegistry;
        readonly Dictionary<HumanBodyBones, BoneTarget> m_FrameTargets = new();
        FootEstimator m_LeftFootEst;
        FootEstimator m_RightFootEst;

        /// <summary>
        /// Attach a full-body tracker source (e.g. ManualTrackerSourceProvider)
        /// to enable FBT mode. Set to null to revert to 3-point HMD+controllers.
        /// </summary>
        public ITrackerSourceProvider TrackerProvider
        {
            get => m_TrackerProvider;
            set
            {
                m_TrackerProvider = value;
                m_FbtPipeline = value != null
                    ? new FBTPipeline(GetComponent<Animator>(), value)
                    : null;
            }
        }

        /// <summary>
        /// XR Origin reference used to auto-wire HMD + controllers. Assign in
        /// Inspector or set at runtime before <see cref="Initialize"/> runs.
        /// </summary>
        public XROrigin XROrigin
        {
            get => m_XROrigin;
            set => m_XROrigin = value;
        }

        /// <summary>
        /// Swap the anatomical ROM reference at runtime. Pass null to revert
        /// to the static AAOS-based defaults in <see cref="JointROMLimits"/>.
        /// </summary>
        public void SetROMReference(JointROMReference reference)
        {
            m_ROMReference = reference;
            JointROMLimits.SetReference(reference);
        }

        public NYIKReferences References => m_References;

        /// <summary>
        /// ユーザー→アバター ターゲット再マップ倍率（哲学B, avatar/user）。1.0=従来挙動（no-op）。
        /// 0/負値は安全側で 1.0 にクランプ。VRCalibration からの結線で設定する想定。
        /// </summary>
        public float UserScale
        {
            get => m_UserScale;
            set => m_UserScale = value > 1e-4f ? Mathf.Clamp(value, 0.3f, 3f) : 1f;
        }
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

#if UNITY_EDITOR
        [Header("Editor Gizmos")]
        [Tooltip("Show estimated HMD / controller positions in Scene view (helps tune the VR Offsets).")]
        [SerializeField] bool m_ShowViewpointGizmos = true;
        [SerializeField, Range(0.005f, 0.05f)] float m_ViewpointGizmoRadius = 0.025f;
        [SerializeField] Color m_HmdViewpointColor = new(1f, 0.8f, 0.2f, 1f);
        [SerializeField] Color m_HandViewpointColor = new(0.3f, 1f, 0.4f, 1f);

        /// <summary>
        /// Scene-view visualization: draws a sphere where the HMD / controllers
        /// will end up once the player puts the headset on, given the current
        /// VR offsets. Tune the offsets until the HMD sphere sits at the brow
        /// (just in front of and slightly above the head bone), and the
        /// controller spheres sit at the avatar's palms.
        ///
        /// Only drawn when this GameObject is selected to keep the Scene tidy.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (!m_ShowViewpointGizmos) return;
            if (m_References == null) return;
            // Auto-detect bones if the user hasn't entered Play yet.
            if (!m_References.IsValid())
            {
                var animator = GetComponent<Animator>();
                if (animator != null && animator.isHuman) m_References.AutoDetect(animator);
            }
            if (!m_References.IsValid()) return;

            Vector3 scale = transform.lossyScale;
            DrawViewpointGizmo(m_References.Head, m_HeadPositionOffset, scale,
                m_HmdViewpointColor, "HMD");
            DrawViewpointGizmo(m_References.LeftHand, m_LeftHandPositionOffset, scale,
                m_HandViewpointColor, "L Controller");
            DrawViewpointGizmo(m_References.RightHand, m_RightHandPositionOffset, scale,
                m_HandViewpointColor, "R Controller");
        }

        void DrawViewpointGizmo(Transform bone, Vector3 offset, Vector3 scale,
                                Color viewpointColor, string label)
        {
            if (bone == null) return;

            // VRIKTarget.UpdateTracking:  bone_target = source + source.rot * offset
            // → source (HMD / controller) = bone - bone.rot * offset
            // (At runtime the source IS the HMD; in edit mode we treat the bone
            // as the proxy since the HMD doesn't exist yet.)
            Vector3 scaledOffset = Vector3.Scale(offset, scale);
            Vector3 viewpoint = bone.position - bone.rotation * scaledOffset;

            // Bone marker (small wire sphere — where the bone IS)
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawWireSphere(bone.position, m_ViewpointGizmoRadius * 0.6f);

            // Viewpoint marker (solid sphere — where the HMD/controller WILL be)
            Gizmos.color = viewpointColor;
            Gizmos.DrawSphere(viewpoint, m_ViewpointGizmoRadius);

            // Connector line
            var line = viewpointColor; line.a = 0.45f;
            Gizmos.color = line;
            Gizmos.DrawLine(bone.position, viewpoint);

            UnityEditor.Handles.color = viewpointColor;
            UnityEditor.Handles.Label(viewpoint + Vector3.up * m_ViewpointGizmoRadius * 1.5f, label);
        }
#endif

        void Start()
        {
            Initialize();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            // Restore head bone scale on the off chance we were mid-render.
            RestoreHeadBone();
        }

        // First-person head hiding: scale head bone to ~0 while the first-person
        // camera renders, then restore. Mirrors, screenshots, and other cameras
        // render the head at normal scale. This is the trick VRChat uses.
        Vector3 _headBoneScaleBackup;
        bool _headBoneScaled;

        void HandleBeginCameraRendering(ScriptableRenderContext _, Camera camera)
        {
            if (!m_HideHeadInFirstPerson) return;
            if (!IsFirstPersonCamera(camera)) return;
            var head = m_References?.Head;
            if (head == null || _headBoneScaled) return;

            _headBoneScaleBackup = head.localScale;
            head.localScale = Vector3.one * m_HeadScaleWhileHidden;
            _headBoneScaled = true;
        }

        void HandleEndCameraRendering(ScriptableRenderContext _, Camera camera)
        {
            if (!_headBoneScaled) return;
            if (!IsFirstPersonCamera(camera)) return;
            RestoreHeadBone();
        }

        void RestoreHeadBone()
        {
            if (!_headBoneScaled) return;
            var head = m_References?.Head;
            if (head != null) head.localScale = _headBoneScaleBackup;
            _headBoneScaled = false;
        }

        bool IsFirstPersonCamera(Camera camera)
        {
            if (camera == null) return false;
            if (m_FirstPersonCamera != null) return camera == m_FirstPersonCamera;
            // Fallback: treat Camera.main as the user's view when not explicitly assigned.
            return camera == Camera.main;
        }

        /// <summary>
        /// Explicit setter for the first-person camera. Useful when the HMD
        /// camera is instantiated at runtime (XR rig prefab).
        /// </summary>
        public void SetFirstPersonCamera(Camera camera) => m_FirstPersonCamera = camera;

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

            // Wire the per-avatar ROM reference (or revert to defaults if null).
            // Doing this in Initialize means runtime ApplyOffsets-style adjustments
            // can swap references with NYIKHumanoid.SetROMReference at any time.
            JointROMLimits.SetReference(m_ROMReference);

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

            // 5. Auto-detect a tracker provider on the same GameObject (or in
            //    children) if one was not explicitly assigned via the property.
            if (m_TrackerProvider == null)
            {
                var auto = GetComponentInChildren<MonoBehaviour>(true) as ITrackerSourceProvider;
                if (auto == null)
                {
                    // GetComponentInChildren<MonoBehaviour> returns the first
                    // MonoBehaviour, not necessarily a provider. Iterate explicitly.
                    var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (var b in behaviours)
                    {
                        if (b is ITrackerSourceProvider p)
                        {
                            auto = p;
                            break;
                        }
                    }
                }
                if (auto != null)
                {
                    TrackerProvider = auto;
                    Debug.Log($"[NYIK] Auto-detected tracker provider: {auto.GetType().Name}", this);
                }
            }

            m_Initialized = true;
        }

        #region VR Auto Detection

        void AutoDetectTrackingSources()
        {
            if (m_XROrigin == null)
            {
                if (m_HeadSource == null || m_LeftHandSource == null || m_RightHandSource == null)
                    Debug.LogWarning("[NYIK] XROrigin is not assigned; VR tracking sources " +
                                     "that were not wired manually will be null.", this);
                return;
            }

            // HMD
            if (m_HeadSource == null)
                m_HeadSource = m_XROrigin.Camera?.transform;

            // Left/right controllers
            if (m_LeftHandSource == null || m_RightHandSource == null)
            {
                DetectControllers(m_XROrigin);
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

        /// <summary>
        /// Push the configured offsets onto the VRIKTargets. Multiply position
        /// offsets by the avatar's lossy scale so they stay anatomically correct
        /// for non-unit avatars (e.g. Milltina at scale 1.5). Call again
        /// whenever the avatar scale changes at runtime (VRCalibration).
        /// </summary>
        public void ApplyOffsets()
        {
            Vector3 s = transform.lossyScale;
            m_HeadTarget.PositionOffset = Vector3.Scale(m_HeadPositionOffset, s);
            m_HeadTarget.RotationOffset = m_HeadRotationOffset;
            m_LeftHandTarget.PositionOffset = Vector3.Scale(m_LeftHandPositionOffset, s);
            m_LeftHandTarget.RotationOffset = m_LeftHandRotationOffset;
            m_RightHandTarget.PositionOffset = Vector3.Scale(m_RightHandPositionOffset, s);
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

            SetupEstimators();
        }

        /// <summary>
        /// Register the default estimator chain. Trackers always win over
        /// estimators — slots with a tracker are auto-skipped by the registry.
        /// </summary>
        void SetupEstimators()
        {
            m_EstimatorRegistry = new BodyPartEstimatorRegistry();
            m_EstimatorRegistry.Register(new HipsFromHeadEstimator(m_Spine.PelvisEstimator, transform));
            m_EstimatorRegistry.Register(new ChestEstimator());
            m_EstimatorRegistry.Register(new ShoulderEstimator(isLeft: true));
            m_EstimatorRegistry.Register(new ShoulderEstimator(isLeft: false));
            m_EstimatorRegistry.Register(new ElbowBendGoalEstimator(isLeft: true));
            m_EstimatorRegistry.Register(new ElbowBendGoalEstimator(isLeft: false));
            m_EstimatorRegistry.Register(new KneeBendGoalEstimator(isLeft: true));
            m_EstimatorRegistry.Register(new KneeBendGoalEstimator(isLeft: false));

            var animator = GetComponent<Animator>();
            m_LeftFootEst = new FootEstimator(isLeft: true);
            m_RightFootEst = new FootEstimator(isLeft: false);
            m_LeftFootEst.CaptureBindPose(animator);
            m_RightFootEst.CaptureBindPose(animator);
            m_EstimatorRegistry.Register(m_LeftFootEst);
            m_EstimatorRegistry.Register(m_RightFootEst);
        }

        #endregion

        void LateUpdate()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            Solve();
        }

        /// <summary>
        /// One-shot first-person co-location: yaw + translate the XR Origin so the
        /// HMD coincides with the avatar's head bone and faces the avatar's forward.
        /// NYIK feeds the HMD/controllers' WORLD positions straight into the solvers
        /// as IK targets, so without this an avatar placed away from the rig (e.g.
        /// Milltina at world (-1.466, 0, 0) while the rig is at the origin) is dragged
        /// toward the rig's world location — the neck/spine stretch sideways, arms
        /// splay, legs can't reach: a twisted, inhuman pose. Aligning the rig onto the
        /// avatar makes the player embody it in place (and see themselves in the mirror).
        ///
        /// Runs every frame until the HMD reports a live standing pose (before XR is
        /// tracking the camera sits at the rig origin), then latches once.
        /// </summary>
        void TryAlignRigToAvatar()
        {
            if (m_XROrigin == null || m_HeadSource == null) return;
            var head = m_References?.Head;
            if (head == null) return;

            Transform rig = m_XROrigin.transform;

            // Gate: wait for live HMD tracking. Until then the camera is at the rig
            // floor (height ~0) and aligning would snap the rig to a bogus pose.
            if (m_HeadSource.position.y - rig.position.y < 0.2f) return;

            // 1. Yaw only (never pitch/roll the player): rotate the rig about the HMD
            //    so the HMD's flattened forward matches the avatar root's forward.
            //    Rotating about the HMD's own world position keeps the HMD position
            //    fixed and only changes facing.
            Vector3 camFwd = Vector3.ProjectOnPlane(m_HeadSource.forward, Vector3.up);
            Vector3 avatarFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (camFwd.sqrMagnitude > 1e-6f && avatarFwd.sqrMagnitude > 1e-6f)
            {
                float yaw = Vector3.SignedAngle(camFwd.normalized, avatarFwd.normalized, Vector3.up);
                rig.RotateAround(m_HeadSource.position, Vector3.up, yaw);
            }

            // 2. Translate the rig so the HMD coincides with the avatar head bone.
            rig.position += head.position - m_HeadSource.position;

            m_RigAligned = true;
            Debug.Log("[NYIK] XR Origin aligned to avatar head — first-person embodiment.", this);
        }

        /// <summary>
        /// Re-arm the one-shot rig alignment (e.g. after the player recenters or the
        /// avatar is repositioned). Next Solve() will realign once HMD tracking is live.
        /// </summary>
        public void RecalibrateRigAlignment() => m_RigAligned = false;

        /// <summary>
        /// Unified solve pipeline:
        ///   1. Provider tick + VRIK target update (HMD + controllers)
        ///   2. Build <see cref="m_FrameTargets"/> from live trackers + VRIK
        ///   3. Estimators fill in every Humanoid bone the trackers didn't
        ///   4. Spine / Arms / Legs IK consume the resolved targets
        ///   5. Tracked spine/limb bones get direct-rotation override
        ///   6. ConstraintRefiner enforces ROM + bone length
        ///
        /// No more "3-point vs FBT" branch — adding a tracker just replaces
        /// that one bone's data path. The estimator chain auto-skips it.
        /// </summary>
        void Solve()
        {
            if (m_AlignXROriginToAvatarOnStart && !m_RigAligned)
                TryAlignRigToAvatar();

            float dt = Time.deltaTime;
            m_TrackerProvider?.Tick(dt);

            m_HeadTarget.UpdateTracking();
            m_LeftHandTarget.UpdateTracking();
            m_RightHandTarget.UpdateTracking();

            BuildFrameTargets();

            var animator = GetComponent<Animator>();
            if (m_EstimatorRegistry != null)
            {
                var ctx = new EstimatorContext(animator, m_TrackerProvider, m_FrameTargets, dt);
                m_EstimatorRegistry.ResolveAll(ctx);
            }

            // ユーザー→アバター 身長スケール（哲学B：トラッカー世界位置を床-腰ピボットで縮める）。
            // estimator 解決後・Hips/Spine 適用前に body ターゲットを一括再マップ。scale=1 は完全 no-op。
            // ピボット = Hips の XZ + Root(=床基準) の Y。生位置→ジッタ再注入は scale 有効化フェーズで
            // EffectivePosition へ差し替え予定（現状は scale=1 既定で無回帰）。
            if (m_UserScale != 1f &&
                m_FrameTargets.TryGetValue(HumanBodyBones.Hips, out var hipsScalePivot) &&
                hipsScalePivot.HasPosition)
            {
                float floorY = m_References != null && m_References.Root != null
                    ? m_References.Root.position.y : transform.position.y;
                var pivot = new Vector3(hipsScalePivot.Position.x, floorY, hipsScalePivot.Position.z);
                FBTCalibrator.ScaleBodyTargetsAboutPivot(m_FrameTargets, pivot, m_UserScale);
            }

            ApplyHipsPosition();
            ApplySpine(dt);

            Quaternion bodyRotation = m_Spine.PelvisYawRotation;

            ApplyArm(m_LeftArm, m_LeftHandTarget, HumanBodyBones.LeftLowerArm, bodyRotation);
            ApplyArm(m_RightArm, m_RightHandTarget, HumanBodyBones.RightLowerArm, bodyRotation);

            Vector3 pelvisPos = m_References.Pelvis != null
                ? m_References.Pelvis.position : transform.position;
            ApplyLeg(m_LeftLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, pelvisPos, bodyRotation);
            ApplyLeg(m_RightLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, pelvisPos, bodyRotation);

            ApplyDirectTrackerRotations();

            // Post: ROM + bone length
            ConstraintRefiner.Refine(animator, 2, 0.8f);
            AnatomicalRefiner.ClampAllJoints(animator, 0.5f);
        }

        void BuildFrameTargets()
        {
            m_FrameTargets.Clear();

            // VRIK targets (HMD/controllers with offset). These take precedence
            // over a tracker for Head/L/R Hand because they include the
            // m_HeadPositionOffset etc. corrections.
            if (m_HeadTarget.IsTracking)
                m_FrameTargets[HumanBodyBones.Head] = BoneTarget.Tracked(m_HeadTarget.Position, m_HeadTarget.Rotation);
            if (m_LeftHandTarget.IsTracking)
                m_FrameTargets[HumanBodyBones.LeftHand] = BoneTarget.Tracked(m_LeftHandTarget.Position, m_LeftHandTarget.Rotation);
            if (m_RightHandTarget.IsTracking)
                m_FrameTargets[HumanBodyBones.RightHand] = BoneTarget.Tracked(m_RightHandTarget.Position, m_RightHandTarget.Rotation);

            // FBT slots from the provider (skip Head/L/R Hand — those came from VRIK above)
            if (m_TrackerProvider == null) return;
            foreach (var slot in m_TrackerProvider.Slots)
            {
                if (slot == null || !slot.IsAssigned || !slot.IsTracking) continue;
                if (slot.Kind == TrackerSlotKind.Head ||
                    slot.Kind == TrackerSlotKind.LeftHand ||
                    slot.Kind == TrackerSlotKind.RightHand) continue;
                var bone = FBTCalibrator.GetBoneForSlot(slot.Kind);
                if (!bone.HasValue) continue;
                if (m_FrameTargets.ContainsKey(bone.Value)) continue;
                // 位置も回転と同様にキャリブ済み値を使う。生 Source.position は T-pose で学習した
                // CalibrationPosOffset（トラッカー実装位置↔ボーン位置のオフセット）を無視し、特に
                // Waist→Hips 駆動で骨盤が数 cm ずれていた。CalibratedPosition は offset 適用＋
                // OneEuro フィルタ済み effective 値も尊重（CalibratedRotation と整合）。
                m_FrameTargets[bone.Value] = BoneTarget.Tracked(slot.CalibratedPosition, slot.CalibratedRotation);
            }
        }

        void ApplyHipsPosition()
        {
            if (!m_FrameTargets.TryGetValue(HumanBodyBones.Hips, out var hips)) return;
            if (!hips.HasPosition) return;
            if (m_References.Pelvis == null) return;
            // VRChat 整合: ハード上書きでなく重みブレンド。既定 1.0 は従来挙動(完全追従)。
            // <1 で前フレーム位置とブレンドし腰トラッカーの揺れ/ポップを減衰(ヘッドセットで調整)。
            m_References.Pelvis.position = Vector3.Lerp(
                m_References.Pelvis.position, hips.Position, m_PelvisPositionWeight);
        }

        void ApplySpine(float deltaTime)
        {
            if (!m_FrameTargets.TryGetValue(HumanBodyBones.Head, out var head)) return;
            if (!m_Spine.IsValid()) return;
            m_Spine.Weight = m_HeadTarget.PositionWeight * m_Weight;
            m_Spine.Solve(head.Position, head.Rotation, deltaTime);
        }

        void ApplyArm(ArmSolver arm, VRIKTarget vrikTarget, HumanBodyBones lowerArmBone, Quaternion bodyRotation)
        {
            if (!vrikTarget.IsTracking) return;
            arm.TargetPosition = vrikTarget.Position;
            arm.TargetRotation = vrikTarget.Rotation;
            arm.Weight = vrikTarget.PositionWeight * m_Weight;
            arm.BodyRotation = bodyRotation;
            arm.DeltaTime = Time.deltaTime;

            // Estimator-supplied bend goal (else solver's internal fallback)
            if (m_FrameTargets.TryGetValue(lowerArmBone, out var bendTarget) && bendTarget.HasPosition)
            {
                arm.BendGoalPosition = bendTarget.Position;
                arm.BendGoalWeight = 1f;
            }
            else
            {
                arm.BendGoalWeight = 0f;
            }

            arm.Solve();
        }

        void ApplyLeg(LegSolver leg, HumanBodyBones footBone, HumanBodyBones lowerLegBone,
                      Vector3 pelvisPos, Quaternion bodyRotation)
        {
            leg.BodyRotation = bodyRotation;
            leg.Weight = m_Weight;
            leg.DeltaTime = Time.deltaTime;

            // FootEstimator output (or live tracker if bound)
            if (m_FrameTargets.TryGetValue(footBone, out var foot) && foot.HasPosition)
            {
                leg.FootTargetPosition = foot.Position;
            }

            if (m_FrameTargets.TryGetValue(lowerLegBone, out var bend) && bend.HasPosition)
            {
                leg.SetExternalBendGoal(bend.Position);
            }
            else
            {
                leg.ClearExternalBendGoal();
            }

            leg.Solve(pelvisPos);
        }

        /// <summary>
        /// Write tracked-slot rotations directly to their bones, overriding
        /// the IK solver output where a live tracker is present. Skips
        /// Head/LeftHand/RightHand (already handled via VRIKTargets + IK).
        /// </summary>
        void ApplyDirectTrackerRotations()
        {
            if (m_TrackerProvider == null) return;
            foreach (var slot in m_TrackerProvider.Slots)
            {
                if (slot == null || !slot.IsAssigned || !slot.IsTracking) continue;
                if (slot.Kind == TrackerSlotKind.Head ||
                    slot.Kind == TrackerSlotKind.LeftHand ||
                    slot.Kind == TrackerSlotKind.RightHand) continue;

                var bone = FBTCalibrator.GetBoneForSlot(slot.Kind);
                if (!bone.HasValue) continue;
                var t = GetComponent<Animator>().GetBoneTransform(bone.Value);
                if (t == null) continue;

                Quaternion target = slot.CalibratedRotation;
                if (slot.TrustWeight >= 0.999f) t.rotation = target;
                else t.rotation = Quaternion.Slerp(t.rotation, target, slot.TrustWeight);
            }
        }

        /// <summary>
        /// Manually triggers the unified solve. For Editor-time IK preview
        /// (NYIKTestTargets) when no VR runtime is available.
        /// </summary>
        public void SolveManual()
        {
            if (!m_Initialized || m_Weight <= 0f) return;
            Solve();
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
