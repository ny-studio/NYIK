using System;
using System.Collections.Generic;
using UnityEngine;
using NYIK.Solvers;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Humanoid spine solver using rotation distribution.
    /// Distributes pelvis-to-head rotation proportionally across spine bones,
    /// with direct HMD rotation applied to the head.
    /// Resets to initial pose each frame to prevent error accumulation.
    /// </summary>
    [Serializable]
    public class SpineSolver
    {
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        [SerializeField, Range(0f, 1f)] float m_TwistWeight = 0.5f;
        [SerializeField, Range(0f, 1f)] float m_BodyTurnWeight = 0.7f;

        [Header("Solver Method")]
        [Tooltip("Use FABRIK chain (constrained) instead of static weight-distribution. " +
                 "FABRIK converges to the head target geometrically and applies a swing-cone " +
                 "constraint per spine joint each iteration. Eliminates the 'residual correction' " +
                 "hack and gives cleaner spine bending for complex poses.")]
        [SerializeField] bool m_UseFABRIK = true;
        [SerializeField, Range(1, 5)] int m_FABRIKIterations = 3;
        [Tooltip("Per-spine-joint swing cone half-angle (degrees) used by Constrained FABRIK.")]
        [SerializeField, Range(5f, 60f)] float m_PerJointSwingMaxDeg = 25f;

        // Scratch buffers for FABRIK to avoid per-frame allocations
        Vector3[] m_FABRIKPositions;
        float[] m_FABRIKLengths;

        PelvisEstimator m_PelvisEstimator = new PelvisEstimator();

        Transform m_Pelvis;
        Transform m_Head;
        Transform m_Root;

        // Spine bones from pelvis to neck (head excluded)
        Transform[] m_SpineBones;
        Quaternion[] m_InitialLocalRotations;
        Quaternion m_InitialHeadLocalRotation;

        // HMD-to-head bone rotation offset (auto-calculated on first frame)
        Quaternion m_HeadRotationOffset = Quaternion.identity;
        bool m_HeadCalibrated;
        const float k_MinCalibrationHeight = 0.3f;

        // Per-bone bend weights (fraction of remaining rotation absorbed by each bone)
        float[] m_BendWeights;

        Quaternion m_PelvisYawRotation = Quaternion.identity;
        float m_SmoothedPelvisYaw;
        const float k_PelvisYawSmoothTime = 0.08f;
        bool m_Initialized;

        /// <summary>
        /// Spine solver weight (0 = disabled, 1 = full).
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// How much of the head's yaw rotation is distributed to spine bones (0 = none, 1 = full).
        /// </summary>
        public float TwistWeight
        {
            get => m_TwistWeight;
            set => m_TwistWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// How much the pelvis yaw follows the head yaw (0 = fixed, 1 = fully follows).
        /// </summary>
        public float BodyTurnWeight
        {
            get => m_BodyTurnWeight;
            set => m_BodyTurnWeight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// The body rotation after pelvis yaw is applied.
        /// Used by LegSolver and ArmSolver for body-relative calculations.
        /// </summary>
        public Quaternion PelvisYawRotation => m_PelvisYawRotation;

        public PelvisEstimator PelvisEstimator => m_PelvisEstimator;

        /// <summary>
        /// Reset head rotation calibration.
        /// The HMD-to-bone offset will be recalculated on the next frame.
        /// </summary>
        public void RecalibrateHead()
        {
            m_HeadCalibrated = false;
        }

        /// <summary>
        /// Set up the spine chain.
        /// </summary>
        /// <param name="pelvis">Pelvis bone</param>
        /// <param name="spineBones">Spine bones in order (lumbar to cervical)</param>
        /// <param name="head">Head bone</param>
        public void Setup(Transform pelvis, Transform[] spineBones, Transform head)
        {
            m_Pelvis = pelvis;
            m_Head = head;

            // Build array of pelvis + intermediate spine bones (head excluded)
            int spineCount = spineBones?.Length ?? 0;
            m_SpineBones = new Transform[1 + spineCount];
            m_SpineBones[0] = pelvis;
            if (spineBones != null)
            {
                for (int i = 0; i < spineCount; i++)
                    m_SpineBones[i + 1] = spineBones[i];
            }
        }

        /// <summary>
        /// Initialize the solver with root transform.
        /// </summary>
        public void Initialize(Transform root)
        {
            m_Root = root;

            if (m_Pelvis == null || m_Head == null || m_SpineBones == null)
                return;

            // Cache initial local rotations
            m_InitialLocalRotations = new Quaternion[m_SpineBones.Length];
            for (int i = 0; i < m_SpineBones.Length; i++)
            {
                if (m_SpineBones[i] != null)
                    m_InitialLocalRotations[i] = m_SpineBones[i].localRotation;
            }
            m_InitialHeadLocalRotation = m_Head.localRotation;
            m_HeadCalibrated = false;

            // Set up bend weights
            // Each weight represents the percentage of remaining rotation absorbed by this bone
            SetupBendWeights();

            m_PelvisEstimator.Initialize(m_Pelvis, m_Head);
            m_PelvisYawRotation = root.rotation;
            m_Initialized = true;
        }

        /// <summary>
        /// Set anatomically natural bend weights.
        /// Pelvis gets minimum, neck gets maximum.
        /// </summary>
        void SetupBendWeights()
        {
            int count = m_SpineBones.Length;
            m_BendWeights = new float[count];

            if (count == 1)
            {
                // Pelvis only
                m_BendWeights[0] = 0.3f;
            }
            else
            {
                // Pelvis=0.1, last (closest to neck)=0.5, intermediate values linearly interpolated
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / (count - 1);
                    m_BendWeights[i] = Mathf.Lerp(0.1f, 0.5f, t);
                }
            }
        }

        /// <summary>
        /// Rotates the pelvis bone in yaw toward the head direction.
        /// Called after pelvis position is set but before spine bend distribution.
        /// </summary>
        void ApplyPelvisYaw(Vector3 headTargetPosition, Quaternion headTargetRotation, float deltaTime)
        {
            if (m_Root == null || m_Pelvis == null || m_BodyTurnWeight <= 0f)
            {
                m_PelvisYawRotation = m_Root != null ? m_Root.rotation : Quaternion.identity;
                return;
            }

            Vector3 rootForward = m_Root.forward;
            rootForward.y = 0f;
            if (rootForward.sqrMagnitude < 0.0001f)
            {
                m_PelvisYawRotation = m_Root.rotation;
                return;
            }
            rootForward.Normalize();

            // Use head rotation yaw only (not position) to avoid
            // false rotation from joystick locomotion / pelvis smoothing lag
            Vector3 headForward = headTargetRotation * Vector3.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude < 0.0001f)
            {
                m_PelvisYawRotation = m_Root.rotation;
                return;
            }

            float yawAngle = Vector3.SignedAngle(rootForward, headForward.normalized, Vector3.up);

            float targetYaw = yawAngle * m_BodyTurnWeight;

            // Frame-rate independent exponential smoothing to prevent snapping
            float dt = deltaTime;
            if (dt > 0f)
            {
                float alpha = 1f - Mathf.Exp(-dt / k_PelvisYawSmoothTime);
                m_SmoothedPelvisYaw = Mathf.LerpAngle(m_SmoothedPelvisYaw, targetYaw, alpha);
            }
            else
            {
                m_SmoothedPelvisYaw = targetYaw; // Edit mode: apply directly
            }

            Quaternion pelvisYawDelta = Quaternion.AngleAxis(m_SmoothedPelvisYaw, Vector3.up);

            m_Pelvis.rotation = pelvisYawDelta * m_Pelvis.rotation;
            m_PelvisYawRotation = pelvisYawDelta * m_Root.rotation;
        }

        /// <summary>
        /// Solve spine IK using rotation distribution.
        /// </summary>
        /// <param name="headTargetPosition">Head target position from HMD</param>
        /// <param name="headTargetRotation">Head target rotation from HMD</param>
        public void Solve(Vector3 headTargetPosition, Quaternion headTargetRotation, float deltaTime)
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            // 1. Reset all spine bones + head to initial local rotations
            for (int i = 0; i < m_SpineBones.Length; i++)
            {
                if (m_SpineBones[i] != null)
                    m_SpineBones[i].localRotation = m_InitialLocalRotations[i];
            }
            m_Head.localRotation = m_InitialHeadLocalRotation;

            // 2. Calibration (using head world rotation after reset, before spine bending).
            //    Guard: skip if HMD is too low (e.g. sitting on a desk at startup).
            if (!m_HeadCalibrated)
            {
                if (headTargetPosition.y < k_MinCalibrationHeight)
                    return;
                m_HeadRotationOffset = Quaternion.Inverse(headTargetRotation) * m_Head.rotation;
                m_HeadCalibrated = true;
            }

            // 3. Estimate and apply pelvis position directly (no Lerp to prevent oscillation)
            Vector3 pelvisPos = m_PelvisEstimator.Estimate(
                headTargetPosition, headTargetRotation, m_Root
            );
            if (m_Pelvis != null)
                m_Pelvis.position = pelvisPos;

            // 3.5. Apply pelvis yaw rotation toward head direction
            ApplyPelvisYaw(headTargetPosition, headTargetRotation, deltaTime);

            // 4. Bend distribution — FABRIK chain (Aristidou & Lasenby 2017) or
            // legacy static-weight rotation distribution.
            if (m_UseFABRIK)
            {
                SolveSpineFABRIK(headTargetPosition);
                ApplyFinalHeadRotation(headTargetRotation);
                return;
            }

            // 4. (legacy) Distribute spine bend from root to tip.
            //    Each bone absorbs a weighted fraction of the rotation from its position
            //    toward the head target.
            int lastValidIndex = -1;
            for (int i = 0; i < m_SpineBones.Length; i++)
            {
                if (m_SpineBones[i] == null)
                    continue;

                Vector3 bonePos = m_SpineBones[i].position;
                Vector3 currentDir = m_Head.position - bonePos;
                Vector3 targetDir = headTargetPosition - bonePos;

                if (currentDir.sqrMagnitude < 0.0001f || targetDir.sqrMagnitude < 0.0001f)
                    continue;

                if (Vector3.Dot(currentDir.normalized, targetDir.normalized) >= 0.9999f)
                    break;

                lastValidIndex = i;

                Quaternion remainingBend = Quaternion.FromToRotation(
                    currentDir.normalized, targetDir.normalized
                );
                Quaternion boneBend = Quaternion.Slerp(
                    Quaternion.identity, remainingBend, m_BendWeights[i]
                );

                m_SpineBones[i].rotation = boneBend * m_SpineBones[i].rotation;
            }

            // 5. Residual correction on the last bone (compensate for weight sum < 100%)
            if (lastValidIndex >= 0 && m_SpineBones[lastValidIndex] != null)
            {
                Vector3 lastBonePos = m_SpineBones[lastValidIndex].position;
                Vector3 currentDir = m_Head.position - lastBonePos;
                Vector3 targetDir = headTargetPosition - lastBonePos;

                if (currentDir.sqrMagnitude >= 0.0001f && targetDir.sqrMagnitude >= 0.0001f)
                {
                    float dot = Vector3.Dot(currentDir.normalized, targetDir.normalized);
                    if (dot < 0.9999f)
                    {
                        Quaternion residual = Quaternion.FromToRotation(
                            currentDir.normalized, targetDir.normalized
                        );
                        m_SpineBones[lastValidIndex].rotation =
                            residual * m_SpineBones[lastValidIndex].rotation;
                    }
                }
            }

            // 6. Distribute spine twist (yaw rotation around vertical axis)
            if (m_TwistWeight > 0f)
                DistributeSpineTwist(headTargetPosition, headTargetRotation);

            // 7. Apply HMD rotation to head with offset
            ApplyFinalHeadRotation(headTargetRotation);
        }

        void ApplyFinalHeadRotation(Quaternion headTargetRotation)
        {
            Quaternion targetHeadRotation = headTargetRotation * m_HeadRotationOffset;
            m_Head.rotation = Quaternion.Slerp(m_Head.rotation, targetHeadRotation, m_Weight);
        }

        /// <summary>
        /// Position-based spine solve via Constrained FABRIK
        /// (Aristidou & Lasenby 2017). Builds a chain Pelvis→Spine[0..N]→Head,
        /// iterates forward+backward reaching, then projects each joint
        /// direction into a swing cone around its rest pose. Bone rotations
        /// are derived from the resulting position chain via FromToRotation.
        /// </summary>
        void SolveSpineFABRIK(Vector3 headTargetPosition)
        {
            int spineCount = m_SpineBones?.Length ?? 0;
            if (spineCount == 0 || m_Pelvis == null || m_Head == null) return;
            int total = 1 + spineCount + 1;

            if (m_FABRIKPositions == null || m_FABRIKPositions.Length != total)
            {
                m_FABRIKPositions = new Vector3[total];
                m_FABRIKLengths = new float[total - 1];
            }

            // Seed positions from current world pose.
            m_FABRIKPositions[0] = m_Pelvis.position;
            for (int i = 0; i < spineCount; i++)
                m_FABRIKPositions[i + 1] = m_SpineBones[i].position;
            m_FABRIKPositions[total - 1] = m_Head.position;

            // Lock bone lengths from this initial snapshot.
            for (int i = 0; i < total - 1; i++)
                m_FABRIKLengths[i] = Vector3.Distance(m_FABRIKPositions[i], m_FABRIKPositions[i + 1]);

            Vector3 rootPos = m_FABRIKPositions[0];
            float maxSwingRad = m_PerJointSwingMaxDeg * Mathf.Deg2Rad;

            for (int iter = 0; iter < m_FABRIKIterations; iter++)
            {
                // Forward reach (head → pelvis)
                m_FABRIKPositions[total - 1] = headTargetPosition;
                for (int i = total - 2; i >= 0; i--)
                {
                    Vector3 diff = m_FABRIKPositions[i] - m_FABRIKPositions[i + 1];
                    Vector3 dir = diff.sqrMagnitude > 1e-8f ? diff.normalized : Vector3.up;
                    m_FABRIKPositions[i] = m_FABRIKPositions[i + 1] + dir * m_FABRIKLengths[i];
                }
                // Backward reach (pelvis → head)
                m_FABRIKPositions[0] = rootPos;
                for (int i = 1; i < total; i++)
                {
                    Vector3 diff = m_FABRIKPositions[i] - m_FABRIKPositions[i - 1];
                    Vector3 dir = diff.sqrMagnitude > 1e-8f ? diff.normalized : Vector3.up;
                    m_FABRIKPositions[i] = m_FABRIKPositions[i - 1] + dir * m_FABRIKLengths[i - 1];
                }

                // Cone constraint each joint: limit per-joint deviation from the PARENT
                // segment direction (rest-relative), not from world up. 旧実装は各セグメントを
                // world up 基準でクランプしたため、背骨ほぼ水平の屈み姿勢(マッサージ師が台に屈む)が
                // 毎フレーム垂直へ戻される実バグだった。親基準なら各関節が少しずつ曲がり、
                // 累積で背中を水平近くまで倒せる(根 i=0 のみ up を錨に残す)。
                for (int i = 0; i < total - 1; i++)
                {
                    Vector3 dir = m_FABRIKPositions[i + 1] - m_FABRIKPositions[i];
                    if (dir.sqrMagnitude < 1e-8f) continue;
                    Vector3 reference = Vector3.up;
                    if (i > 0)
                    {
                        Vector3 parent = m_FABRIKPositions[i] - m_FABRIKPositions[i - 1];
                        if (parent.sqrMagnitude > 1e-8f) reference = parent;
                    }
                    Vector3 clamped = ConeClamp(dir, reference, m_PerJointSwingMaxDeg);
                    m_FABRIKPositions[i + 1] = m_FABRIKPositions[i] + clamped * m_FABRIKLengths[i];
                }
            }

            // Derive bone rotations from the new position chain.
            for (int i = 0; i < spineCount; i++)
            {
                Vector3 desired = m_FABRIKPositions[i + 2] - m_FABRIKPositions[i + 1];
                if (desired.sqrMagnitude < 1e-8f) continue;
                Vector3 childWorldPos = i + 1 < spineCount ? m_SpineBones[i + 1].position : m_Head.position;
                Vector3 current = childWorldPos - m_SpineBones[i].position;
                if (current.sqrMagnitude < 1e-8f) continue;

                Quaternion correction = QuaternionMath.FromToRotationStable(current, desired);
                m_SpineBones[i].rotation = Quaternion.Slerp(
                    m_SpineBones[i].rotation,
                    correction * m_SpineBones[i].rotation,
                    m_Weight);
            }
        }

        /// <summary>
        /// 方向 dir を、参照方向 reference を中心とする半角 maxAngleDeg の円錐内へクランプする純関数。
        /// reference を rest(親セグメント)方向にすると、世界の垂直でなく「親に対する曲がり」を制限でき、
        /// 背骨が屈み姿勢で垂直へ戻されない。ヘッドレス特性化テスト可能。
        /// </summary>
        public static Vector3 ConeClamp(Vector3 dir, Vector3 reference, float maxAngleDeg)
        {
            if (dir.sqrMagnitude < 1e-8f || reference.sqrMagnitude < 1e-8f) return dir;
            dir = dir.normalized;
            reference = reference.normalized;
            float angle = Vector3.Angle(dir, reference);
            if (angle <= maxAngleDeg) return dir;
            return Vector3.Slerp(reference, dir, maxAngleDeg / angle).normalized;
        }

        /// <summary>
        /// Distribute yaw across spine bones so the torso follows the head direction.
        /// Uses both positional direction (pelvis→head) and head rotation to determine yaw.
        /// </summary>
        void DistributeSpineTwist(Vector3 headTargetPosition, Quaternion headTargetRotation)
        {
            if (m_Pelvis == null) return;

            // Use pelvis forward (after pelvis yaw rotation) as reference.
            // Spine twist only handles the residual yaw not absorbed by pelvis rotation.
            Vector3 referenceForward = m_Pelvis.forward;
            referenceForward.y = 0f;
            if (referenceForward.sqrMagnitude < 0.0001f) return;
            referenceForward.Normalize();

            // Yaw from position: direction from pelvis to head target in XZ
            float posYaw = 0f;
            Vector3 posDir = headTargetPosition - m_Pelvis.position;
            posDir.y = 0f;
            if (posDir.sqrMagnitude > 0.0001f)
                posYaw = Vector3.SignedAngle(referenceForward, posDir.normalized, Vector3.up);

            // Yaw from rotation: head facing direction in XZ
            float rotYaw = 0f;
            Vector3 headForward = headTargetRotation * Vector3.forward;
            headForward.y = 0f;
            if (headForward.sqrMagnitude > 0.0001f)
                rotYaw = Vector3.SignedAngle(referenceForward, headForward.normalized, Vector3.up);

            // Use whichever has larger magnitude
            float yawAngle = Mathf.Abs(rotYaw) > Mathf.Abs(posYaw) ? rotYaw : posYaw;
            if (Mathf.Abs(yawAngle) < 0.5f) return;

            // Distribute a fraction of the yaw across spine bones
            float distributedYaw = yawAngle * m_TwistWeight;

            // Sum bend weights for normalization
            float totalWeight = 0f;
            for (int i = 0; i < m_SpineBones.Length; i++)
            {
                if (m_SpineBones[i] != null)
                    totalWeight += m_BendWeights[i];
            }
            if (totalWeight <= 0f) return;

            for (int i = 0; i < m_SpineBones.Length; i++)
            {
                if (m_SpineBones[i] == null) continue;

                float boneRatio = m_BendWeights[i] / totalWeight;
                float boneYaw = distributedYaw * boneRatio;

                Quaternion twist = Quaternion.AngleAxis(boneYaw, Vector3.up);
                m_SpineBones[i].rotation = twist * m_SpineBones[i].rotation;
            }
        }

        public bool IsValid()
        {
            return m_Pelvis != null && m_Head != null
                && m_SpineBones != null && m_SpineBones.Length > 0;
        }

        public List<string> GetWarnings()
        {
            var warnings = new List<string>();
            if (m_Pelvis == null) warnings.Add("Spine: Pelvis is not assigned.");
            if (m_Head == null) warnings.Add("Spine: Head is not assigned.");
            if (m_SpineBones == null || m_SpineBones.Length == 0)
                warnings.Add("Spine: No spine bones assigned.");
            return warnings;
        }
    }
}
