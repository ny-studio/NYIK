using System.Collections.Generic;
using UnityEngine;
using NYIK.Tracker;

namespace NYIK.Calibration
{
    /// <summary>キャリブ姿勢。VRChat 既定は I-pose(直立・腕を体側に下ろす)、T-pose は legacy。</summary>
    public enum CalibrationPose { IPose, TPose }

    /// <summary>
    /// Heuristic check that the performer is actually in T-pose before we
    /// commit calibration offsets. Catches the common failure mode of
    /// pressing "Calibrate" while standing relaxed — bad offsets silently
    /// persist and the avatar looks broken every session afterward.
    ///
    /// Geometry checks (all relative to the head tracker):
    ///   - Head upright: HMD's up vector is within ~30° of world up.
    ///   - Hands at shoulder height: each hand's Y is within ±20 cm of head Y - shoulderDrop.
    ///   - Hands extended outward: |hand.x - head.x| > horizontalExtensionMin (~0.4 m).
    ///   - Hands roughly horizontal: |hand.z - head.z| &lt; depthDriftMax (~0.25 m).
    ///   - Hands mirrored: left.x &lt; head.x &lt; right.x.
    /// </summary>
    public static class TPoseValidator
    {
        public readonly struct Result
        {
            public readonly bool IsTPose;
            public readonly IReadOnlyList<string> Issues;

            public Result(bool ok, IReadOnlyList<string> issues)
            {
                IsTPose = ok;
                Issues = issues;
            }

            public string Summary => IsTPose
                ? "T-pose OK."
                : "Not T-pose: " + string.Join("; ", Issues);
        }

        public struct Tolerances
        {
            public float HeadUprightDegMax;        // 30
            public float ShoulderDropFromHead;     // 0.20 (hands ~20cm below head)
            public float HandYRangeFromShoulder;   // 0.20
            public float HorizontalExtensionMin;   // 0.40 each side
            public float DepthDriftMax;            // 0.25
            public static Tolerances Default => new()
            {
                HeadUprightDegMax = 30f,
                ShoulderDropFromHead = 0.20f,
                HandYRangeFromShoulder = 0.20f,
                HorizontalExtensionMin = 0.40f,
                DepthDriftMax = 0.25f,
            };
        }

        public static Result Validate(ITrackerSourceProvider provider, Tolerances tol = default)
        {
            var t = tol.HeadUprightDegMax > 0f ? tol : Tolerances.Default;
            var issues = new List<string>();

            if (provider == null)
            {
                issues.Add("provider is null");
                return new Result(false, issues);
            }

            var head = provider.GetSlot(TrackerSlotKind.Head);
            var left = provider.GetSlot(TrackerSlotKind.LeftHand);
            var right = provider.GetSlot(TrackerSlotKind.RightHand);

            if (head == null || !head.IsAssigned || !head.IsTracking)
                issues.Add("head not tracking");
            if (left == null || !left.IsAssigned || !left.IsTracking)
                issues.Add("left hand not tracking");
            if (right == null || !right.IsAssigned || !right.IsTracking)
                issues.Add("right hand not tracking");

            if (issues.Count > 0) return new Result(false, issues);

            // Use source transforms directly (effective override is fine).
            Vector3 headPos = head.Source.position;
            Quaternion headRot = head.Source.rotation;
            Vector3 leftPos = left.Source.position;
            Vector3 rightPos = right.Source.position;

            // 1. Head upright
            float headTiltDeg = Vector3.Angle(headRot * Vector3.up, Vector3.up);
            if (headTiltDeg > t.HeadUprightDegMax)
                issues.Add($"head tilted {headTiltDeg:F0}° from upright (max {t.HeadUprightDegMax:F0}°)");

            // 2. Hands at shoulder height (head Y - shoulderDrop) ± range
            float shoulderY = headPos.y - t.ShoulderDropFromHead;
            float leftYDelta = Mathf.Abs(leftPos.y - shoulderY);
            float rightYDelta = Mathf.Abs(rightPos.y - shoulderY);
            if (leftYDelta > t.HandYRangeFromShoulder)
                issues.Add($"left hand {leftYDelta * 100f:F0}cm off shoulder height");
            if (rightYDelta > t.HandYRangeFromShoulder)
                issues.Add($"right hand {rightYDelta * 100f:F0}cm off shoulder height");

            // 3. Hands extended outward in world X
            float leftExtension = headPos.x - leftPos.x;   // positive if left is at -X of head
            float rightExtension = rightPos.x - headPos.x; // positive if right is at +X of head
            if (leftExtension < t.HorizontalExtensionMin)
                issues.Add($"left hand only {leftExtension * 100f:F0}cm out (need ≥{t.HorizontalExtensionMin * 100f:F0}cm)");
            if (rightExtension < t.HorizontalExtensionMin)
                issues.Add($"right hand only {rightExtension * 100f:F0}cm out (need ≥{t.HorizontalExtensionMin * 100f:F0}cm)");

            // 4. Hands not far forward or backward
            float leftZDrift = Mathf.Abs(leftPos.z - headPos.z);
            float rightZDrift = Mathf.Abs(rightPos.z - headPos.z);
            if (leftZDrift > t.DepthDriftMax)
                issues.Add($"left hand drifted {leftZDrift * 100f:F0}cm forward/back");
            if (rightZDrift > t.DepthDriftMax)
                issues.Add($"right hand drifted {rightZDrift * 100f:F0}cm forward/back");

            return new Result(issues.Count == 0, issues);
        }

        public struct ITolerances
        {
            public float HeadUprightDegMax;     // 30
            public float HandMinDropFromHead;   // 0.45 (hands clearly below shoulder)
            public float HandMaxDropFromHead;   // 1.15 (sanity lower bound)
            public float HandHalfWidthMin;      // 0.05 (mirrored, not crossed to center)
            public float HandHalfWidthMax;      // 0.35 (at sides, not extended like T-pose)
            public float DepthDriftMax;         // 0.30
            public static ITolerances Default => new()
            {
                HeadUprightDegMax = 30f,
                HandMinDropFromHead = 0.45f,
                HandMaxDropFromHead = 1.15f,
                HandHalfWidthMin = 0.05f,
                HandHalfWidthMax = 0.35f,
                DepthDriftMax = 0.30f,
            };
        }

        /// <summary>
        /// I-pose（直立・腕を体側に下ろす）の検証。VRChat 既定姿勢。T-pose（腕を肩高で外に伸ばす）と
        /// 逆の幾何: 手が低く(肩より下)・体側に近い(narrow X)・前後ドリフト小・左右ミラー。
        /// キャリブ数学(FBTCalibrator)は姿勢非依存なので、検証だけ姿勢別に切り替える。
        /// </summary>
        public static Result ValidateIPose(ITrackerSourceProvider provider, ITolerances tol = default)
        {
            var t = tol.HeadUprightDegMax > 0f ? tol : ITolerances.Default;
            var issues = new List<string>();

            if (provider == null)
            {
                issues.Add("provider is null");
                return new Result(false, issues);
            }

            var head = provider.GetSlot(TrackerSlotKind.Head);
            var left = provider.GetSlot(TrackerSlotKind.LeftHand);
            var right = provider.GetSlot(TrackerSlotKind.RightHand);

            if (head == null || !head.IsAssigned || !head.IsTracking) issues.Add("head not tracking");
            if (left == null || !left.IsAssigned || !left.IsTracking) issues.Add("left hand not tracking");
            if (right == null || !right.IsAssigned || !right.IsTracking) issues.Add("right hand not tracking");
            if (issues.Count > 0) return new Result(false, issues);

            Vector3 headPos = head.Source.position;
            Quaternion headRot = head.Source.rotation;
            Vector3 leftPos = left.Source.position;
            Vector3 rightPos = right.Source.position;

            // 1. Head upright
            float headTiltDeg = Vector3.Angle(headRot * Vector3.up, Vector3.up);
            if (headTiltDeg > t.HeadUprightDegMax)
                issues.Add($"head tilted {headTiltDeg:F0}° from upright (max {t.HeadUprightDegMax:F0}°)");

            // 2. Hands hang low (clearly below shoulder, within a sane range)
            float leftDrop = headPos.y - leftPos.y;
            float rightDrop = headPos.y - rightPos.y;
            if (leftDrop < t.HandMinDropFromHead || leftDrop > t.HandMaxDropFromHead)
                issues.Add($"left hand drop {leftDrop * 100f:F0}cm outside I-pose range");
            if (rightDrop < t.HandMinDropFromHead || rightDrop > t.HandMaxDropFromHead)
                issues.Add($"right hand drop {rightDrop * 100f:F0}cm outside I-pose range");

            // 3. Hands at sides (narrow X, mirrored — not extended like T-pose)
            float leftHalf = headPos.x - leftPos.x;   // positive if left is at -X of head
            float rightHalf = rightPos.x - headPos.x; // positive if right is at +X of head
            if (leftHalf < t.HandHalfWidthMin || leftHalf > t.HandHalfWidthMax)
                issues.Add($"left hand {leftHalf * 100f:F0}cm from center (I-pose {t.HandHalfWidthMin * 100f:F0}-{t.HandHalfWidthMax * 100f:F0}cm)");
            if (rightHalf < t.HandHalfWidthMin || rightHalf > t.HandHalfWidthMax)
                issues.Add($"right hand {rightHalf * 100f:F0}cm from center (I-pose {t.HandHalfWidthMin * 100f:F0}-{t.HandHalfWidthMax * 100f:F0}cm)");

            // 4. Hands not far forward/back
            float leftZDrift = Mathf.Abs(leftPos.z - headPos.z);
            float rightZDrift = Mathf.Abs(rightPos.z - headPos.z);
            if (leftZDrift > t.DepthDriftMax) issues.Add($"left hand drifted {leftZDrift * 100f:F0}cm fwd/back");
            if (rightZDrift > t.DepthDriftMax) issues.Add($"right hand drifted {rightZDrift * 100f:F0}cm fwd/back");

            return new Result(issues.Count == 0, issues);
        }
    }
}
