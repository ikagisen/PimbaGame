using UnityEditor;
using UnityEngine;

namespace PimbaStage.EditorTools
{
    /// <summary>Names a free layer "Pixelated 3D" (Project Settings > Tags and Layers) for the 3D version's pixel look.</summary>
    [InitializeOnLoad]
    internal static class PixelLayerSetup
    {
        static PixelLayerSetup()
        {
            EditorApplication.delayCall += Ensure;
        }

        private static void Ensure()
        {
            if (LayerMask.NameToLayer(Pixelated3D.LayerName) >= 0) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tags = new SerializedObject(assets[0]);
            var layers = tags.FindProperty("layers");
            if (layers == null) return;
            for (int i = 31; i >= 8; i--)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = Pixelated3D.LayerName;
                tags.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("Pimba Stage: layer " + i + " is now \"" + Pixelated3D.LayerName + "\" (used for the 3D version's pixel look).");
                return;
            }
        }
    }
}
