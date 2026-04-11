using UnityEditor;
using UnityEngine;
using NYIK.Humanoid;

namespace NYIK.Editor
{
    /// <summary>
    /// Scene view handles for NYIKTestTargets.
    /// </summary>
    [CustomEditor(typeof(NYIKTestTargets))]
    public class NYIKTestTargetsEditor : UnityEditor.Editor
    {
        static readonly Color k_HeadColor = new Color(1f, 0.3f, 0.3f, 1f);
        static readonly Color k_LeftColor = new Color(0.3f, 0.5f, 1f, 1f);
        static readonly Color k_RightColor = new Color(0.3f, 1f, 0.5f, 1f);

        NYIKTestTargets Target => (NYIKTestTargets)target;

        void OnSceneGUI()
        {
            if (Application.isPlaying) return;
            DrawHandle(Target.HeadTarget, "Head", k_HeadColor);
            DrawHandle(Target.LeftHandTarget, "L Hand", k_LeftColor);
            DrawHandle(Target.RightHandTarget, "R Hand", k_RightColor);
            DrawHandle(Target.LeftFootTarget, "L Foot", k_LeftColor);
            DrawHandle(Target.RightFootTarget, "R Foot", k_RightColor);
        }

        static void DrawHandle(Transform t, string label, Color color)
        {
            if (t == null) return;

            Handles.color = color;
            Handles.Label(t.position + Vector3.up * 0.06f, label, EditorStyles.whiteBoldLabel);
            Handles.SphereHandleCap(0, t.position, Quaternion.identity, 0.025f, EventType.Repaint);

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(t.position, t.rotation);
            Quaternion newRot = Handles.RotationHandle(t.rotation, t.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, $"Move {label}");
                t.position = newPos;
                t.rotation = newRot;
            }
        }
    }
}
