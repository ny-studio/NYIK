using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Computes a target pose for a single Humanoid bone, given the targets
    /// of upstream bones the estimator depends on. The unified solve pipeline
    /// runs estimators in dependency order — by the time an estimator runs,
    /// all bones listed in <see cref="DependsOn"/> have either a tracker
    /// target or a prior estimator's output available in
    /// <see cref="EstimatorContext.TryGetTarget"/>.
    ///
    /// Estimators are skipped entirely when their target bone already has a
    /// live tracker assigned — that's the "tracker just replaces this part"
    /// semantic the architecture is built around.
    /// </summary>
    public interface IBodyPartEstimator
    {
        /// <summary>The Humanoid bone this estimator provides a target for.</summary>
        HumanBodyBones TargetBone { get; }

        /// <summary>
        /// Bones whose target must be resolved before this estimator runs.
        /// Used by <see cref="BodyPartEstimatorRegistry"/> to topologically
        /// sort estimators.
        /// </summary>
        HumanBodyBones[] DependsOn { get; }

        /// <summary>
        /// Compute a target for <see cref="TargetBone"/> using context data
        /// already resolved by prior estimators / tracker reads. Should call
        /// <see cref="EstimatorContext.SetTarget"/> when successful.
        /// </summary>
        void Estimate(in EstimatorContext ctx);
    }
}
