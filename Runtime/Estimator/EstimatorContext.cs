using System.Collections.Generic;
using UnityEngine;
using NYIK.Tracker;

namespace NYIK.Estimator
{
    /// <summary>
    /// State threaded through every estimator call during one solve frame.
    /// Owns the per-bone target dictionary and provides the Animator + tracker
    /// provider for context lookups.
    ///
    /// Passed by ref-readonly (`in`) so estimators get cheap access without
    /// copying the underlying dictionary. SetTarget / TryGetTarget mutate the
    /// shared dictionary directly.
    /// </summary>
    public readonly struct EstimatorContext
    {
        public readonly Animator Animator;
        public readonly ITrackerSourceProvider Provider;
        public readonly Dictionary<HumanBodyBones, BoneTarget> Targets;
        public readonly float DeltaTime;

        public EstimatorContext(
            Animator animator,
            ITrackerSourceProvider provider,
            Dictionary<HumanBodyBones, BoneTarget> targets,
            float deltaTime)
        {
            Animator = animator;
            Provider = provider;
            Targets = targets;
            DeltaTime = deltaTime;
        }

        /// <summary>True if a target (tracker or estimator) is resolved for <paramref name="bone"/>.</summary>
        public bool HasTarget(HumanBodyBones bone) => Targets.ContainsKey(bone);

        public bool TryGetTarget(HumanBodyBones bone, out BoneTarget target) =>
            Targets.TryGetValue(bone, out target);

        public void SetTarget(HumanBodyBones bone, BoneTarget target) => Targets[bone] = target;

        public Transform GetBoneTransform(HumanBodyBones bone) =>
            Animator != null ? Animator.GetBoneTransform(bone) : null;
    }
}
