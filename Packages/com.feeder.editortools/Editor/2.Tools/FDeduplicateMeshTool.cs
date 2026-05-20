using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Feeder
{
    public sealed class FDeduplicateMeshTool : FTargetMeshesToolBase
    {
        public static readonly string LeftPreviewSlotId = "DeduplicateMeshTool_Left";
        public static readonly string RightPreviewSlotId = "DeduplicateMeshTool_Right";

        public Mesh MeshA { get; private set; }
        public Mesh MeshB { get; private set; }

        private const float ContentMargin = 16f;
        private const float NavButtonWidth = 40f;
        private const float NavButtonHeight = 28f;
        private const float NavGap = 8f;

        [PropertyOrder(100)]
        [ShowInInspector, HideLabel]
        [OnInspectorGUI(nameof(DrawMeshCompareUI))]
        private object MeshComparePlaceholder => null;

        protected override string GetDescription()
        {
            return "So sánh từng cặp Mesh asset để tìm và gộp mesh trùng lặp. Kéo mesh vào TargetMeshes, dùng Prev/Next để chọn cặp so sánh, Merge để gộp.";
        }

        private const float ActionButtonGap = 6f;

        private void DrawMeshCompareUI()
        {
            var list = TargetMeshes;
            if (list == null || list.Count == 0)
            {
                MeshA = null;
                MeshB = null;
                EditorGUILayout.HelpBox("Add at least one Mesh (asset) to TargetMeshes to compare.", MessageType.Info);
                return;
            }

            var wasEnabled = GUI.enabled;
            GUI.enabled = true;

            var data = GetDataContainer();
            var count = list.Count;
            var maxIdx = count - 1;

            // clamp stored indices when list size changed (e.g. was 5 items, now 2) to avoid ArgumentOutOfRangeException
            var leftIdx = Mathf.Clamp(data.TargetMeshesLeftPreviewIndex, 0, maxIdx);
            var rightIdx = Mathf.Clamp(data.TargetMeshesRightPreviewIndex, 0, maxIdx);
            if (leftIdx != data.TargetMeshesLeftPreviewIndex) data.TargetMeshesLeftPreviewIndex = leftIdx;
            if (rightIdx != data.TargetMeshesRightPreviewIndex) data.TargetMeshesRightPreviewIndex = rightIdx;

            EnsureLeftRightDifferent(data, count);
            leftIdx = data.TargetMeshesLeftPreviewIndex;
            rightIdx = data.TargetMeshesRightPreviewIndex;
            MeshA = list[leftIdx];
            MeshB = list[rightIdx];

            var availableWidth = Mathf.Max(200f, FOdinMenuLayoutUtils.GetContentWidthWithMargin(ContentMargin));
            var halfW = (availableWidth - NavGap) * 0.5f;
            var totalBlocksWidth = halfW + NavGap + halfW;
            var blocksStartX = (availableWidth - totalBlocksWidth) * 0.5f;

            // nav row: left [Prev][Next] + label | right [Prev][Next] + label
            var navRowHeight = NavButtonHeight + 4f;
            var navRect = GUILayoutUtility.GetRect(availableWidth, navRowHeight);
            var leftNavStart = navRect.x + blocksStartX;
            var rightNavStart = navRect.x + blocksStartX + halfW + NavGap;

            DrawPreviewNavRow(navRect, leftNavStart, halfW, leftIdx, rightIdx, count, data, isLeft: true);
            DrawPreviewNavRow(navRect, rightNavStart, halfW, rightIdx, leftIdx, count, data, isLeft: false);

            var meshRowHeight = EditorGUIUtility.singleLineHeight;
            var meshRowRect = GUILayoutUtility.GetRect(availableWidth, meshRowHeight);
            const float indexLabelWidth = 28f;
            var leftIndexRect = new Rect(meshRowRect.x + blocksStartX, meshRowRect.y, indexLabelWidth, meshRowHeight);
            var leftFieldRect = new Rect(leftIndexRect.xMax, meshRowRect.y, halfW - indexLabelWidth, meshRowHeight);
            var rightIndexRect = new Rect(meshRowRect.x + blocksStartX + halfW + NavGap, meshRowRect.y, indexLabelWidth, meshRowHeight);
            var rightFieldRect = new Rect(rightIndexRect.xMax, meshRowRect.y, halfW - indexLabelWidth, meshRowHeight);

            GUI.Label(leftIndexRect, $"[{leftIdx}]", EditorStyles.miniLabel);
            EditorGUI.ObjectField(leftFieldRect, MeshA, typeof(Mesh), false);
            GUI.Label(rightIndexRect, $"[{rightIdx}]", EditorStyles.miniLabel);
            EditorGUI.ObjectField(rightFieldRect, MeshB, typeof(Mesh), false);

            GUILayout.Space(4f);

            var blockHeight = FMeshPreviewDrawer.BlockHeight;
            var rowRect = GUILayoutUtility.GetRect(availableWidth, blockHeight);
            var leftBlock = new Rect(rowRect.x + blocksStartX, rowRect.y, halfW, blockHeight);
            var rightBlock = new Rect(rowRect.x + blocksStartX + halfW + NavGap, rowRect.y, halfW, blockHeight);

            FMeshPreviewDrawer.DrawBlock(leftBlock, MeshA, LeftPreviewSlotId);
            FMeshPreviewDrawer.DrawBlock(rightBlock, MeshB, RightPreviewSlotId);

            var rotationRowHeight = EditorGUIUtility.singleLineHeight;
            var rotationRowRect = GUILayoutUtility.GetRect(availableWidth, rotationRowHeight);
            PreviewUtils.DrawMeshRotationFields(new Rect(rotationRowRect.x + blocksStartX, rotationRowRect.y, halfW, rotationRowHeight), LeftPreviewSlotId, "Rotation");
            PreviewUtils.DrawMeshRotationFields(new Rect(rotationRowRect.x + blocksStartX + halfW + NavGap, rotationRowRect.y, halfW, rotationRowHeight), RightPreviewSlotId, "Rotation");

            GUILayout.Space(10f);

            // bottom row: FindMesh | AnalyzeScene | AlignMesh (evenly spaced)
            var actionRowHeight = NavButtonHeight;
            var actionRowRect = GUILayoutUtility.GetRect(availableWidth, actionRowHeight);
            var actionButtonWidth = (availableWidth - 2f * ActionButtonGap) / 3f;
            var ax = actionRowRect.x;
            var ay = actionRowRect.y;

            if (GUI.Button(new Rect(ax, ay, actionButtonWidth, actionRowHeight), "FindMesh")) { RunFindMesh(list, leftIdx, rightIdx, data); }
            ax += actionButtonWidth + ActionButtonGap;
            if (GUI.Button(new Rect(ax, ay, actionButtonWidth, actionRowHeight), "AnalyzeScene")) { RunAnalyzeScene(list, leftIdx, rightIdx); }
            ax += actionButtonWidth + ActionButtonGap;
            if (GUI.Button(new Rect(ax, ay, actionButtonWidth, actionRowHeight), "AlignMesh")) { /* TODO */ }

            GUI.enabled = wasEnabled;
        }

        private static void RunFindMesh(IReadOnlyList<Mesh> list, int leftIdx, int rightIdx, FDataContainer data)
        {
            if (list == null || list.Count == 0) return;
            var mesh1 = list[leftIdx];
            if (mesh1 == null) return;
            var foundIdx = DeduplicateMeshUtils.FindSimilarMeshIndex(list, mesh1, leftIdx, 0.1f);
            if (foundIdx < 0) return;
            data.TargetMeshesRightPreviewIndex = foundIdx;
            FDataPersistenceService.SaveData(data);
            GUI.changed = true;
            EditorWindow.focusedWindow?.Repaint();
        }

        private static void RunAnalyzeScene(IReadOnlyList<Mesh> list, int leftIdx, int rightIdx)
        {
            var meshRight = list[rightIdx];
            if (meshRight == null) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            var gos = SceneMeshHighlightUtils.FindGameObjectsWithMesh(scene, meshRight);
            if (gos.Count == 0)
            {
                EditorUtility.DisplayDialog("Analyze Scene", "No GameObjects in the current scene use this mesh.", "OK");
                return;
            }
            SceneMeshHighlightUtils.IsolateGameObjects(scene, gos);
            var meshLeft = list[leftIdx];
            FAlignMeshSceneOverlay.OpenWithSceneMeshCandidates(gos, meshLeft);
            EditorWindow.focusedWindow?.Repaint();
        }

        private static void EnsureLeftRightDifferent(FDataContainer data, int count)
        {
            if (count < 2) return;
            var l = data.TargetMeshesLeftPreviewIndex;
            var r = data.TargetMeshesRightPreviewIndex;
            if (l != r) return;
            data.TargetMeshesRightPreviewIndex = (r + 1) % count;
        }

        private void DrawPreviewNavRow(Rect navRect, float startX, float halfW, int currentIndex, int otherIndex, int count, FDataContainer data, bool isLeft)
        {
            var navGroupWidth = NavButtonWidth + NavGap + NavButtonWidth;
            var groupStart = startX + Mathf.Max(0, (halfW - navGroupWidth) * 0.5f);
            var leftBtn = new Rect(navRect.x + groupStart, navRect.y, NavButtonWidth, NavButtonHeight);
            var rightBtn = new Rect(navRect.x + groupStart + NavButtonWidth + NavGap, navRect.y, NavButtonWidth, NavButtonHeight);

            var prevIcon = EditorGUIUtility.IconContent("d_Animation.PrevKey")?.image;
            var nextIcon = EditorGUIUtility.IconContent("d_Animation.NextKey")?.image;
            if (GUI.Button(leftBtn, new GUIContent(prevIcon, isLeft ? "Previous mesh (left)" : "Previous mesh (right)")))
            {
                var next = (currentIndex - 1 + count) % count;
                while (next == otherIndex && count > 1)
                    next = (next - 1 + count) % count;
                if (isLeft) data.TargetMeshesLeftPreviewIndex = next; else data.TargetMeshesRightPreviewIndex = next;
                FDataPersistenceService.SaveData(data);
                if (Event.current != null) Event.current.Use();
                GUI.changed = true;
                EditorWindow.focusedWindow?.Repaint();
            }
            if (GUI.Button(rightBtn, new GUIContent(nextIcon, isLeft ? "Next mesh (left)" : "Next mesh (right)")))
            {
                var next = (currentIndex + 1) % count;
                while (next == otherIndex && count > 1)
                    next = (next + 1) % count;
                if (isLeft) data.TargetMeshesLeftPreviewIndex = next; else data.TargetMeshesRightPreviewIndex = next;
                FDataPersistenceService.SaveData(data);
                if (Event.current != null) Event.current.Use();
                GUI.changed = true;
                EditorWindow.focusedWindow?.Repaint();
            }
        }
    }
}
