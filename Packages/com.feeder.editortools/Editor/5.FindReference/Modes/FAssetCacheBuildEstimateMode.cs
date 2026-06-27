using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace Feeder
{
    public sealed class FAssetCacheBuildEstimateMode : FRefModeBase
    {
        private enum BuildEstimateSortType
        {
            AssetFullPath,
            AssetFilename,
            Type,
            SizeBeforeBuild,
            SizeEstimate,
            PercentSize
        }

        private enum BuildEstimateSortOrder
        {
            Ascending,
            Descending
        }

        [NonSerialized] private List<FBuildEstimateRow> rows;
        [NonSerialized] private List<FBuildEstimateRow> displayRows;
        [NonSerialized] private string searchText;
        [NonSerialized] private string lastSearchText;
        [NonSerialized] private Vector2 scroll;
        [NonSerialized] private int sceneCount;
        [NonSerialized] private long totalBefore;
        [NonSerialized] private long totalEstimate;
        [NonSerialized] private int pageIndex;
        [NonSerialized] private int pageSize = 100;
        [NonSerialized] private bool viewDirty = true;
        [NonSerialized] private BuildEstimateSortType sortType = BuildEstimateSortType.SizeEstimate;
        [NonSerialized] private BuildEstimateSortOrder sortOrder = BuildEstimateSortOrder.Descending;

        private static readonly int[] PageSizeValues = { 25, 50, 100, 200, 500 };
        private static readonly string[] PageSizeLabels = { "25", "50", "100", "200", "500" };
        private const float TypeWidth = 110f;
        private const float SizeBeforeWidth = 120f;
        private const float SizeEstimateWidth = 120f;
        private const float PercentWidth = 70f;
        private const float ScrollbarReserveWidth = 18f;
        private const float IconAndPaddingWidth = 24f;

        protected override string GetDescription() =>
            "Estimate build asset usage from every scene under Assets. The table is paged and sorted like Build Report asset lists.";

        [OnInspectorGUI]
        private void Draw()
        {
            EnsureLoadedFromCache();
            DrawToolbar();
            DrawSummary();
            DrawRows();
        }

        private void EnsureLoadedFromCache()
        {
            if (rows != null) return;
            var cache = FBuildEstimateCache.instance;
            if (!cache.HasData) return;
            rows = cache.rows;
            sceneCount = cache.sceneCount;
            totalBefore = cache.totalBefore;
            totalEstimate = cache.totalEstimate;
            viewDirty = true;
        }

        private void DrawToolbar()
        {
            EnsureDisplayRows();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(FRefDatabase.instance.IsBuilding))
            {
                if (GUILayout.Button("Build Estimate", EditorStyles.toolbarButton, GUILayout.Width(110)))
                    BuildEstimate();
            }

            using (new EditorGUI.DisabledScope(displayRows == null || displayRows.Count == 0))
            {
                if (GUILayout.Button("Select Page", EditorStyles.toolbarButton, GUILayout.Width(82)))
                    SelectRows(GetCurrentPageRows());

                if (GUILayout.Button("Copy Page", EditorStyles.toolbarButton, GUILayout.Width(76)))
                    CopyRows(GetCurrentPageRows());

                if (GUILayout.Button("Copy All CSV", EditorStyles.toolbarButton, GUILayout.Width(88)))
                    CopyRows(displayRows);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(rows == null ? "Not estimated" : $"{GetDisplayCount()}/{rows.Count} assets", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            if (rows == null)
            {
                EditorGUILayout.HelpBox("Click Build Estimate to build a fast table from all scenes in Assets.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Build Estimate Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scenes", sceneCount.ToString());
            EditorGUILayout.LabelField("Assets", rows.Count.ToString());
            EditorGUILayout.LabelField("Size Before Build", FormatBytes(totalBefore));
            EditorGUILayout.LabelField("Estimated Build Size", FormatBytes(totalEstimate));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawRows()
        {
            if (rows == null) return;

            DrawFilterSortToolbar();
            EnsureDisplayRows();

            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No scene dependencies were found under Assets.", MessageType.Info);
                return;
            }

            if (displayRows.Count == 0)
            {
                EditorGUILayout.HelpBox("No asset matches the current filter.", MessageType.Info);
                return;
            }

            DrawPageToolbar();
            DrawHeaderRow();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in GetCurrentPageRows())
                DrawRow(row);
            EditorGUILayout.EndScrollView();

            DrawPageToolbar();
        }

        private void DrawFilterSortToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Search", GUILayout.Width(46));
            string nextSearch = GUILayout.TextField(searchText ?? string.Empty, GetToolbarSearchStyle(), GUILayout.MinWidth(90));
            if (nextSearch != searchText)
            {
                searchText = nextSearch;
                pageIndex = 0;
                viewDirty = true;
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchText)))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(46)))
                {
                    searchText = string.Empty;
                    pageIndex = 0;
                    viewDirty = true;
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Sort", GUILayout.Width(32));
            EditorGUI.BeginChangeCheck();
            var nextSortType = (BuildEstimateSortType)EditorGUILayout.EnumPopup(sortType, EditorStyles.toolbarPopup, GUILayout.Width(132));
            var nextSortOrder = (BuildEstimateSortOrder)EditorGUILayout.EnumPopup(sortOrder, EditorStyles.toolbarPopup, GUILayout.Width(94));
            if (EditorGUI.EndChangeCheck())
            {
                sortType = nextSortType;
                sortOrder = nextSortOrder;
                pageIndex = 0;
                viewDirty = true;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(displayRows == null ? "0 shown" : $"{displayRows.Count} shown", EditorStyles.miniLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPageToolbar()
        {
            int count = GetDisplayCount();
            int pageCount = GetPageCount(count);
            ClampPage(count);

            int first = count == 0 ? 0 : pageIndex * pageSize + 1;
            int last = Math.Min(count, (pageIndex + 1) * pageSize);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(pageIndex <= 0))
            {
                if (GUILayout.Button("First", EditorStyles.toolbarButton, GUILayout.Width(44)))
                {
                    pageIndex = 0;
                    scroll = Vector2.zero;
                }

                if (GUILayout.Button("Prev", EditorStyles.toolbarButton, GUILayout.Width(42)))
                {
                    pageIndex--;
                    scroll = Vector2.zero;
                }
            }

            GUILayout.Label("Page", GUILayout.Width(34));
            EditorGUI.BeginChangeCheck();
            int nextPage = EditorGUILayout.IntField(pageIndex + 1, GUILayout.Width(44));
            if (EditorGUI.EndChangeCheck())
            {
                pageIndex = Mathf.Clamp(nextPage - 1, 0, pageCount - 1);
                scroll = Vector2.zero;
            }

            GUILayout.Label($"/ {pageCount}", GUILayout.Width(48));

            using (new EditorGUI.DisabledScope(pageIndex >= pageCount - 1))
            {
                if (GUILayout.Button("Next", EditorStyles.toolbarButton, GUILayout.Width(42)))
                {
                    pageIndex++;
                    scroll = Vector2.zero;
                }

                if (GUILayout.Button("Last", EditorStyles.toolbarButton, GUILayout.Width(42)))
                {
                    pageIndex = pageCount - 1;
                    scroll = Vector2.zero;
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Rows", GUILayout.Width(34));
            EditorGUI.BeginChangeCheck();
            int nextPageSize = EditorGUILayout.IntPopup(pageSize, PageSizeLabels, PageSizeValues, EditorStyles.toolbarPopup, GUILayout.Width(58));
            if (EditorGUI.EndChangeCheck())
            {
                pageSize = nextPageSize;
                pageIndex = 0;
                scroll = Vector2.zero;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Showing {first}-{last} / {count}", EditorStyles.miniLabel, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();
        }

        private void BuildEstimate()
        {
            if (!RequireDatabaseReady()) return;

            string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(p => p)
                .ToArray();

            sceneCount = scenes.Length;
            var dependencyPaths = new HashSet<string>();

            foreach (string scene in scenes)
            {
                dependencyPaths.Add(scene);
                foreach (string dep in AssetDatabase.GetDependencies(scene, true))
                    dependencyPaths.Add(dep);
            }

            var builtRows = new List<FBuildEstimateRow>();
            foreach (string path in dependencyPaths.OrderBy(p => p))
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                FRefAsset cached = !string.IsNullOrEmpty(guid) ? FRefDatabase.instance.Get(guid) : null;
                long sizeBefore = cached != null ? cached.fileSize : GetFileSize(path);
                FRefAssetType type = cached != null ? cached.type : FRefAssetTypeUtil.FromPath(path);
                long sizeEstimate = EstimateSize(path, type, sizeBefore);

                builtRows.Add(new FBuildEstimateRow
                {
                    path = path,
                    fileName = Path.GetFileName(path),
                    type = type,
                    sizeBefore = sizeBefore,
                    sizeEstimate = sizeEstimate
                });
            }

            rows = builtRows;
            totalBefore = rows.Sum(r => r.sizeBefore);
            totalEstimate = rows.Sum(r => r.sizeEstimate);

            foreach (FBuildEstimateRow row in rows)
                row.percent = totalEstimate > 0 ? (double)row.sizeEstimate / totalEstimate * 100d : 0d;

            var cache = FBuildEstimateCache.instance;
            cache.rows = rows;
            cache.sceneCount = sceneCount;
            cache.totalBefore = totalBefore;
            cache.totalEstimate = totalEstimate;
            cache.Save(true);

            pageIndex = 0;
            scroll = Vector2.zero;
            viewDirty = true;
        }

        private void EnsureDisplayRows()
        {
            if (rows == null) return;
            if (!viewDirty && searchText == lastSearchText && displayRows != null) return;

            IEnumerable<FBuildEstimateRow> query = rows;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(r =>
                    Contains(r.path, searchText) ||
                    Contains(r.fileName, searchText) ||
                    Contains(r.type.ToString(), searchText));
            }

            displayRows = SortRows(query).ToList();
            lastSearchText = searchText;
            viewDirty = false;
            ClampPage(displayRows.Count);
        }

        private IOrderedEnumerable<FBuildEstimateRow> SortRows(IEnumerable<FBuildEstimateRow> source)
        {
            bool desc = sortOrder == BuildEstimateSortOrder.Descending;

            switch (sortType)
            {
                case BuildEstimateSortType.AssetFilename:
                    return desc
                        ? source.OrderByDescending(r => r.fileName, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.path, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.fileName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.path, StringComparer.OrdinalIgnoreCase);
                case BuildEstimateSortType.Type:
                    return desc
                        ? source.OrderByDescending(r => r.type.ToString(), StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.fileName, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.type.ToString(), StringComparer.OrdinalIgnoreCase).ThenBy(r => r.fileName, StringComparer.OrdinalIgnoreCase);
                case BuildEstimateSortType.SizeBeforeBuild:
                    return desc
                        ? source.OrderByDescending(r => r.sizeBefore).ThenByDescending(r => r.fileName, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.sizeBefore).ThenBy(r => r.fileName, StringComparer.OrdinalIgnoreCase);
                case BuildEstimateSortType.PercentSize:
                    return desc
                        ? source.OrderByDescending(r => r.percent).ThenByDescending(r => r.fileName, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.percent).ThenBy(r => r.fileName, StringComparer.OrdinalIgnoreCase);
                case BuildEstimateSortType.AssetFullPath:
                    return desc
                        ? source.OrderByDescending(r => r.path, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase);
                case BuildEstimateSortType.SizeEstimate:
                default:
                    return desc
                        ? source.OrderByDescending(r => r.sizeEstimate).ThenByDescending(r => r.fileName, StringComparer.OrdinalIgnoreCase)
                        : source.OrderBy(r => r.sizeEstimate).ThenBy(r => r.fileName, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static long EstimateSize(string path, FRefAssetType type, long sizeBefore)
        {
            long runtimeSize = 0;
            try
            {
                Object obj = AssetDatabase.LoadMainAssetAtPath(path);
                if (obj != null)
                    runtimeSize = Profiler.GetRuntimeMemorySizeLong(obj);
            }
            catch
            {
                runtimeSize = 0;
            }

            if (runtimeSize <= 0)
                return Math.Max(0, sizeBefore);

            switch (type)
            {
                case FRefAssetType.Texture:
                    return Math.Max(sizeBefore, runtimeSize / 4);
                case FRefAssetType.Model:
                case FRefAssetType.Prefab:
                case FRefAssetType.Scene:
                case FRefAssetType.Material:
                case FRefAssetType.ScriptableObject:
                case FRefAssetType.Animation:
                case FRefAssetType.AnimatorController:
                    return Math.Max(sizeBefore, runtimeSize);
                default:
                    return Math.Max(0, sizeBefore);
            }
        }

        private void DrawHeaderRow()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);

            TableMetrics metrics = GetTableMetrics(rect);
            if (DrawHeaderButton(metrics.assetRect, "Asset", BuildEstimateSortType.AssetFullPath))
                ToggleSort(BuildEstimateSortType.AssetFullPath);
            if (DrawHeaderButton(metrics.typeRect, "Type", BuildEstimateSortType.Type))
                ToggleSort(BuildEstimateSortType.Type);
            if (DrawHeaderButton(metrics.sizeBeforeRect, "Size Before Build", BuildEstimateSortType.SizeBeforeBuild))
                ToggleSort(BuildEstimateSortType.SizeBeforeBuild);
            if (DrawHeaderButton(metrics.sizeEstimateRect, "Size Estimate", BuildEstimateSortType.SizeEstimate))
                ToggleSort(BuildEstimateSortType.SizeEstimate);
            if (DrawHeaderButton(metrics.percentRect, "Percent", BuildEstimateSortType.PercentSize))
                ToggleSort(BuildEstimateSortType.PercentSize);
        }

        private bool DrawHeaderButton(Rect rect, string label, BuildEstimateSortType headerSortType)
        {
            string marker = sortType == headerSortType
                ? sortOrder == BuildEstimateSortOrder.Descending ? " v" : " ^"
                : string.Empty;

            return GUI.Button(rect, label + marker, EditorStyles.toolbarButton);
        }

        private void ToggleSort(BuildEstimateSortType nextSortType)
        {
            if (sortType == nextSortType)
            {
                sortOrder = sortOrder == BuildEstimateSortOrder.Descending
                    ? BuildEstimateSortOrder.Ascending
                    : BuildEstimateSortOrder.Descending;
            }
            else
            {
                sortType = nextSortType;
                sortOrder = IsSizeSort(nextSortType) ? BuildEstimateSortOrder.Descending : BuildEstimateSortOrder.Ascending;
            }

            pageIndex = 0;
            scroll = Vector2.zero;
            viewDirty = true;
        }

        private static bool IsSizeSort(BuildEstimateSortType value)
        {
            return value == BuildEstimateSortType.SizeBeforeBuild ||
                   value == BuildEstimateSortType.SizeEstimate ||
                   value == BuildEstimateSortType.PercentSize;
        }

        private static void DrawRow(FBuildEstimateRow row)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.55f, 1f, 0.12f));

            TableMetrics metrics = GetTableMetrics(rect);
            Rect iconRect = new Rect(metrics.assetRect.x + 3f, rect.y + 3f, 16f, 16f);
            Texture icon = AssetDatabase.GetCachedIcon(row.path);
            if (icon != null) GUI.DrawTexture(iconRect, icon);

            Rect pathRect = metrics.assetRect;
            pathRect.xMin += IconAndPaddingWidth;
            pathRect.xMax -= 4f;
            pathRect.y += 2f;
            pathRect.height = 18f;

            GUI.Label(pathRect, new GUIContent(row.path, row.path), EditorStyles.miniLabel);
            GUI.Label(InsetCell(metrics.typeRect), row.type.ToString(), EditorStyles.miniLabel);
            GUI.Label(InsetCell(metrics.sizeBeforeRect), FormatBytes(row.sizeBefore), EditorStyles.miniLabel);
            GUI.Label(InsetCell(metrics.sizeEstimateRect), FormatBytes(row.sizeEstimate), EditorStyles.miniLabel);
            GUI.Label(InsetCell(metrics.percentRect), $"{row.percent:0.##}%", EditorStyles.miniLabel);

            HandleAssetClick(rect, row.path);
        }

        private static Rect InsetCell(Rect rect)
        {
            rect.xMin += 4f;
            rect.xMax -= 4f;
            rect.y += 2f;
            rect.height = 18f;
            return rect;
        }

        private static TableMetrics GetTableMetrics(Rect rect)
        {
            rect.xMax -= ScrollbarReserveWidth;

            float percentX = rect.xMax - PercentWidth;
            float sizeEstimateX = percentX - SizeEstimateWidth;
            float sizeBeforeX = sizeEstimateX - SizeBeforeWidth;
            float typeX = sizeBeforeX - TypeWidth;

            float minTypeX = rect.x + 280f;
            if (typeX < minTypeX)
                typeX = minTypeX;

            return new TableMetrics
            {
                assetRect = new Rect(rect.x, rect.y, Mathf.Max(80f, typeX - rect.x), rect.height),
                typeRect = new Rect(typeX, rect.y, TypeWidth, rect.height),
                sizeBeforeRect = new Rect(typeX + TypeWidth, rect.y, SizeBeforeWidth, rect.height),
                sizeEstimateRect = new Rect(typeX + TypeWidth + SizeBeforeWidth, rect.y, SizeEstimateWidth, rect.height),
                percentRect = new Rect(typeX + TypeWidth + SizeBeforeWidth + SizeEstimateWidth, rect.y, PercentWidth, rect.height)
            };
        }

        private struct TableMetrics
        {
            public Rect assetRect;
            public Rect typeRect;
            public Rect sizeBeforeRect;
            public Rect sizeEstimateRect;
            public Rect percentRect;
        }

        private IEnumerable<FBuildEstimateRow> GetCurrentPageRows()
        {
            EnsureDisplayRows();
            if (displayRows == null || displayRows.Count == 0)
                return Enumerable.Empty<FBuildEstimateRow>();

            ClampPage(displayRows.Count);
            return displayRows.Skip(pageIndex * pageSize).Take(pageSize);
        }

        private int GetDisplayCount()
        {
            EnsureDisplayRows();
            return displayRows != null ? displayRows.Count : 0;
        }

        private int GetPageCount(int count)
        {
            return Math.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, pageSize)));
        }

        private void ClampPage(int count)
        {
            pageSize = Mathf.Max(1, pageSize);
            int pageCount = GetPageCount(count);
            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
        }

        private static void SelectRows(IEnumerable<FBuildEstimateRow> selectedRows)
        {
            Selection.objects = selectedRows
                .Select(r => AssetDatabase.LoadAssetAtPath<Object>(r.path))
                .Where(o => o != null)
                .ToArray();

            if (Selection.objects.Length > 0)
                EditorGUIUtility.PingObject(Selection.objects[0]);
        }

        private static void CopyRows(IEnumerable<FBuildEstimateRow> selectedRows)
        {
            EditorGUIUtility.systemCopyBuffer = string.Join(
                "\n",
                selectedRows.Select(r => $"{r.path},{r.type},{r.sizeBefore},{r.sizeEstimate},{r.percent:0.##}"));
        }

        private static void HandleAssetClick(Rect rect, string path)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || !rect.Contains(e.mousePosition)) return;

            Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
                if (e.clickCount == 2)
                    AssetDatabase.OpenAsset(obj);
            }

            e.Use();
        }

        private static long GetFileSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   !string.IsNullOrEmpty(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double value = bytes / 1024d;
            if (value < 1024) return $"{value:0.##} KB";
            value /= 1024d;
            if (value < 1024) return $"{value:0.##} MB";
            value /= 1024d;
            return $"{value:0.##} GB";
        }

        private static GUIStyle GetToolbarSearchStyle()
        {
            return GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
        }

    }
}
