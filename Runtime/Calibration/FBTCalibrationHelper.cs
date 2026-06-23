using UnityEngine;
using NYIK.Humanoid;
using NYIK.Tracker;

namespace NYIK.Calibration
{
    /// <summary>
    /// Convenience component that ties an Animator, an
    /// <see cref="ITrackerSourceProvider"/>, and a <see cref="FBTCalibrationData"/>
    /// asset together for one-shot calibration workflows.
    ///
    /// Attach to the avatar root (or a dedicated rig GameObject), wire the
    /// references in the Inspector, then trigger calibration via the
    /// right-click context menu (or call the public methods from your own UI).
    ///
    /// Typical workflow:
    ///   1. Stand in T-pose with all trackers active and SlimeVR calibrated.
    ///   2. Right-click → "Calibrate At T-Pose" — learns tracker→bone offsets.
    ///   3. Right-click → "Save Calibration" — persists to ScriptableObject.
    ///   4. Next session: "Load Calibration" restores prior offsets.
    ///   5. Right-click → "Quick Reset" — re-syncs rotations only (drift fix).
    /// </summary>
    [AddComponentMenu("NYIK/FBT Calibration Helper")]
    public sealed class FBTCalibrationHelper : MonoBehaviour
    {
        [Tooltip("Humanoid Animator that will be calibrated.")]
        [SerializeField] private Animator _animator;

        [Tooltip("Tracker source provider (e.g. ManualTrackerSourceProvider).")]
        [SerializeField] private MonoBehaviour _providerBehaviour;
        // Stored as MonoBehaviour because Unity does not serialize interface
        // references. Validated at runtime via TryGetProvider.

        [Tooltip("Persistent calibration storage. Created via VRH/FBT Calibration Data menu.")]
        [SerializeField] private FBTCalibrationData _calibrationData;

        [Header("Height Scale (philosophy B: shrink targets, avatar fixed)")]
        [Tooltip("身長スケールを適用する NYIKHumanoid。空なら自動検出。頭+手のみ構成で performer↔avatar の" +
                 "身長差を UserScale で吸収し、足ターゲット(y=0)が脚長で届く高さに収まる(足の接地不良の根治)。")]
        [SerializeField] private NYIKHumanoid _nyikHumanoid;
        [Tooltip("T-pose/I-pose キャリブ時に身長スケールも併せて計算・適用するか。")]
        [SerializeField] private bool _calibrateScaleWithPose = true;

        [Header("Pose Validation")]
        [Tooltip("Refuse to capture calibration unless the current pose passes validation. " +
                 "Prevents the common 'I clicked Calibrate while relaxed and now everything is broken' problem.")]
        [SerializeField] private bool _requireValidTPose = true;
        [Tooltip("検証する姿勢。VRChat 既定は I-pose(直立・腕を体側に下ろす)。厳密 T-pose は legacy。" +
                 "キャリブ数学は姿勢非依存なので、ここは検証ゲートの姿勢だけを切り替える。")]
        [SerializeField] private CalibrationPose _calibrationPose = CalibrationPose.IPose;

        private void Reset()
        {
            AutoDetectReferences();
        }

        private void Awake()
        {
            // Runtime fallback for references that were not assigned in editor.
            if (_animator == null || _providerBehaviour == null)
                AutoDetectReferences();
        }

        private void AutoDetectReferences()
        {
            // Animator: prefer self / parent (humanoid avatar root) / children
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
                if (_animator == null) _animator = GetComponentInParent<Animator>();
                if (_animator == null) _animator = GetComponentInChildren<Animator>();
            }

            // Provider: find any MonoBehaviour on this hierarchy that
            // implements ITrackerSourceProvider
            if (_providerBehaviour == null)
            {
                var behaviours = GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var b in behaviours)
                {
                    if (b is ITrackerSourceProvider)
                    {
                        _providerBehaviour = b;
                        break;
                    }
                }
            }

            // NYIKHumanoid (for height scale): self / parent / children
            if (_nyikHumanoid == null)
            {
                _nyikHumanoid = GetComponent<NYIKHumanoid>();
                if (_nyikHumanoid == null) _nyikHumanoid = GetComponentInParent<NYIKHumanoid>();
                if (_nyikHumanoid == null) _nyikHumanoid = GetComponentInChildren<NYIKHumanoid>();
            }
        }

        /// <summary>
        /// 身長スケール キャリブ(哲学B: ターゲットを縮める・アバター固定)。立位の HMD 頭高とアバター頭高の
        /// 比から <see cref="FBTCalibrator.ComputeTargetRemapScale"/> を求め <see cref="NYIKHumanoid.UserScale"/>
        /// に入れる。頭+手のみ構成で performer↔avatar の身長差を吸収し、床スナップした足ターゲット(y=0)が
        /// アバター脚長で届く高さに骨盤/足を再マップする(= 足が曲がって接地しない問題の根治)。
        /// **直立して呼ぶこと。トラッカー不要(頭の高さだけ使う)。**
        /// </summary>
        [ContextMenu("Calibrate Scale (Height)")]
        public void CalibrateScale()
        {
            if (_nyikHumanoid == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] NYIKHumanoid 未割当でスケールキャリブ不可。", this);
                return;
            }
            var refs = _nyikHumanoid.References;
            if (refs == null || !refs.IsValid() || refs.Head == null || refs.Root == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] References が無効でスケールキャリブ不可。", this);
                return;
            }

            var head = _nyikHumanoid.HeadTarget;
            head.UpdateTracking();
            if (!head.IsTracking)
            {
                Debug.LogWarning("[FBTCalibrationHelper] 頭が未トラッキングでスケールキャリブ不可(Play中・立位で呼ぶ)。", this);
                return;
            }

            // 床基準の頭高。XROrigin の floor offset 未補正のため絶対値は近似だが、比は概ね正しい。
            float floorY = refs.Root.position.y;
            float userHeadHeight = head.Position.y - floorY;        // 実プレイヤー頭高
            float avatarHeadHeight = refs.Head.position.y - floorY; // アバター頭高(現スケール)
            float scale = FBTCalibrator.ComputeTargetRemapScale(
                UserScaleMode.Height, userHeadHeight, avatarHeadHeight); // = avatar/user (哲学B)
            _nyikHumanoid.UserScale = scale;
            Debug.Log($"[FBTCalibrationHelper] Scale calibrated: userHead={userHeadHeight:F2}m " +
                      $"avatarHead={avatarHeadHeight:F2}m → UserScale={scale:F3}", this);
        }

        /// <summary>
        /// Capture tracker→bone offsets at the current pose. Caller is
        /// expected to be in T-pose. Does not save to the ScriptableObject;
        /// call <see cref="SaveCalibration"/> after this.
        /// </summary>
        [ContextMenu("Calibrate At T-Pose")]
        public void CalibrateAtTPose()
        {
            if (!TryGetProvider(out var provider)) return;
            if (_animator == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] Animator is not assigned.", this);
                return;
            }
            if (_requireValidTPose)
            {
                var check = _calibrationPose == CalibrationPose.TPose
                    ? TPoseValidator.Validate(provider)
                    : TPoseValidator.ValidateIPose(provider);
                if (!check.IsTPose)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrationHelper] Calibration aborted. {check.Summary} " +
                        $"Use 'Force Calibrate (Skip T-pose Check)' to override.", this);
                    return;
                }
            }
            FBTCalibrator.CalibrateAtTPose(_animator, provider);
            Debug.Log("[FBTCalibrationHelper] T-pose calibration captured.", this);
            if (_calibrateScaleWithPose) CalibrateScale();
        }

        /// <summary>
        /// Bypass the T-pose check. Useful when the validator's heuristics
        /// reject a valid stance (asymmetric avatars, accessibility setups).
        /// </summary>
        [ContextMenu("Force Calibrate (Skip T-pose Check)")]
        public void ForceCalibrateAtTPose()
        {
            if (!TryGetProvider(out var provider)) return;
            if (_animator == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] Animator is not assigned.", this);
                return;
            }
            FBTCalibrator.CalibrateAtTPose(_animator, provider);
            Debug.Log("[FBTCalibrationHelper] T-pose calibration captured (validation bypassed).", this);
            if (_calibrateScaleWithPose) CalibrateScale();
        }

        /// <summary>
        /// Re-syncs rotations only, preserving position offsets. Useful as
        /// a fast drift correction without re-measuring tracker placement.
        /// </summary>
        [ContextMenu("Quick Reset (Rotation Only)")]
        public void QuickReset()
        {
            if (!TryGetProvider(out var provider)) return;
            if (_animator == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] Animator is not assigned.", this);
                return;
            }
            FBTCalibrator.QuickReset(_animator, provider);
            Debug.Log("[FBTCalibrationHelper] Quick rotation reset applied.", this);
        }

        /// <summary>
        /// Persist current in-memory offsets to the linked ScriptableObject
        /// (Editor only) AND to a JSON file under persistentDataPath (runtime).
        /// In a build the asset path is read-only, so the JSON is what survives.
        /// </summary>
        [ContextMenu("Save Calibration")]
        public void SaveCalibration()
        {
            if (!TryGetProvider(out var provider)) return;
            if (_calibrationData == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] No FBTCalibrationData asset assigned.", this);
                return;
            }
            _calibrationData.CaptureFrom(provider);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_calibrationData);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(_calibrationData);
#endif
            _calibrationData.SaveToJson();
            Debug.Log("[FBTCalibrationHelper] Calibration saved (asset + JSON).", this);
        }

        /// <summary>
        /// Apply persisted offsets. Prefers the JSON file (so a calibration
        /// captured at runtime survives a build restart); falls back to the
        /// linked ScriptableObject if no JSON exists.
        /// </summary>
        [ContextMenu("Load Calibration")]
        public void LoadCalibration()
        {
            if (!TryGetProvider(out var provider)) return;
            if (_calibrationData == null)
            {
                Debug.LogWarning("[FBTCalibrationHelper] No FBTCalibrationData asset assigned.", this);
                return;
            }
            bool fromJson = _calibrationData.LoadFromJson();
            _calibrationData.ApplyTo(provider);
            Debug.Log(fromJson
                ? "[FBTCalibrationHelper] Calibration loaded from JSON + applied."
                : "[FBTCalibrationHelper] Calibration applied from asset (no JSON found).", this);
        }

        /// <summary>
        /// Try to auto-load a saved calibration on first frame so the user
        /// doesn't need to manually trigger LoadCalibration in builds.
        /// </summary>
        private void Start()
        {
            if (_calibrationData == null) return;
            if (_calibrationData.LoadFromJson())
            {
                if (TryGetProvider(out var provider))
                    _calibrationData.ApplyTo(provider);
            }
        }

        private bool TryGetProvider(out ITrackerSourceProvider provider)
        {
            provider = _providerBehaviour as ITrackerSourceProvider;
            if (provider == null)
            {
                Debug.LogWarning(
                    "[FBTCalibrationHelper] Provider behaviour does not implement ITrackerSourceProvider.",
                    this);
                return false;
            }
            return true;
        }
    }
}
