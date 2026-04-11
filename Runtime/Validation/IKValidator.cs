using System.Collections.Generic;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Validation
{
    /// <summary>
    /// Validates the NYIK setup state and issues warnings when problems are found.
    /// Detects null references, invalid bone hierarchies, and incorrect parameters.
    /// </summary>
    public static class IKValidator
    {
        /// <summary>
        /// Validates the NYIKHumanoid component and returns a list of warning messages.
        /// </summary>
        public static List<string> Validate(NYIKHumanoid humanoid)
        {
            var warnings = new List<string>();

            if (humanoid == null)
            {
                warnings.Add("NYIKHumanoid component is null.");
                return warnings;
            }

            // Validate bone references
            var refs = humanoid.References;
            if (refs == null)
            {
                warnings.Add("References are null.");
                return warnings;
            }

            warnings.AddRange(refs.GetWarnings());

            // Check Animator
            var animator = humanoid.GetComponent<Animator>();
            if (animator == null)
            {
                warnings.Add("No Animator component found. Auto-detect will not work.");
            }
            else if (!animator.isHuman)
            {
                warnings.Add("Animator is not using a Humanoid avatar. Auto-detect requires a Humanoid rig.");
            }

            // Bone hierarchy consistency check
            if (refs.IsValid())
            {
                ValidateBoneHierarchy(refs, warnings);
            }

            return warnings;
        }

        /// <summary>
        /// Validates that the bone hierarchy has correct parent-child relationships.
        /// </summary>
        static void ValidateBoneHierarchy(NYIKReferences refs, List<string> warnings)
        {
            // Left arm hierarchy check
            if (refs.LeftUpperArm != null && refs.LeftForearm != null)
            {
                if (!IsDescendant(refs.LeftForearm, refs.LeftUpperArm))
                    warnings.Add("Left Forearm is not a descendant of Left Upper Arm.");
            }
            if (refs.LeftForearm != null && refs.LeftHand != null)
            {
                if (!IsDescendant(refs.LeftHand, refs.LeftForearm))
                    warnings.Add("Left Hand is not a descendant of Left Forearm.");
            }

            // Right arm hierarchy check
            if (refs.RightUpperArm != null && refs.RightForearm != null)
            {
                if (!IsDescendant(refs.RightForearm, refs.RightUpperArm))
                    warnings.Add("Right Forearm is not a descendant of Right Upper Arm.");
            }
            if (refs.RightForearm != null && refs.RightHand != null)
            {
                if (!IsDescendant(refs.RightHand, refs.RightForearm))
                    warnings.Add("Right Hand is not a descendant of Right Forearm.");
            }

            // Left leg hierarchy check
            if (refs.LeftThigh != null && refs.LeftCalf != null)
            {
                if (!IsDescendant(refs.LeftCalf, refs.LeftThigh))
                    warnings.Add("Left Calf is not a descendant of Left Thigh.");
            }
            if (refs.LeftCalf != null && refs.LeftFoot != null)
            {
                if (!IsDescendant(refs.LeftFoot, refs.LeftCalf))
                    warnings.Add("Left Foot is not a descendant of Left Calf.");
            }

            // Right leg hierarchy check
            if (refs.RightThigh != null && refs.RightCalf != null)
            {
                if (!IsDescendant(refs.RightCalf, refs.RightThigh))
                    warnings.Add("Right Calf is not a descendant of Right Thigh.");
            }
            if (refs.RightCalf != null && refs.RightFoot != null)
            {
                if (!IsDescendant(refs.RightFoot, refs.RightCalf))
                    warnings.Add("Right Foot is not a descendant of Right Calf.");
            }
        }

        /// <summary>
        /// Checks whether child is a descendant of parent (not necessarily a direct child).
        /// </summary>
        static bool IsDescendant(Transform child, Transform parent)
        {
            Transform current = child.parent;
            int maxDepth = 10; // Prevent infinite loop
            while (current != null && maxDepth > 0)
            {
                if (current == parent)
                    return true;
                current = current.parent;
                maxDepth--;
            }
            return false;
        }
    }
}
