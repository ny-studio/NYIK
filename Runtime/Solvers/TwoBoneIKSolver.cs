using System;
using System.Collections.Generic;
using UnityEngine;
using NYIK.Core;

namespace NYIK.Solvers
{
    /// <summary>
    /// Analytical two-bone IK solver. Used for arms (upper arm → forearm → wrist)
    /// and legs (thigh → shin → ankle).
    ///
    /// Algorithm:
    ///   1. Snapshot original local rotations.
    ///   2. Compute mid joint world position via Law of Cosines + stable bend
    ///      direction (BendGoal or previous-frame normal).
    ///   3. Apply full-strength rotations to root + mid + tip in sequence.
    ///   4. Restore tip twist (rotation around hand-forward axis) so wrist
    ///      orientation is preserved across solves — without this, the user's
    ///      controller twist gets erased by the IK.
    ///   5. Slerp from original rotations to solved rotations with Weight.
    ///      This avoids the order-dependent partial-strength artefact of
    ///      slerping each bone in sequence.
    ///   6. Use numerically stable Quaternion.FromToRotation so near-180°
    ///      target alignments don't flip-flop.
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

        /// <summary>
        /// When true, the tip's local twist (rotation around the bone's long
        /// axis) is captured before the solve and restored afterwards. This
        /// preserves the user's wrist twist (e.g. from a VR controller's
        /// rotation) which a naive IK would overwrite. Default: true.
        /// </summary>
        public bool PreserveTipTwist = true;

        /// <summary>
        /// Frame delta supplied by the caller for time-dependent smoothing
        /// (bend-normal filter). Defaults to 1/90 s for Edit-mode safety.
        /// Set from <see cref="UnityEngine.Time.deltaTime"/> in Play mode.
        /// </summary>
        public float DeltaTime = 1f / 90f;

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

            m_RootInitialLocalRot = m_Root.Transform.localRotation;
            m_MidInitialLocalRot = m_Mid.Transform.localRotation;
            m_TipInitialLocalRot = m_Tip.Transform.localRotation;

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

            // Snapshot original rotations so we can Slerp back at end with Weight.
            Quaternion originalRoot = rootT.rotation;
            Quaternion originalMid = midT.rotation;
            Quaternion originalTip = tipT.rotation;

            // Capture tip twist around the current mid→tip axis BEFORE we move bones.
            Vector3 origTipAxis = (tipT.position - midT.position);
            Quaternion savedTipTwist = Quaternion.identity;
            if (PreserveTipTwist && origTipAxis.sqrMagnitude > 1e-8f)
            {
                SwingTwistDecomposition.Decompose(originalTip, origTipAxis.normalized,
                    out _, out savedTipTwist);
            }

            // Distance to target
            Vector3 toTarget = m_TargetPosition - rootT.position;
            float targetDist = toTarget.magnitude;
            if (targetDist < 1e-4f) return;

            float maxReach = m_UpperLength + m_LowerLength;
            float clampedDist = Mathf.Clamp(targetDist,
                Mathf.Abs(m_UpperLength - m_LowerLength) * 1.001f,
                maxReach * 0.999f);
            Vector3 targetDir = toTarget / targetDist;

            // Analytical mid position from Law of Cosines + bend plane.
            Vector3 midGoal = CalculateMidPosition(rootT.position, targetDir, clampedDist);

            // ---- Apply FULL strength rotations (Slerp back to original at end) ----

            // Root: align root→mid to root→midGoal
            Vector3 currentRootToMid = midT.position - rootT.position;
            Vector3 desiredRootToMid = midGoal - rootT.position;
            if (currentRootToMid.sqrMagnitude > 1e-8f && desiredRootToMid.sqrMagnitude > 1e-8f)
            {
                Quaternion rootCorrection = QuaternionMath.FromToRotationStable(
                    currentRootToMid, desiredRootToMid);
                rootT.rotation = rootCorrection * rootT.rotation;
            }

            // After root rotates, mid/tip moved — re-fetch positions.
            Vector3 midPosAfterRoot = midT.position;
            Vector3 tipPosAfterRoot = tipT.position;

            // Mid: align mid→tip to mid→target
            Vector3 currentMidToTip = tipPosAfterRoot - midPosAfterRoot;
            Vector3 desiredMidToTip = m_TargetPosition - midPosAfterRoot;
            if (currentMidToTip.sqrMagnitude > 1e-8f && desiredMidToTip.sqrMagnitude > 1e-8f)
            {
                Quaternion midCorrection = QuaternionMath.FromToRotationStable(
                    currentMidToTip, desiredMidToTip);
                midT.rotation = midCorrection * midT.rotation;
            }

            // Tip: target rotation (only if rotation weight > 0)
            Quaternion solvedTip = tipT.rotation;
            if (m_RotationWeight > 0f)
            {
                solvedTip = QuaternionMath.SafeSlerp(tipT.rotation, m_TargetRotation, m_RotationWeight);
                tipT.rotation = solvedTip;
            }

            // Restore tip twist around the new mid→tip axis. The user's
            // controller twist is preserved; only swing is replaced by IK.
            if (PreserveTipTwist && origTipAxis.sqrMagnitude > 1e-8f)
            {
                Vector3 newTipAxis = (tipT.position - midT.position);
                if (newTipAxis.sqrMagnitude > 1e-8f)
                {
                    newTipAxis.Normalize();
                    SwingTwistDecomposition.Decompose(tipT.rotation, newTipAxis,
                        out var swing, out _);
                    // Build axis-aligned saved twist (re-expressed on the new axis).
                    // Since the twist is a rotation around the bone axis, "saved"
                    // and "new" axes differ only slightly; we project savedTipTwist's
                    // angle onto the new axis.
                    savedTipTwist.ToAngleAxis(out float twistAngle, out Vector3 twistAxis);
                    if (twistAxis.sqrMagnitude > 1e-6f &&
                        Vector3.Dot(twistAxis, origTipAxis.normalized) < 0f)
                    {
                        twistAngle = -twistAngle;
                    }
                    if (twistAngle > 180f) twistAngle -= 360f;
                    var reExpressedTwist = Quaternion.AngleAxis(twistAngle, newTipAxis);
                    tipT.rotation = swing * reExpressedTwist;
                }
            }

            // ---- Slerp back to original with Weight (unified blend) ----
            if (Weight < 1f - 1e-4f)
            {
                rootT.rotation = QuaternionMath.SafeSlerp(originalRoot, rootT.rotation, Weight);
                midT.rotation = QuaternionMath.SafeSlerp(originalMid, midT.rotation, Weight);
                tipT.rotation = QuaternionMath.SafeSlerp(originalTip, tipT.rotation, Weight);
            }
        }

        /// <summary>
        /// Computes the world position of the mid joint using the law of cosines and the bend plane.
        /// </summary>
        Vector3 CalculateMidPosition(Vector3 rootPos, Vector3 targetDir, float targetDist)
        {
            Vector3 bendDir = GetStableBendDirection(rootPos, targetDir);
            return SolveMidPosition(rootPos, targetDir, targetDist, m_UpperLength, m_LowerLength, bendDir);
        }

        /// <summary>
        /// 二骨IKの中間関節ワールド位置を余弦定理で解く純関数。
        /// root-mid=upperLen / mid-target=lowerLen / root-target=targetDist の三角形を、
        /// targetDir 軸（root→target 単位ベクトル）と bendDir（targetDir に直交する曲げ方向）が
        /// 張る平面上に構成する。正しさの不変条件:
        ///   ・|結果 - rootPos| == upperLen（上骨長を厳密に保存）
        ///   ・|結果 - (rootPos + targetDir*targetDist)| == lowerLen（下骨が target に届く）
        /// targetDist は [|upper-lower|, upper+lower] 内・>0、bendDir は単位かつ targetDir に直交、を呼び側が保証。
        /// 純関数＝ヘッドレス特性化テスト可能。
        /// </summary>
        public static Vector3 SolveMidPosition(Vector3 rootPos, Vector3 targetDir, float targetDist,
                                               float upperLen, float lowerLen, Vector3 bendDir)
        {
            float cosRootAngle = (targetDist * targetDist + upperLen * upperLen - lowerLen * lowerLen)
                / (2f * targetDist * upperLen);
            cosRootAngle = Mathf.Clamp(cosRootAngle, -1f, 1f);
            float rootAngle = Mathf.Acos(cosRootAngle);

            float along = Mathf.Cos(rootAngle) * upperLen;
            float perp = Mathf.Sin(rootAngle) * upperLen;

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
                Vector3 toGoal = m_BendGoalPosition - rootPos;
                bendDir = toGoal - Vector3.Dot(toGoal, targetDir) * targetDir;

                if (bendDir.sqrMagnitude < 1e-8f)
                {
                    bendDir = GetFallbackBendDirection(targetDir);
                }
                else
                {
                    bendDir.Normalize();
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

            if (m_HasPrevBendNormal)
            {
                if (Vector3.Dot(bendDir, m_PrevBendNormal) < 0f)
                    bendDir = -bendDir;

                // Frame-rate independent smoothing via caller-supplied dt.
                float alpha = 1f - Mathf.Exp(-DeltaTime * 20f);
                bendDir = Vector3.Slerp(m_PrevBendNormal, bendDir, alpha).normalized;
            }

            m_PrevBendNormal = bendDir;
            m_HasPrevBendNormal = true;
            return bendDir;
        }

        Vector3 GetFallbackBendDirection(Vector3 targetDir)
        {
            if (m_HasPrevBendNormal)
            {
                Vector3 reprojected = m_PrevBendNormal - Vector3.Dot(m_PrevBendNormal, targetDir) * targetDir;
                if (reprojected.sqrMagnitude > 1e-8f)
                    return reprojected.normalized;
            }

            if (m_Mid != null && m_Mid.IsValid && m_Root != null && m_Root.IsValid)
            {
                Vector3 rootToMid = m_Mid.Transform.position - m_Root.Transform.position;
                Vector3 projected = rootToMid - Vector3.Dot(rootToMid, targetDir) * targetDir;
                if (projected.sqrMagnitude > 1e-8f)
                    return projected.normalized;
            }

            // Final fallback: stable perpendicular
            return QuaternionMath.StablePerpendicular(targetDir);
        }
    }
}
