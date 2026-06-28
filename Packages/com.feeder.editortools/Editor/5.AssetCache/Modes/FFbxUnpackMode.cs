using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder
{
    public sealed class FFbxUnpackMode : FRefModeBase
    {
        [Title("Source")]
        [LabelText("Source FBX")]
        [AssetsOnly]
        [ShowInInspector, OdinSerialize]
        private Object sourceFbx;

        [LabelText("Save Folder")]
        [FolderPath(AbsolutePath = false, RequireExistingPath = true)]
        [ShowInInspector, OdinSerialize]
        private string saveFolderPath = "Assets";

        [NonSerialized] private FFbxUnpackPlan previewPlan;
        [NonSerialized] private FFbxUnpackApplyResult lastApplyResult;
        [NonSerialized] private string searchText;
        [NonSerialized] private Vector2 scroll;

        protected override string GetDescription() =>
            "Preview and unpack one FBX into standalone assets, then remap prefab/scene/asset references by GUID + local file ID.";

        [OnInspectorGUI]
        private void Draw()
        {
            DrawToolbar();
            EditorGUILayout.Space(4);
            DrawSummary();
            EditorGUILayout.Space(4);
            DrawResults();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Use Selection", EditorStyles.toolbarButton, GUILayout.Width(95)))
                UseSelection();

            using (new EditorGUI.DisabledScope(sourceFbx == null || string.IsNullOrWhiteSpace(saveFolderPath)))
            {
                if (GUILayout.Button("Preview", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    RunPreview();
            }

            using (new EditorGUI.DisabledScope(previewPlan == null || !previewPlan.HasReferences))
            {
                if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    RunApply();
            }

            using (new EditorGUI.DisabledScope(previewPlan == null))
            {
                if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(65)))
                    Rescan();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Search", GUILayout.Width(44));
            searchText = GUILayout.TextField(searchText ?? string.Empty, GetToolbarSearchStyle(), GUILayout.Width(220));
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(searchText)))
            {
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(46)))
                    searchText = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void UseSelection()
        {
            Object active = Selection.activeObject;
            if (active == null)
                return;

            string path = AssetDatabase.GetAssetPath(active);
            if (!string.IsNullOrEmpty(path) && Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                sourceFbx = active;
        }

        private void RunPreview()
        {
            try
            {
                previewPlan = FFbxUnpackService.BuildPreview(sourceFbx, saveFolderPath);
                lastApplyResult = null;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("FBX Unpack", ex.Message, "OK");
            }
        }

        private void RunApply()
        {
            if (previewPlan == null) return;

            bool confirmed = EditorUtility.DisplayDialog(
                "FBX Unpack",
                $"This will create standalone assets and modify {previewPlan.ReferenceHits.Select(h => h.AssetPath).Distinct().Count()} asset file(s).\n\nThe source FBX will not be deleted.",
                "Apply",
                "Cancel");
            if (!confirmed) return;

            try
            {
                lastApplyResult = FFbxUnpackService.Apply(previewPlan);
                EditorUtility.DisplayDialog(
                    "FBX Unpack",
                    $"Extracted: {lastApplyResult.ExtractedCount}\nTouched assets: {lastApplyResult.TouchedAssetCount}\nReplaced refs: {lastApplyResult.ReplacedReferenceCount}\nRemaining refs: {lastApplyResult.RemainingReferenceCount}",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("FBX Unpack", ex.Message, "OK");
            }
        }

        private void Rescan()
        {
            if (previewPlan == null) return;
            FFbxUnpackService.ScanRemainingReferences(previewPlan);
        }

        private void DrawSummary()
        {
            if (previewPlan == null)
            {
                EditorGUILayout.HelpBox(
                    "Select one FBX, choose a save folder, then click Preview. The tool uses the Asset Cache database to find direct users and remaps exact GUID + local file ID references.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Source", previewPlan.SourceFbxPath);
            EditorGUILayout.LabelField("Output", previewPlan.RootOutputFolderPath);
            EditorGUILayout.LabelField(
                "Counts",
                $"{previewPlan.SubAssets.Count} sub-assets | {previewPlan.CandidateAssetPaths.Count} candidates | {previewPlan.ReferenceHits.Count} reference rows");

            if (lastApplyResult != null)
            {
                EditorGUILayout.HelpBox(
                    $"Last Apply: extracted {lastApplyResult.ExtractedCount}, touched {lastApplyResult.TouchedAssetCount}, replaced {lastApplyResult.ReplacedReferenceCount}, remaining {lastApplyResult.RemainingReferenceCount}.",
                    lastApplyResult.RemainingReferenceCount == 0 ? MessageType.Info : MessageType.Warning);
            }

            foreach (string warning in previewPlan.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        private void DrawResults()
        {
            if (previewPlan == null)
                return;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawSubAssets();
            EditorGUILayout.Space(6);
            DrawReferenceHits();
            EditorGUILayout.Space(6);
            DrawLogs();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSubAssets()
        {
            EditorGUILayout.LabelField("FBX Sub-Assets", EditorStyles.boldLabel);
            if (previewPlan.SubAssets.Count == 0)
            {
                EditorGUILayout.HelpBox("No supported FBX sub-assets found.", MessageType.Warning);
                return;
            }

            foreach (FFbxSubAssetInfo info in FilterSubAssets(previewPlan.SubAssets))
            {
                Rect rect = EditorGUILayout.GetControlRect(false, 22f);
                DrawRowBackground(rect);

                Texture icon = EditorGUIUtility.ObjectContent(info.SourceObject, info.SourceObject != null ? info.SourceObject.GetType() : typeof(Object)).image;
                if (icon != null)
                    GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 3f, 16f, 16f), icon);

                string label = $"{info.DisplayName}    <{info.DisplayType}>    {info.Key.LocalFileId}";
                GUI.Label(new Rect(rect.x + 24f, rect.y + 2f, rect.width - 28f, 18f), new GUIContent(label, info.Key.ToString()));

                HandleObjectRowClick(rect, info.SourceObject);
            }
        }

        private void DrawReferenceHits()
        {
            EditorGUILayout.LabelField("References To Remap", EditorStyles.boldLabel);
            if (previewPlan.ReferenceHits.Count == 0)
            {
                EditorGUILayout.HelpBox("No direct references found in cache candidates.", MessageType.Info);
                return;
            }

            List<FFbxReferenceHit> rows = FilterHits(previewPlan.ReferenceHits);
            FRefResultGUI.DrawAssetPathActions(rows.Select(r => r.AssetPath));

            foreach (IGrouping<string, FFbxReferenceHit> group in rows.GroupBy(r => r.AssetPath).OrderBy(g => g.Key))
            {
                EditorGUILayout.LabelField($"{group.Key} ({group.Sum(r => r.Count)})", EditorStyles.miniBoldLabel);
                foreach (FFbxReferenceHit hit in group)
                    DrawHitRow(hit);
            }
        }

        private void DrawHitRow(FFbxReferenceHit hit)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            rect.xMin += 14f;
            DrawRowBackground(rect);

            Texture icon = AssetDatabase.GetCachedIcon(hit.AssetPath);
            if (icon != null)
                GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 3f, 16f, 16f), icon);

            string label = $"{hit.SourceName}    <{hit.SourceType}>    x{hit.Count}    {hit.ReferenceKind}";
            GUI.Label(new Rect(rect.x + 24f, rect.y + 2f, rect.width - 28f, 18f), new GUIContent(label, hit.AssetPath));

            HandleObjectRowClick(rect, AssetDatabase.LoadAssetAtPath<Object>(hit.AssetPath));
        }

        private void DrawLogs()
        {
            if (lastApplyResult == null || lastApplyResult.Logs.Count == 0)
                return;

            EditorGUILayout.LabelField("Apply Log", EditorStyles.boldLabel);
            foreach (string log in lastApplyResult.Logs)
                EditorGUILayout.LabelField(log, EditorStyles.wordWrappedMiniLabel);
        }

        private List<FFbxSubAssetInfo> FilterSubAssets(List<FFbxSubAssetInfo> source)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return source;
            return source.Where(s =>
                Contains(s.DisplayName, searchText) ||
                Contains(s.DisplayType, searchText) ||
                Contains(s.Key.LocalFileId.ToString(), searchText)).ToList();
        }

        private List<FFbxReferenceHit> FilterHits(List<FFbxReferenceHit> source)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return source;
            return source.Where(h =>
                Contains(h.AssetPath, searchText) ||
                Contains(h.SourceName, searchText) ||
                Contains(h.SourceType, searchText) ||
                Contains(h.ReferenceKind, searchText)).ToList();
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   !string.IsNullOrEmpty(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawRowBackground(Rect rect)
        {
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(0.25f, 0.55f, 1f, 0.12f));
        }

        private static void HandleObjectRowClick(Rect rect, Object obj)
        {
            if (obj == null) return;

            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
                if (e.clickCount == 2)
                    AssetDatabase.OpenAsset(obj);
                e.Use();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        private static GUIStyle GetToolbarSearchStyle()
        {
            return GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
        }
    }
}
