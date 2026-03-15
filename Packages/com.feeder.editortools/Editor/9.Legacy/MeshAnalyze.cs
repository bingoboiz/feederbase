using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    /// <summary>Editor window to compare two meshes by listing vertices side-by-side (index from 0).</summary>
    public sealed class MeshAnalyzeWindow : EditorWindow
    {
        private const string WindowTitle = "Mesh Analyze (Legacy)";
        private const int PageSize = 5;
        private const float PreviewGap = 8f;
        private const float SectionMargin = 8f;
        private const float PreviewMinHeight = 120f;
        private const string SlotIdA = "MeshAnalyzeA";
        private const string SlotIdB = "MeshAnalyzeB";

        [SerializeField] private Mesh _meshA;
        [SerializeField] private Mesh _meshB;
        [SerializeField] private float _positionTolerance = 0.0001f;
        [SerializeField] private bool _showMatchColumn = true;
        [SerializeField] private int _startIndex;

        private Vector3[] _vertsA;
        private Vector3[] _vertsB;
        private int _vertexCountA;
        private int _vertexCountB;

        [MenuItem("Tools/Feeder/Legacy/Mesh Analyze")]
        private static void Open()
        {
            var w = GetWindow<MeshAnalyzeWindow>(WindowTitle);
            w.minSize = new Vector2(420f, 280f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            _meshA = (Mesh)EditorGUILayout.ObjectField("Mesh A", _meshA, typeof(Mesh), false);
            _meshB = (Mesh)EditorGUILayout.ObjectField("Mesh B", _meshB, typeof(Mesh), false);
            _positionTolerance = EditorGUILayout.FloatField("Position tolerance", _positionTolerance);
            _showMatchColumn = EditorGUILayout.Toggle("Show match column", _showMatchColumn);
            EditorGUILayout.Space(4f);

            RefreshVertexData();
            var maxRows = Mathf.Max(_vertexCountA, _vertexCountB);
            if (maxRows == 0)
            {
                EditorGUILayout.HelpBox("Assign both meshes to compare vertices.", MessageType.Info);
                return;
            }

            var maxStart = Mathf.Max(0, maxRows - PageSize);
            _startIndex = Mathf.Clamp(_startIndex, 0, maxStart);
            var endIndex = Mathf.Min(_startIndex + PageSize, maxRows);

            var topSectionBottom = GUILayoutUtility.GetLastRect().yMax;
            var lineH = EditorGUIUtility.singleLineHeight;
            // test random number of lines to find a good height for the table section
            var tableHeight = lineH * 4 + (PageSize * lineH) + SectionMargin; 
            var tableY = position.height - tableHeight;
            var previewY = topSectionBottom + SectionMargin;
            var previewHeight = Mathf.Max(PreviewMinHeight, tableY - previewY - SectionMargin);

            var previewRect = new Rect(0, previewY, position.width, previewHeight);
            DrawPreviewBlocks(previewRect, endIndex);

            var tableRect = new Rect(0, tableY, position.width, tableHeight);
            DrawTableBlock(tableRect, maxRows, endIndex);
        }

        private void DrawTableBlock(Rect tableRect, int maxRows, int endIndex)
        {
            GUILayout.BeginArea(tableRect);
            DrawPagination(maxRows, endIndex);
            DrawTableHeader();
            for (var i = _startIndex; i < endIndex; i++)
                DrawVertexRow(i);
            GUILayout.EndArea();
        }

        private void DrawPreviewBlocks(Rect previewRect, int endIndex)
        {
            if (previewRect.height <= 0) return;
            var halfW = (previewRect.width - PreviewGap) * 0.5f;
            var startX = previewRect.x + (previewRect.width - (halfW + PreviewGap + halfW)) * 0.5f;
            var leftBlock = new Rect(startX, previewRect.y, halfW, previewRect.height);
            var rightBlock = new Rect(startX + halfW + PreviewGap, previewRect.y, halfW, previewRect.height);

            var highlightIndices = GetCurrentPageIndices(endIndex);
            FMeshPreviewDrawer.DrawBlock(leftBlock, _meshA, SlotIdA, highlightIndices);
            FMeshPreviewDrawer.DrawBlock(rightBlock, _meshB, SlotIdB, highlightIndices);
        }

        private List<int> GetCurrentPageIndices(int endIndex)
        {
            var list = new List<int>(PageSize);
            for (var i = _startIndex; i < endIndex; i++)
                list.Add(i);
            return list;
        }

        private void DrawPagination(int maxRows, int endIndex)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Vertices {_startIndex}–{endIndex - 1} of {maxRows}", EditorStyles.miniLabel);
                GUI.enabled = _startIndex > 0;
                if (GUILayout.Button("Prev", GUILayout.Width(48f)))
                    _startIndex = Mathf.Max(0, _startIndex - PageSize);
                GUI.enabled = _startIndex < maxRows - PageSize;
                if (GUILayout.Button("Next", GUILayout.Width(48f)))
                    _startIndex = Mathf.Min(maxRows - PageSize, _startIndex + PageSize);
                GUI.enabled = true;
            }
        }

        private void RefreshVertexData()
        {
            _vertexCountA = _meshA?.vertexCount ?? 0;
            _vertexCountB = _meshB?.vertexCount ?? 0;
            _vertsA = _meshA != null ? _meshA.vertices : null;
            _vertsB = _meshB != null ? _meshB.vertices : null;
        }

        private void DrawTableHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Index", EditorStyles.toolbarButton, GUILayout.Width(48f));
                GUILayout.Label("Mesh A (x, y, z)", EditorStyles.toolbarButton, GUILayout.MinWidth(140f));
                GUILayout.Label("Mesh B (x, y, z)", EditorStyles.toolbarButton, GUILayout.MinWidth(140f));
                if (_showMatchColumn)
                    GUILayout.Label("Match", EditorStyles.toolbarButton, GUILayout.Width(44f));
            }
        }

        private void DrawVertexRow(int index)
        {
            var vA = index < _vertexCountA && _vertsA != null ? _vertsA[index] : (Vector3?)null;
            var vB = index < _vertexCountB && _vertsB != null ? _vertsB[index] : (Vector3?)null;
            var match = _showMatchColumn && vA.HasValue && vB.HasValue && VerticesEqual(vA.Value, vB.Value, _positionTolerance);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(index.ToString(), GUILayout.Width(48f));
                EditorGUILayout.LabelField(FormatVertex(vA), EditorStyles.wordWrappedLabel, GUILayout.MinWidth(140f));
                EditorGUILayout.LabelField(FormatVertex(vB), EditorStyles.wordWrappedLabel, GUILayout.MinWidth(140f));
                if (_showMatchColumn)
                {
                    var style = match ? EditorStyles.label : new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } };
                    EditorGUILayout.LabelField(match ? "Yes" : "No", style, GUILayout.Width(44f));
                }
            }
        }

        private static string FormatVertex(Vector3? v)
        {
            if (!v.HasValue) return "—";
            var p = v.Value;
            return $"({p.x:F4}, {p.y:F4}, {p.z:F4})";
        }

        private static bool VerticesEqual(Vector3 a, Vector3 b, float tolerance)
        {
            return Math.Abs(a.x - b.x) <= tolerance
                   && Math.Abs(a.y - b.y) <= tolerance
                   && Math.Abs(a.z - b.z) <= tolerance;
        }
    }
}
