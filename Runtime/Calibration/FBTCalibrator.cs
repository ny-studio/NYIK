using System.Collections.Generic;
using UnityEngine;
using NYIK.Tracker;
using NYIK.Estimator;

namespace NYIK.Calibration
{
    /// <summary>ユーザー→アバターのスケール計測方式。VRChat の Measure by height / by arms 相当。
    /// (UnityEngine.ScaleMode との衝突回避のため UserScaleMode)。</summary>
    public enum UserScaleMode { None, Height, ArmSpan }

    /// <summary>
    /// Stateless utility that learns the offset between each tracker's raw
    /// pose and the avatar bone it represents while the performer stands in
    /// T-pose. Once calibrated, <see cref="TrackerSlot.CalibratedRotation"/>
    /// and <see cref="TrackerSlot.CalibratedPosition"/> return bone-aligned
    /// values.
    /// </summary>
    public static class FBTCalibrator
    {
        /// <summary>
        /// ユーザー身体寸法とアバター寸法の比からスケールを算出する純関数（VRChat 整合）。
        ///   Height  : userMeasure = キャリブ時の HMD 床上高, avatarMeasure = アバターの目/頭高
        ///   ArmSpan : userMeasure = 両コントローラ間距離,  avatarMeasure = アバターの腕スパン
        /// None は常に 1.0（スケール無効＝従来挙動）。0/不正値も安全側で 1.0。範囲はクランプ。
        /// ※ランタイムでの適用点は二重スケール（既存 per-slot offset / lossyScale）に注意し別途
        /// 慎重に実装する。この純関数は「計測値→倍率」の算出のみ（ヘッドレステスト可能）。
        /// </summary>
        public static float ComputeUserScale(UserScaleMode mode, float userMeasure, float avatarMeasure,
                                             float min = 0.3f, float max = 3f)
        {
            if (mode == UserScaleMode.None) return 1f;
            if (avatarMeasure <= 1e-4f || userMeasure <= 1e-4f) return 1f;
            return Mathf.Clamp(userMeasure / avatarMeasure, min, max);
        }

        /// <summary>
        /// ターゲット再マップ用スケール（VRChat 整合・哲学B）。`ComputeUserScale` は user/avatar（アバターを
        /// 拡大する FinalIK 流）だが、本作はアバターを固定し**ユーザー空間トラッカーを小さいアバターへ縮める**
        /// ので係数は**逆数 avatar/user**。estimator 後の body ターゲット変位 (pos-pivot) に対してのみ掛ける。
        /// None/等値は厳密 1.0（no-op）。微小/不正計測は安全側 1.0（退化スケール暴走防止）。純関数＝テスト可能。
        /// </summary>
        public static float ComputeTargetRemapScale(UserScaleMode mode, float userMeasure, float avatarMeasure,
                                                    float min = 0.3f, float max = 3f)
        {
            if (mode == UserScaleMode.None) return 1f;
            if (userMeasure <= 0.05f || avatarMeasure <= 0.05f) return 1f; // 退化計測ガード
            float fit = ComputeUserScale(mode, userMeasure, avatarMeasure, min, max); // user/avatar
            if (Mathf.Approximately(fit, 1f)) return 1f;                               // 等値 → 厳密 no-op
            return Mathf.Clamp(1f / fit, min, max);                                    // avatar/user
        }

        /// <summary>
        /// 点 p を pivot を不動点とする一様スケール（相似変換）。距離のみを scale 倍し、角度・距離比は保存する。
        /// body トラッカー世界位置を「床-腰ピボット」まわりで縮め、performer↔avatar の身長差を吸収する用途。
        /// scale==1 は厳密に p を返す（no-op）。純関数＝ヘッドレステスト可能。
        /// </summary>
        public static Vector3 ScaleAboutPivot(Vector3 p, Vector3 pivot, float scale)
            => scale == 1f ? p : pivot + (p - pivot) * scale;

        // estimator 後の body ターゲット一括スケール用キー・スクラッチ（毎フレーム alloc 回避、メインスレッド専用）。
        static readonly List<HumanBodyBones> s_ScaleKeyScratch = new();

        /// <summary>
        /// estimator 解決後の body ターゲット群を「床-腰ピボット」まわりで一様スケールする（哲学B：
        /// アバター固定・トラッカーを縮める）。Head / L/R Hand（HMD・コントローラ由来＝実測）と全回転は
        /// 不変、<see cref="BoneTarget.HasPosition"/> の body ボーン Position のみ
        /// <see cref="ScaleAboutPivot"/> で再マップする。scale==1 または null は完全 no-op（従来挙動）。
        /// dict を in-place 更新。副作用は渡された dict と内部スクラッチのみ＝ヘッドレステスト可能。
        /// </summary>
        public static void ScaleBodyTargetsAboutPivot(
            IDictionary<HumanBodyBones, BoneTarget> targets, Vector3 pivot, float scale)
        {
            if (targets == null || scale == 1f) return;

            // 反復中に dict を変更できないので、対象キーを先に収集してから書き戻す。
            s_ScaleKeyScratch.Clear();
            foreach (var kv in targets)
            {
                var bone = kv.Key;
                if (bone == HumanBodyBones.Head ||
                    bone == HumanBodyBones.LeftHand ||
                    bone == HumanBodyBones.RightHand) continue;   // VR デバイス由来は対象外
                if (!kv.Value.HasPosition) continue;
                s_ScaleKeyScratch.Add(bone);
            }

            foreach (var bone in s_ScaleKeyScratch)
            {
                var t = targets[bone];
                t.Position = ScaleAboutPivot(t.Position, pivot, scale);
                targets[bone] = t;
            }
        }

        /// <summary>
        /// Capture full rotation + position offsets for every assigned slot
        /// while the performer stands in T-pose.
        /// </summary>
        /// <param name="animator">Humanoid animator of the avatar.</param>
        /// <param name="provider">Tracker source provider whose slots will be updated in-place.</param>
        public static void CalibrateAtTPose(Animator animator, ITrackerSourceProvider provider)
        {
            if (animator == null)
            {
                Debug.LogWarning("[FBTCalibrator] CalibrateAtTPose called with null animator.");
                return;
            }
            if (provider == null)
            {
                Debug.LogWarning("[FBTCalibrator] CalibrateAtTPose called with null provider.");
                return;
            }

            foreach (var slot in provider.Slots)
            {
                if (slot == null || slot.Kind == TrackerSlotKind.None) continue;
                if (slot.Source == null)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] Slot {slot.Kind} has no source; skipping.");
                    continue;
                }

                var bone = GetBoneForSlot(slot.Kind);
                if (!bone.HasValue)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] No HumanBodyBones mapping for slot {slot.Kind}; skipping.");
                    continue;
                }

                var boneTransform = animator.GetBoneTransform(bone.Value);
                if (boneTransform == null)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] Animator has no bone transform for {bone.Value} (slot {slot.Kind}); skipping.");
                    continue;
                }

                var trackerRot = slot.Source.rotation;
                var boneRot = boneTransform.rotation;
                slot.CalibrationRotOffset = Quaternion.Inverse(trackerRot) * boneRot;

                var trackerPos = slot.Source.position;
                var bonePos = boneTransform.position;
                slot.CalibrationPosOffset = Quaternion.Inverse(trackerRot) * (trackerPos - bonePos);
            }
        }

        /// <summary>
        /// Re-learn only the rotation offsets while keeping the previously
        /// captured position offsets. Useful for compensating IMU yaw drift
        /// without forcing the performer to re-do a full T-pose setup.
        /// </summary>
        public static void QuickReset(Animator animator, ITrackerSourceProvider provider)
        {
            if (animator == null)
            {
                Debug.LogWarning("[FBTCalibrator] QuickReset called with null animator.");
                return;
            }
            if (provider == null)
            {
                Debug.LogWarning("[FBTCalibrator] QuickReset called with null provider.");
                return;
            }

            foreach (var slot in provider.Slots)
            {
                if (slot == null || slot.Kind == TrackerSlotKind.None) continue;
                if (slot.Source == null)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] Slot {slot.Kind} has no source; skipping.");
                    continue;
                }

                var bone = GetBoneForSlot(slot.Kind);
                if (!bone.HasValue)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] No HumanBodyBones mapping for slot {slot.Kind}; skipping.");
                    continue;
                }

                var boneTransform = animator.GetBoneTransform(bone.Value);
                if (boneTransform == null)
                {
                    Debug.LogWarning(
                        $"[FBTCalibrator] Animator has no bone transform for {bone.Value} (slot {slot.Kind}); skipping.");
                    continue;
                }

                var trackerRot = slot.Source.rotation;
                var boneRot = boneTransform.rotation;
                slot.CalibrationRotOffset = Quaternion.Inverse(trackerRot) * boneRot;
                // Position offset is intentionally preserved.
            }
        }

        /// <summary>
        /// Map a tracker slot to the Humanoid bone it drives.
        /// Returns null when no direct correspondence exists (e.g. None).
        /// </summary>
        public static HumanBodyBones? GetBoneForSlot(TrackerSlotKind kind)
        {
            switch (kind)
            {
                case TrackerSlotKind.Head: return HumanBodyBones.Head;
                case TrackerSlotKind.LeftHand: return HumanBodyBones.LeftHand;
                case TrackerSlotKind.RightHand: return HumanBodyBones.RightHand;

                case TrackerSlotKind.UpperChest: return HumanBodyBones.UpperChest;
                case TrackerSlotKind.Chest: return HumanBodyBones.Chest;
                case TrackerSlotKind.Spine: return HumanBodyBones.Spine;
                case TrackerSlotKind.Waist: return HumanBodyBones.Hips;
                case TrackerSlotKind.Neck: return HumanBodyBones.Neck;

                case TrackerSlotKind.LeftShoulder: return HumanBodyBones.LeftShoulder;
                case TrackerSlotKind.RightShoulder: return HumanBodyBones.RightShoulder;
                case TrackerSlotKind.LeftUpperArm: return HumanBodyBones.LeftUpperArm;
                case TrackerSlotKind.RightUpperArm: return HumanBodyBones.RightUpperArm;
                case TrackerSlotKind.LeftLowerArm: return HumanBodyBones.LeftLowerArm;
                case TrackerSlotKind.RightLowerArm: return HumanBodyBones.RightLowerArm;

                case TrackerSlotKind.LeftUpperLeg: return HumanBodyBones.LeftUpperLeg;
                case TrackerSlotKind.RightUpperLeg: return HumanBodyBones.RightUpperLeg;
                case TrackerSlotKind.LeftLowerLeg: return HumanBodyBones.LeftLowerLeg;
                case TrackerSlotKind.RightLowerLeg: return HumanBodyBones.RightLowerLeg;
                case TrackerSlotKind.LeftFoot: return HumanBodyBones.LeftFoot;
                case TrackerSlotKind.RightFoot: return HumanBodyBones.RightFoot;
                case TrackerSlotKind.LeftToe: return HumanBodyBones.LeftToes;
                case TrackerSlotKind.RightToe: return HumanBodyBones.RightToes;

                case TrackerSlotKind.None:
                default:
                    return null;
            }
        }
    }
}
