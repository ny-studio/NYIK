using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using NYIK.Tracker;

namespace NYIK.Calibration
{
    /// <summary>
    /// Persistent calibration record for an FBT setup. Stores per-slot
    /// rotation and position offsets learned at T-pose, so a performer's
    /// calibration survives between sessions.
    ///
    /// Two persistence paths:
    /// - ScriptableObject asset (Editor only): CaptureFrom / ApplyTo
    /// - JSON file at any path (runtime + Editor): SaveToJson / LoadFromJson
    ///
    /// In builds, the SO asset is read-only (Unity can't write to AssetDatabase),
    /// so use SaveToJson into Application.persistentDataPath to keep calibration
    /// between game sessions.
    /// </summary>
    [CreateAssetMenu(menuName = "NYIK/FBT Calibration Data", fileName = "FBTCalibrationData")]
    public sealed class FBTCalibrationData : ScriptableObject
    {
        /// <summary>
        /// One serialized record per tracker slot.
        /// </summary>
        [Serializable]
        public class CalibrationEntry
        {
            public TrackerSlotKind Kind;
            public Quaternion RotOffset = Quaternion.identity;
            public Vector3 PosOffset = Vector3.zero;
        }

        // JsonUtility doesn't serialize List<T> at the top level, so wrap it.
        [Serializable]
        private sealed class JsonPayload
        {
            public int version = 1;
            public string capturedAtIso = "";
            public float userScale = 1f;
            public List<CalibrationEntry> entries = new();
        }

        [SerializeField] private List<CalibrationEntry> _entries = new();

        [SerializeField] private float _userScale = 1f;

        /// <summary>Read-only access to the stored entries.</summary>
        public IReadOnlyList<CalibrationEntry> Entries => _entries;

        /// <summary>
        /// ユーザー→アバター ターゲット再マップ倍率（哲学B, avatar/user）。1.0=スケール無効（従来挙動）。
        /// 0/負値は安全側 1.0。NYIKHumanoid.UserScale へ橋渡しして永続化する想定。
        /// </summary>
        public float UserScale
        {
            get => _userScale;
            set => _userScale = value > 1e-4f ? value : 1f;
        }

        /// <summary>
        /// Default JSON path for persisting between game sessions in builds.
        /// Always lives under <see cref="Application.persistentDataPath"/>.
        /// </summary>
        public static string DefaultJsonPath =>
            Path.Combine(Application.persistentDataPath, "nyik_calibration.json");

        /// <summary>
        /// Capture the current calibration offsets from each assigned slot of
        /// the given provider and store them in this asset. Existing entries
        /// are replaced; unassigned slots are skipped.
        /// </summary>
        public void CaptureFrom(ITrackerSourceProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("[FBTCalibrationData] CaptureFrom called with null provider.");
                return;
            }

            _entries.Clear();
            foreach (var slot in provider.Slots)
            {
                if (slot == null || slot.Kind == TrackerSlotKind.None) continue;
                if (!slot.IsAssigned) continue;

                _entries.Add(new CalibrationEntry
                {
                    Kind = slot.Kind,
                    RotOffset = slot.CalibrationRotOffset,
                    PosOffset = slot.CalibrationPosOffset,
                });
            }
        }

        /// <summary>
        /// Restore stored offsets onto the matching slots of the given provider.
        /// Slots without a stored entry are left untouched.
        /// </summary>
        public void ApplyTo(ITrackerSourceProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("[FBTCalibrationData] ApplyTo called with null provider.");
                return;
            }

            foreach (var entry in _entries)
            {
                var slot = provider.GetSlot(entry.Kind);
                if (slot == null)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrationData] No slot found for {entry.Kind}; skipping.");
                    continue;
                }
                slot.CalibrationRotOffset = entry.RotOffset;
                slot.CalibrationPosOffset = entry.PosOffset;
            }
        }

        /// <summary>
        /// Look up the stored offsets for a slot kind.
        /// </summary>
        /// <returns>True when an entry exists for the requested kind.</returns>
        public bool TryGet(TrackerSlotKind kind, out Quaternion rotOffset, out Vector3 posOffset)
        {
            foreach (var entry in _entries)
            {
                if (entry.Kind != kind) continue;
                rotOffset = entry.RotOffset;
                posOffset = entry.PosOffset;
                return true;
            }
            rotOffset = Quaternion.identity;
            posOffset = Vector3.zero;
            return false;
        }

        /// <summary>
        /// Serialize the current entries to a JSON file. Works at runtime in
        /// builds. Path defaults to <see cref="DefaultJsonPath"/>.
        /// </summary>
        public void SaveToJson(string path = null)
        {
            path ??= DefaultJsonPath;
            var payload = new JsonPayload
            {
                version = 1,
                capturedAtIso = DateTime.UtcNow.ToString("o"),
                userScale = _userScale,
                entries = new List<CalibrationEntry>(_entries),
            };
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtility.ToJson(payload, true));
                Debug.Log($"[FBTCalibrationData] Saved calibration to {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FBTCalibrationData] Failed to save to '{path}': {e.Message}");
            }
        }

        /// <summary>
        /// Load entries from a JSON file. Returns true on success. Path
        /// defaults to <see cref="DefaultJsonPath"/>. Missing file is not an
        /// error — just returns false.
        /// </summary>
        public bool LoadFromJson(string path = null)
        {
            path ??= DefaultJsonPath;
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                var payload = JsonUtility.FromJson<JsonPayload>(json);
                if (payload?.entries == null)
                {
                    Debug.LogWarning($"[FBTCalibrationData] '{path}' had no entries.");
                    return false;
                }
                _entries.Clear();
                _entries.AddRange(payload.entries);
                // 旧 JSON（userScale 欠落）は payload 初期値 1f を保持。0/負値は安全側 1.0。
                _userScale = payload.userScale > 1e-4f ? payload.userScale : 1f;
                Debug.Log($"[FBTCalibrationData] Loaded {_entries.Count} entries from {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FBTCalibrationData] Failed to load '{path}': {e.Message}");
                return false;
            }
        }
    }
}
