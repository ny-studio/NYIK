using System;
using UnityEngine;
using NYIK.Core;
using NYIK.Solvers;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Humanoid arm IK solver. Wraps TwoBoneIKSolver and adds
    /// shoulder rotation and wrist rotation offset correction.
    /// </summary>
    [Serializable]
    public class ArmSolver
    {
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        // VRChat/VRIK 既定の肩回転は強め(1.0)。NYIK は 0.5 だったが、肘を曲げて寄る姿勢で
        // 肩を効かせるため 0.8 を新既定に（既存シーンのシリアライズ値は Inspector 側で要調整）。
        [SerializeField, Range(0f, 1f)] float m_ShoulderRotationWeight = 0.8f;
        [Tooltip("肩が寄与し始める reach 比率(下限)。reachRatio がこの値→1.0 にかけて肩が連続ランプイン。" +
                 "小さいほど早く効く。旧実装はこれを上端デッドゾーン幅として使い、手が腕長の ~90% を超えるまで" +
                 "肩を完全に殺していた（VRChat/VRIK は全 reach 域で連続回転）。")]
        [SerializeField] float m_ShoulderReachDistance = 0.2f;

        TwoBoneIKSolver m_IKSolver = new TwoBoneIKSolver();
        BoneTransform m_Shoulder;
        BoneTransform m_UpperArm;
        BoneTransform m_Forearm;
        BoneTransform m_Hand;

        Quaternion m_ShoulderInitialRotation;
        Quaternion m_HandInitialRotation;
        Quaternion m_HandRotationOffset = Quaternion.identity;
        bool m_HandCalibrated;
        float m_ArmLength;
        bool m_IsLeft;
        bool m_Initialized;
        Transform m_Root;
        Quaternion m_BodyRotation;
        bool m_HasBodyRotation;

        /// <summary>
        /// Arm solver weight.
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Shoulder rotation weight.
        /// </summary>
        public float ShoulderRotationWeight
        {
            get => m_ShoulderRotationWeight;
            set => m_ShoulderRotationWeight = Mathf.Clamp01(value);
        }

        public Vector3 TargetPosition
        {
            get => m_IKSolver.TargetPosition;
            set => m_IKSolver.TargetPosition = value;
        }

        public Quaternion TargetRotation
        {
            get => m_IKSolver.TargetRotation;
            set => m_IKSolver.TargetRotation = value;
        }

        public Vector3 BendGoalPosition
        {
            get => m_IKSolver.BendGoalPosition;
            set => m_IKSolver.BendGoalPosition = value;
        }

        public float BendGoalWeight
        {
            get => m_IKSolver.BendGoalWeight;
            set => m_IKSolver.BendGoalWeight = value;
        }

        public TwoBoneIKSolver IKSolver => m_IKSolver;

        /// <summary>
        /// Manually calculates the offset between controller rotation and hand bone rotation.
        /// Calling this sets the m_HandCalibrated flag to true, so the automatic calibration
        /// (auto-offset calculation on the first frame) in Solve() will be skipped.
        /// To revert to automatic calibration, call <see cref="ResetHandCalibration"/>.
        /// </summary>
        /// <param name="sourceRotation">Current world rotation of the controller.</param>
        public void CalibrateHandRotation(Quaternion sourceRotation)
        {
            m_HandRotationOffset = ComputeHandRotationOffset(sourceRotation, m_HandInitialRotation);
            m_HandCalibrated = true;
        }

        /// <summary>
        /// 「コントローラ向き ↔ 手ボーンのバインド向き」のオフセットを求める純関数。
        /// キャリブ瞬間のターゲット回転 <paramref name="targetRotation"/> と手ボーンのバインド
        /// ワールド回転 <paramref name="handBindRotation"/> から、適用時に手をバインドへ戻すオフセットを返す。
        /// ヘッドレス特性化テスト可能(回転は決定論的)。
        /// </summary>
        public static Quaternion ComputeHandRotationOffset(Quaternion targetRotation, Quaternion handBindRotation)
            => Quaternion.Inverse(targetRotation) * handBindRotation;

        /// <summary>
        /// <see cref="ComputeHandRotationOffset"/> で焼き込んだオフセットをターゲット回転へ適用する純関数。
        /// キャリブ瞬間は手がバインドへ、その後はコントローラの world delta と同じ delta だけ手が回る(1:1 追従)。
        /// </summary>
        public static Quaternion ApplyHandRotationOffset(Quaternion targetRotation, Quaternion offset)
            => targetRotation * offset;

        /// <summary>
        /// Resets hand rotation calibration.
        /// </summary>
        public void ResetHandCalibration()
        {
            m_HandCalibrated = false;
            m_HandRotationOffset = Quaternion.identity;
        }

        /// <summary>
        /// Current body rotation for elbow hint calculation.
        /// </summary>
        public Quaternion BodyRotation
        {
            set
            {
                m_BodyRotation = value;
                m_HasBodyRotation = true;
            }
        }

        public bool IsHandCalibrated => m_HandCalibrated;

        public void Setup(Transform shoulder, Transform upperArm, Transform forearm, Transform hand, bool isLeft)
        {
            m_IsLeft = isLeft;

            m_UpperArm = new BoneTransform(upperArm);
            m_Forearm = new BoneTransform(forearm);
            m_Hand = new BoneTransform(hand);

            if (shoulder != null)
                m_Shoulder = new BoneTransform(shoulder);
            else
                m_Shoulder = null;

            m_IKSolver.SetBones(m_UpperArm, m_Forearm, m_Hand);
        }

        public void Initialize(Transform root)
        {
            m_Root = root;
            m_IKSolver.Initialize(root);

            if (m_Shoulder != null && m_Shoulder.IsValid)
                m_ShoulderInitialRotation = m_Shoulder.Transform.localRotation;

            // Cache initial wrist world rotation (used for controller-to-wrist offset calculation)
            if (m_Hand.IsValid)
                m_HandInitialRotation = m_Hand.Transform.rotation;

            m_ArmLength = 0f;
            if (m_UpperArm.IsValid && m_Forearm.IsValid)
                m_ArmLength += Vector3.Distance(m_UpperArm.Position, m_Forearm.Position);
            if (m_Forearm.IsValid && m_Hand.IsValid)
                m_ArmLength += Vector3.Distance(m_Forearm.Position, m_Hand.Position);

            // Default bend goal (elbow bends backward and downward)
            m_IKSolver.BendGoalWeight = 1f;

            m_Initialized = true;
        }

        /// <summary>
        /// Frame delta forwarded to the inner TwoBoneIKSolver for time-aware
        /// smoothing (bend-normal filter). Settable so editor / test code can
        /// drive the solver deterministically.
        /// </summary>
        public float DeltaTime { get; set; } = 1f / 90f;

        public void Solve()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            m_IKSolver.Weight = m_Weight;

            // Auto-calculate controller-to-hand bone rotation offset on the first frame
            // it runs. NOTE: this captures whatever pose the controller is in right now,
            // so the *first* run should be deferred to a known/stable pose. NYIKHumanoid
            // re-arms this (ResetHandCalibration) once HMD tracking is live and again on
            // a deliberate "Calibrate Hands" so it isn't latched to a frame-1 garbage pose.
            if (!m_HandCalibrated)
            {
                m_HandRotationOffset = ComputeHandRotationOffset(m_IKSolver.TargetRotation, m_HandInitialRotation);
                m_HandCalibrated = true;
            }

            // Calculate rotation with offset applied in a local variable (original property unchanged)
            Quaternion originalTargetRotation = m_IKSolver.TargetRotation;
            Quaternion adjustedRotation = ApplyHandRotationOffset(originalTargetRotation, m_HandRotationOffset);

            // Update bend goal every frame (follows avatar orientation)
            UpdateBendGoal();

            // Shoulder rotation
            if (m_Shoulder != null && m_Shoulder.IsValid && m_ShoulderRotationWeight > 0f)
                SolveShoulder();

            // Set offset-applied rotation just before Solve, then restore immediately after
            m_IKSolver.TargetRotation = adjustedRotation;
            m_IKSolver.DeltaTime = DeltaTime;
            m_IKSolver.Solve();
            m_IKSolver.TargetRotation = originalTargetRotation;
        }

        /// <summary>
        /// Updates the bend goal every frame to match the avatar's orientation.
        /// Ensures the elbow always bends backward and downward.
        /// </summary>
        void UpdateBendGoal()
        {
            if (!m_UpperArm.IsValid || m_Root == null)
                return;

            Quaternion bodyRot = m_HasBodyRotation ? m_BodyRotation : m_Root.rotation;
            Vector3 bodyForward = bodyRot * Vector3.forward;
            Vector3 bodyRight = bodyRot * Vector3.right;

            // Elbow hint: body's backward direction + slightly downward
            Vector3 elbowHintDir = -bodyForward * 0.7f + Vector3.down * 0.3f;
            // Spread slightly outward for left/right
            elbowHintDir += (m_IsLeft ? -bodyRight : bodyRight) * 0.2f;

            m_IKSolver.BendGoalPosition = m_UpperArm.Position + elbowHintDir.normalized * 0.5f;
        }

        void SolveShoulder()
        {
            // Always reset to initial local rotation first to prevent drift
            m_Shoulder.Transform.localRotation = m_ShoulderInitialRotation;

            Vector3 shoulderPos = m_Shoulder.Position;
            Vector3 toTarget = m_IKSolver.TargetPosition - shoulderPos;
            float distToTarget = toTarget.magnitude;

            float reachRatio = m_ArmLength > 0f ? distToTarget / m_ArmLength : 0f;
            // VRChat/VRIK 整合: m_ShoulderReachDistance(下限 reach)→1.0 にかけて肩を連続ランプイン。
            // 旧式は reach=0.1 だと ~90% reach まで肩が無効で、肘を曲げて寄る姿勢で肩が死んでいた。
            // 純関数 ShoulderActivation に切り出してヘッドレステスト可能にした。
            float shoulderActivation = ShoulderActivation(reachRatio, m_ShoulderReachDistance);

            if (shoulderActivation <= 0f)
                return;

            float effectiveWeight = shoulderActivation * m_ShoulderRotationWeight * m_Weight;

            Vector3 shoulderToUpperArm = m_UpperArm.Position - shoulderPos;
            if (shoulderToUpperArm.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
            {
                Quaternion shoulderRot = Quaternion.FromToRotation(shoulderToUpperArm.normalized, toTarget.normalized);
                m_Shoulder.Transform.rotation = Quaternion.Slerp(
                    m_Shoulder.Transform.rotation,
                    shoulderRot * m_Shoulder.Transform.rotation,
                    effectiveWeight
                );
            }
        }

        /// <summary>
        /// 肩の連続アクティベーション（VRChat/VRIK 整合）。reachStart..1.0 にかけて 0→1 にランプ。
        /// reachStart は「肩が寄与し始める reach 比率(下限)」。旧実装の上端デッドゾーンと違い、
        /// 中間 reach（肘を曲げて寄る姿勢）でも肩が連続的に効く。純関数＝ヘッドレステスト可能。
        /// </summary>
        public static float ShoulderActivation(float reachRatio, float reachStart)
            => Mathf.Clamp01(Mathf.InverseLerp(reachStart, 1f, reachRatio));

        public bool IsValid()
        {
            return m_UpperArm != null && m_UpperArm.IsValid
                && m_Forearm != null && m_Forearm.IsValid
                && m_Hand != null && m_Hand.IsValid;
        }
    }
}
