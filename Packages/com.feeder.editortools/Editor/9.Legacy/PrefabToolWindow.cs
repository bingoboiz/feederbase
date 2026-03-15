using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public class PrefabToolWindow : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Tool")]
        private static void OpenWindow()
        {
            GetWindow<PrefabToolWindow>("Prefab Tool").Show();
        }

        [Title("Settings")]
        [LabelText("Target Component Type")]
        [ValueDropdown("GetComponentTypeOptions")]
        [OnValueChanged(nameof(OnComponentTypeChanged))]
        public Type componentType;

        [Title("Target Prefabs")]
        [InfoBox("Drag your Prefabs/GameObjects here")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        [OnValueChanged(nameof(OnTargetObjectsChanged))]
        public List<GameObject> targetObjects = new List<GameObject>();

        #region   =================== TAB ADD ===================

        [TabGroup("Tabs", "Add")]
        [CustomValueDrawer(nameof(DrawAddHeader))]
        public string addHeader = "";

        private string DrawAddHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "ADD COMPONENT INFO",
                "Select a hierarchy path and add a component to that GameObject.",
                new Color(0.2f, 0.8f, 0.2f) // green
            );
        }

        [TabGroup("Tabs", "Add")]
        [LabelText("Hierarchy Path")]
        [ValueDropdown(nameof(GetHierarchyOptions), IsUniqueList = false, DropdownWidth = 400, DropdownHeight = 300, DrawDropdownForListElements = false)]
        public string selectedHierarchyPath;

        [TabGroup("Tabs", "Add")]
        [OnInspectorGUI]
        private void DrawPrefabHierarchy()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Prefab Hierarchy", EditorStyles.boldLabel);

            if (!(targetObjects?.Count > 0))
            {
                EditorGUILayout.HelpBox("Add prefabs to show hierarchy.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Scan Hierarchy", GUILayout.Height(22)))
            {
                BuildHierarchyOptions();
            }

            if (hierarchyOptions.Count == 0)
            {
                EditorGUILayout.HelpBox("No hierarchy data. Click Scan.", MessageType.Info);
                return;
            }

            if (cachedPrefabCount < 2)
            {
                EditorGUILayout.HelpBox("Add at least two prefabs to show conflicts.", MessageType.Info);
            }

            if (conflictPaths.Count > 0)
            {
                EditorGUILayout.HelpBox("Items marked [Conflict] differ between prefabs.", MessageType.Warning);
            }
        }

        [TabGroup("Tabs", "Add")]
        [Button(ButtonSizes.Large)]
        public void AddComponent()
        {
            // Resolve the selected System.Type from your field
            Type selectedType = componentType;                    // If field is `System.Type`
                                                                  // Type selectedType = componentType?.Type;           // If field is `SerializedType` from Odin

            if (selectedType == null)
            {
                Debug.LogError("Please assign a component type to add!");
                return;
            }

            // Validate that it's a concrete Component
            if (!typeof(Component).IsAssignableFrom(selectedType) || selectedType.IsAbstract)
            {
                Debug.LogError($"Selected type must be a non-abstract Component. Got: {selectedType.FullName}");
                return;
            }

            // Disallow Transform/RectTransform etc. (cannot be added)
            if (selectedType == typeof(Transform) || selectedType == typeof(RectTransform))
            {
                Debug.LogError($"Cannot add {selectedType.Name} via AddComponent.");
                return;
            }

            if (string.IsNullOrEmpty(selectedHierarchyPath))
            {
                Debug.LogError("Please select a hierarchy path before adding.");
                return;
            }

            int addedCount = 0;

            foreach (var go in targetObjects)
            {
                if (go == null) continue;

                // Scene object case
                if (go.scene.IsValid())
                {
                    var target = ResolveTargetByPath(go.transform, selectedHierarchyPath);
                    if (target == null)
                    {
                        Debug.LogWarning($"No valid target at path '{selectedHierarchyPath}' for {go.name}");
                        continue;
                    }

                    if (target.GetComponent(selectedType) == null)
                    {
                        Undo.AddComponent(target.gameObject, selectedType);
                        addedCount++;
                        Debug.Log($"<color=green>Added {selectedType.Name} to {target.name}</color>");
                    }
                    else
                    {
                        Debug.Log($"<color=yellow>{target.name} already has {selectedType.Name}</color>");
                    }
                }
                // Prefab asset case
                else
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path)) continue;

                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var target = ResolveTargetByPath(prefabRoot.transform, selectedHierarchyPath);
                        if (target == null)
                        {
                            Debug.LogWarning($"No valid target at path '{selectedHierarchyPath}' in prefab {go.name}");
                            continue;
                        }

                        if (target.GetComponent(selectedType) == null)
                        {
                            target.gameObject.AddComponent(selectedType); // direct add on prefab contents
                            addedCount++;
                            PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                            Debug.Log($"<color=green>[Prefab] Added {selectedType.Name} to {target.name} in {go.name}</color>");
                        }
                        else
                        {
                            Debug.Log($"<color=yellow>[Prefab] {target.name} already has {selectedType.Name} in {go.name}</color>");
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
        }

        private Transform ResolveTargetByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            var current = root;
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                current = current?.Find(parts[i]);
                if (current == null) return null;
            }

            return current;
        }
        #endregion

        #region =================== TAB MODIFY ===================
        [TabGroup("Tabs", "Modify")]
        [CustomValueDrawer(nameof(DrawModifyHeader))]
        public string modifyHeader = "";

        private string DrawModifyHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "MODIFY COMPONENT INFO",
                "Change your component data here and click apply.",
                Color.cyan // change this to cyan
            );
        }
        private GameObject previewHolder; // hidden GameObject in scene
        private Component originalComponent; // save original state for diffing
        private GameObject originalHolder;
        
        // track which properties user has actually modified
        private HashSet<string> modifiedPropertyPaths = new HashSet<string>();
        
        [HideInInspector]
        public Component previewComponent;
        
        [TabGroup("Tabs", "Modify")]
        [OnInspectorGUI]
        private void DrawPreviewComponentWithTracking()
        {
            if (previewComponent == null)
            {
                EditorGUILayout.HelpBox("Select a component type above to begin editing.", MessageType.Info);
                return;
            }
            
            // show modified fields count
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Modified Fields: {modifiedPropertyPaths.Count}", EditorStyles.boldLabel);
            if (modifiedPropertyPaths.Count > 0 && GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                modifiedPropertyPaths.Clear();
                // reset preview to original values
                if (originalComponent != null)
                {
                    EditorUtility.CopySerialized(originalComponent, previewComponent);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(5);
            
            var so = new SerializedObject(previewComponent);
            so.Update();
            
            var prop = so.GetIterator();
            bool enterChildren = true;
            
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "m_Script") continue;
                
                bool wasModified = modifiedPropertyPaths.Contains(prop.propertyPath);
                
                EditorGUILayout.BeginHorizontal();
                
                // indicator for modified fields
                Color originalColor = GUI.color;
                if (wasModified)
                {
                    GUI.color = Color.cyan;
                    EditorGUILayout.LabelField("●", GUILayout.Width(14));
                    GUI.color = originalColor;
                }
                else
                {
                    GUILayout.Space(18);
                }
                
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(prop, true);
                if (EditorGUI.EndChangeCheck())
                {
                    modifiedPropertyPaths.Add(prop.propertyPath);
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            so.ApplyModifiedProperties();
        }

        [TabGroup("Tabs", "Modify")]
        [Button(ButtonSizes.Large)]
        public void ApplyModify()
        {
            int modifiedCount = 0;

            for (int objIndex = 0; objIndex < targetObjects.Count; objIndex++)
            {
                var go = targetObjects[objIndex];
                if (go == null) continue;

                // Scene object
                if (go.scene.IsValid())
                {
                    var targets = go.GetComponentsInChildren(componentType, true);
                    foreach (var comp in targets)
                    {
                        ApplyDifferences(previewComponent, comp, modifiedCount);
                        modifiedCount++;
                    }
                }
                // Prefab asset
                else
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path)) continue;

                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var targets = prefabRoot.GetComponentsInChildren(componentType, true);
                        foreach (var comp in targets)
                        {
                            ApplyDifferences(previewComponent, comp, modifiedCount);
                            modifiedCount++;
                        }
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"<color=cyan>Modified {modifiedCount} component(s) of type {componentType?.Name}</color>");
        }

        [TabGroup("Tabs", "Modify")]
        [BoxGroup("Tabs/Modify/Modify Settings", LabelText = "Modify Settings")]
        public bool incrementChanges;

        [TabGroup("Tabs", "Modify")]
        [BoxGroup("Tabs/Modify/Modify Settings", LabelText = "Modify Settings")]
        [ShowIf(nameof(incrementChanges))]
        public int incrementRate = 1;

        private void ApplyIncrementedValue(SerializedProperty srcProp, SerializedProperty dstProp, int index)
        {
            switch (srcProp.propertyType)
            {
                case SerializedPropertyType.Integer:
                    int baseInt = srcProp.intValue;
                    dstProp.intValue = baseInt + (index * incrementRate);
                    break;

                case SerializedPropertyType.Float:
                    float baseFloat = srcProp.floatValue;
                    dstProp.floatValue = baseFloat + (index * incrementRate);
                    break;

                case SerializedPropertyType.Enum:
                    int enumCount = srcProp.enumDisplayNames.Length;
                    int baseEnum = srcProp.enumValueIndex;
                    dstProp.enumValueIndex = (baseEnum + (index * incrementRate)) % enumCount;
                    break;

                default:
                    if (!SerializedProperty.DataEquals(srcProp, dstProp))
                    {
                        dstProp.serializedObject.CopyFromSerializedProperty(srcProp);
                    }
                    break;
            }
        }

        #endregion

        #region =================== TAB REMOVE ===================
        [TabGroup("Tabs", "Remove")]
        [CustomValueDrawer(nameof(DrawRemoveHeader))]
        public string removeHeader = "";

        private string DrawRemoveHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "REMOVE COMPONENT INFO",
                "Removes all components of the chosen type from target objects/prefabs hierachy.",
                new Color(1f, 0.5f, 0f) // orange
            );
        }

        [TabGroup("Tabs", "Remove")]
        [Button(ButtonSizes.Large)]
        public void RemoveComponent()
        {
            if (componentType == null)
            {
                EditorUtility.DisplayDialog("Error", "Please choose a component type!", "OK");
                return;
            }

            int removedCount = 0;

            foreach (var go in targetObjects)
            {
                if (go == null) continue;

                // Case 1: GameObject in Scene
                if (go.scene.IsValid())
                {
                    var comps = go.GetComponentsInChildren(componentType, true);
                    foreach (var comp in comps)
                    {
                        if (comp != null)
                        {
                            Undo.DestroyObjectImmediate(comp);
                            removedCount++;
                        }
                    }
                }
                // Case 2: Prefab Asset from Project
                else
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(path)) continue;

                    // Load prefab as editable root
                    GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    bool modified = false;

                    var comps = prefabRoot.GetComponentsInChildren(componentType, true);
                    foreach (var comp in comps)
                    {
                        if (comp != null)
                        {
                            DestroyImmediate(comp, true); // destroy in prefab asset
                            removedCount++;
                            modified = true;
                        }
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
            Debug.Log($"<color=green>Removed {removedCount} components of type {componentType.Name}</color>");
        }

        #endregion

        private string DrawTabHeader(string value, string title, string description, Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = color;

            GUILayout.Space(10);
            GUILayout.Label(title, style);

            GUIStyle subStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12
            };
            subStyle.normal.textColor = Color.gray;
            GUILayout.Label(description, subStyle);

            GUILayout.Space(10);

            return value;
        }

        private void OnComponentTypeChanged()
        {
            CreatePreviewHolder();
        }

        // apply only properties that user explicitly modified
        private void ApplyDifferences(Component modified, Component dst, int modifiedCount)
        {
            if (modified == null || dst == null) return;
            if (modifiedPropertyPaths.Count == 0)
            {
                Debug.LogWarning("No properties were modified. Nothing to apply.");
                return;
            }

            var soModified = new SerializedObject(modified);
            var soDst = new SerializedObject(dst);

            foreach (string propertyPath in modifiedPropertyPaths)
            {
                var srcProp = soModified.FindProperty(propertyPath);
                var dstProp = soDst.FindProperty(propertyPath);
                
                if (srcProp == null || dstProp == null) continue;

                if (incrementChanges)
                {
                    ApplyIncrementedValue(srcProp, dstProp, modifiedCount);
                }
                else
                {
                    dstProp.serializedObject.CopyFromSerializedProperty(srcProp);
                }
            }
            
            soDst.ApplyModifiedProperties();
        }

        private void CreatePreviewHolder()
        {
            CleanupPreview();
            if (componentType == null) return;

            // clear tracked modifications when component type changes
            modifiedPropertyPaths.Clear();

            // create hidden gameobject in the scene to hold preview component
            previewHolder = new GameObject($"[PreviewHolder_{componentType.Name}]");
            previewHolder.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            previewComponent = previewHolder.AddComponent(componentType);

            // create another gameobject to save original component data for reset functionality
            originalHolder = new GameObject($"[Original_{componentType.Name}]");
            originalHolder.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            originalComponent = originalHolder.AddComponent(componentType);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (componentType != null)
            {
                CreatePreviewHolder();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CleanupPreview();
        }

        private void CleanupPreview()
        {
            if (previewHolder != null)
            {
                DestroyImmediate(previewHolder);
                previewHolder = null;
                previewComponent = null;
            }
            if (originalHolder != null)
            {
                DestroyImmediate(originalHolder);
                originalHolder = null;
                originalComponent = null;
            }
        }

        private IEnumerable<ValueDropdownItem<Type>> GetComponentTypeOptions()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    if (typeof(Component).IsAssignableFrom(t) && !t.IsAbstract)
                    {
                        yield return new ValueDropdownItem<Type>(t.FullName, t);
                    }
                }
            }
        }

        private List<ValueDropdownItem<string>> hierarchyOptions = new List<ValueDropdownItem<string>>();
        private HashSet<string> conflictPaths = new HashSet<string>();
        private int cachedPrefabCount;

        private IEnumerable<ValueDropdownItem<string>> GetHierarchyOptions()
        {
            return hierarchyOptions;
        }

        private void OnTargetObjectsChanged()
        {
            BuildHierarchyOptions();
        }

        private void BuildHierarchyOptions()
        {
            hierarchyOptions.Clear();
            conflictPaths.Clear();
            cachedPrefabCount = 0;

            if (!(targetObjects?.Count > 0)) return;

            var pathPresence = new Dictionary<string, int>();
            var pathSignatures = new Dictionary<string, HashSet<string>>();
            var allPaths = new HashSet<string>();

            foreach (var go in targetObjects)
            {
                if (go == null) continue;

                var rootTransform = GetRootTransform(go, out GameObject prefabRoot, out bool shouldUnload);
                if (rootTransform == null) continue;
                cachedPrefabCount++;

                try
                {
                    var infos = PrefabHierarchyTool.CollectHierarchyInfo(rootTransform, false);
                    var prefabPaths = new HashSet<string>();

                    foreach (var info in infos)
                    {
                        if (string.IsNullOrEmpty(info.Path)) continue;
                        allPaths.Add(info.Path);
                        prefabPaths.Add(info.Path);

                        if (!pathSignatures.TryGetValue(info.Path, out var signatureSet))
                        {
                            signatureSet = new HashSet<string>();
                            pathSignatures[info.Path] = signatureSet;
                        }

                        signatureSet.Add(info.ChildSignature);
                    }

                    foreach (var path in prefabPaths)
                    {
                        if (!pathPresence.ContainsKey(path))
                        {
                            pathPresence[path] = 0;
                        }
                        pathPresence[path]++;
                    }
                }
                finally
                {
                    if (shouldUnload && prefabRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                }
            }

            foreach (var path in allPaths)
            {
                pathPresence.TryGetValue(path, out int count);
                bool missingInAny = cachedPrefabCount > 0 && count != cachedPrefabCount;

                bool signatureConflict = false;
                if (pathSignatures.TryGetValue(path, out var signatures))
                {
                    signatureConflict = signatures.Count > 1;
                }

                if (missingInAny || signatureConflict)
                {
                    conflictPaths.Add(path);
                }
            }

            var sortedPaths = new List<string>(allPaths);
            sortedPaths.Sort(StringComparer.Ordinal);

            for (int i = 0; i < sortedPaths.Count; i++)
            {
                string path = sortedPaths[i];
                string label = conflictPaths.Contains(path) ? "[Conflict] " + path : path;
                hierarchyOptions.Add(new ValueDropdownItem<string>(label, path));
            }
        }

        private Transform GetRootTransform(GameObject go, out GameObject prefabRoot, out bool shouldUnload)
        {
            prefabRoot = null;
            shouldUnload = false;

            if (go?.scene.IsValid() ?? false)
            {
                return go.transform;
            }

            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path)) return null;

            prefabRoot = PrefabUtility.LoadPrefabContents(path);
            shouldUnload = true;
            return prefabRoot?.transform;
        }

    }
}
