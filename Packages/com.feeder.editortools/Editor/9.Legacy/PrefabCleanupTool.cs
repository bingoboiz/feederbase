using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder {
    public class PrefabCleanupTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Child Cleanup")]
        private static void OpenWindow()
        {
            GetWindow<PrefabCleanupTool>("Prefab Child Cleanup").Show();
        }

        [Title("Prefab Child Cleanup Tool")]
        [InfoBox("This tool removes inactive children within a depth range.", InfoMessageType.Info)]
        [Space(10)]
        [LabelText("Prefab List")]
        [InfoBox("Drag and drop prefab assets here", InfoMessageType.None)]
        public GameObject[] prefabList = new GameObject[0];

        [Space(10)]
        [LabelText("Search Depth")]
        [InfoBox("Depth 1 = children, 2 = grandchildren, etc.", InfoMessageType.None)]
        [MinValue(1)]
        public int searchDepth = 1;

        [Space(10)]
        [LabelText("Protect Referenced Objects")]
        [InfoBox("If enabled, inactive GameObjects referenced by root components will not be deleted.", InfoMessageType.None)]
        public bool protectReferencedObjects = true;

        [PropertySpace]
        [Button("Cleanup Inactive Children", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.8f, 1f)]
        [EnableIf("@prefabList != null && prefabList.Length > 0")]
        private void CleanupInactiveChildren()
        {
            if (prefabList == null || prefabList.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please add at least one prefab.", "OK");
                return;
            }

            var removedCount = 0;
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

                    // collect all referenced GameObjects to avoid deleting them (only from root components)
                    HashSet<GameObject> referencedGameObjects = null;
                    if (protectReferencedObjects)
                    {
                        referencedGameObjects = CollectReferencedGameObjects(prefabRoot);
                    }
                    
                    var removedInPrefab = 0;
                    CleanupInactiveByDepth(rootTransform, searchDepth, referencedGameObjects, ref removedInPrefab);
                    if (removedInPrefab == 0)
                    {
                        skippedCount++;
                        continue;
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    removedCount += removedInPrefab;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to cleanup {prefab?.name}: {e.Message}");
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

            EditorUtility.DisplayDialog("Cleanup Complete",
                $"Removed {removedCount} inactive child(ren).\n" +
                $"Skipped {skippedCount} prefab(s).\n" +
                $"Failed {failCount} prefab(s).",
                "OK");
        }

        private static HashSet<GameObject> CollectReferencedGameObjects(GameObject root)
        {
            var referencedSet = new HashSet<GameObject>();
            if (root == null) return referencedSet;

            // only scan components on root GameObject, not children
            var components = root.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;

                var so = new SerializedObject(comp);
                var prop = so.GetIterator();
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.propertyPath == "m_Script") continue;

                    if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue != null)
                    {
                        var refValue = prop.objectReferenceValue;
                        GameObject refGameObject = null;

                        if (refValue is Component refComp)
                        {
                            refGameObject = refComp.gameObject;
                        }
                        else if (refValue is GameObject refGo)
                        {
                            refGameObject = refGo;
                        }

                        // only cache if reference is to a child of root
                        if (refGameObject != null && refGameObject != root && refGameObject.transform.IsChildOf(root.transform))
                        {
                            referencedSet.Add(refGameObject);
                        }
                    }
                }
            }

            return referencedSet;
        }

        private static void CleanupInactiveByDepth(Transform rootTransform, int depth, HashSet<GameObject> referencedSet, ref int removedCount)
        {
            if (depth <= 0 || rootTransform == null)
            {
                return;
            }

            var childCount = rootTransform.childCount;
            if (childCount == 0)
            {
                return;
            }

            var children = new Transform[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = rootTransform.GetChild(i);
            }

            foreach (var child in children)
            {
                if (child?.gameObject.activeSelf == false)
                {
                    // skip if this GameObject is referenced by root components
                    if (referencedSet != null && referencedSet.Contains(child.gameObject))
                    {
                        continue;
                    }

                    Object.DestroyImmediate(child.gameObject);
                    removedCount++;
                    continue;
                }

                CleanupInactiveByDepth(child, depth - 1, referencedSet, ref removedCount);
            }
        }

        [PropertySpace]
        [Button("Clear All", ButtonSizes.Medium)]
        [GUIColor(1f, 0.6f, 0.6f)]
        private void ClearAll()
        {
            prefabList = new GameObject[0];
            searchDepth = 1;
            protectReferencedObjects = true;
        }
    }
}
