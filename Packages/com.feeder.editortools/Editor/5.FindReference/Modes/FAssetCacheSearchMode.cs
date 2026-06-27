using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder
{
    public sealed class FAssetCacheSearchMode : FRefModeBase
    {
        private enum SearchTab
        {
            Uses,
            UsedBy
        }

        private enum SearchGroupMode
        {
            Dependency,
            Depth,
            Type,
            Extension,
            Folder,
            None
        }

        private enum SearchSortMode
        {
            Path,
            Type,
            Size
        }

        private enum RefSource
        {
            Asset,
            Scene
        }

        private SearchTab tab;
        private bool showSceneReference = true;
        private bool showAssetReference = true;
        private bool showFullPath;
        private bool showFileSize = true;
        private bool showExtension = true;
        private SearchGroupMode groupBy = SearchGroupMode.Type;
        private SearchSortMode sortBy = SearchSortMode.Path;
        private bool showDetails = true;

        [NonSerialized] private bool hasRun;
        [NonSerialized] private string searchText;
        [NonSerialized] private string assetMessage;
        [NonSerialized] private string sceneMessage;
        [NonSerialized] private Vector2 assetScroll;
        [NonSerialized] private Vector2 sceneScroll;
        [NonSerialized] private Vector2 detailScroll;
        [NonSerialized] private List<RefRow> assetRows;
        [NonSerialized] private List<RefRow> sceneRows;
        [NonSerialized] private RefRow selectedRow;
        [NonSerialized] private bool lockSelection;
        [NonSerialized] private List<Object> selectionTargets;
        [NonSerialized] private string[] selectionTargetGuids;
        [NonSerialized] private Object[] ignoredEditorSelection;

        protected override string GetDescription() =>
            "Search references with an FR2-style layout: Selection, Scene Reference, Asset Reference, and Details.";

        [OnInspectorGUI]
        private void Draw()
        {
            if (!lockSelection && !ShouldIgnoreEditorSelection() && SyncSelectionFromEditor())
                RefreshSearch();

            EnsureStyles();
            DrawToolbar();
            DrawSelectionBar();
            EditorGUILayout.Space(2);
            DrawPanels();
            DrawFooter();
        }

        internal override void OnHostSelectionChanged()
        {
            if (lockSelection || ShouldIgnoreEditorSelection()) return;
            if (SyncSelectionFromEditor())
                RefreshSearch();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            int newTab = GUILayout.Toolbar((int)tab, new[] { "Uses", "Used By" }, EditorStyles.toolbarButton, GUILayout.Width(160));
            if (newTab != (int)tab)
            {
                tab = (SearchTab)newTab;
                selectedRow = null;
                if (hasRun)
                    RefreshSearch();
            }

            GUILayout.Space(8);
            bool scene = GUILayout.Toggle(showSceneReference, "Scene Reference", EditorStyles.toolbarButton, GUILayout.Width(118));
            bool asset = GUILayout.Toggle(showAssetReference, "Asset Reference", EditorStyles.toolbarButton, GUILayout.Width(118));

            if (!scene && !asset)
            {
                if (showSceneReference)
                    asset = true;
                else
                    scene = true;
            }

            showSceneReference = scene;
            showAssetReference = asset;

            GUILayout.Space(8);
            GUILayout.Label("Search", GUILayout.Width(44));
            searchText = GUILayout.TextField(searchText ?? string.Empty, GetToolbarSearchStyle(), GUILayout.MinWidth(90));

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchText)))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(46)))
                    searchText = string.Empty;
            }

            GUILayout.FlexibleSpace();

            if (DrawIconButton(SdfIconType.ArrowClockwise, "Refresh Search"))
                RefreshSearch();

            DrawLockToggle();

            showDetails = DrawIconToggle(
                showDetails,
                SdfIconType.LayoutSidebarInsetReverse,
                SdfIconType.LayoutSidebarInset,
                showDetails ? "Hide Details panel" : "Show Details panel");

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLockToggle()
        {
            EditorGUI.BeginChangeCheck();
            bool nextLock = DrawIconToggle(lockSelection, SdfIconType.LockFill, SdfIconType.Unlock, GetSelectionTooltip());

            if (EditorGUI.EndChangeCheck())
            {
                lockSelection = nextLock;
                if (!lockSelection)
                {
                    ignoredEditorSelection = null;
                    if (SyncSelectionFromEditor())
                        RefreshSearch();
                }
            }
        }

        private void DrawSelectionBar()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            EditorGUI.DrawRect(rect, SelectionBarColor);

            int count = selectionTargets != null ? selectionTargets.Count : 0;
            Object first = count > 0 ? selectionTargets[0] : null;

            if (first == null)
            {
                GUI.Label(
                    new Rect(rect.x + 6f, rect.y, rect.width - 12f, rect.height),
                    "No selection — pick assets/folders in the Project window",
                    s_RowSub);
                return;
            }

            GUIContent content = EditorGUIUtility.ObjectContent(first, first.GetType());
            var iconRect = new Rect(rect.x + 6f, rect.y + (rect.height - 16f) * 0.5f, 16f, 16f);
            if (content.image != null) GUI.DrawTexture(iconRect, content.image);

            string label = first.name + (count > 1 ? $"     (+{count - 1} more)" : string.Empty);
            var labelRect = new Rect(iconRect.xMax + 5f, rect.y, rect.width - (iconRect.xMax + 5f) - 8f, rect.height);
            GUI.Label(labelRect, new GUIContent(label, AssetDatabase.GetAssetPath(first)), s_SelectionLabel);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                EditorGUIUtility.PingObject(first);
                e.Use();
            }
        }

        private bool SyncSelectionFromEditor()
        {
            var nextTargets = BuildSelectionTargets(Selection.objects);
            if (AreObjectListsEqual(selectionTargets, nextTargets))
                return false;

            selectionTargets = nextTargets;
            selectionTargetGuids = GetSelectionGuids(selectionTargets);
            selectedRow = null;
            return true;
        }

        private string[] GetSelectionTargetGuids(bool expandFolders = false)
        {
            if (selectionTargets == null)
                SyncSelectionFromEditor();

            string[] guids = selectionTargetGuids ?? Array.Empty<string>();
            return expandFolders ? ExpandFolderGuids(guids) : guids;
        }

        private string GetSelectionTooltip()
        {
            if (selectionTargets == null || selectionTargets.Count == 0)
                return "Select Project assets or folders to search.";

            return lockSelection
                ? "Unlock to follow the current Project selection."
                : "Lock the current Project asset/folder selection.";
        }

        private bool ShouldIgnoreEditorSelection()
        {
            if (ignoredEditorSelection == null)
                return false;

            Object[] editorSelection = Selection.objects ?? Array.Empty<Object>();
            if (AreObjectArraysEqual(editorSelection, ignoredEditorSelection))
                return true;

            ignoredEditorSelection = null;
            return false;
        }

        private void SetEditorSelection(Object obj)
        {
            if (obj == null) return;
            SetEditorSelection(new[] { obj });
        }

        private void SetEditorSelection(Object[] objects)
        {
            ignoredEditorSelection = objects ?? Array.Empty<Object>();
            Selection.objects = ignoredEditorSelection;
        }

        private static List<Object> BuildSelectionTargets(Object[] objects)
        {
            var result = new List<Object>();
            if (objects == null) return result;

            var seenGuids = new HashSet<string>();
            foreach (Object obj in objects)
            {
                if (!FRefData.IsAssetTarget(obj)) continue;

                string path = AssetDatabase.GetAssetPath(obj);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) || !seenGuids.Add(guid)) continue;

                result.Add(obj);
            }

            return result;
        }

        private static string[] GetSelectionGuids(IEnumerable<Object> objects)
        {
            if (objects == null) return Array.Empty<string>();

            var result = new List<string>();
            foreach (Object obj in objects)
            {
                if (!FRefData.IsAssetTarget(obj)) continue;

                string path = AssetDatabase.GetAssetPath(obj);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    result.Add(guid);
            }

            return result.Distinct().ToArray();
        }

        private static string[] ExpandFolderGuids(IEnumerable<string> guids)
        {
            if (guids == null) return Array.Empty<string>();

            var result = new HashSet<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    foreach (string subGuid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                        result.Add(subGuid);
                }
                else if (!string.IsNullOrEmpty(guid))
                {
                    result.Add(guid);
                }
            }

            return result.ToArray();
        }

        private static bool AreObjectListsEqual(List<Object> left, List<Object> right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Count != right.Count)
                return false;

            for (int i = 0; i < left.Count; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        private static bool AreObjectArraysEqual(Object[] left, Object[] right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        private void DrawPanels()
        {
            float viewWidth = EditorGUIUtility.currentViewWidth;
            bool detailsVisible = showDetails && viewWidth >= 560f;

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (showSceneReference)
                DrawResultPanel("Scene", RefSource.Scene, sceneRows, sceneMessage, ref sceneScroll);
            if (showAssetReference)
                DrawResultPanel("Assets", RefSource.Asset, assetRows, assetMessage, ref assetScroll);
            EditorGUILayout.EndVertical();

            if (detailsVisible)
                DrawDetailsPanel(viewWidth);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawResultPanel(string title, RefSource source, List<RefRow> sourceRows, string message, ref Vector2 scroll)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            List<RefRow> visible = FilterAndSort(sourceRows, source);
            EditorGUILayout.LabelField($"{title} ({(sourceRows == null ? 0 : visible.Count)})", s_PanelTitle);

            if (!hasRun)
            {
                EditorGUILayout.HelpBox("Click Refresh Search to populate this panel.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (sourceRows == null || sourceRows.Count == 0)
            {
                EditorGUILayout.HelpBox("No reference found.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox("No reference matches the current filter.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawPanelHeader();
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            DrawGroupedRows(visible, source);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawDetailsPanel(float viewWidth)
        {
            float width = Mathf.Clamp(viewWidth * 0.32f, 220f, 360f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Details", s_PanelTitle);

            if (selectedRow == null)
            {
                EditorGUILayout.HelpBox("Select a row to see details.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 64f;

            EditorGUILayout.LabelField("Name", selectedRow.displayName, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Type", selectedRow.type.ToString());
            EditorGUILayout.LabelField("Size", FormatBytes(selectedRow.size));

            if (!string.IsNullOrEmpty(selectedRow.assetPath))
                DrawDetailPath("Asset", selectedRow.assetPath);

            if (selectedRow.source == RefSource.Scene)
            {
                DrawDetailPath("Scene", selectedRow.scenePath);
                EditorGUILayout.LabelField("Object", selectedRow.objectPath ?? "(missing)", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Property", selectedRow.propertyPath ?? "(unknown)", EditorStyles.wordWrappedLabel);
            }

            if (selectedRow.chainPaths != null && selectedRow.chainPaths.Length > 0)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Reference Chain", EditorStyles.boldLabel);
                for (int i = 0; i < selectedRow.chainPaths.Length; i++)
                    EditorGUILayout.LabelField($"{i + 1}. {selectedRow.chainPaths[i]}", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Group By", GUILayout.Width(58));
            groupBy = (SearchGroupMode)EditorGUILayout.EnumPopup(groupBy, EditorStyles.toolbarPopup, GUILayout.Width(105));

            GUILayout.Label("Sort By", GUILayout.Width(48));
            sortBy = (SearchSortMode)EditorGUILayout.EnumPopup(sortBy, EditorStyles.toolbarPopup, GUILayout.Width(82));

            GUILayout.Space(8);
            showFullPath = GUILayout.Toggle(showFullPath, "Full Path", EditorStyles.toolbarButton, GUILayout.Width(74));
            showFileSize = GUILayout.Toggle(showFileSize, "Size", EditorStyles.toolbarButton, GUILayout.Width(48));
            showExtension = GUILayout.Toggle(showExtension, "Ext", EditorStyles.toolbarButton, GUILayout.Width(42));

            GUILayout.FlexibleSpace();
            GUILayout.Label(Settings.Recursive ? $"Recursive / Depth {(Settings.MaxDepth == 0 ? "Unlimited" : Settings.MaxDepth.ToString())}" : "Direct references", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void RefreshSearch()
        {
            hasRun = true;
            selectedRow = null;
            assetMessage = null;
            sceneMessage = null;
            assetRows = new List<RefRow>();
            sceneRows = new List<RefRow>();

            BuildAssetRows();
            BuildSceneRows();
        }

        private void BuildAssetRows()
        {
            if (!FRefDatabase.instance.IsReady)
            {
                assetMessage = "Reference Database is not ready. Run Scan Database first.";
                return;
            }

            string[] guids = GetSelectionTargetGuids();
            if (guids.Length == 0)
            {
                assetMessage = "Select Project assets or folders to search.";
                return;
            }

            List<FRefResult> results = tab == SearchTab.Uses
                ? FRefQuery.FindUses(guids, Settings.Recursive, Settings.MaxDepth)
                : FRefQuery.FindUsedBy(guids, Settings.Recursive, Settings.MaxDepth);

            var byGuid = results
                .Where(r => r.asset != null && !string.IsNullOrEmpty(r.asset.guid))
                .GroupBy(r => r.asset.guid)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (FRefResult result in results)
            {
                if (result.asset == null) continue;
                assetRows.Add(new RefRow
                {
                    source = RefSource.Asset,
                    displayName = Path.GetFileName(result.asset.path),
                    assetPath = result.asset.path,
                    type = result.asset.type,
                    size = result.asset.fileSize,
                    depth = result.depth,
                    rootPath = GuidToPath(result.rootGuid),
                    chainPaths = BuildChain(result, byGuid)
                });
            }
        }

        private void BuildSceneRows()
        {
            string[] guids = GetSelectionTargetGuids(expandFolders: true);
            if (guids.Length == 0)
            {
                sceneMessage = "Select Project assets or folders to search.";
                return;
            }

            List<FSceneRefResult> results = FRefScanner.FindInScenes(guids);

            foreach (FSceneRefResult result in results)
            {
                string path = result.targetPath;
                if (string.IsNullOrEmpty(path)) continue;

                FRefAsset cached = FRefDatabase.instance.Get(AssetDatabase.AssetPathToGUID(path));
                FRefAssetType type = cached != null ? cached.type : FRefAssetTypeUtil.FromPath(path);
                long size = cached != null ? cached.fileSize : GetFileSize(path);
                string objectPath = GetObjectPath(result.obj);

                sceneRows.Add(new RefRow
                {
                    source = RefSource.Scene,
                    displayName = result.obj != null ? result.obj.name : Path.GetFileName(path),
                    assetPath = path,
                    type = type,
                    size = size,
                    depth = 0,
                    scenePath = result.scenePath,
                    objectPath = objectPath,
                    propertyPath = result.propertyPath,
                    rootPath = string.IsNullOrEmpty(result.scenePath) ? "Unsaved Scene" : result.scenePath,
                    sceneObject = result.obj,
                    chainPaths = BuildSceneChain(result.scenePath, objectPath, result.propertyPath, path)
                });
            }
        }

        private List<RefRow> FilterAndSort(List<RefRow> sourceRows, RefSource source)
        {
            if (sourceRows == null)
                return new List<RefRow>();

            IEnumerable<RefRow> query = sourceRows;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                query = query.Where(r =>
                    Contains(r.assetPath, searchText) ||
                    Contains(r.displayName, searchText) ||
                    Contains(r.type.ToString(), searchText) ||
                    Contains(r.scenePath, searchText) ||
                    Contains(r.objectPath, searchText) ||
                    Contains(r.propertyPath, searchText));
            }

            switch (sortBy)
            {
                case SearchSortMode.Type:
                    query = query.OrderBy(r => r.type.ToString()).ThenBy(r => r.assetPath);
                    break;
                case SearchSortMode.Size:
                    query = query.OrderByDescending(r => r.size).ThenBy(r => r.assetPath);
                    break;
                default:
                    query = query.OrderBy(r => source == RefSource.Scene ? r.objectPath : r.assetPath);
                    break;
            }

            return query.ToList();
        }

        private void DrawGroupedRows(List<RefRow> rowsToDraw, RefSource source)
        {
            if (groupBy == SearchGroupMode.None)
            {
                foreach (RefRow row in rowsToDraw)
                    DrawRefRow(row, source);
                return;
            }

            foreach (var group in rowsToDraw.GroupBy(GetGroupKey).OrderBy(g => g.Key))
            {
                EditorGUILayout.LabelField($"{group.Key} ({group.Count()})", EditorStyles.miniBoldLabel);
                foreach (RefRow row in group)
                    DrawRefRow(row, source);
            }
        }

        private void DrawPanelHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(rect, HeaderBgColor);

            ColumnRects cols = GetColumns(rect, showFileSize, showExtension, 0);
            var nameRect = new Rect(cols.label.x, rect.y, cols.label.width, rect.height);

            GUI.Label(nameRect, "Reference", s_HeaderCol);
            GUI.Label(cols.type, "Type", s_HeaderCol);
            if (showFileSize)
                GUI.Label(cols.size, "Size", s_HeaderColRight);
            if (showExtension)
                GUI.Label(cols.ext, "Ext", s_HeaderColRight);
        }

        private void DrawRefRow(RefRow row, RefSource source)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 26f);

            if (selectedRow == row)
                EditorGUI.DrawRect(rect, RowSelectedColor);
            else if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, RowHoverColor);

            int depth = Mathf.Max(0, row.depth - 1);
            ColumnRects cols = GetColumns(rect, showFileSize, showExtension, depth);

            Texture icon = source == RefSource.Scene && row.sceneObject != null
                ? EditorGUIUtility.ObjectContent(row.sceneObject, row.sceneObject.GetType()).image
                : AssetDatabase.GetCachedIcon(row.assetPath);
            if (icon != null) GUI.DrawTexture(cols.icon, icon);

            string main = source == RefSource.Scene
                ? (showFullPath ? row.objectPath : row.displayName)
                : (showFullPath ? row.assetPath : Path.GetFileName(row.assetPath));
            string secondary = source == RefSource.Scene ? row.assetPath : row.rootPath;

            var mainRect = new Rect(cols.label.x, rect.y + 2f, cols.label.width, 15f);
            var subRect = new Rect(cols.label.x, rect.y + 15f, cols.label.width, 11f);
            GUI.Label(mainRect, new GUIContent(main, row.assetPath), s_RowMain);
            if (!string.IsNullOrEmpty(secondary))
                GUI.Label(subRect, new GUIContent(ShortenPath(secondary, 90)), s_RowSub);

            GUI.Label(cols.type, row.type.ToString(), s_RowMeta);
            if (showFileSize)
                GUI.Label(cols.size, FormatBytes(row.size), s_RowMetaRight);
            if (showExtension)
                GUI.Label(cols.ext, GetExtension(row.assetPath), s_RowMetaRight);

            HandleRowClick(rect, row, source);
        }

        private void HandleRowClick(Rect rowRect, RefRow row, RefSource source)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || !rowRect.Contains(e.mousePosition))
                return;

            selectedRow = row;

            Object obj = source == RefSource.Scene && row.sceneObject != null
                ? row.sceneObject
                : AssetDatabase.LoadAssetAtPath<Object>(row.assetPath);

            if (obj != null)
            {
                SetEditorSelection(obj);
                EditorGUIUtility.PingObject(obj);
                if (e.clickCount == 2 && source == RefSource.Asset)
                    AssetDatabase.OpenAsset(obj);
            }

            e.Use();
        }

        private static string[] BuildChain(FRefResult result, Dictionary<string, FRefResult> byGuid)
        {
            var stack = new Stack<string>();
            string cursor = result.asset != null ? result.asset.guid : null;
            int guard = 0;

            while (!string.IsNullOrEmpty(cursor) && guard++ < 128)
            {
                FRefAsset asset = FRefDatabase.instance.Get(cursor);
                if (asset != null)
                    stack.Push(asset.path);

                if (cursor == result.rootGuid)
                    break;

                if (!byGuid.TryGetValue(cursor, out FRefResult parentResult))
                    break;

                cursor = parentResult.parentGuid;
            }

            return stack.ToArray();
        }

        private static string[] BuildSceneChain(string scenePath, string objectPath, string propertyPath, string assetPath)
        {
            var chain = new List<string>();
            chain.Add(string.IsNullOrEmpty(scenePath) ? "Unsaved Scene" : scenePath);
            if (!string.IsNullOrEmpty(objectPath)) chain.Add(objectPath);
            if (!string.IsNullOrEmpty(propertyPath)) chain.Add(propertyPath);
            if (!string.IsNullOrEmpty(assetPath)) chain.Add(assetPath);
            return chain.ToArray();
        }

        private string GetGroupKey(RefRow row)
        {
            switch (groupBy)
            {
                case SearchGroupMode.Dependency:
                    return string.IsNullOrEmpty(row.rootPath) ? "(unknown)" : row.rootPath;
                case SearchGroupMode.Depth:
                    return $"Depth {row.depth}";
                case SearchGroupMode.Extension:
                    return string.IsNullOrEmpty(GetExtension(row.assetPath)) ? "(none)" : GetExtension(row.assetPath);
                case SearchGroupMode.Folder:
                    return Path.GetDirectoryName(row.assetPath)?.Replace('\\', '/') ?? "(root)";
                case SearchGroupMode.Type:
                    return row.type.ToString();
                default:
                    return "(all)";
            }
        }

        private static void DrawDetailPath(string label, string path)
        {
            EditorGUILayout.LabelField(label, string.IsNullOrEmpty(path) ? "(none)" : path, EditorStyles.wordWrappedLabel);
        }

        private static string GuidToPath(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private static string GetObjectPath(Object obj)
        {
            if (obj == null) return null;

            GameObject go = obj as GameObject;
            Component component = obj as Component;
            if (component != null)
                go = component.gameObject;

            if (go == null)
                return obj.name;

            var parts = new Stack<string>();
            Transform t = go.transform;
            while (t != null)
            {
                parts.Push(t.name);
                t = t.parent;
            }

            string path = string.Join("/", parts.ToArray());
            if (component != null)
                path += $" ({component.GetType().Name})";
            return path;
        }

        private static long GetFileSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static string GetExtension(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return Path.GetExtension(path);
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   !string.IsNullOrEmpty(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
                return path ?? string.Empty;
            return "..." + path.Substring(path.Length - maxLength + 3);
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

        // ----- Column layout (shared by header + rows so they stay aligned) -----

        private struct ColumnRects
        {
            public Rect icon;
            public Rect label;
            public Rect type;
            public Rect size;
            public Rect ext;
        }

        private static ColumnRects GetColumns(Rect row, bool withSize, bool withExt, int depth)
        {
            const float gap = 6f;
            const float iconSize = 16f;
            const float typeWidth = 90f;
            const float sizeWidth = 70f;
            const float extWidth = 46f;

            var c = new ColumnRects
            {
                icon = new Rect(row.x + 2f + depth * 12f, row.y + (row.height - iconSize) * 0.5f, iconSize, iconSize)
            };

            float right = row.xMax - 4f;
            if (withExt)
            {
                c.ext = new Rect(right - extWidth, row.y, extWidth, row.height);
                right = c.ext.x - gap;
            }
            if (withSize)
            {
                c.size = new Rect(right - sizeWidth, row.y, sizeWidth, row.height);
                right = c.size.x - gap;
            }
            c.type = new Rect(right - typeWidth, row.y, typeWidth, row.height);
            right = c.type.x - gap;

            float labelX = c.icon.xMax + 6f;
            c.label = new Rect(labelX, row.y, Mathf.Max(60f, right - labelX), row.height);
            return c;
        }

        // ----- Icon buttons -----

        private static bool DrawIconButton(SdfIconType icon, string tooltip, float width = 30f)
        {
            Rect rect = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight, EditorStyles.toolbarButton, GUILayout.Width(width));
            bool clicked = SirenixEditorGUI.SDFIconButton(rect, icon, EditorStyles.toolbarButton);
            GUI.Label(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            return clicked;
        }

        private static bool DrawIconToggle(bool value, SdfIconType onIcon, SdfIconType offIcon, string tooltip, float width = 30f)
        {
            Rect rect = GUILayoutUtility.GetRect(width, EditorGUIUtility.singleLineHeight, EditorStyles.toolbarButton, GUILayout.Width(width));
            GUIStyle style = value ? SirenixGUIStyles.ToolbarButtonSelected : EditorStyles.toolbarButton;
            bool clicked = SirenixEditorGUI.SDFIconButton(rect, value ? onIcon : offIcon, style);
            GUI.Label(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            return clicked ? !value : value;
        }

        // ----- Styles & colors -----

        private static readonly Color HeaderBgColor = new Color(0f, 0f, 0f, 0.15f);
        private static readonly Color RowHoverColor = new Color(0.25f, 0.55f, 1f, 0.12f);
        private static readonly Color RowSelectedColor = new Color(0.2f, 0.55f, 1f, 0.22f);
        private static readonly Color SelectionBarColor = new Color(0.3f, 0.6f, 1f, 0.10f);

        private static GUIStyle s_PanelTitle;
        private static GUIStyle s_HeaderCol;
        private static GUIStyle s_HeaderColRight;
        private static GUIStyle s_RowMain;
        private static GUIStyle s_RowSub;
        private static GUIStyle s_RowMeta;
        private static GUIStyle s_RowMetaRight;
        private static GUIStyle s_SelectionLabel;

        private static void EnsureStyles()
        {
            if (s_PanelTitle != null) return;

            s_PanelTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };

            s_HeaderCol = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };
            s_HeaderColRight = new GUIStyle(s_HeaderCol) { alignment = TextAnchor.MiddleRight };

            s_RowMain = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            s_RowSub = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            s_RowMeta = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            s_RowMetaRight = new GUIStyle(s_RowMeta) { alignment = TextAnchor.MiddleRight };

            s_SelectionLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
        }

        private sealed class RefRow
        {
            public RefSource source;
            public string displayName;
            public string assetPath;
            public FRefAssetType type;
            public long size;
            public int depth;
            public string rootPath;
            public string scenePath;
            public string objectPath;
            public string propertyPath;
            public Object sceneObject;
            public string[] chainPaths;
        }
    }
}
