using System;
using UnityEngine;

namespace NYIK.Constraints
{
    /// <summary>
    /// Bend goal (pole target). Specifies the bend direction for elbows and knees.
    /// Manages the value passed to TwoBoneIKSolver's BendGoalPosition.
    /// </summary>
    [Serializable]
    public class BendGoal
    {
        [SerializeField] Transform m_Target;
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        [SerializeField] Vector3 m_DefaultDirection = -Vector3.forward;

        /// <summary>
        /// The bend goal's target Transform (a scene object).
        /// </summary>
        public Transform Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        /// <summary>
        /// Influence weight of the bend goal.
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Default bend direction used when no target is assigned.
        /// </summary>
        public Vector3 DefaultDirection
        {
            get => m_DefaultDirection;
            set => m_DefaultDirection = value.normalized;
        }

        /// <summary>
        /// Gets the bend goal's world position.
        /// </summary>
        /// <param name="jointPosition">Reference position of the joint (upper arm or thigh).</param>
        /// <param name="rootTransform">The avatar's root Transform.</param>
        public Vector3 GetPosition(Vector3 jointPosition, Transform rootTransform)
        {
            if (m_Target != null)
                return m_Target.position;

            // Convert the default direction to root Transform orientation
            Vector3 worldDir = rootTransform.TransformDirection(m_DefaultDirection);
            return jointPosition + worldDir;
        }
    }
}
