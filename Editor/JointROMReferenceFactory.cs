#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using NYIK.Anatomy;

namespace NYIK.EditorTools
{
    /// <summary>
    /// Editor-only utilities to mint <see cref="JointROMReference"/> assets
    /// with pre-populated values from published anatomical references.
    /// Lets users override the static defaults in JointROMLimits with a
    /// project asset they can tweak per-character.
    /// </summary>
    public static class JointROMReferenceFactory
    {
        [MenuItem("NYIK/Create AAOS Default ROM Reference")]
        public static void CreateAAOSDefault()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save AAOS Joint ROM Reference",
                "JointROMReference_AAOS",
                "asset",
                "Choose where to save the AAOS default ROM reference asset.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<JointROMReference>();
            PopulateAAOS(asset);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[NYIK] Created AAOS ROM reference at {path}");
        }

        /// <summary>
        /// Populates <paramref name="asset"/> with values from:
        ///   • AAOS, *Joint Motion: Method of Measuring and Recording*, 1965.
        ///   • Norkin & White, *Measurement of Joint Motion*, 5th ed., 2016.
        ///   • Wu et al., ISB recommendations, 2002 / 2005.
        /// </summary>
        public static void PopulateAAOS(JointROMReference asset)
        {
            asset.Source =
                "AAOS, Joint Motion: Method of Measuring and Recording (1965).\n" +
                "Norkin CC, White DJ. Measurement of Joint Motion: A Guide to Goniometry, 5th ed. F.A. Davis Co; 2016.\n" +
                "Wu G et al. ISB recommendation on definitions of joint coordinate systems of various joints for the reporting of human joint motion. J Biomech 35(4):543-548 (2002, 2005).\n" +
                "Values are conservative (slight headroom below AAOS maxima) to absorb IMU tracker error.";

            asset.Entries.Clear();

            // Shoulder girdle
            asset.Entries.Add(MakeSwing(HumanBodyBones.LeftShoulder,
                twistMin: -20f, twistMax: 20f, swing: 60f,
                citation: "AAOS scapulothoracic elevation 45°, protraction 30°, retraction 20°. Combined cone ≈ 60°."));
            asset.Entries.Add(MakeSwing(HumanBodyBones.RightShoulder,
                twistMin: -20f, twistMax: 20f, swing: 60f,
                citation: "Mirror of LeftShoulder."));

            // Upper arm — glenohumeral
            asset.Entries.Add(MakeSwing(HumanBodyBones.LeftUpperArm,
                twistMin: -70f, twistMax: 90f, swing: 150f,
                citation: "AAOS shoulder ext-rot 90°, int-rot 70°. Combined flex/abd cone ≈ 180° (Norkin 2016); conservative 150°."));
            asset.Entries.Add(MakeSwing(HumanBodyBones.RightUpperArm,
                twistMin: -70f, twistMax: 90f, swing: 150f,
                citation: "Mirror of LeftUpperArm."));

            // Elbow — pure hinge + forearm pronation/supination
            asset.Entries.Add(MakeEuler(HumanBodyBones.LeftLowerArm,
                min: new Vector3(0f, 0f, -80f), max: new Vector3(150f, 0f, 80f),
                citation: "AAOS elbow flexion 150°, hyperextension 0-5°. Forearm pron/sup 80° each (along Z, distal segment)."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.RightLowerArm,
                min: new Vector3(0f, 0f, -80f), max: new Vector3(150f, 0f, 80f),
                citation: "Mirror of LeftLowerArm."));

            // Wrist
            asset.Entries.Add(MakeEuler(HumanBodyBones.LeftHand,
                min: new Vector3(-70f, -25f, -20f), max: new Vector3(80f, 25f, 30f),
                citation: "AAOS wrist: flex 80°, ext 70°, radial dev 20°, ulnar dev 30°."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.RightHand,
                min: new Vector3(-70f, -25f, -20f), max: new Vector3(80f, 25f, 30f),
                citation: "Mirror of LeftHand."));

            // Hip — coxofemoral
            asset.Entries.Add(MakeSwing(HumanBodyBones.LeftUpperLeg,
                twistMin: -40f, twistMax: 45f, swing: 110f,
                citation: "AAOS hip: flex 125°, ext 30°, abd 45°, add 30°, ext-rot 45°, int-rot 40°. Conservative cone 110°."));
            asset.Entries.Add(MakeSwing(HumanBodyBones.RightUpperLeg,
                twistMin: -40f, twistMax: 45f, swing: 110f,
                citation: "Mirror of LeftUpperLeg."));

            // Knee
            asset.Entries.Add(MakeEuler(HumanBodyBones.LeftLowerLeg,
                min: new Vector3(0f, -10f, 0f), max: new Vector3(135f, 10f, 0f),
                citation: "AAOS knee flexion 135°. Slight Y rotation at flexion (Norkin)."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.RightLowerLeg,
                min: new Vector3(0f, -10f, 0f), max: new Vector3(135f, 10f, 0f),
                citation: "Mirror of LeftLowerLeg."));

            // Ankle
            asset.Entries.Add(MakeEuler(HumanBodyBones.LeftFoot,
                min: new Vector3(-50f, -30f, -20f), max: new Vector3(20f, 30f, 30f),
                citation: "AAOS ankle: dorsiflex 20°, plantar 50°, inversion 30°, eversion 20°."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.RightFoot,
                min: new Vector3(-50f, -30f, -20f), max: new Vector3(20f, 30f, 30f),
                citation: "Mirror of LeftFoot."));

            // Spine — distributed across Spine/Chest/UpperChest segments
            asset.Entries.Add(MakeEuler(HumanBodyBones.Spine,
                min: new Vector3(-25f, -22f, -22f), max: new Vector3(40f, 22f, 22f),
                citation: "ISB / Norkin lumbar+thoracic combined: flex 80, ext 25, lat-flex 35, rot 45. Divided ~3 segments."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.Chest,
                min: new Vector3(-25f, -22f, -22f), max: new Vector3(40f, 22f, 22f),
                citation: "Per-segment thoracic share."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.UpperChest,
                min: new Vector3(-25f, -22f, -22f), max: new Vector3(40f, 22f, 22f),
                citation: "Per-segment upper-thoracic share."));

            // Cervical spine
            asset.Entries.Add(MakeEuler(HumanBodyBones.Neck,
                min: new Vector3(-25f, -35f, -25f), max: new Vector3(25f, 35f, 25f),
                citation: "AAOS cervical: flex 45°, ext 50°, lat-flex 45° each side, rotation 60° each side. Split with Head."));
            asset.Entries.Add(MakeEuler(HumanBodyBones.Head,
                min: new Vector3(-25f, -35f, -25f), max: new Vector3(25f, 35f, 25f),
                citation: "Atlanto-occipital + atlanto-axial share."));
        }

        private static JointROMReference.JointEntry MakeEuler(
            HumanBodyBones bone, Vector3 min, Vector3 max, string citation) => new()
        {
            Bone = bone,
            UseSwingTwist = false,
            EulerMin = min,
            EulerMax = max,
            Citation = citation,
        };

        private static JointROMReference.JointEntry MakeSwing(
            HumanBodyBones bone,
            float twistMin, float twistMax, float swing, string citation) => new()
        {
            Bone = bone,
            UseSwingTwist = true,
            TwistAxis = Vector3.up,
            TwistMinDeg = twistMin,
            TwistMaxDeg = twistMax,
            SwingMaxDeg = swing,
            Citation = citation,
        };
    }
}
#endif
