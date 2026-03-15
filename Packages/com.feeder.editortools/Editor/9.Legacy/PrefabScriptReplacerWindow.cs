using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Feeder {
    public class PrefabScriptReplacerWindow : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Script Replacer")]
        private static void OpenWindow()
        {
            GetWindow<PrefabScriptReplacerWindow>("Prefab Script Replacer").Show();
        }

        [Title("Target Objects")]
        [InfoBox("Drag Prefab assets or Scene objects here")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public List<GameObject> targetPrefabs = new List<GameObject>();

        [Title("Component Types")]
        [LabelText("Replace With (Script A)")]
        [ValueDropdown(nameof(GetComponentTypeOptions))]
        public Type replaceWithType;

        [LabelText("Find (Script B)")]
        [ValueDropdown(nameof(GetComponentTypeOptions))]
        public Type findType;

        [Button(ButtonSizes.Large)]
        public void ReplaceScript()
        {
            if (targetPrefabs == null || targetPrefabs.Count == 0)
            {
                Debug.LogError("Please assign at least one prefab.");
                return;
            }

            if (replaceWithType == null || findType == null)
            {
                Debug.LogError("Please assign both component types.");
                return;
            }

            if (replaceWithType == findType)
            {
                Debug.LogError("Replace type must be different from find type.");
                return;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(replaceWithType) || replaceWithType.IsAbstract)
            {
                Debug.LogError($"Replace type must be a non-abstract MonoBehaviour. Got: {replaceWithType?.FullName}");
                return;
            }

            if (!typeof(MonoBehaviour).IsAssignableFrom(findType) || findType.IsAbstract)
            {
                Debug.LogError($"Find type must be a non-abstract MonoBehaviour. Got: {findType?.FullName}");
                return;
            }

            int replacedCount = 0;
            int modifiedPrefabs = 0;
            int modifiedSceneObjects = 0;

            foreach (var prefab in targetPrefabs)
            {
                if (prefab == null)
                {
                    Debug.LogWarning("Prefab entry is null.");
                    continue;
                }

                if (prefab.scene.IsValid())
                {
                    var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(prefab) ?? prefab;
                    int replacedInScene = ReplaceOnSceneRoot(instanceRoot, replaceWithType, prefab.name);
                    if (replacedInScene > 0)
                    {
                        modifiedSceneObjects++;
                    }
                    replacedCount += replacedInScene;
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning($"Invalid prefab path for {prefab.name}");
                    continue;
                }

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                bool modified = false;

                try
                {
                    int replacedInPrefab = ReplaceOnRoot(prefabRoot, replaceWithType, prefab.name, null);
                    if (replacedInPrefab > 0) modified = true;
                    replacedCount += replacedInPrefab;

                    if (modified)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        modifiedPrefabs++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"<color=green>Replaced {replacedCount} component(s) in {modifiedPrefabs} prefab(s), {modifiedSceneObjects} scene object(s).</color>");
        }

        private int ReplaceOnSceneRoot(GameObject root, Type replaceType, string contextName)
        {
            if (root == null || replaceType == null || findType == null) return 0;

            int replaced = ReplaceOnRoot(root, replaceType, contextName, (target) =>
            {
                if (target == null) return;
                EditorUtility.SetDirty(target);
                if (target is Component componentTarget && PrefabUtility.IsPartOfPrefabInstance(componentTarget))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(componentTarget);
                }
            });
            if (replaced <= 0) return 0;

            EditorUtility.SetDirty(root);
            if (root.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
            }

            return replaced;
        }

        private void TryReplaceOnPrefabAsset(GameObject instanceRoot, Type replaceType, string contextName, ref int modifiedPrefabs, ref int replacedCount)
        {
            if (instanceRoot == null || replaceType == null) return;
            if (!PrefabUtility.IsPartOfPrefabInstance(instanceRoot)) return;

            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (string.IsNullOrEmpty(path)) return;

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
            bool modified = false;

            try
            {
                int replacedInPrefab = ReplaceOnRoot(prefabRoot, replaceType, contextName, null);
                if (replacedInPrefab > 0) modified = true;
                replacedCount += replacedInPrefab;

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    modifiedPrefabs++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private int ReplaceOnRoot(GameObject root, Type replaceType, string contextName, Action<UnityEngine.Object> onChanged)
        {
            if (root == null || replaceType == null || findType == null) return 0;

            int replaced = 0;
            var targets = root.GetComponentsInChildren(findType, true);
            var existingComponentsByObject = new Dictionary<GameObject, Component[]>();
            var nextReplaceIndexByObject = new Dictionary<GameObject, int>();
            foreach (var comp in targets)
            {
                if (comp == null) continue;

                var sourceComponent = comp as Component;
                if (sourceComponent == null) continue;

                var targetGameObject = sourceComponent.gameObject;
                if (targetGameObject == null) continue;

                if (!existingComponentsByObject.TryGetValue(targetGameObject, out var existingTargets))
                {
                    existingTargets = targetGameObject.GetComponents(replaceType);
                    existingComponentsByObject[targetGameObject] = existingTargets;
                }

                if (!nextReplaceIndexByObject.TryGetValue(targetGameObject, out var replaceIndex))
                {
                    replaceIndex = 0;
                }
                nextReplaceIndexByObject[targetGameObject] = replaceIndex + 1;

                var targetComponent = (existingTargets != null && replaceIndex < existingTargets.Length)
                    ? existingTargets[replaceIndex]
                    : targetGameObject.AddComponent(replaceType) as Component;

                if (targetComponent == null)
                {
                    Debug.LogError($"Failed to add component {replaceType?.FullName} in {contextName}");
                    continue;
                }

                CopySerializedProperties(sourceComponent, targetComponent, contextName);
                onChanged?.Invoke(targetComponent);

                UnityEngine.Object.DestroyImmediate(sourceComponent, true);
                replaced++;
            }

            return replaced;
        }

        private void CopySerializedProperties(Component source, Component target, string contextName)
        {
            if (source == null || target == null) return;

            var soSource = new SerializedObject(source);
            var soTarget = new SerializedObject(target);

            EnsureArraySizes(soSource, soTarget, contextName);

            var prop = soSource.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (IsEngineInternalProperty(prop)) continue;

                var targetProp = soTarget.FindProperty(prop.propertyPath);
                if (targetProp == null)
                {
                    Debug.LogError($"Missing target property: {prop.propertyPath} | Source: {source.GetType().Name} | Context: {contextName}");
                    continue;
                }

                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    targetProp.objectReferenceValue = prop.objectReferenceValue;
                    continue;
                }

                TryCopyValue(prop, targetProp);
            }

            soTarget.ApplyModifiedProperties();
        }

        private void EnsureArraySizes(SerializedObject soSource, SerializedObject soTarget, string contextName)
        {
            if (soSource == null || soTarget == null) return;

            var prop = soSource.GetIterator();
            bool enterChildren = true;

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (IsEngineInternalProperty(prop)) continue;
                if (!prop.isArray || prop.propertyType == SerializedPropertyType.String) continue;

                var targetProp = soTarget.FindProperty(prop.propertyPath);
                if (targetProp == null)
                {
                    Debug.LogError($"Missing target array: {prop.propertyPath} | Context: {contextName}");
                    continue;
                }

                if (targetProp.arraySize != prop.arraySize)
                {
                    targetProp.arraySize = prop.arraySize;
                }
            }

            soTarget.ApplyModifiedPropertiesWithoutUndo();
        }

        private bool TryCopyValue(SerializedProperty source, SerializedProperty target)
        {
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                    target.intValue = source.intValue;
                    return true;
                case SerializedPropertyType.Boolean:
                    target.boolValue = source.boolValue;
                    return true;
                case SerializedPropertyType.Float:
                    target.floatValue = source.floatValue;
                    return true;
                case SerializedPropertyType.String:
                    target.stringValue = source.stringValue;
                    return true;
                case SerializedPropertyType.Color:
                    target.colorValue = source.colorValue;
                    return true;
                case SerializedPropertyType.Enum:
                    target.enumValueIndex = source.enumValueIndex;
                    return true;
                case SerializedPropertyType.Vector2:
                    target.vector2Value = source.vector2Value;
                    return true;
                case SerializedPropertyType.Vector3:
                    target.vector3Value = source.vector3Value;
                    return true;
                case SerializedPropertyType.Vector4:
                    target.vector4Value = source.vector4Value;
                    return true;
                case SerializedPropertyType.Rect:
                    target.rectValue = source.rectValue;
                    return true;
                case SerializedPropertyType.Bounds:
                    target.boundsValue = source.boundsValue;
                    return true;
                case SerializedPropertyType.Quaternion:
                    target.quaternionValue = source.quaternionValue;
                    return true;
                case SerializedPropertyType.Vector2Int:
                    target.vector2IntValue = source.vector2IntValue;
                    return true;
                case SerializedPropertyType.Vector3Int:
                    target.vector3IntValue = source.vector3IntValue;
                    return true;
                case SerializedPropertyType.RectInt:
                    target.rectIntValue = source.rectIntValue;
                    return true;
                case SerializedPropertyType.BoundsInt:
                    target.boundsIntValue = source.boundsIntValue;
                    return true;
                case SerializedPropertyType.AnimationCurve:
                    target.animationCurveValue = source.animationCurveValue;
                    return true;
                case SerializedPropertyType.ManagedReference:
                    target.managedReferenceValue = source.managedReferenceValue;
                    return true;
            }

            return false;
        }

        private bool IsEngineInternalProperty(SerializedProperty property)
        {
            if (property == null) return true;
            if (property.depth != 0) return false;

            string path = property.propertyPath;
            return path == "m_Script"
                   || path == "m_GameObject"
                   || path == "m_PrefabInstance"
                   || path == "m_PrefabInternal"
                   || path == "m_Father"
                   || path == "m_Children";
        }

        private IEnumerable<ValueDropdownItem<Type>> GetComponentTypeOptions()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract)
                    {
                        yield return new ValueDropdownItem<Type>(t.FullName, t);
                    }
                }
            }
        }
    }
}
