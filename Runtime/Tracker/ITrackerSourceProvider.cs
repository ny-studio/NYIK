using System.Collections.Generic;

namespace NYIK.Tracker
{
    /// <summary>
    /// Provides tracker slots for the IK pipeline. Implementations can read from
    /// SteamVR, OpenXR, manual scene assignment, or test mocks.
    /// </summary>
    public interface ITrackerSourceProvider
    {
        /// <summary>
        /// All slots managed by this provider. Slots without an assigned Source
        /// are returned as well; consumers should check IsAssigned/IsTracking.
        /// </summary>
        IReadOnlyList<TrackerSlot> Slots { get; }

        /// <summary>
        /// True when at least one full-body tracker (waist/chest/legs/etc.) is
        /// assigned and tracking. When false, the IK pipeline should fall back
        /// to 3-point HMD+controllers mode.
        /// </summary>
        bool HasFullBodyTrackers { get; }

        /// <summary>Get a specific slot by kind, or null if not assigned.</summary>
        TrackerSlot GetSlot(TrackerSlotKind kind);

        /// <summary>
        /// Called once per frame before the IK solver runs. Implementations
        /// should refresh IsTracking flags and any filtered values.
        /// </summary>
        void Tick(float deltaTime);
    }
}
