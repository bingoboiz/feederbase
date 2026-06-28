using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder
{
    /// <summary>"Scene References": objects in open scenes that reference the target assets.</summary>
    public sealed class FRefSceneMode : FRefModeBase
    {
        [NonSerialized] private List<FSceneRefResult> _results;
        [NonSerialized] private string _searchText;

        protected override string GetDescription() =>
            "Tìm GameObject/Component trong các scene đang mở có tham chiếu tới TargetAssets.";

        [Button("Find In Open Scenes", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.3f), PropertyOrder(1)]
        private void Find()
        {
            string[] guids = TargetGuids;
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Feeder Asset Cache", "Please add assets to TargetAssets first.", "OK");
                return;
            }
            _results = FRefScanner.FindInScenes(guids);
        }

        [OnInspectorGUI, PropertyOrder(2)]
        private void DrawResults()
        {
            if (_results == null)
            {
                EditorGUILayout.HelpBox("Bấm Find In Open Scenes để quét scene đang mở.", MessageType.Info);
                return;
            }

            List<FSceneRefResult> filtered = FilterResults(_results, _searchText);
            _searchText = FRefResultGUI.DrawSearchToolbar(_searchText, filtered.Count, _results.Count);

            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox("Không tìm thấy tham chiếu trong scene đang mở.", MessageType.Info);
                return;
            }

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("Không có scene reference khớp bộ lọc hiện tại.", MessageType.Info);
                return;
            }

            DrawSceneActions(filtered);
            EditorGUILayout.LabelField($"Kết quả: {filtered.Count}", EditorStyles.boldLabel);
            foreach (var result in filtered)
            {
                string target = Path.GetFileName(result.targetPath);
                string scene = string.IsNullOrEmpty(result.scenePath) ? "Unsaved Scene" : Path.GetFileName(result.scenePath);
                FRefResultGUI.DrawSceneObjectRow(result.obj, $"{target} | {scene}");
            }
        }

        private static List<FSceneRefResult> FilterResults(List<FSceneRefResult> results, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return results;

            var filtered = new List<FSceneRefResult>();
            foreach (var result in results)
                if (Matches(result, searchText))
                    filtered.Add(result);

            return filtered;
        }

        private static bool Matches(FSceneRefResult result, string searchText)
        {
            if (Contains(result.obj != null ? result.obj.name : null, searchText)) return true;
            if (Contains(result.scenePath, searchText)) return true;
            if (Contains(result.targetPath, searchText)) return true;
            return false;
        }

        private static bool Contains(string value, string searchText)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawSceneActions(List<FSceneRefResult> results)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Select Objects", EditorStyles.miniButton, GUILayout.Width(105)))
            {
                Object[] objects = results
                    .Select(r => r.obj)
                    .Where(o => o != null)
                    .Distinct()
                    .ToArray();

                Selection.objects = objects;
                if (objects.Length > 0)
                    EditorGUIUtility.PingObject(objects[0]);
            }

            if (GUILayout.Button("Copy Report", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                EditorGUIUtility.systemCopyBuffer = string.Join(
                    "\n",
                    results.Select(r => $"{r.scenePath} | {(r.obj != null ? r.obj.name : "(null)")} | {r.targetPath}"));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}
