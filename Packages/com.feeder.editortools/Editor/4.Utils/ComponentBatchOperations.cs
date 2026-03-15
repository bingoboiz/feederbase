using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public static class ComponentBatchOperations
    {
        public static int AddComponentToTargets(
            Type componentType,
            string hierarchyPath,
            IReadOnlyList<GameObject> targetObjects)
        {
            if (componentType == null)
                throw new InvalidOperationException("component type is null.");
            if (!typeof(Component).IsAssignableFrom(componentType) || componentType.IsAbstract)
                throw new InvalidOperationException($"component type is invalid: {componentType.FullName}.");
            if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
                throw new InvalidOperationException($"cannot add {componentType.Name} via AddComponent.");
            if (string.IsNullOrEmpty(hierarchyPath))
                throw new InvalidOperationException("selected hierarchy path is empty.");
            if (!(targetObjects?.Count > 0))
                throw new InvalidOperationException("target objects is empty.");

            int addedCount = 0;

            for (int i = 0; i < targetObjects.Count; i++)
            {
                var go = targetObjects[i];
                if (go == null)
                {
                    Debug.LogWarning($"[ComponentBatchOperations] Skipping null at targetObjects[{i}].");
                    continue;
                }

                if (go.scene.IsValid())
                {
                    var target = HierarchyPathResolver.ResolveTargetByPath(go.transform, hierarchyPath);
                    if (target.GetComponent(componentType) == null)
                    {
                        Undo.AddComponent(target.gameObject, componentType);
                        addedCount++;
                    }
                }
                else
                {
                    var path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning($"[ComponentBatchOperations] Skipping targetObjects[{i}] (no asset path).");
                        continue;
                    }

                    var prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var target = HierarchyPathResolver.ResolveTargetByPath(prefabRoot.transform, hierarchyPath);
                        if (target.GetComponent(componentType) == null)
                        {
                            target.gameObject.AddComponent(componentType);
                            addedCount++;
                            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return addedCount;
        }

        public static int ApplyModifyToTargets(
            Type componentType,
            Component previewComponent,
            IReadOnlyCollection<string> modifiedPropertyPaths,
            bool incrementChanges,
            int incrementRate,
            IReadOnlyList<GameObject> targetObjects)
        {
            if (componentType == null)
                throw new InvalidOperationException("component type is null.");
            if (previewComponent == null)
                throw new InvalidOperationException("preview component is null.");
            if (modifiedPropertyPaths == null || modifiedPropertyPaths.Count == 0)
                throw new InvalidOperationException("no modified properties.");
            if (!(targetObjects?.Count > 0))
                throw new InvalidOperationException("target objects is empty.");

            int modifiedCount = 0;

            for (int objIndex = 0; objIndex < targetObjects.Count; objIndex++)
            {
                var go = targetObjects[objIndex];
                if (go == null)
                {
                    Debug.LogWarning($"[ComponentBatchOperations] Skipping null at targetObjects[{objIndex}].");
                    continue;
                }

                if (go.scene.IsValid())
                {
                    var targets = go.GetComponentsInChildren(componentType, true);
                    foreach (var comp in targets)
                    {
                        ComponentPropertyApplier.ApplyDifferences(
                            previewComponent,
                            comp,
                            modifiedPropertyPaths,
                            incrementChanges,
                            incrementRate,
                            modifiedCount);
                        modifiedCount++;
                    }
                }
                else
                {
                    var path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning($"[ComponentBatchOperations] Skipping targetObjects[{objIndex}] (no asset path).");
                        continue;
                    }

                    var prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var targets = prefabRoot.GetComponentsInChildren(componentType, true);
                        bool shouldSave = false;
                        foreach (var comp in targets)
                        {
                            ComponentPropertyApplier.ApplyDifferences(
                                previewComponent,
                                comp,
                                modifiedPropertyPaths,
                                incrementChanges,
                                incrementRate,
                                modifiedCount);
                            modifiedCount++;
                            shouldSave = true;
                        }
                        if (shouldSave)
                        {
                            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return modifiedCount;
        }

        public static int RemoveComponentFromTargets(Type componentType, IReadOnlyList<GameObject> targetObjects)
        {
            if (componentType == null)
                throw new InvalidOperationException("component type is null.");
            if (!(targetObjects?.Count > 0))
                throw new InvalidOperationException("target objects is empty.");

            int removedCount = 0;

            for (int i = 0; i < targetObjects.Count; i++)
            {
                var go = targetObjects[i];
                if (go == null)
                {
                    Debug.LogWarning($"[ComponentBatchOperations] Skipping null at targetObjects[{i}].");
                    continue;
                }

                if (go.scene.IsValid())
                {
                    var comps = go.GetComponentsInChildren(componentType, true);
                    foreach (var comp in comps)
                    {
                        Undo.DestroyObjectImmediate(comp);
                        removedCount++;
                    }
                }
                else
                {
                    var path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning($"[ComponentBatchOperations] Skipping targetObjects[{i}] (no asset path).");
                        continue;
                    }

                    var prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    bool modified = false;

                    var comps = prefabRoot.GetComponentsInChildren(componentType, true);
                    foreach (var comp in comps)
                    {
                        UnityEngine.Object.DestroyImmediate(comp, true);
                        removedCount++;
                        modified = true;
                    }

                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    }

                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return removedCount;
        }
    }
}
