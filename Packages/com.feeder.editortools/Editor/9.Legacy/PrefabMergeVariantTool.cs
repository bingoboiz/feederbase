using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Feeder {
    public class PrefabMergeVariantTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Merge Variant")]
        private static void ShowWindow()
        {
            GetWindow<PrefabMergeVariantTool>("Prefab Merge Variant").Show();
        }

        [Title("Merge Settings")]
        [InfoBox("Merge Prefab (A) into Prefab Variants (C) created from list B.", InfoMessageType.Info)]
        [LabelText("Merge Prefab (A)")]
        public GameObject mergePrefab;

        [LabelText("Save Folder")]
        [FolderPath(AbsolutePath = false, RequireExistingPath = true)]
        public string saveFolderPath;

        [Title("Target Prefabs (B)")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public List<GameObject> targetPrefabs = new List<GameObject>();

        [PropertySpace]
        [Button("Create Merge Variants", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 1f)]
        private void CreateMergeVariants()
        {
            if (mergePrefab == null)
            {
                EditorUtility.DisplayDialog("Error", "Merge Prefab (A) is missing.", "OK");
                return;
            }

            if (targetPrefabs == null || targetPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Target Prefabs (B) list is empty.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(saveFolderPath) || !AssetDatabase.IsValidFolder(saveFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "Save folder is invalid.", "OK");
                return;
            }

            var mergePath = AssetDatabase.GetAssetPath(mergePrefab);
            if (string.IsNullOrEmpty(mergePath))
            {
                EditorUtility.DisplayDialog("Error", "Merge Prefab (A) is not a valid asset.", "OK");
                return;
            }

            var successCount = 0;
            var failCount = 0;
            var skippedCount = 0;

            GameObject mergeRoot = null;
            try
            {
                mergeRoot = PrefabUtility.LoadPrefabContents(mergePath);
                foreach (var basePrefab in targetPrefabs)
                {
                    if (basePrefab == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var basePath = AssetDatabase.GetAssetPath(basePrefab);
                    if (string.IsNullOrEmpty(basePath))
                    {
                        skippedCount++;
                        continue;
                    }

                    var variantPath = $"{saveFolderPath}/{basePrefab.name}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    GameObject tempInstance = null;
                    try
                    {
                        tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
                        MergeComponents(tempInstance, mergeRoot);
                        MergeChildren(tempInstance.transform, mergeRoot.transform);
                        PrefabUtility.SaveAsPrefabAsset(tempInstance, variantPath);
                        successCount++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to merge {basePrefab.name}: {e.Message}");
                        failCount++;
                    }
                    finally
                    {
                        if (tempInstance != null)
                        {
                            DestroyImmediate(tempInstance);
                        }
                    }
                }
            }
            finally
            {
                if (mergeRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(mergeRoot);
                }
            }

            EditorUtility.DisplayDialog("Merge Complete",
                $"Created {successCount} variant(s).\n" +
                $"Skipped {skippedCount} prefab(s).\n" +
                $"Failed {failCount} prefab(s).",
                "OK");
        }

        private static void MergeComponents(GameObject targetRoot, GameObject mergeRoot)
        {
            var components = mergeRoot.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component is Transform)
                {
                    continue;
                }

                var type = component.GetType();
                var existing = targetRoot.GetComponent(type);
                if (existing != null && HasDisallowMultiple(type))
                {
                    ComponentUtility.CopyComponent(component);
                    ComponentUtility.PasteComponentValues(existing);
                    continue;
                }

                ComponentUtility.CopyComponent(component);
                ComponentUtility.PasteComponentAsNew(targetRoot);
            }
        }

        private static void MergeChildren(Transform targetRoot, Transform mergeRoot)
        {
            var childCount = mergeRoot.childCount;
            for (var i = 0; i < childCount; i++)
            {
                var child = mergeRoot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                var clone = Object.Instantiate(child.gameObject, targetRoot, false);
                clone.name = child.name;
            }
        }

        private static bool HasDisallowMultiple(System.Type componentType)
        {
            return System.Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent));
        }
    }
}
