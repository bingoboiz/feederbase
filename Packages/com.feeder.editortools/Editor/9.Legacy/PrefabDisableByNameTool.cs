using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public class PrefabDisableByNameTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Disable By Name")]
        private static void OpenWindow()
        {
            GetWindow<PrefabDisableByNameTool>("Prefab Disable By Name").Show();
        }

        [Title("Prefab Disable By Name Tool")]
        [InfoBox("Find first matching GameObject in each prefab and disable it.", InfoMessageType.Info)]
        [Space(10)]
        [LabelText("Prefab List")]
        [InfoBox("Drag and drop prefab assets here", InfoMessageType.None)]
        public GameObject[] prefabList = new GameObject[0];

        [Space(10)]
        [LabelText("Target Name")]
        public string targetName = string.Empty;

        [PropertySpace]
        [Button("Disable By Name", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.8f, 1f)]
        [EnableIf("@prefabList != null && prefabList.Length > 0")]
        private void DisableByName()
        {
            if (prefabList == null || prefabList.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one prefab.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(targetName))
            {
                EditorUtility.DisplayDialog("Error", "Please input target name.", "OK");
                return;
            }

            var changedCount = 0;
            var skippedCount = 0;
            var failCount = 0;

            foreach (var prefab in prefabList)
            {
                if (prefab?.transform == null)
                {
                    skippedCount++;
                    continue;
                }

                var prefabPath = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                {
                    skippedCount++;
                    continue;
                }

                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    var rootTransform = prefabRoot?.transform;
                    if (rootTransform == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var targetTransform = FindFirstByName(rootTransform, targetName);
                    if (targetTransform == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    Undo.RegisterFullObjectHierarchyUndo(prefabRoot, "Disable Prefab Child");
                    targetTransform.gameObject.SetActive(false);

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    changedCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to disable by name in {prefab?.name}: {e.Message}");
                    failCount++;
                }
                finally
                {
                    if (prefabRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }

            EditorUtility.DisplayDialog("Disable Complete",
                $"Changed {changedCount} prefab(s).\n" +
                $"Skipped {skippedCount} prefab(s).\n" +
                $"Failed {failCount} prefab(s).",
                "OK");
        }

        private static Transform FindFirstByName(Transform root, string nameToFind)
        {
            if (root == null || string.IsNullOrEmpty(nameToFind))
            {
                return null;
            }

            var queue = new System.Collections.Generic.Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current?.name == nameToFind)
                {
                    return current;
                }

                var childCount = current?.childCount ?? 0;
                for (var i = 0; i < childCount; i++)
                {
                    var child = current.GetChild(i);
                    if (child != null)
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            return null;
        }

        [PropertySpace]
        [Button("Clear All", ButtonSizes.Medium)]
        [GUIColor(1f, 0.6f, 0.6f)]
        private void ClearAll()
        {
            prefabList = new GameObject[0];
            targetName = string.Empty;
        }
    }
}
