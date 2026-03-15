using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Feeder {
    // Compare by contain all correct character, ignore orders and positions
    // Have a % correctness match setting (give a score based on all type of mismatch)
    // Save all correct matchs and then compare % correctness

    public class ScriptableObjectsFillerTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Scriptable Objects Filler")]
        private static void OpenWindow()
        {
            GetWindow<ScriptableObjectsFillerTool>("Scriptable Objects Filler Tool").Show();
        }

        [System.Flags]
        private enum MatchMode
        {
            AllLowerCase = 1 << 0,
            TrimAfterNthSeparator = 1 << 1,
            IgnoreSpecialCharacters = 1 << 2,
        }

        [BoxGroup("Target SO")]
        [AssetSelector(Paths = "Assets")]
        public ScriptableObject targetScriptableObject;

        [BoxGroup("Settings")]
        [FolderPath(AbsolutePath = false)]
        public string searchFolder = "Assets/";

        [ValueDropdown("GetDictionaryFields")]
        [BoxGroup("Settings")]
        public string selectedFieldName;

        [BoxGroup("Key Settings")]
        [SerializeField, EnumToggleButtons]
        [HorizontalGroup("Key Settings/Row", Width = 150)]
        [LabelText("Match Mode")]
        private MatchMode keyMatchMode = MatchMode.AllLowerCase | MatchMode.IgnoreSpecialCharacters;

        [ShowIf("@keyMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Key Settings/Row")]
        [LabelText("Separator")]
        public char keyTrimSeparator = '_';

        [ShowIf("@keyMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Key Settings/Row")]
        [LabelText("Trim After N")]
        public int keyTrimAfterNth = 1;


        [BoxGroup("Value Settings")]
        [SerializeField, EnumToggleButtons]
        [HorizontalGroup("Value Settings/Row", Width = 150)]
        [LabelText("Match Mode")]
        private MatchMode valueMatchMode = MatchMode.AllLowerCase | MatchMode.IgnoreSpecialCharacters;

        [ShowIf("@valueMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Value Settings/Row")]
        [LabelText("Separator")]
        public char valueTrimSeparator = '_';

        [ShowIf("@valueMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Value Settings/Row")]
        [LabelText("Trim After N")]
        public int valueTrimAfterNth = 1;

        [BoxGroup("Override Settings")]
        [LabelText("Override Existing Values")]
        [Tooltip("If true, will override existing sprite values. If false, will skip enum values that already have sprites.")]
        public bool overrideExistingValues = true;

        private FieldInfo selectedFieldInfo;
        private Type selectedDictionaryType;
        private Type keyType;
        private Type valueType;
        private object selectedDictionary;
        private string[] guids;
        private List<Sprite> sprites;

        [Button(ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 1f)]
        public void FillDictionary()
        {
            CheckBeforeFilling();
            Undo.RegisterCompleteObjectUndo(targetScriptableObject, "Fill Dictionary");
            // Get the dictionary value from the ScriptableObject
            selectedDictionary = selectedFieldInfo.GetValue(targetScriptableObject);
            if (selectedDictionary == null)
            {
                selectedDictionary = Activator.CreateInstance(selectedDictionaryType);
                selectedFieldInfo.SetValue(targetScriptableObject, selectedDictionary);
            }
            
            FindValueTypeFromFolder();

            FillValueTypeToDictionary();

            EditorUtility.SetDirty(targetScriptableObject);
            AssetDatabase.SaveAssets();
            Debug.Log("Dictionary filled successfully!");
        }

        [Button(ButtonSizes.Large)]
        [GUIColor(1f, 0.5f, 0.5f)]
        public void ReportMissingValues()
        {
            if (targetScriptableObject == null)
            {
                Debug.LogError("Target ScriptableObject not set!");
                return;
            }

            var logLines = new List<string>();
            
            // Get all dictionary fields in the target ScriptableObject
            var dictionaryFields = targetScriptableObject.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType && 
                           f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                           f.FieldType.GetGenericArguments()[0].IsEnum &&
                           f.FieldType.GetGenericArguments()[1] == typeof(Sprite));

            foreach (var field in dictionaryFields)
            {
                var missingValues = new List<string>();
                var dictionary = field.GetValue(targetScriptableObject);
                
                if (dictionary == null)
                {
                    // If dictionary is null, get all enum values as missing
                    var enumType = field.FieldType.GetGenericArguments()[0];
                    var allEnumValues = Enum.GetValues(enumType);
                    foreach (var enumValue in allEnumValues)
                    {
                        missingValues.Add(enumValue.ToString());
                    }
                }
                else
                {
                    var dictionaryType = field.FieldType;
                    var keyType = dictionaryType.GetGenericArguments()[0];
                    var enumValues = Enum.GetValues(keyType);
                    
                    // Check each enum value
                    foreach (var enumValue in enumValues)
                    {
                        var containsKeyMethod = dictionaryType.GetMethod("ContainsKey");
                        var containsKey = (bool)containsKeyMethod.Invoke(dictionary, new[] { enumValue });
                        
                        if (!containsKey)
                        {
                            missingValues.Add(enumValue.ToString());
                        }
                        else
                        {
                            var getItemMethod = dictionaryType.GetMethod("get_Item");
                            var value = getItemMethod.Invoke(dictionary, new[] { enumValue });
                            
                            if (value == null)
                            {
                                missingValues.Add(enumValue.ToString());
                            }
                        }
                    }
                }

                // Add header and missing values for this dictionary
                if (missingValues.Count > 0)
                {
                    logLines.Add($"================= {field.Name} ==================");
                    logLines.AddRange(missingValues);
                }
            }

            // Log all missing values with proper formatting
            if (logLines.Count > 0)
            {
                var logMessage = string.Join("\n", logLines);
                Debug.Log(logMessage);
            }
            else
            {
                Debug.Log("No missing values found!");
            }
        }

        private void CheckBeforeFilling()
        {
            if (targetScriptableObject == null || string.IsNullOrEmpty(selectedFieldName))
            {
                Debug.LogError("Target ScriptableObject or field not set!");
                return;
            }

            // Get field info
            selectedFieldInfo = targetScriptableObject.GetType()
                .GetField(selectedFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (selectedFieldInfo == null)
            {
                Debug.LogError("Field not found!");
                return;
            }

            // Check if field is type Dictionary<Enum, Sprite>
            selectedDictionaryType = selectedFieldInfo.FieldType;
            if (!selectedDictionaryType.IsGenericType || selectedDictionaryType.GetGenericTypeDefinition() != typeof(Dictionary<,>))
            {
                Debug.LogError("Selected field is not a Dictionary!");
                return;
            }

            keyType = selectedDictionaryType.GetGenericArguments()[0];
            valueType = selectedDictionaryType.GetGenericArguments()[1];

            // fix type to be Enum and Sprite
            if (!keyType.IsEnum || valueType != typeof(Sprite))
            {
                Debug.LogError("Dictionary must be Dictionary<Enum, Sprite>!");
                return;
            }
        }

        private void FindValueTypeFromFolder()
        {
            // Find all sprites in folder
            guids = AssetDatabase.FindAssets("t:Sprite", new[] { searchFolder });
            sprites = guids
                .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
        }

        private string NormalizeName(string input, MatchMode mode, char trimSeparator, int trimAfterNth)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string result = input;

            if (mode.HasFlag(MatchMode.AllLowerCase))
            {
                result = result.ToLower();
            }
            
            if (mode.HasFlag(MatchMode.TrimAfterNthSeparator))
            {
                result = TrimAfterNthOccurrence(result, trimSeparator, trimAfterNth);
            }

            if (mode.HasFlag(MatchMode.IgnoreSpecialCharacters))
            {
                result = Regex.Replace(result, @"[^a-zA-Z0-9]", "");
            }

            return result;
        }

        private string TrimAfterNthOccurrence(string input, char separator, int n)
        {
            if (n <= 0) return input;

            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == separator)
                {
                    count++;
                    if (count == n)
                    {
                        return input.Substring(i + 1);
                    }
                }
            }
            return input; 
        }

        private bool IsMatch(string enumName, string spriteName)
        {
            return spriteName.Contains(enumName);
        }

        bool findExactMatch = false;
        private void FillValueTypeToDictionary()
        {
            foreach (var enumValue in Enum.GetValues(keyType))
            {
                string normEnum = NormalizeName(enumValue.ToString(), keyMatchMode, keyTrimSeparator, keyTrimAfterNth);
                
                // Check if enum already has a sprite and override is disabled
                if (!overrideExistingValues)
                {
                    var containsKeyMethod = selectedDictionaryType.GetMethod("ContainsKey");
                    var containsKey = (bool)containsKeyMethod.Invoke(selectedDictionary, new[] { enumValue });
                    
                    if (containsKey)
                    {
                        var getItemMethod = selectedDictionaryType.GetMethod("get_Item");
                        var existingValue = getItemMethod.Invoke(selectedDictionary, new[] { enumValue });
                        if (existingValue != null)
                        {
                            Debug.Log($"<color=yellow>Skipping '{normEnum}' - Already has sprite (override disabled)</color>");
                            continue;
                        }
                    }
                }
                
                findExactMatch = false;
                Sprite matchedSprite = null;
                
                foreach (var sprite in sprites)
                {
                    string normSprite = NormalizeName(sprite.name, valueMatchMode, valueTrimSeparator, valueTrimAfterNth);
                    //Debug.Log($"Checking sprite: '{normSprite}' for enum '{normEnum}'");
                    if (IsMatch(normEnum, normSprite))
                    {
                        Debug.Log($"<color=cyan>Match found:'{normSprite}' Contains '{normEnum}'</color>");
                        matchedSprite = sprite;
                        findExactMatch = true;
                        break;
                    }
                }
                
                // Always add the enum to dictionary, with null sprite if no match found
                selectedDictionaryType.GetMethod("set_Item").Invoke(selectedDictionary, new[] { enumValue, matchedSprite });
                
                if (!findExactMatch) 
                {
                    Debug.Log($"<color=red>No match found for '{normEnum}' - Added with null sprite</color>");
                }
            }
        }

        private IEnumerable<string> GetDictionaryFields()
        {
            if (targetScriptableObject == null) return Enumerable.Empty<string>();

            return targetScriptableObject.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                            f.FieldType.GetGenericArguments()[0].IsEnum &&
                            f.FieldType.GetGenericArguments()[1] == typeof(Sprite))
                .Select(f => f.Name);
        }
    }
}
