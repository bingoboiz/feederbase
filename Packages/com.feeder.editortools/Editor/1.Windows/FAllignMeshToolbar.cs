using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder
{
    // scene overlay: opened by FDeduplicateMeshTool Analyze Scene
    public static class FAlignMeshSceneOverlay
    {
        private const string HighlightHolderName = "AlignMeshToolbar_HighlightDrawer";
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
        private static GameObject _highlightDrawerHolder;

        private static GUIStyle _headerStyle;
        private static GUIStyle _bodyStyle;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.afterAssemblyReload += DestroyAllMeshHighlightDrawerHoldersInLoadedScenes;
        }

        public static void OpenWithSceneMeshCandidates(List<GameObject> candidates, Mesh leftPreviewMesh)
        {
            if (candidates == null) return;
            SceneMeshCandidates.Clear();
            SceneMeshCandidates.AddRange(candidates);
            _showWindow = true;
            EnsureHighlightDrawerHolder(leftPreviewMesh);
            _currentIndex = SceneMeshCandidates.Count > 0 ? 0 : -1;
            _compareMeshObject = (_currentIndex >= 0 && _currentIndex < SceneMeshCandidates.Count) ? SceneMeshCandidates[_currentIndex] : null;
            SyncHighlightTransformFrom(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
            SceneView.RepaintAll();
        }

        private static void EnsureHighlightDrawerHolder(Mesh sharedMesh)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) return;
            // reuse existing or create exactly one; holder must be root (no parent)
            if (_highlightDrawerHolder == null)
                _highlightDrawerHolder = FindOrCreateSingleMeshHighlightDrawerHolder(scene);
            MeshHighlightDrawer.SharedMesh = sharedMesh;
        }

        private static GameObject FindOrCreateSingleMeshHighlightDrawerHolder(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            GameObject found = null;
            var toDestroy = new List<GameObject>();
            for (int i = 0; i < roots.Length; i++)
            {
                var drawers = roots[i].GetComponentsInChildren<MeshHighlightDrawer>(true);
                for (int j = 0; j < drawers.Length; j++)
                {
                    var go = drawers[j].gameObject;
                    if (found == null)
                        found = go;
                    else
                        toDestroy.Add(go);
                }
            }
            for (int i = 0; i < toDestroy.Count; i++)
                Object.DestroyImmediate(toDestroy[i]);
            if (found != null)
            {
                found.transform.SetParent(null);
                return found;
            }
            var created = new GameObject(HighlightHolderName);
            created.AddComponent<MeshHighlightDrawer>();
            if (roots.Length > 0)
            {
                created.transform.SetParent(roots[0].transform);
                Undo.RegisterCreatedObjectUndo(created, "Align Mesh Toolbar Highlight");
                created.transform.SetParent(null);
            }
            else
                Undo.RegisterCreatedObjectUndo(created, "Align Mesh Toolbar Highlight");
            return created;
        }

        private static void DestroyHighlightDrawerHolder()
        {
            if (_highlightDrawerHolder == null) return;
            Object.DestroyImmediate(_highlightDrawerHolder);
            _highlightDrawerHolder = null;
        }

        private static void DestroyAllMeshHighlightDrawerHoldersInLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid()) continue;
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    var drawers = roots[r].GetComponentsInChildren<MeshHighlightDrawer>(true);
                    for (int d = 0; d < drawers.Length; d++)
                        Object.DestroyImmediate(drawers[d].gameObject);
                }
            }
            _highlightDrawerHolder = null;
        }

        private static void SyncHighlightTransformFrom(GameObject source)
        {
            if (_highlightDrawerHolder == null) return;
            if (source != null)
            {
                _highlightDrawerHolder.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                _highlightDrawerHolder.transform.localScale = source.transform.lossyScale;
                _highlightDrawerHolder.SetActive(true);
            }
            else
                _highlightDrawerHolder.SetActive(false);
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
            {
                _showWindow = false;
                DestroyHighlightDrawerHolder();
            }
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
            if (_highlightDrawerHolder == null || _compareMeshObject == null) return;

            var newMesh = MeshHighlightDrawer.SharedMesh;
            if (newMesh == null) return;

            var mf = _compareMeshObject.GetComponent<MeshFilter>();
            if (mf == null) return;

            var parent = _compareMeshObject.transform.parent;
            var holderT = _highlightDrawerHolder.transform;

            Undo.RecordObject(mf, "Apply Mesh");
            Undo.RecordObject(_compareMeshObject.transform, "Apply Mesh Transform");

            mf.sharedMesh = newMesh;

            if (parent != null)
            {
                _compareMeshObject.transform.localPosition = parent.InverseTransformPoint(holderT.position);
                _compareMeshObject.transform.localRotation = Quaternion.Inverse(parent.rotation) * holderT.rotation;
                var parentScale = parent.lossyScale;
                var targetScale = holderT.lossyScale;
                _compareMeshObject.transform.localScale = new Vector3(
                    parentScale.x != 0f ? targetScale.x / parentScale.x : 0f,
                    parentScale.y != 0f ? targetScale.y / parentScale.y : 0f,
                    parentScale.z != 0f ? targetScale.z / parentScale.z : 0f);
            }
            else
            {
                _compareMeshObject.transform.SetPositionAndRotation(holderT.position, holderT.rotation);
                _compareMeshObject.transform.localScale = holderT.lossyScale;
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
            if (_highlightDrawerHolder == null || _compareMeshObject == null) return;

            var leftEuler = FMeshPreviewDrawer.GetRotationEuler(FDeduplicateMeshTool.LeftPreviewSlotId);
            var rightEuler = FMeshPreviewDrawer.GetRotationEuler(FDeduplicateMeshTool.RightPreviewSlotId);

            Debug.Log($"Left Euler: {leftEuler}, Right Euler: {rightEuler}");

            var leftQ = Quaternion.Euler(leftEuler);
            var rightQ = Quaternion.Euler(rightEuler);

            var deltaQ = rightQ * Quaternion.Inverse(leftQ);

            var finalRotation = deltaQ * _compareMeshObject.transform.rotation;

            _highlightDrawerHolder.transform.SetPositionAndRotation(
                _compareMeshObject.transform.position,
                finalRotation
            );

            _highlightDrawerHolder.transform.localScale = _compareMeshObject.transform.lossyScale;

            SceneView.RepaintAll();
        }

        // ICP (not Trimmed): same logic as MeshKabschAlignMathNetTool.AlignOnce ? nearest-point then Kabsch, apply delta each iter
        private static void ApplyTrimIcpToHighlightDrawer()
        {
            if (_highlightDrawerHolder == null || _compareMeshObject == null) return;

            var meshGizmo = MeshHighlightDrawer.SharedMesh;
            var mfB = _compareMeshObject.GetComponent<MeshFilter>();
            var meshB = mfB?.sharedMesh;
            if (meshGizmo == null || meshB == null) return;
            if (meshGizmo.vertexCount < 3 || meshB.vertexCount < 3) return;

            var tDrawer = _highlightDrawerHolder.transform;
            var tB = _compareMeshObject.transform;
            var vertsGizmo = meshGizmo.vertices;
            var vertsB = meshB.vertices;
            int nGizmo = vertsGizmo.Length;
            int nB = vertsB.Length;

            var worldPointsB = new Vector3[nB];
            for (int j = 0; j < nB; j++)
                worldPointsB[j] = tB.TransformPoint(vertsB[j]);

            var worldSource = new Vector3[nGizmo];
            var pairedTarget = new Vector3[nGizmo];

            const int icpMaxIterations = 20;
            const float convergencePos = 1e-5f;
            const float convergenceDeg = 0.001f;

            Undo.RecordObject(tDrawer, "Align Mesh ICP (Kabsch)");
            for (int iter = 0; iter < icpMaxIterations; iter++)
            {
                for (int i = 0; i < nGizmo; i++)
                    worldSource[i] = tDrawer.TransformPoint(vertsGizmo[i]);

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

                ApplyIcpDeltaTransform(tDrawer, Rt);

                if (IsIcpConverged(Rt, convergencePos, convergenceDeg))
                    break;
            }

            tDrawer.localScale = _compareMeshObject.transform.lossyScale;
            EditorUtility.SetDirty(tDrawer);
            SceneView.RepaintAll();
        }

        private static void ApplyIcpDeltaTransform(Transform t, Matrix4x4 Rt)
        {
            t.position = Rt.MultiplyPoint3x4(t.position);
            t.rotation = Rt.rotation * t.rotation;
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
            SyncHighlightTransformFrom(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
        }

        private static void SelectNextCandidate()
        {
            if (SceneMeshCandidates.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % SceneMeshCandidates.Count;
            _compareMeshObject = SceneMeshCandidates[_currentIndex];
            SyncHighlightTransformFrom(_compareMeshObject);
            FocusSceneViewOn(_compareMeshObject);
        }
    }
}
