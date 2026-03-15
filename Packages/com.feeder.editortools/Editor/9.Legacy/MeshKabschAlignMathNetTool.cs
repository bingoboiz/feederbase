using System;
using MathNet.Numerics.LinearAlgebra.Double;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    /// <summary>Align MeshGizmoDrawer to mesh B using ICP with nearest-point correspondence (Kabsch unchanged).</summary>
    public sealed class MeshKabschAlignMathNetToolWindow : EditorWindow
    {
        private const string WindowTitle = "Mesh ICP Align (Kabsch + MathNet)";

        private const float DefaultConvergencePos = 1e-5f;
        private const float DefaultConvergenceDeg = 0.001f;

        [SerializeField] private MeshGizmoDrawer _gizmoDrawer;
        [SerializeField] private GameObject _goB;
        [SerializeField] private int _icpMaxIterations = 20;
        [SerializeField] private float _convergencePos = DefaultConvergencePos;
        [SerializeField] private float _convergenceDeg = DefaultConvergenceDeg;
        [SerializeField] private Vector2 _scroll;

        [MenuItem("Tools/Feeder/Mesh Kabsch Align (MathNet)")]
        private static void Open()
        {
            var w = GetWindow<MeshKabschAlignMathNetToolWindow>(WindowTitle);
            w.minSize = new Vector2(280f, 200f);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4f);
            _gizmoDrawer = (MeshGizmoDrawer)EditorGUILayout.ObjectField("Gizmo drawer", _gizmoDrawer, typeof(MeshGizmoDrawer), true);
            _goB = (GameObject)EditorGUILayout.ObjectField("Mesh B (target)", _goB, typeof(GameObject), true);
            _icpMaxIterations = Mathf.Max(1, EditorGUILayout.IntField("ICP max iterations", _icpMaxIterations));
            _convergencePos = Mathf.Max(0f, EditorGUILayout.FloatField("Convergence position", _convergencePos));
            _convergenceDeg = Mathf.Max(0f, EditorGUILayout.FloatField("Convergence rotation (deg)", _convergenceDeg));
            EditorGUILayout.HelpBox("ICP: vertex order can differ; each step finds nearest B-point per source, then Kabsch until convergence.", MessageType.None);
            EditorGUILayout.Space(4f);

            EditorGUI.BeginDisabledGroup(!CanAlign());
            if (GUILayout.Button("Align (ICP)"))
                AlignOnce();
            EditorGUI.EndDisabledGroup();

            if (!CanAlign() && (_gizmoDrawer != null || _goB != null))
                EditorGUILayout.HelpBox("Assign Gizmo drawer (Mesh) and Mesh B (MeshFilter). Both meshes need at least 3 vertices.", MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        private bool CanAlign()
        {
            if (_gizmoDrawer?.Mesh == null || _goB == null) return false;
            var mfB = _goB.GetComponent<MeshFilter>();
            if (mfB?.sharedMesh == null) return false;
            var meshGizmo = _gizmoDrawer.Mesh;
            var meshB = mfB.sharedMesh;
            return meshGizmo.vertexCount >= 3 && meshB.vertexCount >= 3;
        }

        private void AlignOnce()
        {
            if (!CanAlign()) return;

            var meshGizmo = _gizmoDrawer.Mesh;
            var meshB = _goB.GetComponent<MeshFilter>().sharedMesh;
            var tDrawer = _gizmoDrawer.transform;
            var tB = _goB.transform;
            var vertsGizmo = meshGizmo.vertices;
            var vertsB = meshB.vertices;
            int nGizmo = vertsGizmo.Length;
            int nB = vertsB.Length;

            // mesh B world points (fixed for whole ICP)
            var worldPointsB = new Vector3[nB];
            for (int j = 0; j < nB; j++)
                worldPointsB[j] = tB.TransformPoint(vertsB[j]);

            var worldSource = new Vector3[nGizmo];
            var pairedTarget = new Vector3[nGizmo];

            Undo.RecordObject(tDrawer, "ICP Align (Kabsch)");
            for (int iter = 0; iter < _icpMaxIterations; iter++)
            {
                for (int i = 0; i < nGizmo; i++)
                    worldSource[i] = tDrawer.TransformPoint(vertsGizmo[i]);

                // nearest-point correspondence: each source -> nearest in B
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

                if (!KabschRigidMathNet.ComputeRigidTransform(worldSource, pairedTarget, out Matrix4x4 Rt))
                {
                    Debug.LogWarning("[ICP] ComputeRigidTransform failed.");
                    break;
                }

                ApplyDeltaTransform(tDrawer, Rt);

                if (IsConverged(Rt))
                    break;
            }

            EditorUtility.SetDirty(tDrawer);
            SceneView.RepaintAll();
        }

        private bool IsConverged(Matrix4x4 Rt)
        {
            Vector3 trans = Rt.GetColumn(3);
            if (trans.sqrMagnitude > _convergencePos * _convergencePos) return false;
            float angleDeg = Quaternion.Angle(Quaternion.identity, Rt.rotation);
            return angleDeg <= _convergenceDeg;
        }

        private static void ApplyDeltaTransform(Transform t, Matrix4x4 Rt)
        {
            Vector3 newPos = Rt.MultiplyPoint3x4(t.position);
            Quaternion newRot = Rt.rotation * t.rotation;

            t.position = newPos;
            t.rotation = newRot;
        }

        private static void ApplyWorldMatrixToTransform(Transform t, Matrix4x4 world)
        {
            if (t == null) return;
            t.position = world.GetColumn(3);
            t.rotation = world.rotation;
            t.localScale = new Vector3(
                world.GetColumn(0).magnitude,
                world.GetColumn(1).magnitude,
                world.GetColumn(2).magnitude);
        }

        /// <summary>Kabsch rigid transform using MathNet SVD (R = V * U^T).</summary>
        private static class KabschRigidMathNet
        {
            public static bool ComputeRigidTransform(Vector3[] source, Vector3[] target, out Matrix4x4 result)
            {
                result = Matrix4x4.identity;
                if (source == null || target == null || source.Length != target.Length || source.Length < 3)
                    return false;

                int n = source.Length;
                Vector3 cenP = Vector3.zero;
                Vector3 cenQ = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    cenP += source[i];
                    cenQ += target[i];
                }
                cenP /= n;
                cenQ /= n;

                var H = new DenseMatrix(3, 3);
                for (int k = 0; k < n; k++)
                {
                    Vector3 p = source[k] - cenP;
                    Vector3 q = target[k] - cenQ;
                    H[0, 0] += p.x * q.x; H[0, 1] += p.x * q.y; H[0, 2] += p.x * q.z;
                    H[1, 0] += p.y * q.x; H[1, 1] += p.y * q.y; H[1, 2] += p.y * q.z;
                    H[2, 0] += p.z * q.x; H[2, 1] += p.z * q.y; H[2, 2] += p.z * q.z;
                }

                var svd = H.Svd(true);
                var U = svd.U;
                var V = svd.VT.Transpose();

                double det = V.Determinant() * U.Determinant();
                double sign = det >= 0.0 ? 1.0 : -1.0;

                var diag = DenseMatrix.CreateIdentity(3);
                diag[2, 2] = sign;

                var Rm = V * diag * U.Transpose();

                float r00 = (float)Rm[0, 0];
                float r01 = (float)Rm[0, 1];
                float r02 = (float)Rm[0, 2];
                float r10 = (float)Rm[1, 0];
                float r11 = (float)Rm[1, 1];
                float r12 = (float)Rm[1, 2];
                float r20 = (float)Rm[2, 0];
                float r21 = (float)Rm[2, 1];
                float r22 = (float)Rm[2, 2];

                float tx = cenQ.x - (r00 * cenP.x + r01 * cenP.y + r02 * cenP.z);
                float ty = cenQ.y - (r10 * cenP.x + r11 * cenP.y + r12 * cenP.z);
                float tz = cenQ.z - (r20 * cenP.x + r21 * cenP.y + r22 * cenP.z);

                var m = Matrix4x4.identity;
                m.SetColumn(0, new Vector4(r00, r10, r20, 0f));
                m.SetColumn(1, new Vector4(r01, r11, r21, 0f));
                m.SetColumn(2, new Vector4(r02, r12, r22, 0f));
                m.SetColumn(3, new Vector4(tx, ty, tz, 1f));

                result = m;
                return true;
            }
        }
    }
}

