using System.Collections.Generic;
using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Holds all registered <see cref="IBodyPartEstimator"/> instances for an
    /// avatar and runs them in dependency order each frame.
    ///
    /// Topological sort happens once (at registration time) and is cached.
    /// Re-sort when estimators are added/removed at runtime.
    ///
    /// Cycle detection: throws on circular dependencies at sort time, never
    /// silently produces a broken order.
    /// </summary>
    public sealed class BodyPartEstimatorRegistry
    {
        private readonly Dictionary<HumanBodyBones, IBodyPartEstimator> _byTarget = new();
        private readonly List<IBodyPartEstimator> _sorted = new();
        private bool _isSorted;

        public IReadOnlyList<IBodyPartEstimator> SortedOrder
        {
            get
            {
                if (!_isSorted) Resort();
                return _sorted;
            }
        }

        public void Register(IBodyPartEstimator estimator)
        {
            if (estimator == null) return;
            _byTarget[estimator.TargetBone] = estimator;
            _isSorted = false;
        }

        public bool Unregister(HumanBodyBones target)
        {
            bool removed = _byTarget.Remove(target);
            if (removed) _isSorted = false;
            return removed;
        }

        public void Clear()
        {
            _byTarget.Clear();
            _sorted.Clear();
            _isSorted = true;
        }

        /// <summary>
        /// Resolve every registered estimator into the context's Targets dict.
        /// Estimators whose <see cref="IBodyPartEstimator.TargetBone"/> is
        /// already in <see cref="EstimatorContext.Targets"/> (tracker won)
        /// are skipped — that's the graceful-degradation contract.
        /// </summary>
        public void ResolveAll(in EstimatorContext ctx)
        {
            if (!_isSorted) Resort();
            foreach (var est in _sorted)
            {
                if (ctx.HasTarget(est.TargetBone)) continue; // tracker present, skip
                est.Estimate(ctx);
            }
        }

        // Kahn's algorithm: stable topological sort.
        private void Resort()
        {
            _sorted.Clear();

            var inDegree = new Dictionary<HumanBodyBones, int>();
            foreach (var kv in _byTarget) inDegree[kv.Key] = 0;
            foreach (var kv in _byTarget)
            {
                foreach (var dep in kv.Value.DependsOn ?? System.Array.Empty<HumanBodyBones>())
                {
                    if (!_byTarget.ContainsKey(dep)) continue; // external dep (tracker), ignore
                    inDegree[kv.Key]++;
                }
            }

            // Reverse-adjacency: who depends on each bone?
            var dependents = new Dictionary<HumanBodyBones, List<HumanBodyBones>>();
            foreach (var kv in _byTarget)
            {
                foreach (var dep in kv.Value.DependsOn ?? System.Array.Empty<HumanBodyBones>())
                {
                    if (!_byTarget.ContainsKey(dep)) continue;
                    if (!dependents.TryGetValue(dep, out var list))
                    {
                        list = new List<HumanBodyBones>();
                        dependents[dep] = list;
                    }
                    list.Add(kv.Key);
                }
            }

            var ready = new Queue<HumanBodyBones>();
            foreach (var kv in inDegree)
                if (kv.Value == 0) ready.Enqueue(kv.Key);

            while (ready.Count > 0)
            {
                var bone = ready.Dequeue();
                _sorted.Add(_byTarget[bone]);
                if (!dependents.TryGetValue(bone, out var list)) continue;
                foreach (var dep in list)
                {
                    inDegree[dep]--;
                    if (inDegree[dep] == 0) ready.Enqueue(dep);
                }
            }

            if (_sorted.Count != _byTarget.Count)
            {
                Debug.LogError("[BodyPartEstimatorRegistry] Cyclic dependency among estimators — solver order broken.");
            }

            _isSorted = true;
        }
    }
}
