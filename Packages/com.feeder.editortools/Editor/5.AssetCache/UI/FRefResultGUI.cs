using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder
{
    /// <summary>Shared IMGUI helpers for drawing Find Reference result rows.</summary>
    public static class FRefResultGUI
    {
        public static readonly string[] GroupModes = { "None", "Type", "Folder" };

        private static readonly Color RowHoverColor = new Color(0.25f, 0.55f, 1f, 0.12f);

        /// <summary>Draws one asset row: icon + name (+ path tooltip). Click pings, double-click opens.</summary>
        public static void DrawAssetRow(string assetPath, int depth)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            rect.xMin += depth * 14f;

            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, RowHoverColor);

            var iconRect = new Rect(rect.x + 2f, rect.y + 2f, 16f, 16f);
            Texture icon = AssetDatabase.GetCachedIcon(assetPath);
            if (icon != null) GUI.DrawTexture(iconRect, icon);

            Rect labelRect = rect;
            labelRect.xMin += 24f;
            string name = Path.GetFileName(assetPath);
            GUI.Label(labelRect, new GUIContent(name, assetPath));

            HandleRowClick(rect, () => AssetDatabase.LoadAssetAtPath<Object>(assetPath));
        }

        /// <summary>Draws a row for an in-scene object (ping/select in hierarchy).</summary>
        public static void DrawSceneObjectRow(Object obj, string subLabel)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);

            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, RowHoverColor);

            var iconRect = new Rect(rect.x + 2f, rect.y + 2f, 16f, 16f);
            GUIContent content = EditorGUIUtility.ObjectContent(obj, obj != null ? obj.GetType() : typeof(Object));
            if (content.image != null) GUI.DrawTexture(iconRect, content.image);

            Rect labelRect = rect;
            labelRect.xMin += 24f;
            string label = obj != null ? obj.name : "(null)";
            if (!string.IsNullOrEmpty(subLabel)) label += $"    <{subLabel}>";
            GUI.Label(labelRect, new GUIContent(label));

            HandleRowClick(rect, () => obj);
        }

        public static string DrawSearchToolbar(string searchText, int shownCount, int totalCount)
        {
            searchText ??= string.Empty;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Search", GUILayout.Width(46));
            searchText = GUILayout.TextField(searchText, GetToolbarSearchStyle(), GUILayout.MinWidth(80));

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchText)))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(46)))
                    searchText = string.Empty;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{shownCount}/{totalCount}", EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.EndHorizontal();

            return searchText;
        }

        /// <summary>Draws Used-By / Uses / Unused results, optionally grouped by Type or Folder.</summary>
        public static void DrawGroupedResults(List<FRefResult> results, string groupBy, ref string searchText)
        {
            if (results == null)
            {
                EditorGUILayout.HelpBox("Bấm Find để tìm kết quả.", MessageType.Info);
                return;
            }

            List<FRefResult> filtered = FilterAssetResults(results, searchText);
            searchText = DrawSearchToolbar(searchText, filtered.Count, results.Count);

            if (results.Count == 0)
            {
                EditorGUILayout.HelpBox("Không tìm thấy reference nào.", MessageType.Info);
                return;
            }

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("Không có kết quả khớp bộ lọc hiện tại.", MessageType.Info);
                return;
            }

            DrawAssetResultActions(filtered);
            EditorGUILayout.LabelField($"Kết quả: {filtered.Count}", EditorStyles.boldLabel);

            if (groupBy == "None" || string.IsNullOrEmpty(groupBy))
            {
                foreach (var result in filtered)
                    DrawAssetRow(result.asset.path, Mathf.Max(0, result.depth - 1));
                return;
            }

            var groups = new Dictionary<string, List<FRefResult>>();
            foreach (var result in filtered)
            {
                string key = groupBy == "Folder"
                    ? (Path.GetDirectoryName(result.asset.path)?.Replace('\\', '/') ?? "(root)")
                    : result.asset.type.ToString();

                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<FRefResult>();
                list.Add(result);
            }

            foreach (var group in groups.OrderBy(g => g.Key))
            {
                EditorGUILayout.LabelField($"{group.Key} ({group.Value.Count})", EditorStyles.miniBoldLabel);
                foreach (var result in group.Value)
                    DrawAssetRow(result.asset.path, 0);
            }
        }

        public static bool AssetPathMatchesSearch(string assetPath, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return true;
            if (string.IsNullOrEmpty(assetPath)) return false;

            return assetPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   Path.GetFileName(assetPath).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void DrawAssetPathActions(IEnumerable<string> assetPaths)
        {
            string[] paths = assetPaths?
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToArray() ?? Array.Empty<string>();

            if (paths.Length == 0) return;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Results", EditorStyles.miniButton, GUILayout.Width(105)))
                SelectAssetPaths(paths);

            if (GUILayout.Button("Copy Paths", EditorStyles.miniButton, GUILayout.Width(85)))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", paths);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static List<FRefResult> FilterAssetResults(List<FRefResult> results, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return results;

            var filtered = new List<FRefResult>();
            foreach (var result in results)
                if (result.asset != null && AssetPathMatchesSearch(result.asset.path, searchText))
                    filtered.Add(result);
            return filtered;
        }

        private static void DrawAssetResultActions(List<FRefResult> results)
        {
            DrawAssetPathActions(results.Select(r => r.asset != null ? r.asset.path : null));
        }

        private static void SelectAssetPaths(IEnumerable<string> paths)
        {
            var objects = new List<Object>();
            foreach (string path in paths)
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (obj != null) objects.Add(obj);
            }

            Selection.objects = objects.ToArray();
            if (objects.Count > 0)
                EditorGUIUtility.PingObject(objects[0]);
        }

        private static GUIStyle GetToolbarSearchStyle()
        {
            return GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
        }

        private static void HandleRowClick(Rect rect, Func<Object> resolve)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                Object obj = resolve();
                if (obj != null)
                {
                    if (e.clickCount == 2) AssetDatabase.OpenAsset(obj);
                    else EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
                e.Use();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }
    }
}
