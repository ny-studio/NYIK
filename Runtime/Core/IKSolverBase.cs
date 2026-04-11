using System;
using System.Collections.Generic;
using UnityEngine;

namespace NYIK.Core
{
    /// <summary>
    /// Abstract base class for all IK solvers.
    /// </summary>
    [Serializable]
    public abstract class IKSolverBase
    {
        [SerializeField, Range(0f, 1f)] float m_Weight = 1f;
        [SerializeField] int m_MaxIterations = 10;
        [SerializeField] float m_Tolerance = 0.001f;

        bool m_Initialized;

        /// <summary>
        /// Solver weight (0 = disabled, 1 = fully applied).
        /// </summary>
        public float Weight
        {
            get => m_Weight;
            set => m_Weight = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Maximum number of iterations for the iterative solver.
        /// </summary>
        public int MaxIterations
        {
            get => m_MaxIterations;
            set => m_MaxIterations = Mathf.Max(1, value);
        }

        /// <summary>
        /// Convergence tolerance (in meters).
        /// </summary>
        public float Tolerance
        {
            get => m_Tolerance;
            set => m_Tolerance = Mathf.Max(0f, value);
        }

        public bool IsInitialized => m_Initialized;

        /// <summary>
        /// Initializes the solver. Caches bone lengths and other data.
        /// </summary>
        public void Initialize(Transform root)
        {
            OnInitialize(root);
            m_Initialized = IsValid();
        }

        /// <summary>
        /// Solves IK. Skipped when Weight is 0.
        /// </summary>
        public void Solve()
        {
            if (!m_Initialized || m_Weight <= 0f)
                return;

            OnSolve();
        }

        /// <summary>
        /// Validates whether the setup is valid.
        /// </summary>
        public abstract bool IsValid();

        /// <summary>
        /// Returns a list of warning messages.
        /// </summary>
        public virtual List<string> GetWarnings()
        {
            return new List<string>();
        }

        protected abstract void OnInitialize(Transform root);
        protected abstract void OnSolve();
    }
}
