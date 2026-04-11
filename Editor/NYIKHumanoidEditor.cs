using UnityEditor;
using UnityEngine;
using NYIK.Humanoid;
using NYIK.Validation;

namespace NYIK.Editor
{
    /// <summary>
    /// Custom inspector for NYIKHumanoid.
    /// Provides automatic bone detection buttons, warning display, and setup state visualization.
    /// </summary>
    [CustomEditor(typeof(NYIKHumanoid))]
    public class NYIKHumanoidEditor : UnityEditor.Editor
    {
        NYIKHumanoid m_Target;

        void OnEnable()
        {
            m_Target = (NYIKHumanoid)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header
            EditorGUILayout.LabelField("NYIK Humanoid IK", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Auto detect button
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Detect Bones", GUILayout.Height(30)))
            {
                Undo.RecordObject(m_Target, "Auto Detect Bones");
                m_Target.AutoDetectBones();
                EditorUtility.SetDirty(m_Target);
            }

            if (GUILayout.Button("Validate Setup", GUILayout.Height(30)))
            {
                var warnings = IKValidator.Validate(m_Target);
                if (warnings.Count == 0)
                    Debug.Log("[NYIK] Setup is valid.", m_Target);
                else
                    foreach (var w in warnings)
                        Debug.LogWarning($"[NYIK] {w}", m_Target);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Warning display
            DrawWarnings();

            EditorGUILayout.Space(4);

            // Default inspector
            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawWarnings()
        {
            if (m_Target.References == null)
                return;

            var warnings = m_Target.References.GetWarnings();
            if (warnings.Count == 0)
            {
                EditorGUILayout.HelpBox("All bone references are assigned.", MessageType.Info);
            }
            else
            {
                foreach (var warning in warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }
    }
}
