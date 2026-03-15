using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using UnityEditor;
using UnityEngine;
using System;

namespace Feeder
{
    public sealed class FComponentModifyTool : FTargetObjectsToolBase
    {
        protected override string GetDescription()
        {
            return "Chọn prefab/scene, chọn loại component; tab Add thêm component theo path, tab Modify sửa giá trị và Apply, tab Remove xóa component khỏi tất cả target.";
        }

        [LabelText("Target Component Type")]
        [ValueDropdown(nameof(GetComponentTypeOptions))]
        [OnValueChanged(nameof(OnComponentTypeChanged))]
        [ShowInInspector]
        private Type componentType;

        [TabGroup("Tabs", "Add")]
        [CustomValueDrawer(nameof(DrawAddHeader))]
        public string AddHeader = "";

        [TabGroup("Tabs", "Add")]
        [LabelText("Hierarchy Path")]
        [ValueDropdown(nameof(GetHierarchyOptions), IsUniqueList = false, DropdownWidth = 400, DropdownHeight = 300, DrawDropdownForListElements = false)]
        public string SelectedHierarchyPath;

        [TabGroup("Tabs", "Add")]
        [OnInspectorGUI]
        private void DrawPrefabHierarchy()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Prefab Hierarchy", EditorStyles.boldLabel);

            if (!(TargetObjects?.Count > 0))
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
            var selectedType = ComponentType;
            int addedCount = ComponentBatchOperations.AddComponentToTargets(selectedType, SelectedHierarchyPath, TargetObjects);
            Debug.Log($"<color=green>Added {addedCount} component(s) of type {selectedType.Name}</color>");
        }

        [TabGroup("Tabs", "Modify")]
        [CustomValueDrawer(nameof(DrawModifyHeader))]
        public string ModifyHeader = "";

        private HashSet<string> modifiedPropertyPaths = new HashSet<string>();
        private readonly ComponentPreviewHandle previewHandle = new ComponentPreviewHandle();

        [TabGroup("Tabs", "Modify")]
        [OnInspectorGUI]
        private void DrawPreviewComponentWithTracking()
        {
            var previewComponent = previewHandle.PreviewComponent;
            var originalComponent = previewHandle.OriginalComponent;

            if (previewComponent == null)
            {
                EditorGUILayout.HelpBox("Select a component type above to begin editing.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Modified Fields: {modifiedPropertyPaths.Count}", EditorStyles.boldLabel);
            if (modifiedPropertyPaths.Count > 0 && GUILayout.Button("Clear All", GUILayout.Width(80)))
            {
                modifiedPropertyPaths.Clear();
                if (originalComponent == null)
                    throw new InvalidOperationException("original component is null.");

                EditorUtility.CopySerialized(originalComponent, previewComponent);
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
            int modifiedCount = ComponentBatchOperations.ApplyModifyToTargets(
                ComponentType,
                previewHandle.PreviewComponent,
                modifiedPropertyPaths,
                IncrementChanges,
                IncrementRate,
                TargetObjects);
            Debug.Log($"<color=cyan>Modified {modifiedCount} component(s) of type {ComponentType.Name}</color>");
        }

        [TabGroup("Tabs", "Modify")]
        [BoxGroup("Tabs/Modify/Modify Settings", LabelText = "Modify Settings")]
        public bool IncrementChanges;

        [TabGroup("Tabs", "Modify")]
        [BoxGroup("Tabs/Modify/Modify Settings", LabelText = "Modify Settings")]
        [ShowIf(nameof(IncrementChanges))]
        public int IncrementRate = 1;

        [TabGroup("Tabs", "Remove")]
        [CustomValueDrawer(nameof(DrawRemoveHeader))]
        public string RemoveHeader = "";

        [TabGroup("Tabs", "Remove")]
        [Button(ButtonSizes.Large)]
        public void RemoveComponent()
        {
            int removedCount = ComponentBatchOperations.RemoveComponentFromTargets(ComponentType, TargetObjects);
            Debug.Log($"<color=green>Removed {removedCount} components of type {ComponentType.Name}</color>");
        }

        private string DrawAddHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "ADD COMPONENT INFO",
                "Select a hierarchy path and add a component to that GameObject.",
                new Color(0.2f, 0.8f, 0.2f)
            );
        }

        private string DrawModifyHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "MODIFY COMPONENT INFO",
                "Change your component data here and click apply.",
                Color.cyan
            );
        }

        private string DrawRemoveHeader(string value, GUIContent label)
        {
            return DrawTabHeader(
                value,
                "REMOVE COMPONENT INFO",
                "Removes all components of the chosen type from target objects/prefabs hierachy.",
                new Color(1f, 0.5f, 0f)
            );
        }

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
            if (componentType == null)
            {
                previewHandle.Cleanup();
                modifiedPropertyPaths.Clear();
                return;
            }

            previewHandle.Reset(ComponentType, modifiedPropertyPaths);
        }

        [OnInspectorInit]
        private void OnInspectorInit()
        {
            if (componentType != null)
            {
                previewHandle.Reset(ComponentType, modifiedPropertyPaths);
            }
        }

        [OnInspectorDispose]
        private void OnInspectorDispose()
        {
            previewHandle.Cleanup();
        }

        private IEnumerable<ValueDropdownItem<Type>> GetComponentTypeOptions()
        {
            return ComponentTypeOptionsProvider.GetComponentTypeOptions();
        }

        private List<ValueDropdownItem<string>> hierarchyOptions = new List<ValueDropdownItem<string>>();
        private HashSet<string> conflictPaths = new HashSet<string>();
        private int cachedPrefabCount;

        private IEnumerable<ValueDropdownItem<string>> GetHierarchyOptions()
        {
            return hierarchyOptions;
        }

        protected override void OnTargetObjectsChanged()
        {
            if (!(TargetObjects?.Count > 0))
                return;

            BuildHierarchyOptions();
        }

        private void BuildHierarchyOptions()
        {
            var result = HierarchyOptionsBuilder.Build(TargetObjects);
            hierarchyOptions = result.Options;
            conflictPaths = result.ConflictPaths;
            cachedPrefabCount = result.PrefabCount;
        }

        private Type ComponentType
        {
            get
            {
                return componentType ?? throw new InvalidOperationException("component type is null.");
            }
        }
    }
}
