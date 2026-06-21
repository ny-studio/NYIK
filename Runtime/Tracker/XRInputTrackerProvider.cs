using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace NYIK.Tracker
{
    /// <summary>
    /// Automatic tracker source provider that discovers SteamVR / OpenXR
    /// tracked devices via Unity's <see cref="InputDevices"/> API and maps
    /// them to <see cref="TrackerSlotKind"/> entries.
    ///
    /// Usage:
    /// 1. Attach this component to a GameObject in the scene.
    /// 2. Hit Play. The component lists every detected device in the Console.
    /// 3. Stop Play. Use the Inspector to fill the Mappings list:
    ///    paste each tracker's serial or name fragment and pick its Kind.
    /// 4. Hit Play again. Trackers are auto-bound and the provider feeds
    ///    NYIKHumanoid.
    ///
    /// HMD and controllers are automatically detected by their characteristics
    /// and do not require explicit mapping (TrackerSlotKind.Head, LeftHand,
    /// RightHand). Body trackers must be mapped manually.
    /// </summary>
    [AddComponentMenu("NYIK/XR Input Tracker Provider")]
    public sealed class XRInputTrackerProvider : MonoBehaviour, ITrackerSourceProvider
    {
        [Serializable]
        public sealed class Mapping
        {
            [Tooltip("Substring of the device serial number OR device name. " +
                     "Use the console log after Play to find your tracker's serial.")]
            public string SerialOrName;

            public TrackerSlotKind Kind;

            [Range(0f, 1f)]
            [Tooltip("Override the default trust weight. -1 = use default.")]
            public float TrustOverride = -1f;
        }

        [SerializeField] private List<Mapping> _mappings = new();

        [Tooltip("Log every detected device on Awake to help with mapping.")]
        [SerializeField] private bool _logDetectedDevices = true;

        [Header("OneEuro Filter")]
        [Tooltip("Smooth tracker pose with OneEuro filter. Strongly recommended for SlimeVR / IMU-based trackers.")]
        [SerializeField] private bool _enableFilter = true;
        [Tooltip("Hz. Lower = smoother at rest. Range 0.5–3.0 typical.")]
        [SerializeField] private float _minCutoff = 1.0f;
        [Tooltip("Higher = follow fast motion. 0.001–0.05 typical.")]
        [SerializeField] private float _beta = 0.007f;

        // Runtime state
        private readonly List<TrackerSlot> _slots = new();
        private readonly Dictionary<TrackerSlotKind, TrackerSlot> _byKind = new();
        private readonly Dictionary<TrackerSlot, InputDevice> _deviceBySlot = new();
        private readonly Dictionary<TrackerSlot, Transform> _proxyBySlot = new();
        private readonly Dictionary<TrackerSlot, SlotFilter> _filterBySlot = new();

        private sealed class SlotFilter
        {
            public readonly OneEuroFilter X = new();
            public readonly OneEuroFilter Y = new();
            public readonly OneEuroFilter Z = new();
            public readonly OneEuroQuaternionFilter Rot = new();

            public void Configure(float minCutoff, float beta)
            {
                X.MinCutoff = minCutoff; X.Beta = beta;
                Y.MinCutoff = minCutoff; Y.Beta = beta;
                Z.MinCutoff = minCutoff; Z.Beta = beta;
                Rot.MinCutoff = minCutoff; Rot.Beta = beta;
            }
        }

        public IReadOnlyList<TrackerSlot> Slots => _slots;

        public bool HasFullBodyTrackers
        {
            get
            {
                foreach (var slot in _slots)
                {
                    if (!slot.IsAssigned || !slot.IsTracking) continue;
                    if (IsFullBodyKind(slot.Kind)) return true;
                }
                return false;
            }
        }

        public TrackerSlot GetSlot(TrackerSlotKind kind)
        {
            return _byKind.TryGetValue(kind, out var s) ? s : null;
        }

        private void Awake()
        {
            DiscoverDevices();
        }

        private void OnDestroy()
        {
            // Clean up proxy GameObjects we created
            foreach (var kvp in _proxyBySlot)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value.gameObject);
                }
            }
        }

        /// <summary>
        /// Refresh the device list. Useful if trackers were connected after
        /// the scene started.
        /// </summary>
        [ContextMenu("Rescan Devices")]
        public void DiscoverDevices()
        {
            ClearSlots();

            var devices = new List<InputDevice>();
            InputDevices.GetDevices(devices);

            if (_logDetectedDevices)
            {
                Debug.Log($"[XRInputTrackerProvider] Found {devices.Count} XR devices:");
                foreach (var d in devices)
                {
                    Debug.Log($"  - name='{d.name}' serial='{d.serialNumber}' " +
                              $"characteristics={d.characteristics}");
                }
            }

            foreach (var device in devices)
            {
                if (!device.isValid) continue;

                var kind = ResolveKind(device, out var trustOverride);
                if (kind == TrackerSlotKind.None) continue;
                if (_byKind.ContainsKey(kind))
                {
                    Debug.LogWarning(
                        $"[XRInputTrackerProvider] Duplicate slot {kind} for device " +
                        $"'{device.name}' (serial '{device.serialNumber}'). Skipping.",
                        this);
                    continue;
                }

                var proxy = new GameObject($"Tracker_{kind}");
                proxy.transform.SetParent(transform, false);

                var slot = new TrackerSlot(kind)
                {
                    Source = proxy.transform,
                    TrustWeight = trustOverride >= 0f
                        ? trustOverride
                        : DefaultTrustProfile.Get(kind),
                };
                _slots.Add(slot);
                _byKind[kind] = slot;
                _deviceBySlot[slot] = device;
                _proxyBySlot[slot] = proxy.transform;

                if (_enableFilter)
                {
                    var f = new SlotFilter();
                    f.Configure(_minCutoff, _beta);
                    _filterBySlot[slot] = f;
                }
            }

            Debug.Log($"[XRInputTrackerProvider] Mapped {_slots.Count} tracker slot(s).", this);
        }

        private void ClearSlots()
        {
            foreach (var kvp in _proxyBySlot)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _slots.Clear();
            _byKind.Clear();
            _deviceBySlot.Clear();
            _proxyBySlot.Clear();
            _filterBySlot.Clear();
        }

        public void Tick(float deltaTime)
        {
            foreach (var slot in _slots)
            {
                if (!_deviceBySlot.TryGetValue(slot, out var device))
                {
                    slot.IsTracking = false;
                    continue;
                }
                if (!_proxyBySlot.TryGetValue(slot, out var proxy) || proxy == null)
                {
                    slot.IsTracking = false;
                    continue;
                }

                bool tracked = false;
                Vector3 rawPos = proxy.localPosition;
                Quaternion rawRot = proxy.localRotation;
                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out var pos))
                {
                    rawPos = pos;
                    proxy.localPosition = pos;
                    tracked = true;
                }
                if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot))
                {
                    rawRot = rot;
                    proxy.localRotation = rot;
                    tracked = true;
                }
                slot.IsTracking = tracked && device.isValid;

                // Push filtered pose through the slot's effective override so
                // CalibratedRotation/Position downstream automatically use the
                // smoothed values. The proxy itself keeps the raw values for
                // scene-view debugging.
                if (tracked && _filterBySlot.TryGetValue(slot, out var f))
                {
                    Vector3 worldPos = proxy.parent != null
                        ? proxy.parent.TransformPoint(rawPos)
                        : rawPos;
                    Quaternion worldRot = proxy.parent != null
                        ? proxy.parent.rotation * rawRot
                        : rawRot;
                    Vector3 filteredPos = new(
                        f.X.Filter(worldPos.x, deltaTime),
                        f.Y.Filter(worldPos.y, deltaTime),
                        f.Z.Filter(worldPos.z, deltaTime));
                    Quaternion filteredRot = f.Rot.Filter(worldRot, deltaTime);
                    slot.SetEffective(filteredPos, filteredRot);
                }
                else
                {
                    slot.ClearEffective();
                }
            }
        }

        /// <summary>
        /// Resolve a device to a slot kind using (in order):
        ///   1. User-defined Mapping list (substring match on serial or name)
        ///   2. Device characteristics (HMD / left controller / right controller)
        /// Returns None when unmappable.
        /// </summary>
        private TrackerSlotKind ResolveKind(InputDevice device, out float trustOverride)
        {
            trustOverride = -1f;

            // 1. User mappings
            foreach (var m in _mappings)
            {
                if (m == null || string.IsNullOrEmpty(m.SerialOrName)) continue;
                var pattern = m.SerialOrName;
                if (ContainsIgnoreCase(device.serialNumber, pattern) ||
                    ContainsIgnoreCase(device.name, pattern))
                {
                    trustOverride = m.TrustOverride;
                    return m.Kind;
                }
            }

            // 2. Heuristics from characteristics
            var c = device.characteristics;
            if ((c & InputDeviceCharacteristics.HeadMounted) != 0)
                return TrackerSlotKind.Head;

            if ((c & InputDeviceCharacteristics.Controller) != 0)
            {
                if ((c & InputDeviceCharacteristics.Left) != 0)
                    return TrackerSlotKind.LeftHand;
                if ((c & InputDeviceCharacteristics.Right) != 0)
                    return TrackerSlotKind.RightHand;
            }

            return TrackerSlotKind.None;
        }

        private static bool ContainsIgnoreCase(string source, string substr)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(substr)) return false;
            return source.IndexOf(substr, StringComparison.OrdinalIgnoreCase) >= 0;
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
