using System;
using System.Collections.Generic;
using UnityEngine;

namespace NYIK.Tracker
{
    /// <summary>
    /// Manually-assigned tracker source provider. Edit-friendly: drag Transforms
    /// in the Inspector to assign each body part. Suitable when SteamVR roles
    /// are unreliable or when slot mapping is curated.
    ///
    /// Apply this component to a GameObject in your scene (e.g. the avatar root
    /// or a dedicated rig container), then assign each tracker Transform via
    /// the inspector. The OneEuro filter is applied to each tracked slot
    /// independently.
    /// </summary>
    [AddComponentMenu("NYIK/Manual Tracker Source Provider")]
    public sealed class ManualTrackerSourceProvider : MonoBehaviour, ITrackerSourceProvider
    {
        [Serializable]
        public sealed class SlotAssignment
        {
            public TrackerSlotKind Kind;
            public Transform Source;
            [Range(0f, 1f)] public float TrustWeightOverride = -1f; // -1 = use default

            [Header("Filter")]
            public bool EnableFilter = true;
            [Tooltip("Hz. Lower = smoother at rest. Range 0.5–3.0 typical.")]
            public float MinCutoff = 1.0f;
            [Tooltip("Higher = follow fast motion. 0.001–0.05 typical.")]
            public float Beta = 0.007f;
        }

        [SerializeField] private List<SlotAssignment> _assignments = new();

        // Runtime slots and filters
        private readonly List<TrackerSlot> _slots = new();
        private readonly Dictionary<TrackerSlotKind, TrackerSlot> _byKind = new();
        private readonly Dictionary<TrackerSlotKind, OneEuroQuaternionFilter> _rotFilters = new();
        private readonly Dictionary<TrackerSlotKind, (OneEuroFilter x, OneEuroFilter y, OneEuroFilter z)> _posFilters = new();

        public IReadOnlyList<TrackerSlot> Slots => _slots;

        public bool HasFullBodyTrackers
        {
            get
            {
                foreach (var s in _slots)
                {
                    if (!s.IsAssigned || !s.IsTracking) continue;
                    if (IsFullBodyKind(s.Kind)) return true;
                }
                return false;
            }
        }

        public TrackerSlot GetSlot(TrackerSlotKind kind)
        {
            return _byKind.TryGetValue(kind, out var s) ? s : null;
        }

        private void OnEnable()
        {
            RebuildSlots();
        }

        public void RebuildSlots()
        {
            _slots.Clear();
            _byKind.Clear();
            _rotFilters.Clear();
            _posFilters.Clear();

            foreach (var a in _assignments)
            {
                if (a == null || a.Kind == TrackerSlotKind.None) continue;

                var slot = new TrackerSlot(a.Kind)
                {
                    Source = a.Source,
                    TrustWeight = a.TrustWeightOverride >= 0f
                        ? a.TrustWeightOverride
                        : DefaultTrustProfile.Get(a.Kind),
                };
                _slots.Add(slot);
                _byKind[a.Kind] = slot;

                if (a.EnableFilter)
                {
                    var rf = new OneEuroQuaternionFilter { MinCutoff = a.MinCutoff, Beta = a.Beta };
                    _rotFilters[a.Kind] = rf;
                    _posFilters[a.Kind] = (
                        new OneEuroFilter { MinCutoff = a.MinCutoff, Beta = a.Beta },
                        new OneEuroFilter { MinCutoff = a.MinCutoff, Beta = a.Beta },
                        new OneEuroFilter { MinCutoff = a.MinCutoff, Beta = a.Beta }
                    );
                }
            }
        }

        public void Tick(float deltaTime)
        {
            foreach (var slot in _slots)
            {
                if (slot.Source == null)
                {
                    slot.IsTracking = false;
                    slot.ClearEffective();
                    continue;
                }

                slot.IsTracking = slot.Source.gameObject.activeInHierarchy;

                // If a filter is configured for this slot, push the filtered
                // pose into the slot's effective override so downstream reads
                // (CalibratedRotation/Position) automatically use it. This is
                // the only path that actually applies OneEuro to manual sources.
                if (_rotFilters.TryGetValue(slot.Kind, out var rf))
                {
                    var pf = _posFilters[slot.Kind];
                    var rawPos = slot.Source.position;
                    var filtered = new Vector3(
                        pf.x.Filter(rawPos.x, deltaTime),
                        pf.y.Filter(rawPos.y, deltaTime),
                        pf.z.Filter(rawPos.z, deltaTime));
                    var filteredRot = rf.Filter(slot.Source.rotation, deltaTime);
                    slot.SetEffective(filtered, filteredRot);
                }
                else
                {
                    slot.ClearEffective();
                }
            }
        }

        private static bool IsFullBodyKind(TrackerSlotKind kind)
        {
            switch (kind)
            {
                case TrackerSlotKind.Head:
                case TrackerSlotKind.LeftHand:
                case TrackerSlotKind.RightHand:
                case TrackerSlotKind.None:
                    return false;
                default:
                    return true;
            }
        }
    }
}
