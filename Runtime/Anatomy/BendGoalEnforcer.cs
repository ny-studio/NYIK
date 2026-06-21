using UnityEngine;

namespace NYIK.Anatomy
{
    /// <summary>
    /// Enforces that a three-bone chain (root, mid, tip) bends toward a specified
    /// world-space hint direction. The mid joint of an IK chain is otherwise free
    /// to rotate around the root-to-tip axis without changing the tip position;
    /// this class rotates the root so that the mid lands on the chosen side.
    ///
    /// Designed for:
    /// <list type="bullet">
    ///   <item><description>Hip → Knee → Ankle (forward-facing knees)</description></item>
    ///   <item><description>Shoulder → Elbow → Hand (outward-facing elbows)</description></item>
    /// </list>
    /// </summary>
    public static class BendGoalEnforcer
    {
        /// <summary>
        /// Forces the knee to bend toward <paramref name="bendHintWorld"/> by rotating
        /// the hip around the hip-to-ankle axis. Tip (ankle) position is preserved.
        /// </summary>
        /// <param name="hip">Upper-leg root bone.</param>
        /// <param name="knee">Middle bone whose bend direction is constrained.</param>
        /// <param name="ankle">Lower-leg tip bone (anchor for the bend axis).</param>
        /// <param name="bendHintWorld">Desired bend direction in world space (e.g. character forward).</param>
        /// <param name="strength">Blend factor between identity and the full corrective rotation.</param>
        public static void EnforceKneeBend(
            Transform hip, Transform knee, Transform ankle,
            Vector3 bendHintWorld, float strength = 1f)
        {
            EnforceBend(hip, knee, ankle, bendHintWorld, strength, "knee");
        }

        /// <summary>
        /// Forces the elbow to bend toward <paramref name="bendHintWorld"/> by rotating
        /// the shoulder around the shoulder-to-hand axis. Tip (hand) position is preserved.
        /// </summary>
        /// <param name="shoulder">Upper-arm root bone.</param>
        /// <param name="elbow">Middle bone whose bend direction is constrained.</param>
        /// <param name="hand">Lower-arm tip bone (anchor for the bend axis).</param>
        /// <param name="bendHintWorld">Desired bend direction in world space (e.g. character backward for elbows).</param>
        /// <param name="strength">Blend factor between identity and the full corrective rotation.</param>
        public static void EnforceElbowBend(
            Transform shoulder, Transform elbow, Transform hand,
            Vector3 bendHintWorld, float strength = 1f)
        {
            EnforceBend(shoulder, elbow, hand, bendHintWorld, strength, "elbow");
        }

        /// <summary>
        /// Shared implementation for three-bone bend enforcement.
        /// Rotates the root so that the mid joint lies on the half-plane containing
        /// <paramref name="bendHintWorld"/>, while keeping the tip in place because the
        /// rotation is performed around the root→tip axis.
        /// </summary>
        private static void EnforceBend(
            Transform root, Transform mid, Transform tip,
            Vector3 bendHintWorld, float strength, string label)
        {
            if (root == null || mid == null || tip == null)
            {
                Debug.LogWarning(
                    $"[BendGoalEnforcer] Enforce{label}Bend called with null bone " +
                    $"(root={root}, mid={mid}, tip={tip}).");
                return;
            }

            if (strength <= 0f)
                return;

            // Axis from root to tip — rotation around this axis preserves the tip position.
            Vector3 axis = tip.position - root.position;
            float axisLenSqr = axis.sqrMagnitude;
            if (axisLenSqr < 1e-10f)
                return;
            axis /= Mathf.Sqrt(axisLenSqr);

            // Current bend direction: mid offset projected onto the plane perpendicular to axis.
            Vector3 midOffset = mid.position - root.position;
            Vector3 currentBendDir = Vector3.ProjectOnPlane(midOffset, axis);
            if (currentBendDir.sqrMagnitude < 1e-10f)
                return;
            currentBendDir.Normalize();

            // Target bend direction: hint projected onto the same plane.
            Vector3 targetBendDir = Vector3.ProjectOnPlane(bendHintWorld, axis);
            if (targetBendDir.sqrMagnitude < 1e-10f)
                return;
            targetBendDir.Normalize();

            // Compute the rotation around axis that aligns currentBendDir with targetBendDir.
            // Signed angle ensures we rotate the shorter way around the chosen axis.
            float signedAngle = Vector3.SignedAngle(currentBendDir, targetBendDir, axis);
            if (Mathf.Abs(signedAngle) < 1e-4f)
                return;

            Quaternion fullRotation = Quaternion.AngleAxis(signedAngle, axis);
            Quaternion appliedRotation = strength >= 1f
                ? fullRotation
                : Quaternion.Slerp(Quaternion.identity, fullRotation, strength);

            // Apply in world space: rotate the root about itself by appliedRotation.
            // Because the rotation is around the root→tip axis and pivot = root.position,
            // the tip world position is preserved.
            root.rotation = appliedRotation * root.rotation;
        }
    }
}
