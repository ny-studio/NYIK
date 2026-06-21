using UnityEngine;
using NYIK.Anatomy;

namespace NYIK.Solvers
{
    /// <summary>
    /// Refines an IK pose by iterating constraint relaxation:
    /// ROM clamps → bone-length restore → ROM clamps → ...
    ///
    /// This is a practical alternative to full SQP optimization.
    /// FABRIK guarantees bone-length conservation but ignores joint limits;
    /// AnatomicalRefiner enforces joint limits but can stretch bones.
    /// Iterating between them converges to a pose that satisfies both.
    ///
    /// Performance: this class caches Animator.GetBoneTransform() lookups
    /// across iterations so the inner loops don't pay the Mecanim lookup
    /// cost ~165× per frame. With 2 iterations, GetBoneTransform is now
    /// called 55 times total (once per bone) instead of ~165 times.
    ///
    /// Reference: FABRIK + SQP hybrid (arxiv 2209.02532, Springer 2023).
    /// Our simplification: ROM clamping replaces the SQP step. Empirically
    /// 2-3 iterations are sufficient for IMU full-body tracking.
    /// </summary>
    public static class ConstraintRefiner
    {
        private const int BoneCount = (int)HumanBodyBones.LastBone;

        // Reusable scratch arrays to avoid per-frame allocations.
        [System.ThreadStatic] private static Transform[] _bonesScratch;
        [System.ThreadStatic] private static float[] _lengthsScratch;

        /// <summary>
        /// Iteratively refine the current Animator pose to satisfy joint ROM
        /// limits while preserving bone lengths.
        /// </summary>
        public static void Refine(Animator animator, int iterations = 2, float clampStrength = 1f)
        {
            if (animator == null || !animator.isHuman) return;
            iterations = Mathf.Max(1, iterations);

            var bones = GetBoneCache(animator);
            var lengths = GetLengthScratch();
            CaptureBoneLengthsInto(bones, lengths);

            for (int i = 0; i < iterations; i++)
            {
                AnatomicalRefiner.ClampAllJoints(bones, clampStrength);
                RestoreBoneLengthsFromCache(bones, lengths);
            }
        }

        /// <summary>
        /// Public legacy API kept for callers that want a one-shot snapshot.
        /// New code should use the cache-aware path.
        /// </summary>
        public static BoneLengthSnapshot CaptureBoneLengths(Animator animator)
        {
            var snap = new BoneLengthSnapshot();
            if (animator == null || !animator.isHuman) return snap;
            var bones = GetBoneCache(animator);
            snap.Lengths = new float[BoneCount];
            CaptureBoneLengthsInto(bones, snap.Lengths);
            return snap;
        }

        /// <inheritdoc cref="CaptureBoneLengths(Animator)"/>
        public static void RestoreBoneLengths(Animator animator, BoneLengthSnapshot snapshot)
        {
            if (animator == null || snapshot.Lengths == null) return;
            var bones = GetBoneCache(animator);
            RestoreBoneLengthsFromCache(bones, snapshot.Lengths);
        }

        private static Transform[] GetBoneCache(Animator animator)
        {
            if (_bonesScratch == null || _bonesScratch.Length < BoneCount)
                _bonesScratch = new Transform[BoneCount];
            for (int i = 0; i < BoneCount; i++)
            {
                _bonesScratch[i] = animator.GetBoneTransform((HumanBodyBones)i);
            }
            return _bonesScratch;
        }

        private static float[] GetLengthScratch()
        {
            if (_lengthsScratch == null || _lengthsScratch.Length < BoneCount)
                _lengthsScratch = new float[BoneCount];
            return _lengthsScratch;
        }

        private static void CaptureBoneLengthsInto(Transform[] bones, float[] lengths)
        {
            for (int i = 0; i < BoneCount; i++)
            {
                var t = bones[i];
                if (t == null || t.parent == null)
                {
                    lengths[i] = -1f;
                    continue;
                }
                lengths[i] = Vector3.Distance(t.position, t.parent.position);
            }
        }

        private static void RestoreBoneLengthsFromCache(Transform[] bones, float[] lengths)
        {
            for (int i = 0; i < BoneCount; i++)
            {
                float targetLen = lengths[i];
                if (targetLen <= 0f) continue;
                var t = bones[i];
                if (t == null || t.parent == null) continue;

                var parentPos = t.parent.position;
                var dir = (t.position - parentPos).normalized;
                if (dir.sqrMagnitude < 1e-8f) continue;
                t.position = parentPos + dir * targetLen;
            }
        }
    }

    /// <summary>
    /// Captured bone-length snapshot used by <see cref="ConstraintRefiner"/>.
    /// </summary>
    public struct BoneLengthSnapshot
    {
        /// <summary>Indexed by (int)HumanBodyBones. -1 indicates unmapped.</summary>
        public float[] Lengths;
    }
}
