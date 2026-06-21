using System;
using System.Collections.Generic;
using UnityEngine;
using NYIK.Core;

namespace NYIK.Solvers
{
    /// <summary>
    /// FABRIK (Forward And Backward Reaching IK) solver.
    /// A position-based iterative solver that supports variable-length bone chains.
    /// Ideal for multi-joint chains such as spines and tails.
    /// Iterates forward/backward reaching passes to find positions satisfying
    /// inter-bone distance constraints.
    /// </summary>
    [Serializable]
    public class FABRIKSolver : IKSolverBase
    {
        IKChain m_Chain;

        Vector3 m_TargetPosition;
        Quaternion m_TargetRotation = Quaternion.identity;
        float m_RotationWeight = 1f;

        float[] m_BoneLengths;
        Vector3[] m_Positions;
        float[] m_BoneWeights;
        Vector3[] m_InitialBoneDirs;

        // Per-joint cone constraint (Aristidou & Lasenby 2017). When non-null,
        // each iteration projects the (parent→child) direction into a swing
        // cone around the joint's rest direction. Index i constrains the
        // direction from bone[i] to bone[i+1] — i.e. limits bone[i+1]'s
        // deviation from rest pose.
        float[] m_PerJointSwingMaxDeg;
        Vector3[] m_RestBoneDirsWorld;

        /// <summary>
        /// When true, applies a per-joint swing-cone constraint each iteration
        /// (Constrained FABRIK, Aristidou & Lasenby 2017). Requires
        /// <see cref="SetSwingConstraints"/> to be called with per-joint
        /// max swing angles (degrees from rest). Default: false (matches
        /// classic FABRIK).
        /// </summary>
        public bool ApplyConstraints = false;

        /// <summary>
        /// IK target position (the goal the end of the chain should reach).
        /// </summary>
        public Vector3 TargetPosition
        {
            get => m_TargetPosition;
            set => m_TargetPosition = value;
        }

        /// <summary>
        /// IK target rotation.
        /// </summary>
        public Quaternion TargetRotation
        {
            get => m_TargetRotation;
            set => m_TargetRotation = value;
        }

        /// <summary>
        /// Weight for applying the target rotation.
        /// </summary>
        public float RotationWeight
        {
            get => m_RotationWeight;
            set => m_RotationWeight = Mathf.Clamp01(value);
        }

        public IKChain Chain => m_Chain;

        public void SetChain(IKChain chain)
        {
            m_Chain = chain;
        }

        /// <summary>
        /// Sets per-bone weight array. Used to suppress excessive movement of
        /// short bones (e.g. cervical vertebrae). Array length must match the
        /// number of bones in the chain.
        /// </summary>
        public void SetBoneWeights(float[] weights)
        {
            m_BoneWeights = weights;
        }

        /// <summary>
        /// Configure per-joint swing cone limits (in degrees). Index i
        /// constrains the direction from chain bone i to bone i+1. Pass null
        /// to disable. Array length must be <see cref="IKChain.BoneCount"/> - 1.
        /// </summary>
        public void SetSwingConstraints(float[] perJointSwingMaxDeg)
        {
            m_PerJointSwingMaxDeg = perJointSwingMaxDeg;
        }

        public override bool IsValid()
        {
            return m_Chain != null && m_Chain.IsValid();
        }

        public override List<string> GetWarnings()
        {
            var warnings = new List<string>();
            if (m_Chain == null) warnings.Add("Chain is not assigned.");
            else if (!m_Chain.IsValid()) warnings.Add("Chain contains invalid bone references.");
            return warnings;
        }

        protected override void OnInitialize(Transform root)
        {
            if (!IsValid())
                return;

            m_Chain.Initialize();

            int count = m_Chain.BoneCount;
            m_BoneLengths = new float[count];
            m_Positions = new Vector3[count];

            for (int i = 0; i < count; i++)
                m_BoneLengths[i] = m_Chain.Bones[i].Length;

            // Default bone weights (uniform)
            if (m_BoneWeights == null || m_BoneWeights.Length != count)
            {
                m_BoneWeights = new float[count];
                for (int i = 0; i < count; i++)
                    m_BoneWeights[i] = 1f;
            }

            // Cache initial inter-bone directions in local space
            var bones = m_Chain.Bones;
            int dirCount = count - 1;
            m_InitialBoneDirs = new Vector3[dirCount];
            m_RestBoneDirsWorld = new Vector3[dirCount];
            for (int i = 0; i < dirCount; i++)
            {
                Vector3 worldDir = (bones[i + 1].Position - bones[i].Position).normalized;
                m_InitialBoneDirs[i] = Quaternion.Inverse(bones[i].Rotation) * worldDir;
                m_RestBoneDirsWorld[i] = worldDir;
            }
        }

        /// <summary>
        /// Constrained FABRIK (Aristidou & Lasenby 2017): after each forward
        /// or backward pass, project each (parent → child) direction into a
        /// swing cone around the rest direction. Cheap, deterministic, and
        /// converges in fewer iterations than post-hoc ROM clamping.
        /// </summary>
        void ApplySwingConstraints(int count)
        {
            if (!ApplyConstraints || m_PerJointSwingMaxDeg == null || m_RestBoneDirsWorld == null) return;
            int n = Mathf.Min(m_PerJointSwingMaxDeg.Length, count - 1);
            for (int i = 0; i < n; i++)
            {
                float maxDeg = m_PerJointSwingMaxDeg[i];
                if (maxDeg <= 0f) continue;
                Vector3 from = m_Positions[i + 1] - m_Positions[i];
                float lenSqr = from.sqrMagnitude;
                if (lenSqr < 1e-8f) continue;
                Vector3 currentDir = from / Mathf.Sqrt(lenSqr);
                Vector3 rest = m_RestBoneDirsWorld[i];
                float angle = Vector3.Angle(currentDir, rest);
                if (angle <= maxDeg) continue;
                // Project current direction onto the cone surface around rest
                float t = maxDeg / angle;
                Vector3 constrained = Vector3.Slerp(rest, currentDir, t).normalized;
                m_Positions[i + 1] = m_Positions[i] + constrained * m_BoneLengths[i];
            }
        }

        /// <summary>
        /// FABRIK 前方リーチの純関数（end effector → root）。`positions[last]` を target に固定し、
        /// 各ボーン長 `boneLengths[i] = |pos[i]→pos[i+1]|` を保ったまま内側へ手繰る。退化（点が一致）時は
        /// Vector3.up にフォールバックして NaN を出さない。`positions` を in-place 更新＝ヘッドレステスト可能。
        /// </summary>
        public static void ForwardReach(Vector3[] positions, float[] boneLengths, Vector3 target)
        {
            int count = positions.Length;
            positions[count - 1] = target;
            for (int i = count - 2; i >= 0; i--)
            {
                Vector3 diff = positions[i] - positions[i + 1];
                Vector3 dir = diff.sqrMagnitude > 0f ? diff.normalized : Vector3.up;
                positions[i] = positions[i + 1] + dir * boneLengths[i];
            }
        }

        /// <summary>
        /// FABRIK 後方リーチの純関数（root → end effector）。`positions[0]` を rootPos に固定し、
        /// 各ボーン長を保ったまま外側へ手繰る。退化時は Vector3.up フォールバック。in-place 更新。
        /// </summary>
        public static void BackwardReach(Vector3[] positions, float[] boneLengths, Vector3 rootPos)
        {
            positions[0] = rootPos;
            for (int i = 1; i < positions.Length; i++)
            {
                Vector3 diff = positions[i] - positions[i - 1];
                Vector3 dir = diff.sqrMagnitude > 0f ? diff.normalized : Vector3.up;
                positions[i] = positions[i - 1] + dir * boneLengths[i - 1];
            }
        }

        protected override void OnSolve()
        {
            int count = m_Chain.BoneCount;
            var bones = m_Chain.Bones;

            // Copy current bone positions
            for (int i = 0; i < count; i++)
                m_Positions[i] = bones[i].Position;

            Vector3 rootPosition = m_Positions[0];

            // If the target is beyond reachable distance, stretch bones in a straight line
            float totalLength = m_Chain.TotalLength;
            float distToTarget = Vector3.Distance(rootPosition, m_TargetPosition);

            if (distToTarget > totalLength)
            {
                // Unreachable: align all bones in a straight line toward the target
                Vector3 diff = m_TargetPosition - rootPosition;
                Vector3 direction = diff.sqrMagnitude > 0f ? diff.normalized : Vector3.up;
                for (int i = 1; i < count; i++)
                    m_Positions[i] = m_Positions[i - 1] + direction * m_BoneLengths[i - 1];
            }
            else
            {
                // FABRIK iteration
                for (int iteration = 0; iteration < MaxIterations; iteration++)
                {
                    // Convergence check
                    float endEffectorError = Vector3.Distance(m_Positions[count - 1], m_TargetPosition);
                    if (endEffectorError <= Tolerance)
                        break;

                    // Forward reaching (end effector → root) then backward (root → end effector)
                    ForwardReach(m_Positions, m_BoneLengths, m_TargetPosition);
                    BackwardReach(m_Positions, m_BoneLengths, rootPosition);

                    // Constraint pass — projects any joint outside its swing
                    // cone back to the cone surface. Allows the next iteration
                    // to re-converge to the (possibly approximate) target.
                    ApplySwingConstraints(count);
                }
            }

            // Apply bone weights and solver weight to update positions
            for (int i = 0; i < count; i++)
            {
                float boneWeight = m_BoneWeights[i] * Weight;
                Vector3 targetPos = m_Positions[i];
                bones[i].Position = Vector3.Lerp(bones[i].Position, targetPos, boneWeight);
            }

            // Derive bone rotations from FABRIK-computed positions.
            // Using m_Positions[] avoids child position drift caused by parent rotation changes.
            for (int i = 0; i < count - 1; i++)
            {
                Vector3 desiredDir = m_Positions[i + 1] - m_Positions[i];
                if (desiredDir.sqrMagnitude < 0.0001f)
                    continue;

                // Transform the cached local direction to world space using the current rotation
                Vector3 currentBoneDir = bones[i].Rotation * m_InitialBoneDirs[i];

                Quaternion correction = QuaternionMath.FromToRotationStable(currentBoneDir, desiredDir.normalized);
                float boneWeight = m_BoneWeights[i] * Weight;
                bones[i].Rotation = Quaternion.Slerp(
                    bones[i].Rotation,
                    correction * bones[i].Rotation,
                    boneWeight
                );
            }

            // Apply target rotation to the end effector bone
            if (m_RotationWeight > 0f)
            {
                var lastBone = bones[count - 1];
                lastBone.Rotation = Quaternion.Slerp(
                    lastBone.Rotation,
                    m_TargetRotation,
                    m_RotationWeight * Weight
                );
            }
        }
    }
}
