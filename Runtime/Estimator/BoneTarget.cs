using UnityEngine;

namespace NYIK.Estimator
{
    /// <summary>
    /// Resolved target pose for a single Humanoid bone. Filled either by a
    /// live tracker (high confidence) or by an estimator (lower confidence)
    /// during the unified solve pipeline.
    ///
    /// HasPosition / HasRotation let estimators contribute partial data —
    /// for example, the bend-goal estimators only set Position (the
    /// direction the elbow should point), not Rotation.
    /// </summary>
    public struct BoneTarget
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool HasPosition;
        public bool HasRotation;

        /// <summary>
        /// Source confidence: 1.0 = live tracker, lower = estimated. Allows
        /// downstream consumers to weight contributions when blending.
        /// </summary>
        public float Confidence;

        public static BoneTarget Tracked(Vector3 pos, Quaternion rot) => new()
        {
            Position = pos,
            Rotation = rot,
            HasPosition = true,
            HasRotation = true,
            Confidence = 1f,
        };

        public static BoneTarget Estimated(Vector3 pos, Quaternion rot, float confidence) => new()
        {
            Position = pos,
            Rotation = rot,
            HasPosition = true,
            HasRotation = true,
            Confidence = Mathf.Clamp01(confidence),
        };

        public static BoneTarget PositionOnly(Vector3 pos, float confidence) => new()
        {
            Position = pos,
            HasPosition = true,
            Confidence = Mathf.Clamp01(confidence),
        };

        public static BoneTarget RotationOnly(Quaternion rot, float confidence) => new()
        {
            Rotation = rot,
            HasRotation = true,
            Confidence = Mathf.Clamp01(confidence),
        };
    }
}
