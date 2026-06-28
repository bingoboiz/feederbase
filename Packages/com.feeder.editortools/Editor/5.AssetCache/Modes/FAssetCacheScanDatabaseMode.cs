using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FAssetCacheScanDatabaseMode : FRefModeBase
    {
        protected override string GetDescription() =>
            "Scan the project reference database first. These settings are shared by Search.";

        [OnInspectorGUI]
        private void Draw()
        {
            DrawTraversalSettings();
            EditorGUILayout.Space(6);
            FRefHeaderGUI.DrawDatabasePanel(FRefDatabase.instance);

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Recursive controls how far Search walks the cached dependency graph. Max Depth = 0 means unlimited while Recursive is enabled.",
                MessageType.Info);
        }

        private static void DrawTraversalSettings()
        {
            var settings = FAssetCacheSettings.instance;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Scan / Search Defaults", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool recursive = EditorGUILayout.ToggleLeft("Recursive reference walk", settings.Recursive);
            int maxDepth = EditorGUILayout.IntField("Max Depth", settings.MaxDepth);
            if (EditorGUI.EndChangeCheck())
            {
                settings.Recursive = recursive;
                settings.MaxDepth = Mathf.Max(0, maxDepth);
            }

            using (new EditorGUI.DisabledScope(!settings.Recursive))
            {
                EditorGUILayout.LabelField(
                    settings.MaxDepth == 0 ? "Depth: unlimited" : $"Depth: {settings.MaxDepth}",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
