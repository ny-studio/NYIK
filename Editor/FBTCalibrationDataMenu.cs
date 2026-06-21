#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using NYIK.Calibration;

namespace NYIK.EditorTools
{
    /// <summary>
    /// Convenience MenuItem to create a default <see cref="FBTCalibrationData"/>
    /// asset at a fixed path. Useful for one-shot project setup; the standard
    /// "Assets > Create > NYIK > FBT Calibration Data" still works for
    /// arbitrary locations.
    /// </summary>
    public static class FBTCalibrationDataMenu
    {
        private const string DefaultPath = "Assets/Main/Config/FBTCalibrationData.asset";

        [MenuItem("VRH/Recording/Create FBT Calibration Data (Default Path)")]
        public static void CreateDefaultAsset()
        {
            var dir = Path.GetDirectoryName(DefaultPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var existing = AssetDatabase.LoadAssetAtPath<FBTCalibrationData>(DefaultPath);
            if (existing != null)
            {
                Debug.Log($"[NYIK] FBTCalibrationData already exists at {DefaultPath}", existing);
                Selection.activeObject = existing;
                return;
            }

            var asset = ScriptableObject.CreateInstance<FBTCalibrationData>();
            AssetDatabase.CreateAsset(asset, DefaultPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            Debug.Log($"[NYIK] Created FBTCalibrationData at {DefaultPath}");
        }
    }
}
#endif
