using Sirenix.OdinInspector;
using UnityEngine;

namespace Feeder
{
    /// <summary>Draws gizmo mesh at this GameObject's transform (position, rotation, scale).</summary>
    public sealed class MeshHighlightDrawer : MonoBehaviour
    {
        public static Mesh SharedMesh { get; set; }

        [ShowInInspector] public Mesh ShowMesh => SharedMesh;

        private void OnDrawGizmos()
        {
            if (SharedMesh == null) return;
            if (!gameObject.activeInHierarchy) return;

            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            var prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireMesh(SharedMesh);
            Gizmos.matrix = prev;
        }
    }
}   
