namespace NYIK.Tracker
{
    /// <summary>
    /// Default trust weights for each tracker slot.
    /// Distal sensors (extremities) get lower weights because IMU drift
    /// accumulates further from the body root.
    ///
    /// Reference: Effect of downweighting distal IMU sensors when solving inverse kinematics
    /// (ResearchGate, fig 4).
    /// </summary>
    public static class DefaultTrustProfile
    {
        public static float Get(TrackerSlotKind kind)
        {
            switch (kind)
            {
                // Head and hands: optical+IMU hybrid or HMD-grade
                case TrackerSlotKind.Head: return 1.0f;
                case TrackerSlotKind.LeftHand: return 1.0f;
                case TrackerSlotKind.RightHand: return 1.0f;

                // Torso: stable, close to root
                case TrackerSlotKind.Chest: return 0.95f;
                case TrackerSlotKind.Waist: return 0.95f;
                case TrackerSlotKind.UpperChest: return 0.90f;
                case TrackerSlotKind.Spine: return 0.90f;
                case TrackerSlotKind.Neck: return 0.90f;

                // Shoulders: mounting drift moderate
                case TrackerSlotKind.LeftShoulder: return 0.85f;
                case TrackerSlotKind.RightShoulder: return 0.85f;

                // Upper arms / upper legs: moderate
                case TrackerSlotKind.LeftUpperArm: return 0.80f;
                case TrackerSlotKind.RightUpperArm: return 0.80f;
                case TrackerSlotKind.LeftUpperLeg: return 0.80f;
                case TrackerSlotKind.RightUpperLeg: return 0.80f;

                // Knees: anatomy keeps them aligned (low drift)
                case TrackerSlotKind.LeftLowerLeg: return 0.85f;
                case TrackerSlotKind.RightLowerLeg: return 0.85f;

                // Forearms: drift accumulates more than upper arm
                case TrackerSlotKind.LeftLowerArm: return 0.75f;
                case TrackerSlotKind.RightLowerArm: return 0.75f;

                // Feet/toes: most distal, max drift
                case TrackerSlotKind.LeftFoot: return 0.75f;
                case TrackerSlotKind.RightFoot: return 0.75f;
                case TrackerSlotKind.LeftToe: return 0.65f;
                case TrackerSlotKind.RightToe: return 0.65f;

                default: return 0.5f;
            }
        }
    }
}
