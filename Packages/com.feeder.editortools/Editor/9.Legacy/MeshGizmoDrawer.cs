using UnityEngine;

namespace Feeder
{
    /// <summary>Draws a gizmo mesh at this transform; mesh is set per-component.</summary>
    public sealed class MeshGizmoDrawer : MonoBehaviour
    {
        [SerializeField] private Mesh _mesh;

        public Mesh Mesh
        {
            get => _mesh;
            set => _mesh = value;
        }

        private void OnDrawGizmos()
        {
            if (_mesh == null || !gameObject.activeInHierarchy) return;

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            var prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireMesh(_mesh);
            Gizmos.matrix = prev;
        }
    }
}
