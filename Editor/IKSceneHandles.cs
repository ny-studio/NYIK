using UnityEditor;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Editor
{
    /// <summary>
    /// Scene view gizmos for NYIKHumanoid. Visualizes bone connection lines,
    /// IK targets, and bend goal directions.
    /// Draws via the DrawGizmo attribute, so no CustomEditor is required.
    /// </summary>
    public static class IKSceneHandles
    {
        static readonly Color k_BoneColor = new Color(0.2f, 0.8f, 0.4f, 0.8f);
        static readonly Color k_SpineColor = new Color(0.8f, 0.6f, 0.2f, 0.8f);
        static readonly Color k_TargetColor = new Color(1f, 0.3f, 0.3f, 0.8f);

        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        static void DrawGizmos(NYIKHumanoid humanoid, GizmoType gizmoType)
        {
            if (humanoid == null || humanoid.References == null)
                return;

            var refs = humanoid.References;

            // Spine chain
            DrawBoneChain(refs.Pelvis, refs.SpineBones, refs.Head, k_SpineColor);

            // Left arm
            DrawBoneLine(refs.LeftShoulder, refs.LeftUpperArm, k_BoneColor);
            DrawBoneLine(refs.LeftUpperArm, refs.LeftForearm, k_BoneColor);
            DrawBoneLine(refs.LeftForearm, refs.LeftHand, k_BoneColor);

            // Right arm
            DrawBoneLine(refs.RightShoulder, refs.RightUpperArm, k_BoneColor);
            DrawBoneLine(refs.RightUpperArm, refs.RightForearm, k_BoneColor);
            DrawBoneLine(refs.RightForearm, refs.RightHand, k_BoneColor);

            // Left leg
            DrawBoneLine(refs.Pelvis, refs.LeftThigh, k_BoneColor);
            DrawBoneLine(refs.LeftThigh, refs.LeftCalf, k_BoneColor);
            DrawBoneLine(refs.LeftCalf, refs.LeftFoot, k_BoneColor);

            // Right leg
            DrawBoneLine(refs.Pelvis, refs.RightThigh, k_BoneColor);
            DrawBoneLine(refs.RightThigh, refs.RightCalf, k_BoneColor);
            DrawBoneLine(refs.RightCalf, refs.RightFoot, k_BoneColor);

            // Display IK targets (at runtime)
            if (Application.isPlaying)
            {
                DrawTarget(humanoid.HeadTarget.Position, humanoid.HeadTarget.IsTracking);
                DrawTarget(humanoid.LeftHandTarget.Position, humanoid.LeftHandTarget.IsTracking);
                DrawTarget(humanoid.RightHandTarget.Position, humanoid.RightHandTarget.IsTracking);
            }

            // Bone joint points
            DrawJointSpheres(refs);
        }

        static void DrawBoneLine(Transform from, Transform to, Color color)
        {
            if (from == null || to == null)
                return;

            Handles.color = color;
            Handles.DrawLine(from.position, to.position, 2f);
        }

        static void DrawBoneChain(Transform start, Transform[] mid, Transform end, Color color)
        {
            if (start == null || end == null)
                return;

            Handles.color = color;

            Transform prev = start;
            if (mid != null)
            {
                foreach (var bone in mid)
                {
                    if (bone == null) continue;
                    Handles.DrawLine(prev.position, bone.position, 2f);
                    prev = bone;
                }
            }
            Handles.DrawLine(prev.position, end.position, 2f);
        }

        static void DrawTarget(Vector3 position, bool isTracking)
        {
            Handles.color = isTracking ? k_TargetColor : Color.gray;
            Handles.SphereHandleCap(0, position, Quaternion.identity, 0.03f, EventType.Repaint);
        }

        static void DrawJointSpheres(NYIKReferences refs)
        {
            Handles.color = k_BoneColor;
            float radius = 0.015f;

            DrawJoint(refs.Pelvis, radius);
            DrawJoint(refs.Head, radius);
            DrawJoint(refs.LeftShoulder, radius);
            DrawJoint(refs.LeftUpperArm, radius);
            DrawJoint(refs.LeftForearm, radius);
            DrawJoint(refs.LeftHand, radius);
            DrawJoint(refs.RightShoulder, radius);
            DrawJoint(refs.RightUpperArm, radius);
            DrawJoint(refs.RightForearm, radius);
            DrawJoint(refs.RightHand, radius);
            DrawJoint(refs.LeftThigh, radius);
            DrawJoint(refs.LeftCalf, radius);
            DrawJoint(refs.LeftFoot, radius);
            DrawJoint(refs.RightThigh, radius);
            DrawJoint(refs.RightCalf, radius);
            DrawJoint(refs.RightFoot, radius);

            if (refs.SpineBones != null)
            {
                Handles.color = k_SpineColor;
                foreach (var bone in refs.SpineBones)
                    DrawJoint(bone, radius);
            }
        }

        static void DrawJoint(Transform joint, float radius)
        {
            if (joint == null) return;
            Handles.SphereHandleCap(0, joint.position, Quaternion.identity, radius, EventType.Repaint);
        }
    }
}
