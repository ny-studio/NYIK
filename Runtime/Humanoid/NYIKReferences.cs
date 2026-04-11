using System;
using System.Collections.Generic;
using UnityEngine;
using NYIK.Core;

namespace NYIK.Humanoid
{
    /// <summary>
    /// Holds humanoid bone references and supports auto-detection from a Humanoid rig.
    /// </summary>
    [Serializable]
    public class NYIKReferences
    {
        [Header("Root")]
        [SerializeField] Transform m_Root;

        [Header("Spine")]
        [SerializeField] Transform m_Pelvis;
        [SerializeField] Transform[] m_SpineBones;
        [SerializeField] Transform m_Head;

        [Header("Left Arm")]
        [SerializeField] Transform m_LeftShoulder;
        [SerializeField] Transform m_LeftUpperArm;
        [SerializeField] Transform m_LeftForearm;
        [SerializeField] Transform m_LeftHand;

        [Header("Right Arm")]
        [SerializeField] Transform m_RightShoulder;
        [SerializeField] Transform m_RightUpperArm;
        [SerializeField] Transform m_RightForearm;
        [SerializeField] Transform m_RightHand;

        [Header("Left Leg")]
        [SerializeField] Transform m_LeftThigh;
        [SerializeField] Transform m_LeftCalf;
        [SerializeField] Transform m_LeftFoot;

        [Header("Right Leg")]
        [SerializeField] Transform m_RightThigh;
        [SerializeField] Transform m_RightCalf;
        [SerializeField] Transform m_RightFoot;

        public Transform Root { get => m_Root; set => m_Root = value; }
        public Transform Pelvis { get => m_Pelvis; set => m_Pelvis = value; }
        public Transform[] SpineBones { get => m_SpineBones; set => m_SpineBones = value; }
        public Transform Head { get => m_Head; set => m_Head = value; }

        public Transform LeftShoulder { get => m_LeftShoulder; set => m_LeftShoulder = value; }
        public Transform LeftUpperArm { get => m_LeftUpperArm; set => m_LeftUpperArm = value; }
        public Transform LeftForearm { get => m_LeftForearm; set => m_LeftForearm = value; }
        public Transform LeftHand { get => m_LeftHand; set => m_LeftHand = value; }

        public Transform RightShoulder { get => m_RightShoulder; set => m_RightShoulder = value; }
        public Transform RightUpperArm { get => m_RightUpperArm; set => m_RightUpperArm = value; }
        public Transform RightForearm { get => m_RightForearm; set => m_RightForearm = value; }
        public Transform RightHand { get => m_RightHand; set => m_RightHand = value; }

        public Transform LeftThigh { get => m_LeftThigh; set => m_LeftThigh = value; }
        public Transform LeftCalf { get => m_LeftCalf; set => m_LeftCalf = value; }
        public Transform LeftFoot { get => m_LeftFoot; set => m_LeftFoot = value; }

        public Transform RightThigh { get => m_RightThigh; set => m_RightThigh = value; }
        public Transform RightCalf { get => m_RightCalf; set => m_RightCalf = value; }
        public Transform RightFoot { get => m_RightFoot; set => m_RightFoot = value; }

        /// <summary>
        /// Auto-detects bone references from the Animator's Humanoid rig.
        /// </summary>
        /// <returns>True if detection was successful.</returns>
        public bool AutoDetect(Animator animator)
        {
            if (animator == null || !animator.isHuman)
                return false;

            m_Root = animator.transform;

            m_Pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);

            // Build spine chain (Spine -> Chest -> UpperChest -> Neck)
            var spineList = new List<Transform>();
            AddBoneIfExists(spineList, animator, HumanBodyBones.Spine);
            AddBoneIfExists(spineList, animator, HumanBodyBones.Chest);
            AddBoneIfExists(spineList, animator, HumanBodyBones.UpperChest);
            AddBoneIfExists(spineList, animator, HumanBodyBones.Neck);
            m_SpineBones = spineList.ToArray();

            // Left arm
            m_LeftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            m_LeftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            m_LeftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            m_LeftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

            // Right arm
            m_RightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            m_RightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            m_RightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            m_RightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            // Left leg
            m_LeftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            m_LeftCalf = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            m_LeftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            // Right leg
            m_RightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            m_RightCalf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            m_RightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            return IsValid();
        }

        /// <summary>
        /// Validates that all required bones are assigned.
        /// </summary>
        public bool IsValid()
        {
            // Minimum required bones
            return m_Root != null
                && m_Pelvis != null
                && m_Head != null
                && m_LeftUpperArm != null
                && m_LeftForearm != null
                && m_LeftHand != null
                && m_RightUpperArm != null
                && m_RightForearm != null
                && m_RightHand != null
                && m_LeftThigh != null
                && m_LeftCalf != null
                && m_LeftFoot != null
                && m_RightThigh != null
                && m_RightCalf != null
                && m_RightFoot != null;
        }

        /// <summary>
        /// Returns warning messages for unassigned bones.
        /// </summary>
        public List<string> GetWarnings()
        {
            var warnings = new List<string>();
            if (m_Root == null) warnings.Add("Root is not assigned.");
            if (m_Pelvis == null) warnings.Add("Pelvis is not assigned.");
            if (m_Head == null) warnings.Add("Head is not assigned.");
            if (m_SpineBones == null || m_SpineBones.Length == 0)
                warnings.Add("No spine bones assigned. At least one spine bone is recommended.");
            if (m_LeftUpperArm == null) warnings.Add("Left Upper Arm is not assigned.");
            if (m_LeftForearm == null) warnings.Add("Left Forearm is not assigned.");
            if (m_LeftHand == null) warnings.Add("Left Hand is not assigned.");
            if (m_RightUpperArm == null) warnings.Add("Right Upper Arm is not assigned.");
            if (m_RightForearm == null) warnings.Add("Right Forearm is not assigned.");
            if (m_RightHand == null) warnings.Add("Right Hand is not assigned.");
            if (m_LeftThigh == null) warnings.Add("Left Thigh is not assigned.");
            if (m_LeftCalf == null) warnings.Add("Left Calf is not assigned.");
            if (m_LeftFoot == null) warnings.Add("Left Foot is not assigned.");
            if (m_RightThigh == null) warnings.Add("Right Thigh is not assigned.");
            if (m_RightCalf == null) warnings.Add("Right Calf is not assigned.");
            if (m_RightFoot == null) warnings.Add("Right Foot is not assigned.");
            return warnings;
        }

        static void AddBoneIfExists(List<Transform> list, Animator animator, HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            if (t != null)
                list.Add(t);
        }
    }
}
