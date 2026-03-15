using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Feeder
{
    public class PrefabReferenceSyncTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Reference Sync")]
        private static void OpenWindow()
        {
            GetWindow<PrefabReferenceSyncTool>("Prefab Reference Sync").Show();
        }

        [Title("Prefab Reference Sync Tool")]
        [Space(10)]

        [LabelText("Template Prefab (A)")]
        public GameObject templatePrefab;

        [Space(10)]
        [LabelText("Target Prefabs (B)")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public List<GameObject> targetPrefabs = new List<GameObject>();

        [PropertySpace]
        [Button("Sync References", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 1f)]
        [EnableIf("@templatePrefab != null && targetPrefabs != null && targetPrefabs.Count > 0")]
        public void SyncReferences()
        {
            string templatePath = AssetDatabase.GetAssetPath(templatePrefab);
            if (string.IsNullOrEmpty(templatePath))
            {
                EditorUtility.DisplayDialog("Error", "Template prefab is not a valid asset!", "OK");
                return;
            }
            GameObject templateRoot = PrefabUtility.LoadPrefabContents(templatePath);
            try
            {
                int syncedCount = 0;
                int skippedCount = 0;
                int createdCount = 0;
                int createdComponentCount = 0;

                foreach (var targetPrefab in targetPrefabs)
                {
                    if (targetPrefab == null) continue;

                    string targetPath = AssetDatabase.GetAssetPath(targetPrefab);
                    if (string.IsNullOrEmpty(targetPath)) continue;

                    GameObject targetRoot = PrefabUtility.LoadPrefabContents(targetPath);
                    try
                    {
                        var result = SyncPrefabReferences(templateRoot, targetRoot, templatePrefab.name, targetPrefab.name);
                        syncedCount += result.syncedFields;
                        skippedCount += result.skippedFields;
                        createdCount += result.createdObjects;
                        createdComponentCount += result.createdComponents;

                        if (result.syncedFields > 0 || result.createdObjects > 0)
                        {
                            PrefabUtility.SaveAsPrefabAsset(targetRoot, targetPath);
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(targetRoot);
                    }
                }

                EditorUtility.DisplayDialog("Sync Complete",
                    $"Synced {syncedCount} reference(s).\n" +
                    $"Skipped {skippedCount} field(s).\n" +
                    $"Created {createdCount} GameObject(s).\n" +
                    $"Created {createdComponentCount} Component(s).",
                    "OK");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(templateRoot);
            }
        }

        private SyncResult SyncPrefabReferences(GameObject templateRoot, GameObject targetRoot, string templateName, string targetName)
        {
            var result = new SyncResult();
            var templateComponents = templateRoot?.GetComponents<Component>();

            foreach (var templateComp in templateComponents)
            {
                if (templateComp == null) continue;

                var targetComp = GetOrCreateMatchingComponent(templateComp, templateRoot, targetRoot, targetName, ref result);
                if (targetComp == null)
                {
                    Debug.LogWarning($"[PrefabReferenceSync] Missing target component: {templateComp.GetType().Name} | TemplateObj: {templateComp.gameObject.name} | Target: {targetName}");
                    continue;
                }

                var soTemplate = new SerializedObject(templateComp);
                var soTarget = new SerializedObject(targetComp);

                EnsureArraySizes(soTemplate, soTarget, targetName);

                var prop = soTemplate.GetIterator();
                bool enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    var targetProp = soTarget.FindProperty(prop.propertyPath);
                    if (targetProp == null)
                    {
                        Debug.LogWarning($"[PrefabReferenceSync] Missing target property: {prop.propertyPath} | Comp: {templateComp.GetType().Name} | Target: {targetName}");
                        continue;
                    }

                    ProcessProperty(prop, targetProp, templateRoot, targetRoot, templateName, targetName, ref result);
                }

                soTarget.ApplyModifiedProperties();
            }

            return result;
        }

        private void ProcessProperty(SerializedProperty templateProp, SerializedProperty targetProp,
            GameObject templateRoot, GameObject targetRoot, string templateName, string targetName, ref SyncResult result)
        {
            if (IsEngineInternalReference(templateProp)) return;

            if (templateProp.propertyType != SerializedPropertyType.ObjectReference)
            {
                if (TryCopyValue(templateProp, targetProp))
                {
                    result.syncedFields++;
                }
                else
                {
                    result.skippedFields++;
                }
                return;
            }

            Object resolvedRef = ResolveTargetReference(templateProp.objectReferenceValue, templateRoot, targetRoot, templateName, targetName, ref result);
            if (resolvedRef == null)
            {
                result.skippedFields++;
                return;
            }
            if (targetProp.objectReferenceValue == resolvedRef)
            {
                result.skippedFields++;
                return;
            }

            targetProp.objectReferenceValue = resolvedRef;
            result.syncedFields++;
        }

        private void EnsureArraySizes(SerializedObject soTemplate, SerializedObject soTarget, string targetName)
        {
            if (soTemplate == null || soTarget == null) return;

            var prop = soTemplate.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (IsEngineInternalReference(prop)) continue;
                if (!prop.isArray || prop.propertyType == SerializedPropertyType.String) continue;

                var targetProp = soTarget.FindProperty(prop.propertyPath);
                if (targetProp == null)
                {
                    Debug.LogWarning($"[PrefabReferenceSync] Missing target array: {prop.propertyPath} | Target: {targetName}");
                    continue;
                }

                if (targetProp.arraySize != prop.arraySize)
                {
                    targetProp.arraySize = prop.arraySize;
                }
            }

            soTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private GameObject GetReferencedGameObject(Object refValue)
        {
            if (refValue is Component comp)
            {
                return comp.gameObject;
            }
            if (refValue is GameObject go)
            {
                return go;
            }
            return null;
        }

        private Object ResolveTargetReference(Object templateRef, GameObject templateRoot, GameObject targetRoot,
            string templateName, string targetName, ref SyncResult result)
        {
            if (templateRef == null) return null;
            if (AssetDatabase.Contains(templateRef)) return templateRef;

            Transform templateTransform = null;
            System.Type componentType = null;

            if (templateRef is Component templateComponent)
            {
                templateTransform = templateComponent.transform;
                componentType = templateComponent.GetType();
            }
            else if (templateRef is GameObject templateGo)
            {
                templateTransform = templateGo.transform;
            }
            else if (templateRef is Transform templateTr)
            {
                templateTransform = templateTr;
            }

            if (templateTransform == null) return templateRef;

            Transform rootTransform = templateRoot?.transform;
            if (rootTransform == null) return null;

            string relativePath = GetRelativePath(templateTransform, rootTransform);
            if (relativePath == null) return null;

            Transform targetTransform = relativePath == ""
                ? targetRoot.transform
                : targetRoot.transform.Find(relativePath);

            if (targetTransform == null && templateTransform != rootTransform)
            {
                var createdObject = FindOrCreateMatchingGameObject(templateTransform.gameObject, templateRoot, targetRoot, templateName, targetName, ref result);
                targetTransform = createdObject?.transform;
            }

            if (targetTransform == null) return null;

            if (componentType != null)
            {
                var targetComponent = targetTransform.GetComponent(componentType);
                if (targetComponent != null) return targetComponent;

                var createdComp = targetTransform.gameObject.AddComponent(componentType);
                result.createdComponents++;
                Debug.Log($"[PrefabReferenceSync] Created component {componentType.Name} on {targetTransform.name} | Target: {targetName}");
                return createdComp;
            }

            if (templateRef is Transform) return targetTransform;

            return targetTransform.gameObject;
        }

        private bool TryCopyValue(SerializedProperty source, SerializedProperty target)
        {
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (target.intValue == source.intValue) return false;
                    target.intValue = source.intValue;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (target.boolValue == source.boolValue) return false;
                    target.boolValue = source.boolValue;
                    return true;
                case SerializedPropertyType.Float:
                    if (Mathf.Approximately(target.floatValue, source.floatValue)) return false;
                    target.floatValue = source.floatValue;
                    return true;
                case SerializedPropertyType.String:
                    if (target.stringValue == source.stringValue) return false;
                    target.stringValue = source.stringValue;
                    return true;
                case SerializedPropertyType.Color:
                    if (target.colorValue == source.colorValue) return false;
                    target.colorValue = source.colorValue;
                    return true;
                case SerializedPropertyType.Enum:
                    if (target.enumValueIndex == source.enumValueIndex) return false;
                    target.enumValueIndex = source.enumValueIndex;
                    return true;
                case SerializedPropertyType.Vector2:
                    if (target.vector2Value == source.vector2Value) return false;
                    target.vector2Value = source.vector2Value;
                    return true;
                case SerializedPropertyType.Vector3:
                    if (target.vector3Value == source.vector3Value) return false;
                    target.vector3Value = source.vector3Value;
                    return true;
                case SerializedPropertyType.Vector4:
                    if (target.vector4Value == source.vector4Value) return false;
                    target.vector4Value = source.vector4Value;
                    return true;
                case SerializedPropertyType.Rect:
                    if (target.rectValue == source.rectValue) return false;
                    target.rectValue = source.rectValue;
                    return true;
                case SerializedPropertyType.Bounds:
                    if (target.boundsValue == source.boundsValue) return false;
                    target.boundsValue = source.boundsValue;
                    return true;
                case SerializedPropertyType.Quaternion:
                    if (target.quaternionValue == source.quaternionValue) return false;
                    target.quaternionValue = source.quaternionValue;
                    return true;
                case SerializedPropertyType.Vector2Int:
                    if (target.vector2IntValue == source.vector2IntValue) return false;
                    target.vector2IntValue = source.vector2IntValue;
                    return true;
                case SerializedPropertyType.Vector3Int:
                    if (target.vector3IntValue == source.vector3IntValue) return false;
                    target.vector3IntValue = source.vector3IntValue;
                    return true;
                case SerializedPropertyType.RectInt:
                    if (target.rectIntValue.Equals(source.rectIntValue)) return false;
                    target.rectIntValue = source.rectIntValue;
                    return true;
                case SerializedPropertyType.BoundsInt:
                    if (target.boundsIntValue.Equals(source.boundsIntValue)) return false;
                    target.boundsIntValue = source.boundsIntValue;
                    return true;
                case SerializedPropertyType.AnimationCurve:
                    if (Equals(target.animationCurveValue, source.animationCurveValue)) return false;
                    target.animationCurveValue = source.animationCurveValue;
                    return true;
                case SerializedPropertyType.ManagedReference:
                    if (Equals(target.managedReferenceValue, source.managedReferenceValue)) return false;
                    target.managedReferenceValue = source.managedReferenceValue;
                    return true;
            }

            return false;
        }

        private bool IsSameReference(Object currentRef, Object desiredRef)
        {
            if (currentRef == null || desiredRef == null) return false;

            if (currentRef == desiredRef) return true;

            if (currentRef is Component currentComp && desiredRef is Component desiredComp)
            {
                if (currentComp.GetType() != desiredComp.GetType()) return false;

                return currentComp.gameObject == desiredComp.gameObject;
            }

            if (currentRef is GameObject currentGo && desiredRef is GameObject desiredGo)
            {
                return currentGo == desiredGo;
            }

            return false;
        }

        private bool IsEngineInternalReference(SerializedProperty property)
        {
            if (property == null) return true;
            if (property.propertyType != SerializedPropertyType.ObjectReference) return false;
            if (property.depth != 0) return false;

            string path = property.propertyPath;
            return path == "m_Script"
                   || path == "m_GameObject"
                   || path == "m_PrefabInstance"
                   || path == "m_PrefabInternal"
                   || path == "m_Father"
                   || path == "m_Children";
        }

        private GameObject FindOrCreateMatchingGameObject(GameObject refGameObject, GameObject templateRoot, GameObject targetRoot,
            string templateName, string targetName, ref SyncResult result)
        {
            // get path from template root
            string relativePath = GetRelativePath(refGameObject.transform, templateRoot.transform);
            if (string.IsNullOrEmpty(relativePath))
            {
                Debug.LogWarning($"[PrefabReferenceSync] Invalid relative path | Ref: {refGameObject.name} | Target: {targetName}");
                return null;
            }

            // find in target by path
            Transform targetTransform = targetRoot.transform.Find(relativePath);
            if (targetTransform != null)
            {
                return targetTransform.gameObject;
            }

            // not found, need to create parent chain and duplicate
            Transform targetParent = EnsureTargetParentPath(templateRoot, targetRoot, refGameObject.transform, ref result);
            if (targetParent == null)
            {
                Debug.LogWarning($"[PrefabReferenceSync] Failed to resolve target parent | Path: {relativePath} | Target: {targetName}");
                return null;
            }

            GameObject duplicated = Instantiate(refGameObject, targetParent);
            duplicated.name = refGameObject.name;
            result.createdObjects++;

            Debug.Log($"[{targetName}] Created GameObject '{duplicated.name}' at path '{relativePath}'");

            return duplicated;
        }

        private Transform EnsureTargetParentPath(GameObject templateRoot, GameObject targetRoot, Transform templateChild, ref SyncResult result)
        {
            if (templateChild == null) return null;
            if (templateChild.parent == null) return null;

            var parentPath = GetRelativePath(templateChild.parent, templateRoot.transform);
            if (string.IsNullOrEmpty(parentPath))
            {
                return targetRoot.transform;
            }

            var segments = parentPath.Split('/');
            Transform currentTarget = targetRoot.transform;

            for (int i = 0; i < segments.Length; i++)
            {
                var segName = segments[i];
                var nextTarget = currentTarget.Find(segName);
                if (nextTarget == null)
                {
                    return null;
                }

                currentTarget = nextTarget;
            }

            return currentTarget;
        }

        private string GetRelativePath(Transform child, Transform root)
        {
            if (child == root) return "";

            var path = new List<string>();
            Transform current = child;

            while (current != null && current != root)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            if (current != root) return null;

            return string.Join("/", path);
        }

        private Component GetOrCreateMatchingComponent(Component templateComp, GameObject templateRoot, GameObject targetRoot, string targetName, ref SyncResult result)
        {
            if (templateComp == null) return null;

            var templateType = templateComp.GetType();
            var templatePath = GetRelativePath(templateComp.transform, templateRoot.transform);

            if (string.IsNullOrEmpty(templatePath))
            {
                var rootComp = targetRoot.GetComponent(templateType);
                if (rootComp != null) return rootComp;

                var createdRootComp = targetRoot.AddComponent(templateType);
                result.createdComponents++;
                Debug.Log($"[PrefabReferenceSync] Created component {templateType.Name} on root | Target: {targetName}");
                return createdRootComp;
            }

            var targetTransform = targetRoot.transform.Find(templatePath);
            if (targetTransform == null)
            {
                var targetObject = FindOrCreateMatchingGameObject(templateComp.gameObject, templateRoot, targetRoot, templateRoot.name, targetName, ref result);
                targetTransform = targetObject?.transform;
            }

            if (targetTransform == null) return null;

            var targetComp = targetTransform.GetComponent(templateType);
            if (targetComp != null) return targetComp;

            var createdComp = targetTransform.gameObject.AddComponent(templateType);
            result.createdComponents++;
            Debug.Log($"[PrefabReferenceSync] Created component {templateType.Name} on {targetTransform.name} | Target: {targetName}");
            return createdComp;
        }

        private struct SyncResult
        {
            public int syncedFields;
            public int skippedFields;
            public int createdObjects;
            public int createdComponents;
        }
    }
}
