using System;
using System.Collections.Generic;
using UnityEngine;
using NYIK.Core;

namespace NYIK.Solvers
{
    /// <summary>
    /// Analytical two-bone IK solver. Used for arms (upper arm -> forearm -> wrist)
    /// and legs (thigh -> shin -> ankle).
    /// Uses the same algorithm as Unity Animation Rigging, resolving all bone rotations in a single pass.
    /// </summary>
    [Serializable]
    public class TwoBoneIKSolver : IKSolverBase
    {
        BoneTransform m_Root;   // Upper arm or thigh
        BoneTransform m_Mid;    // Forearm or shin
        BoneTransform m_Tip;    // Wrist or ankle

        float m_UpperLength;
        float m_LowerLength;

        // Initial local rotations (cached for stable rotation restoration)
        Quaternion m_RootInitialLocalRot;
        Quaternion m_MidInitialLocalRot;
        Quaternion m_TipInitialLocalRot;
        // Initial local axes (bone directions)
        Vector3 m_RootLocalAxis;
        Vector3 m_MidLocalAxis;

        Vector3 m_TargetPosition;
        Quaternion m_TargetRotation = Quaternion.identity;
        float m_RotationWeight = 1f;

        Vector3 m_BendGoalPosition;
        float m_BendGoalWeight;

        // Previous frame bend normal for stabilization
        Vector3 m_PrevBendNormal;
        bool m_HasPrevBendNormal;

        public Vector3 TargetPosition
        {
            get => m_TargetPosition;
            set => m_TargetPosition = value;
        }

        public Quaternion TargetRotation
        {
            get => m_TargetRotation;
            set => m_TargetRotation = value;
        }

        public float RotationWeight
        {
            get => m_RotationWeight;
            set => m_RotationWeight = Mathf.Clamp01(value);
        }

        public Vector3 BendGoalPosition
        {
            get => m_BendGoalPosition;
            set => m_BendGoalPosition = value;
        }

        public float BendGoalWeight
        {
            get => m_BendGoalWeight;
            set => m_BendGoalWeight = Mathf.Clamp01(value);
        }

        public BoneTransform RootBone => m_Root;
        public BoneTransform MidBone => m_Mid;
        public BoneTransform TipBone => m_Tip;

        public void SetBones(BoneTransform root, BoneTransform mid, BoneTransform tip)
        {
            m_Root = root;
            m_Mid = mid;
            m_Tip = tip;
        }

        public override bool IsValid()
        {
            return m_Root != null && m_Root.IsValid
                && m_Mid != null && m_Mid.IsValid
                && m_Tip != null && m_Tip.IsValid;
        }

        public override List<string> GetWarnings()
        {
            var warnings = new List<string>();
            if (m_Root == null || !m_Root.IsValid) warnings.Add("Root bone is not assigned.");
            if (m_Mid == null || !m_Mid.IsValid) warnings.Add("Mid bone is not assigned.");
            if (m_Tip == null || !m_Tip.IsValid) warnings.Add("Tip bone is not assigned.");
            return warnings;
        }

        protected override void OnInitialize(Transform root)
        {
            if (!IsValid())
                return;

            m_Root.Initialize(m_Mid);
            m_Mid.Initialize(m_Tip);
            m_Tip.Initialize();

            m_UpperLength = m_Root.Length;
            m_LowerLength = m_Mid.Length;

            // Cache initial local rotations
            m_RootInitialLocalRot = m_Root.Transform.localRotation;
            m_MidInitialLocalRot = m_Mid.Transform.localRotation;
            m_TipInitialLocalRot = m_Tip.Transform.localRotation;

            // Bone local axes (parent-to-child direction expressed in local space)
            m_RootLocalAxis = Quaternion.Inverse(m_Root.Transform.rotation)
                * (m_Mid.Transform.position - m_Root.Transform.position).normalized;
            m_MidLocalAxis = Quaternion.Inverse(m_Mid.Transform.rotation)
                * (m_Tip.Transform.position - m_Mid.Transform.position).normalized;

            m_HasPrevBendNormal = false;
        }

        protected override void OnSolve()
        {
            var rootT = m_Root.Transform;
            var midT = m_Mid.Transform;
            var tipT = m_Tip.Transform;

            Vector3 rootPos = rootT.position;
            Vector3 midPos = midT.position;
            Vector3 tipPos = tipT.position;

            // Distance to target
            Vector3 toTarget = m_TargetPosition - rootPos;
            float targetDist = toTarget.magnitude;

            if (targetDist < 0.0001f)
                return;

            // Clamp to reachable range
            float maxReach = m_UpperLength + m_LowerLength;
            float clampedDist = Mathf.Clamp(targetDist, Mathf.Abs(m_UpperLength - m_LowerLength) * 1.001f, maxReach * 0.999f);
            Vector3 targetDir = toTarget / targetDist;

            // Analytically compute the world position of the mid joint (elbow/knee)
            Vector3 midGoal = CalculateMidPosition(rootPos, targetDir, clampedDist);

            // --- Root bone rotation ---
            // Align the current root->mid vector to the computed root->midGoal vector
            Vector3 currentRootToMid = midPos - rootPos;
            Vector3 desiredRootToMid = midGoal - rootPos;

            if (currentRootToMid.sqrMagnitude > 0.0001f && desiredRootToMid.sqrMagnitude > 0.0001f)
            {
                Quaternion rootCorrection = Quaternion.FromToRotation(currentRootToMid, desiredRootToMid);
                rootT.rotation = Quaternion.Slerp(rootT.rotation, rootCorrection * rootT.rotation, Weight);
            }

            // After rotating root, mid/tip world positions change -> re-fetch
            midPos = midT.position;
            tipPos = tipT.position;

            // --- Mid bone rotation ---
            // Align the current mid->tip vector to the mid->target direction
            Vector3 currentMidToTip = tipPos - midPos;
            Vector3 desiredMidToTip = m_TargetPosition - midPos;

            if (currentMidToTip.sqrMagnitude > 0.0001f && desiredMidToTip.sqrMagnitude > 0.0001f)
            {
                Quaternion midCorrection = Quaternion.FromToRotation(currentMidToTip, desiredMidToTip);
                midT.rotation = Quaternion.Slerp(midT.rotation, midCorrection * midT.rotation, Weight);
            }

            // --- Tip bone rotation (wrist/ankle) ---
            if (m_RotationWeight > 0f)
            {
                float effectiveWeight = m_RotationWeight * Weight;
                tipT.rotation = Quaternion.Slerp(tipT.rotation, m_TargetRotation, effectiveWeight);
            }
        }

        /// <summary>
        /// Computes the world position of the mid joint using the law of cosines and the bend plane.
        /// Controls the elbow/knee bend direction stably via the bend goal.
        /// </summary>
        Vector3 CalculateMidPosition(Vector3 rootPos, Vector3 targetDir, float targetDist)
        {
            // Law of cosines: projection distance from root to mid along the target axis
            float cosRootAngle = (targetDist * targetDist + m_UpperLength * m_UpperLength - m_LowerLength * m_LowerLength)
                / (2f * targetDist * m_UpperLength);
            cosRootAngle = Mathf.Clamp(cosRootAngle, -1f, 1f);
            float rootAngle = Mathf.Acos(cosRootAngle);

            // Along-axis and perpendicular components of the mid position
            float along = Mathf.Cos(rootAngle) * m_UpperLength;
            float perp = Mathf.Sin(rootAngle) * m_UpperLength;

            // Compute a stable bend plane normal
            Vector3 bendDir = GetStableBendDirection(rootPos, targetDir);

            return rootPos + targetDir * along + bendDir * perp;
        }

        /// <summary>
        /// Returns a stable bend direction (perpendicular to the target axis).
        /// Retains the previous frame's normal to prevent sudden flipping.
        /// </summary>
        Vector3 GetStableBendDirection(Vector3 rootPos, Vector3 targetDir)
        {
            Vector3 bendDir;

            if (m_BendGoalWeight > 0f)
            {
                // Compute the direction perpendicular to the target axis from the bend goal
                Vector3 toGoal = m_BendGoalPosition - rootPos;
                // Remove the projection onto the target axis
                bendDir = toGoal - Vector3.Dot(toGoal, targetDir) * targetDir;

                if (bendDir.sqrMagnitude < 0.0001f)
                {
                    // Bend goal lies on the target axis -> use previous frame or default
                    bendDir = GetFallbackBendDirection(targetDir);
                }
                else
                {
                    bendDir.Normalize();
                    // If bend goal weight is less than 1, blend with fallback
                    if (m_BendGoalWeight < 1f)
                    {
                        Vector3 fallback = GetFallbackBendDirection(targetDir);
                        bendDir = Vector3.Slerp(fallback, bendDir, m_BendGoalWeight).normalized;
                    }
                }
            }
            else
            {
                bendDir = GetFallbackBendDirection(targetDir);
            }

            // Maintain continuity with the previous frame (prevent sudden flipping)
            if (m_HasPrevBendNormal)
            {
                if (Vector3.Dot(bendDir, m_PrevBendNormal) < 0f)
                    bendDir = -bendDir; // Prevent inversion

                // Frame-rate independent smoothing: interpolate with previous frame to suppress jitter
                float alpha = 1f - Mathf.Exp(-Time.deltaTime * 20f);
                bendDir = Vector3.Slerp(m_PrevBendNormal, bendDir, alpha).normalized;
            }

            m_PrevBendNormal = bendDir;
            m_HasPrevBendNormal = true;

            return bendDir;
        }

        /// <summary>
        /// Fallback bend direction when the bend goal is absent or invalid.
        /// </summary>
        Vector3 GetFallbackBendDirection(Vector3 targetDir)
        {
            if (m_HasPrevBendNormal)
            {
                // Reuse the previous frame's direction (stability first)
                Vector3 reprojected = m_PrevBendNormal - Vector3.Dot(m_PrevBendNormal, targetDir) * targetDir;
                if (reprojected.sqrMagnitude > 0.0001f)
                    return reprojected.normalized;
            }

            // First frame: derive from the current mid joint position
            if (m_Mid != null && m_Mid.IsValid && m_Root != null && m_Root.IsValid)
            {
                Vector3 rootToMid = m_Mid.Transform.position - m_Root.Transform.position;
                Vector3 projected = rootToMid - Vector3.Dot(rootToMid, targetDir) * targetDir;
                if (projected.sqrMagnitude > 0.0001f)
                    return projected.normalized;
            }

            // Final fallback
            Vector3 fallback = Vector3.Cross(targetDir, Vector3.up);
            if (fallback.sqrMagnitude < 0.0001f)
                fallback = Vector3.Cross(targetDir, Vector3.forward);
            return fallback.normalized;
        }
    }
}
