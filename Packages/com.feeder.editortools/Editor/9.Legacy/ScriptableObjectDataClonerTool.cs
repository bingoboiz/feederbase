using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public class ScriptableObjectDataClonerTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Scriptable Object Data Cloner")]
        private static void OpenWindow()
        {
            GetWindow<ScriptableObjectDataClonerTool>("Scriptable Object Data Cloner Tool").Show();
        }

        [BoxGroup("Source")]
        [AssetSelector(Paths = "Assets")]
        [LabelText("Source ScriptableObject")]
        public ScriptableObject sourceScriptableObject;

        [BoxGroup("Source")]
        [ValueDropdown("GetSourceDictionaryFields")]
        [LabelText("Source Field Name")]
        [ShowIf("@sourceScriptableObject != null")]
        public string sourceFieldName;

        [BoxGroup("Destination")]
        [AssetSelector(Paths = "Assets")]
        [LabelText("Destination ScriptableObject")]
        public ScriptableObject destinationScriptableObject;

        [BoxGroup("Destination")]
        [ValueDropdown("GetDestinationDictionaryFields")]
        [LabelText("Destination Field Name")]
        [ShowIf("@destinationScriptableObject != null")]
        public string destinationFieldName;

        [BoxGroup("Settings")]
        [LabelText("Override Existing Values")]
        [Tooltip("If true, will override existing values. If false, will skip enum values that already have sprites.")]
        public bool overrideExistingValues = true;

        [Button(ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 1f)]
        [EnableIf("@sourceScriptableObject != null && destinationScriptableObject != null && !string.IsNullOrEmpty(sourceFieldName) && !string.IsNullOrEmpty(destinationFieldName)")]
        public void CloneData()
        {
            if (!ValidateInputs())
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(destinationScriptableObject, "Clone Dictionary Data");

            var sourceFieldInfo = GetFieldInfo(sourceScriptableObject, sourceFieldName);
            var destinationFieldInfo = GetFieldInfo(destinationScriptableObject, destinationFieldName);

            if (sourceFieldInfo == null || destinationFieldInfo == null)
            {
                Debug.LogError("Failed to get field info!");
                return;
            }

            var sourceDictionary = sourceFieldInfo.GetValue(sourceScriptableObject);
            var destinationDictionary = destinationFieldInfo.GetValue(destinationScriptableObject);

            if (sourceDictionary == null)
            {
                Debug.LogError("Source dictionary is null!");
                return;
            }

            var sourceDictionaryType = sourceFieldInfo.FieldType;
            var destinationDictionaryType = destinationFieldInfo.FieldType;

            var sourceKeyType = sourceDictionaryType.GetGenericArguments()[0];
            var sourceValueType = sourceDictionaryType.GetGenericArguments()[1];
            var destinationKeyType = destinationDictionaryType.GetGenericArguments()[0];
            var destinationValueType = destinationDictionaryType.GetGenericArguments()[1];

            // validate dictionary types
            if (!sourceKeyType.IsEnum || sourceValueType != typeof(Sprite))
            {
                Debug.LogError("Source dictionary must be Dictionary<Enum, Sprite>!");
                return;
            }

            if (!destinationKeyType.IsEnum || destinationValueType != typeof(Sprite))
            {
                Debug.LogError("Destination dictionary must be Dictionary<Enum, Sprite>!");
                return;
            }

            // initialize destination dictionary if null
            if (destinationDictionary == null)
            {
                destinationDictionary = Activator.CreateInstance(destinationDictionaryType);
                destinationFieldInfo.SetValue(destinationScriptableObject, destinationDictionary);
            }

            // get enum values from both types
            var sourceEnumValues = Enum.GetValues(sourceKeyType).Cast<Enum>().ToList();
            var destinationEnumValues = Enum.GetValues(destinationKeyType).Cast<Enum>().ToList();

            // get methods for dictionary operations
            var sourceContainsKeyMethod = sourceDictionaryType.GetMethod("ContainsKey");
            var sourceGetItemMethod = sourceDictionaryType.GetMethod("get_Item");
            var destinationSetItemMethod = destinationDictionaryType.GetMethod("set_Item");
            var destinationContainsKeyMethod = destinationDictionaryType.GetMethod("ContainsKey");

            int clonedCount = 0;
            int skippedCount = 0;

            // clone data for matching enum values
            foreach (var sourceEnumValue in sourceEnumValues)
            {
                // check if source has this key
                var sourceHasKey = (bool)sourceContainsKeyMethod.Invoke(sourceDictionary, new object[] { sourceEnumValue });
                if (!sourceHasKey)
                {
                    continue;
                }

                // get source sprite
                var sourceSprite = sourceGetItemMethod.Invoke(sourceDictionary, new object[] { sourceEnumValue }) as Sprite;
                if (sourceSprite == null)
                {
                    continue;
                }

                // find matching destination enum value by name
                var matchingDestinationEnum = destinationEnumValues.FirstOrDefault(e => e.ToString() == sourceEnumValue.ToString());
                if (matchingDestinationEnum == null)
                {
                    Debug.LogWarning($"No matching enum value found for '{sourceEnumValue}' in destination enum type '{destinationKeyType.Name}'");
                    continue;
                }

                // check if destination already has value and override is disabled
                if (!overrideExistingValues)
                {
                    var destinationHasKey = (bool)destinationContainsKeyMethod.Invoke(destinationDictionary, new object[] { matchingDestinationEnum });
                    if (destinationHasKey)
                    {
                        var destinationGetItemMethod = destinationDictionaryType.GetMethod("get_Item");
                        var existingValue = destinationGetItemMethod.Invoke(destinationDictionary, new object[] { matchingDestinationEnum });
                        if (existingValue != null)
                        {
                            Debug.Log($"<color=yellow>Skipping '{sourceEnumValue}' - Already has sprite (override disabled)</color>");
                            skippedCount++;
                            continue;
                        }
                    }
                }

                // clone sprite to destination
                destinationSetItemMethod.Invoke(destinationDictionary, new object[] { matchingDestinationEnum, sourceSprite });
                Debug.Log($"<color=cyan>Cloned '{sourceEnumValue}' -> '{matchingDestinationEnum}'</color>");
                clonedCount++;
            }

            EditorUtility.SetDirty(destinationScriptableObject);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=green>Clone completed! Cloned: {clonedCount}, Skipped: {skippedCount}</color>");
        }

        private bool ValidateInputs()
        {
            if (sourceScriptableObject == null)
            {
                Debug.LogError("Source ScriptableObject not set!");
                return false;
            }

            if (destinationScriptableObject == null)
            {
                Debug.LogError("Destination ScriptableObject not set!");
                return false;
            }

            if (string.IsNullOrEmpty(sourceFieldName))
            {
                Debug.LogError("Source field name not set!");
                return false;
            }

            if (string.IsNullOrEmpty(destinationFieldName))
            {
                Debug.LogError("Destination field name not set!");
                return false;
            }

            return true;
        }

        private FieldInfo GetFieldInfo(ScriptableObject so, string fieldName)
        {
            if (so == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            return so.GetType()
                .GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private IEnumerable<string> GetSourceDictionaryFields()
        {
            if (sourceScriptableObject == null) return Enumerable.Empty<string>();

            return sourceScriptableObject.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                            f.FieldType.GetGenericArguments()[0].IsEnum &&
                            f.FieldType.GetGenericArguments()[1] == typeof(Sprite))
                .Select(f => f.Name);
        }

        private IEnumerable<string> GetDestinationDictionaryFields()
        {
            if (destinationScriptableObject == null) return Enumerable.Empty<string>();

            return destinationScriptableObject.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                            f.FieldType.GetGenericArguments()[0].IsEnum &&
                            f.FieldType.GetGenericArguments()[1] == typeof(Sprite))
                .Select(f => f.Name);
        }
    }
}
