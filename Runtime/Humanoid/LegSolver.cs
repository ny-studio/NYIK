using System;
using UnityEngine;
using NYIK.Core;
using NYIK.Solvers;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Humanoid leg IK solver. Wraps TwoBoneIKSolver and automatically
    /// calculates foot targets from the pelvis position.
    /// Before locomotion is implemented, feet are fixed directly below the pelvis.
    /// </summary>
    [Serializable]
    public class LegSolver
    {
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        [SerializeField] float m_FootGroundOffset = 0.02f;

        TwoBoneIKSolver m_IKSolver = new TwoBoneIKSolver();
        BoneTransform m_Thigh;
        BoneTransform m_Calf;
        BoneTransform m_Foot;

        float m_LegLength;
        Vector3 m_InitialFootLocalPosition;
        Vector3 m_InitialHipToFootOffset; // Thigh (hip joint) to foot offset (in root-local space)
        bool m_IsLeft;
        bool m_Initialized;
        Transform m_Root;
        Quaternion m_BodyRotation;
        bool m_HasBodyRotation;

        Vector3 m_FootTargetPosition;
        bool m_UseCustomTarget;

        /// <summary>
        /// Leg solver weight.
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Directly sets the foot target position (for locomotion).
        /// </summary>
        public Vector3 FootTargetPosition
        {
            get => m_FootTargetPosition;
            set
            {
                m_FootTargetPosition = value;
                m_UseCustomTarget = true;
            }
        }

        /// <summary>
        /// Disables the custom target and reverts to automatic calculation.
        /// </summary>
        public void ClearCustomTarget()
        {
            m_UseCustomTarget = false;
        }

        /// <summary>
        /// Current body (pelvis) rotation for foot offset and knee direction.
        /// Set each frame from SpineSolver.PelvisYawRotation before calling Solve().
        /// </summary>
        public Quaternion BodyRotation
        {
            set
            {
                m_BodyRotation = value;
                m_HasBodyRotation = true;
            }
        }

        public TwoBoneIKSolver IKSolver => m_IKSolver;

        /// <summary>
        /// Sets up the leg solver.
        /// </summary>
        public void Setup(Transform thigh, Transform calf, Transform foot, bool isLeft)
        {
            m_IsLeft = isLeft;
            m_Thigh = new BoneTransform(thigh);
            m_Calf = new BoneTransform(calf);
            m_Foot = new BoneTransform(foot);

            m_IKSolver.SetBones(m_Thigh, m_Calf, m_Foot);
        }

        /// <summary>
        /// Initializes the solver.
        /// </summary>
        public void Initialize(Transform root)
        {
            m_Root = root;
            m_IKSolver.Initialize(root);

            m_LegLength = 0f;
            if (m_Thigh.IsValid && m_Calf.IsValid)
                m_LegLength += Vector3.Distance(m_Thigh.Position, m_Calf.Position);
            if (m_Calf.IsValid && m_Foot.IsValid)
                m_LegLength += Vector3.Distance(m_Calf.Position, m_Foot.Position);

            if (m_Foot.IsValid)
            {
                m_InitialFootLocalPosition = m_Foot.Transform.localPosition;
            }

            // Store thigh (hip joint) to foot offset in root-local space.
            // Since the thigh follows when SpineSolver moves the pelvis,
            // using this as a reference ensures feet follow during joystick movement.
            if (m_Thigh.IsValid && m_Foot.IsValid)
            {
                Vector3 hipToFoot = m_Foot.Position - m_Thigh.Position;
                m_InitialHipToFootOffset = Quaternion.Inverse(root.rotation) * hipToFoot;
            }

            // Knee bend goal (forward, with strong weight)
            m_IKSolver.BendGoalWeight = 1f;

            m_Initialized = true;
        }

        /// <summary>
        /// Solves IK.
        /// </summary>
        /// <param name="pelvisPosition">Current pelvis position (received from SpineSolver).</param>
        public void Solve(Vector3 pelvisPosition)
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            Quaternion bodyRot = m_HasBodyRotation ? m_BodyRotation : m_Root.rotation;

            Vector3 footTarget;

            if (m_UseCustomTarget)
            {
                footTarget = m_FootTargetPosition;
            }
            else
            {
                // Default: place foot relative to the current thigh (hip joint) position.
                // Since the thigh is a child of the pelvis, it auto-follows when SpineSolver moves the pelvis.
                Vector3 hipOffset = bodyRot * m_InitialHipToFootOffset;
                footTarget = m_Thigh.Position + hipOffset;
                // Foot Y is at ground level (pelvis Y - leg length + offset)
                footTarget.y = pelvisPosition.y - m_LegLength + m_FootGroundOffset;
            }

            // Update knee bend goal every frame (body's forward direction)
            if (m_Thigh.IsValid && m_Root != null)
                m_IKSolver.BendGoalPosition = m_Thigh.Position + (bodyRot * Vector3.forward) * 0.5f;

            m_IKSolver.TargetPosition = footTarget;
            m_IKSolver.TargetRotation = m_Foot.Rotation;
            m_IKSolver.Weight = m_Weight;
            m_IKSolver.Solve();
        }

        public bool IsValid()
        {
            return m_Thigh != null && m_Thigh.IsValid
                && m_Calf != null && m_Calf.IsValid
                && m_Foot != null && m_Foot.IsValid;
        }
    }
}
