using System;
using System.Collections.Generic;
using UnityEngine;

namespace NYIK.Anatomy
{
    /// <summary>
    /// Per-avatar Joint Range of Motion data. Overrides the static defaults
    /// in <see cref="JointROMLimits"/> for joints listed in <see cref="Entries"/>.
    ///
    /// Use cases:
    /// - Swap in athletic / restricted / pediatric profiles per avatar
    /// - Per-character tuning (Milltina vs sotai)
    /// - Research / validation against alternative anatomical references
    ///
    /// Entries are matched by <see cref="JointEntry.Bone"/>. Bones not listed
    /// fall back to the static AAOS-based defaults baked into JointROMLimits.
    /// </summary>
    [CreateAssetMenu(menuName = "NYIK/Joint ROM Reference", fileName = "JointROMReference")]
    public sealed class JointROMReference : ScriptableObject
    {
        /// <summary>
        /// Bibliographic source for the values in this reference. Required —
        /// without a citation, values are just opinions.
        /// </summary>
        [TextArea(3, 8)]
        public string Source =
            "Cite the anatomical reference used (e.g. AAOS 1965, Norkin & White 2016, ISB 2002).";

        public List<JointEntry> Entries = new();

        [Serializable]
        public class JointEntry
        {
            public HumanBodyBones Bone;

            [Tooltip("If true, use swing-twist limits; otherwise use Euler limits.")]
            public bool UseSwingTwist;

            // Euler limit (degrees, bone-local axes)
            public Vector3 EulerMin;
            public Vector3 EulerMax;

            // Swing-twist limit
            public Vector3 TwistAxis = Vector3.up;
            public float TwistMinDeg;
            public float TwistMaxDeg;
            public float SwingMaxDeg;

            [TextArea(2, 4)]
            public string Citation;

            public JointROMLimits.EulerLimit ToEulerLimit() => new JointROMLimits.EulerLimit(EulerMin, EulerMax);

            public JointROMLimits.SwingTwistLimit ToSwingTwistLimit() => new JointROMLimits.SwingTwistLimit
            {
                TwistAxis = TwistAxis,
                TwistMinDeg = TwistMinDeg,
                TwistMaxDeg = TwistMaxDeg,
                SwingMaxDeg = SwingMaxDeg,
            };
        }

        public bool TryGetEntry(HumanBodyBones bone, out JointEntry entry)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i] != null && Entries[i].Bone == bone)
                {
                    entry = Entries[i];
                    return true;
                }
            }
            entry = null;
            return false;
        }
    }
}
