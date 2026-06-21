using System;
using UnityEngine;

namespace NYIK.Tracker
{
    /// <summary>
    /// Runtime representation of a tracker assigned to a body part.
    /// Combines raw Transform source + calibration offsets + trust weight.
    ///
    /// Effective values:
    ///   The provider's Tick() may call <see cref="SetEffective"/> each frame to
    ///   override the rotation/position used by <see cref="CalibratedRotation"/>
    ///   and <see cref="CalibratedPosition"/>. This is the hook for filtering,
    ///   prediction, smoothing, etc. When the provider doesn't override, the
    ///   slot falls back to reading <see cref="Source"/> directly.
    /// </summary>
    [Serializable]
    public class TrackerSlot
    {
        public TrackerSlotKind Kind;

        /// <summary>SteamVR / OpenXR tracked device transform.</summary>
        public Transform Source;

        /// <summary>Rotation offset learned at T-pose calibration.</summary>
        public Quaternion CalibrationRotOffset = Quaternion.identity;

        /// <summary>Position offset (tracker-local) learned at calibration.</summary>
        public Vector3 CalibrationPosOffset = Vector3.zero;

        /// <summary>Trust weight 0..1. Distal sensors should have lower weight.</summary>
        [Range(0f, 1f)]
        public float TrustWeight = 1.0f;

        /// <summary>Whether the user has manually or auto-assigned a source.</summary>
        public bool IsAssigned => Source != null;

        /// <summary>Whether the source is currently producing valid data (driven externally).</summary>
        public bool IsTracking;

        // Effective values overridden by the provider (e.g. filtered pose).
        // When _hasEffective is false, CalibratedRotation/Position fall back to Source.
        private bool _hasEffective;
        private Vector3 _effectivePosition;
        private Quaternion _effectiveRotation = Quaternion.identity;

        /// <summary>
        /// Override the effective pose used by <see cref="CalibratedRotation"/>
        /// and <see cref="CalibratedPosition"/>. Providers call this from Tick()
        /// to inject filtered, smoothed or predicted values. Cleared by
        /// <see cref="ClearEffective"/> or by reassigning <see cref="Source"/>.
        /// </summary>
        public void SetEffective(Vector3 position, Quaternion rotation)
        {
            _effectivePosition = position;
            _effectiveRotation = rotation;
            _hasEffective = true;
        }

        /// <summary>Remove the effective override. Subsequent reads use Source directly.</summary>
        public void ClearEffective() => _hasEffective = false;

        /// <summary>
        /// Rotation after calibration offset is applied.
        /// Returns identity when no source.
        /// </summary>
        public Quaternion CalibratedRotation
        {
            get
            {
                if (Source == null) return Quaternion.identity;
                var rot = _hasEffective ? _effectiveRotation : Source.rotation;
                return rot * CalibrationRotOffset;
            }
        }

        /// <summary>
        /// Position after calibration offset is applied.
        /// PositionOffset is stored in tracker-local space, so we rotate it
        /// by the current tracker rotation to get the world delta.
        /// </summary>
        public Vector3 CalibratedPosition
        {
            get
            {
                if (Source == null) return Vector3.zero;
                var pos = _hasEffective ? _effectivePosition : Source.position;
                var rot = _hasEffective ? _effectiveRotation : Source.rotation;
                return pos - rot * CalibrationPosOffset;
            }
        }

        public TrackerSlot() { }

        public TrackerSlot(TrackerSlotKind kind)
        {
            Kind = kind;
            TrustWeight = DefaultTrustProfile.Get(kind);
        }
    }
}
