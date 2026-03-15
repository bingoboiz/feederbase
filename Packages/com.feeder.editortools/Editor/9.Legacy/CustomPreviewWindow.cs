using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Feeder
{
    public class MeshPreviewWindow : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Mesh Preview Tool")]
        private static void OpenWindow()
        {
            GetWindow<MeshPreviewWindow>("Mesh Preview Tool");
        }

        [Title("Mesh Preview")]
        [InlineEditor(InlineEditorObjectFieldModes.Hidden)]
        public Mesh mesh;

        private Editor _meshEditor;

        protected override void OnImGUI()
        {
            base.OnImGUI();

            if (mesh == null)
                return;

            if (_meshEditor == null || _meshEditor.target != mesh)
            {
                if (_meshEditor != null)
                    DestroyImmediate(_meshEditor);

                _meshEditor = Editor.CreateEditor(mesh);
            }

            GUILayout.Space(10);

            Rect previewRect = GUILayoutUtility.GetRect(
                10,
                10000,
                300,
                400,
                GUILayout.ExpandWidth(true)
            );

            if (_meshEditor.HasPreviewGUI())
            {
                _meshEditor.OnInteractivePreviewGUI(previewRect, GUIStyle.none);
            }
        }

        protected override void OnDisable()
        {
            if (_meshEditor != null)
                DestroyImmediate(_meshEditor);
        }
    }
}