namespace NYIK.Tracker
{
    /// <summary>
    /// Identifies which body part a tracker is attached to.
    /// Maps roughly to Unity HumanBodyBones for direct correspondence.
    /// </summary>
    public enum TrackerSlotKind
    {
        None,

        // Head and hands (3-point tracking baseline)
        Head,
        LeftHand,
        RightHand,

        // Torso
        UpperChest,
        Chest,
        Spine,
        Waist,
        Neck,

        // Arms (shoulders and upper arms)
        LeftShoulder,
        RightShoulder,
        LeftUpperArm,
        RightUpperArm,
        LeftLowerArm,
        RightLowerArm,

        // Legs (upper, lower, foot)
        LeftUpperLeg,
        RightUpperLeg,
        LeftLowerLeg,
        RightLowerLeg,
        LeftFoot,
        RightFoot,
        LeftToe,
        RightToe,
    }
}
