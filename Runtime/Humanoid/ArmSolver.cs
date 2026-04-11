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
        [SerializeField, Range(0f, 1f)] float m_ShoulderRotationWeight = 0.5f;
        [SerializeField] float m_ShoulderReachDistance = 0.1f;

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
            m_HandRotationOffset = Quaternion.Inverse(sourceRotation) * m_HandInitialRotation;
            m_HandCalibrated = true;
        }

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

        public void Solve()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            m_IKSolver.Weight = m_Weight;

            // Auto-calculate controller-to-hand bone rotation offset on the first frame
            if (!m_HandCalibrated)
            {
                m_HandRotationOffset = Quaternion.Inverse(m_IKSolver.TargetRotation) * m_HandInitialRotation;
                m_HandCalibrated = true;
            }

            // Calculate rotation with offset applied in a local variable (original property unchanged)
            Quaternion originalTargetRotation = m_IKSolver.TargetRotation;
            Quaternion adjustedRotation = originalTargetRotation * m_HandRotationOffset;

            // Update bend goal every frame (follows avatar orientation)
            UpdateBendGoal();

            // Shoulder rotation
            if (m_Shoulder != null && m_Shoulder.IsValid && m_ShoulderRotationWeight > 0f)
                SolveShoulder();

            // Set offset-applied rotation just before Solve, then restore immediately after
            m_IKSolver.TargetRotation = adjustedRotation;
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
            float shoulderActivation = Mathf.Clamp01((reachRatio - (1f - m_ShoulderReachDistance)) / m_ShoulderReachDistance);

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

        public bool IsValid()
        {
            return m_UpperArm != null && m_UpperArm.IsValid
                && m_Forearm != null && m_Forearm.IsValid
                && m_Hand != null && m_Hand.IsValid;
        }
    }
}
