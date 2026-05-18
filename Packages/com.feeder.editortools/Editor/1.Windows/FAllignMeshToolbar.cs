using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder
{
    // scene overlay: opened by FDeduplicateMeshTool Analyze Scene
    public static class FAlignMeshSceneOverlay
    {
        private const int WindowId = 999999;
        private const float WindowWidth = 260f;
        private const float WindowHeight = 160f;
        private const float HeaderHeight = 24f;
        private const float NavButtonWidth = 30f;
        private const float CloseButtonSize = 18f;

        private static bool _showWindow;
        private static Rect _windowRect = new Rect(0, 0, WindowWidth, WindowHeight);
        private static bool _initializedPosition;
        private static Rect _headerRect;

        private static readonly List<GameObject> SceneMeshCandidates = new List<GameObject>();
        private static GameObject _compareMeshObject;
        private static int _currentIndex = -1;

        private static GUIStyle _headerStyle;
        private static GUIStyle _bodyStyle;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public static void OpenWithSceneMeshCandidates(List<GameObject> candidates, Mesh leftPreviewMesh)
        {
            if (candidates == null) return;
            SceneMeshCandidates.Clear();
            SceneMeshCandidates.AddRange(candidates);
            _showWindow = true;
            FAlignMeshSceneGizmoDrawer.SetSharedMesh(leftPreviewMesh);
            FAlignMeshSceneGizmoDrawer.SetDrawingEnabled(true);
            _currentIndex = SceneMeshCandidates.Count > 0 ? 0 : -1;
            _compareMeshObject = (_currentIndex >= 0 && _currentIndex < SceneMeshCandidates.Count) ? SceneMeshCandidates[_currentIndex] : null;
            SyncGizmoPoseFromCompareObject(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
            SceneView.RepaintAll();
        }

        private static void OnAfterAssemblyReload()
        {
            DestroyLegacyMeshHighlightDrawerHoldersInLoadedScenes();
            FAlignMeshSceneGizmoDrawer.Clear();
            _showWindow = false;
        }

        private static void DestroyLegacyMeshHighlightDrawerHoldersInLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    MeshHighlightDrawer[] drawers = roots[r].GetComponentsInChildren<MeshHighlightDrawer>(true);
                    for (int d = 0; d < drawers.Length; d++)
                        Object.DestroyImmediate(drawers[d].gameObject);
                }
            }
        }

        private static void CloseOverlay()
        {
            _showWindow = false;
            FAlignMeshSceneGizmoDrawer.Clear();
        }

        private static void SyncGizmoPoseFromCompareObject(GameObject source)
        {
            FAlignMeshSceneGizmoDrawer.CopyPoseFrom(source);
        }

        private static void FocusSceneViewOn(GameObject go)
        {
            if (go == null) return;
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static void InitStyles()
        {
            if (_headerStyle != null) return;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            _bodyStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 8, 8) };
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!_showWindow) return;
            InitStyles();
            if (!_initializedPosition)
            {
                var viewRect = sceneView.position;
                _windowRect.x = viewRect.width - _windowRect.width - 10f;
                _windowRect.y = viewRect.height - _windowRect.height - 30f;
                _initializedPosition = true;
            }
            Handles.BeginGUI();
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "", GUIStyle.none);
            Handles.EndGUI();
        }

        private static void DrawWindow(int id)
        {
            DrawBackground();
            DrawHeader();
            GUILayout.Space(26f);
            GUILayout.BeginVertical(_bodyStyle);
            DrawSceneMeshCandidatesList();
            DrawCompareControls();
            DrawActionButtons();
            GUILayout.EndVertical();
            GUI.DragWindow(_headerRect);
        }

        private static void DrawBackground()
        {
            GUI.Box(new Rect(0, 0, _windowRect.width, _windowRect.height), GUIContent.none);
        }

        private static void DrawHeader()
        {
            _headerRect = new Rect(0, 0, _windowRect.width, HeaderHeight);
            EditorGUI.DrawRect(_headerRect, new Color(0.22f, 0.22f, 0.22f));
            GUI.Label(_headerRect, "Align Mesh Tool", _headerStyle);
            var closeRect = new Rect(_windowRect.width - 22f, 3f, CloseButtonSize, CloseButtonSize);
            if (GUI.Button(closeRect, "X"))
                CloseOverlay();
        }

        private static void DrawSceneMeshCandidatesList()
        {
            GUILayout.Label("Scene mesh candidates");
            var newCount = Mathf.Max(0, EditorGUILayout.IntField("Size", SceneMeshCandidates.Count));
            while (newCount > SceneMeshCandidates.Count)
                SceneMeshCandidates.Add(null);
            while (newCount < SceneMeshCandidates.Count)
                SceneMeshCandidates.RemoveAt(SceneMeshCandidates.Count - 1);
            for (var i = 0; i < SceneMeshCandidates.Count; i++)
                SceneMeshCandidates[i] = (GameObject)EditorGUILayout.ObjectField(SceneMeshCandidates[i], typeof(GameObject), true);
        }

        private static void DrawCompareControls()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(NavButtonWidth)))
                SelectPrevCandidate();
            _compareMeshObject = (GameObject)EditorGUILayout.ObjectField(_compareMeshObject, typeof(GameObject), true);
            if (GUILayout.Button(">", GUILayout.Width(NavButtonWidth)))
                SelectNextCandidate();
            GUILayout.EndHorizontal();
        }

        private static void DrawActionButtons()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("AlignMeshManual"))
            {
                ApplyAlignMeshFromPreviewRotationDelta();
            }
            if (GUILayout.Button("AlignMeshICP"))
            {
                ApplyTrimIcpToHighlightDrawer();
            }
            if (GUILayout.Button("ApplyMesh"))
                ApplyMeshToCompareObject();
            GUILayout.EndHorizontal();
        }

        private static void ApplyMeshToCompareObject()
        {
            if (!FAlignMeshSceneGizmoDrawer.DrawingEnabled || _compareMeshObject == null) return;

            Mesh newMesh = FAlignMeshSceneGizmoDrawer.SharedMesh;
            if (newMesh == null) return;

            MeshFilter mf = _compareMeshObject.GetComponent<MeshFilter>();
            if (mf == null) return;

            Transform compareT = _compareMeshObject.transform;
            Transform parent = compareT.parent;
            Vector3 gizmoPosition = FAlignMeshSceneGizmoDrawer.Position;
            Quaternion gizmoRotation = FAlignMeshSceneGizmoDrawer.Rotation;
            Vector3 gizmoLossyScale = FAlignMeshSceneGizmoDrawer.LossyScale;

            Undo.RecordObject(mf, "Apply Mesh");
            Undo.RecordObject(compareT, "Apply Mesh Transform");

            mf.sharedMesh = newMesh;

            if (parent != null)
            {
                compareT.localPosition = parent.InverseTransformPoint(gizmoPosition);
                compareT.localRotation = Quaternion.Inverse(parent.rotation) * gizmoRotation;
                Vector3 parentScale = parent.lossyScale;
                compareT.localScale = new Vector3(
                    parentScale.x != 0f ? gizmoLossyScale.x / parentScale.x : 0f,
                    parentScale.y != 0f ? gizmoLossyScale.y / parentScale.y : 0f,
                    parentScale.z != 0f ? gizmoLossyScale.z / parentScale.z : 0f);
            }
            else
            {
                compareT.SetPositionAndRotation(gizmoPosition, gizmoRotation);
                compareT.localScale = gizmoLossyScale;
            }

            EditorUtility.SetDirty(_compareMeshObject);

            var idx = SceneMeshCandidates.IndexOf(_compareMeshObject);
            if (idx >= 0)
            {
                SceneMeshCandidates.RemoveAt(idx);
                if (idx < _currentIndex) _currentIndex--;
                if (SceneMeshCandidates.Count == 0) _currentIndex = -1;
                else _currentIndex = Mathf.Clamp(_currentIndex, 0, SceneMeshCandidates.Count - 1);
            }

            SceneView.RepaintAll();
        }

        private static void ApplyAlignMeshFromPreviewRotationDelta()
        {
            if (!FAlignMeshSceneGizmoDrawer.DrawingEnabled || _compareMeshObject == null) return;

            Vector3 leftEuler = FMeshPreviewDrawer.GetRotationEuler(FDeduplicateMeshTool.LeftPreviewSlotId);
            Vector3 rightEuler = FMeshPreviewDrawer.GetRotationEuler(FDeduplicateMeshTool.RightPreviewSlotId);

            Quaternion leftQ = Quaternion.Euler(leftEuler);
            Quaternion rightQ = Quaternion.Euler(rightEuler);
            Quaternion deltaQ = rightQ * Quaternion.Inverse(leftQ);

            Transform compareT = _compareMeshObject.transform;
            Quaternion finalRotation = deltaQ * compareT.rotation;

            FAlignMeshSceneGizmoDrawer.SetPositionAndRotation(compareT.position, finalRotation);
            FAlignMeshSceneGizmoDrawer.SetLossyScale(compareT.lossyScale);
        }

        // ICP (not Trimmed): same logic as MeshKabschAlignMathNetTool.AlignOnce ? nearest-point then Kabsch, apply delta each iter
        private static void ApplyTrimIcpToHighlightDrawer()
        {
            if (!FAlignMeshSceneGizmoDrawer.DrawingEnabled || _compareMeshObject == null) return;

            Mesh meshGizmo = FAlignMeshSceneGizmoDrawer.SharedMesh;
            MeshFilter mfB = _compareMeshObject.GetComponent<MeshFilter>();
            Mesh meshB = mfB.sharedMesh;
            if (meshGizmo.vertexCount < 3 || meshB.vertexCount < 3) return;

            Transform tB = _compareMeshObject.transform;
            Vector3[] vertsGizmo = meshGizmo.vertices;
            Vector3[] vertsB = meshB.vertices;
            int nGizmo = vertsGizmo.Length;
            int nB = vertsB.Length;

            Vector3[] worldPointsB = new Vector3[nB];
            for (int j = 0; j < nB; j++)
                worldPointsB[j] = tB.TransformPoint(vertsB[j]);

            Vector3[] worldSource = new Vector3[nGizmo];
            Vector3[] pairedTarget = new Vector3[nGizmo];

            const int icpMaxIterations = 20;
            const float convergencePos = 1e-5f;
            const float convergenceDeg = 0.001f;

            Matrix4x4 gizmoMatrix = Matrix4x4.TRS(
                FAlignMeshSceneGizmoDrawer.Position,
                FAlignMeshSceneGizmoDrawer.Rotation,
                FAlignMeshSceneGizmoDrawer.LossyScale);

            for (int iter = 0; iter < icpMaxIterations; iter++)
            {
                for (int i = 0; i < nGizmo; i++)
                    worldSource[i] = gizmoMatrix.MultiplyPoint3x4(vertsGizmo[i]);

                for (int i = 0; i < nGizmo; i++)
                {
                    float bestSq = float.MaxValue;
                    int bestJ = 0;
                    for (int j = 0; j < nB; j++)
                    {
                        float sq = (worldSource[i] - worldPointsB[j]).sqrMagnitude;
                        if (sq < bestSq) { bestSq = sq; bestJ = j; }
                    }
                    pairedTarget[i] = worldPointsB[bestJ];
                }

                if (!MeshMatchTransformUtils.ComputeRigidTransform(worldSource, pairedTarget, out Matrix4x4 Rt))
                {
                    Debug.LogWarning("[AlignMesh ICP] ComputeRigidTransform failed.");
                    break;
                }

                FAlignMeshSceneGizmoDrawer.ApplyRigidTransformDelta(Rt);
                gizmoMatrix = Matrix4x4.TRS(
                    FAlignMeshSceneGizmoDrawer.Position,
                    FAlignMeshSceneGizmoDrawer.Rotation,
                    FAlignMeshSceneGizmoDrawer.LossyScale);

                if (IsIcpConverged(Rt, convergencePos, convergenceDeg))
                    break;
            }

            FAlignMeshSceneGizmoDrawer.SetLossyScale(tB.lossyScale);
        }

        private static bool IsIcpConverged(Matrix4x4 Rt, float convergencePos, float convergenceDeg)
        {
            Vector3 trans = Rt.GetColumn(3);
            if (trans.sqrMagnitude > convergencePos * convergencePos) return false;
            return Quaternion.Angle(Quaternion.identity, Rt.rotation) <= convergenceDeg;
        }

        private static void SelectPrevCandidate()
        {
            if (SceneMeshCandidates.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + SceneMeshCandidates.Count) % SceneMeshCandidates.Count;
            _compareMeshObject = SceneMeshCandidates[_currentIndex];
            SyncGizmoPoseFromCompareObject(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
        }

        private static void SelectNextCandidate()
        {
            if (SceneMeshCandidates.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % SceneMeshCandidates.Count;
            _compareMeshObject = SceneMeshCandidates[_currentIndex];
            SyncGizmoPoseFromCompareObject(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
        }
    }
}
