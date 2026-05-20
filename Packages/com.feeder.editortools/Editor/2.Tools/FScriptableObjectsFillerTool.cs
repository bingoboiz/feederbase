using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FScriptableObjectsFillerTool : FTargetScriptableObjectToolBase
    {
        [Flags]
        private enum MatchMode
        {
            AllLowerCase = 1 << 0,
            TrimAfterNthSeparator = 1 << 1,
            IgnoreSpecialCharacters = 1 << 2,
        }

        protected override string GetDescription()
        {
            return "Tự điền Dictionary<Enum, Sprite> trong ScriptableObject bằng cách ghép tên enum với sprite trong thư mục. Dùng Key/Value Settings để tinh chỉnh cách so khớp.";
        }

        [BoxGroup("Target SO")]
        [AssetSelector(Paths = "Assets")]
        [ShowInInspector]
        public new ScriptableObject TargetSO
        {
            get => base.TargetSO;
            set => base.TargetSO = value;
        }

        [BoxGroup("Settings")]
        [FolderPath(AbsolutePath = false)]
        [ShowInInspector]
        public string SearchFolder = "Assets/";

        [ValueDropdown(nameof(GetDictionaryFields))]
        [BoxGroup("Settings")]
        [ShowInInspector]
        public string SelectedFieldName;

        [BoxGroup("Key Settings")]
        [SerializeField, EnumToggleButtons]
        [HorizontalGroup("Key Settings/Row", Width = 150)]
        [LabelText("Match Mode")]
        private MatchMode keyMatchMode = MatchMode.AllLowerCase | MatchMode.IgnoreSpecialCharacters;

        [ShowIf("@keyMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Key Settings/Row")]
        [LabelText("Separator")]
        [ShowInInspector]
        public char KeyTrimSeparator = '_';

        [ShowIf("@keyMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Key Settings/Row")]
        [LabelText("Trim After N")]
        [ShowInInspector]
        public int KeyTrimAfterNth = 1;

        [BoxGroup("Value Settings")]
        [SerializeField, EnumToggleButtons]
        [HorizontalGroup("Value Settings/Row", Width = 150)]
        [LabelText("Match Mode")]
        private MatchMode valueMatchMode = MatchMode.AllLowerCase | MatchMode.IgnoreSpecialCharacters;

        [ShowIf("@valueMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Value Settings/Row")]
        [LabelText("Separator")]
        [ShowInInspector]
        public char ValueTrimSeparator = '_';

        [ShowIf("@valueMatchMode.HasFlag(MatchMode.TrimAfterNthSeparator)")]
        [HorizontalGroup("Value Settings/Row")]
        [LabelText("Trim After N")]
        [ShowInInspector]
        public int ValueTrimAfterNth = 1;

        [BoxGroup("Override Settings")]
        [LabelText("Override Existing Values")]
        [Tooltip("If true, will override existing sprite values. If false, will skip enum values that already have sprites.")]
        [ShowInInspector]
        public bool OverrideExistingValues = true;

        [OnInspectorGUI]
        private void DrawGuide()
        {
            GUILayout.Space(2);
            StylesUtils.DrawInfoBox(
                "AllLowerCase              so sánh sau khi chuyển về chữ thường\n" +
                "IgnoreSpecialChars        bỏ qua _ - . và ký tự đặc biệt khi so sánh\n" +
                "TrimAfterNthSeparator     cắt bỏ phần sau separator thứ N\n" +
                "Key Settings              áp dụng cho tên enum (key)\n" +
                "Value Settings            áp dụng cho tên sprite (value)\n" +
                "Report Missing            liệt kê những enum chưa có sprite tương ứng"
            );
            GUILayout.Space(4);
        }

        [Button(ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 1f)]
        public void FillDictionary()
        {
            if (!CheckBeforeFilling(out var fieldInfo, out var dictionaryType, out var keyType))
                return;

            Undo.RegisterCompleteObjectUndo(TargetSO, "Fill Dictionary");
            object dictionary = fieldInfo.GetValue(TargetSO);
            if (dictionary == null)
            {
                dictionary = Activator.CreateInstance(dictionaryType);
                fieldInfo.SetValue(TargetSO, dictionary);
            }

            var sprites = LoadSpritesFromFolder();
            FillValueTypeToDictionary(dictionary, dictionaryType, keyType, sprites);

            EditorUtility.SetDirty(TargetSO);
            AssetDatabase.SaveAssets();
            Debug.Log("Dictionary filled successfully!");
        }

        [Button(ButtonSizes.Large)]
        [GUIColor(1f, 0.5f, 0.5f)]
        public void ReportMissingValues()
        {
            if (TargetSO == null)
            {
                Debug.LogError("Target ScriptableObject not set!");
                return;
            }

            var logLines = new List<string>();
            var dictionaryFields = TargetSO.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                            f.FieldType.GetGenericArguments()[0].IsEnum &&
                            f.FieldType.GetGenericArguments()[1] == typeof(Sprite));

            foreach (var field in dictionaryFields)
            {
                var missingValues = CollectMissingValues(field, TargetSO);
                if (missingValues.Count > 0)
                {
                    logLines.Add($"================= {field.Name} ==================");
                    logLines.AddRange(missingValues);
                }
            }

            if (logLines.Count > 0)
                Debug.Log(string.Join("\n", logLines));
            else
                Debug.Log("No missing values found!");
        }

        private bool CheckBeforeFilling(out FieldInfo fieldInfo, out Type dictionaryType, out Type keyType)
        {
            fieldInfo = null;
            dictionaryType = null;
            keyType = null;

            if (TargetSO == null || string.IsNullOrEmpty(SelectedFieldName))
            {
                Debug.LogError("Target ScriptableObject or field not set!");
                return false;
            }

            fieldInfo = TargetSO.GetType()
                .GetField(SelectedFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                Debug.LogError("Field not found!");
                return false;
            }

            dictionaryType = fieldInfo.FieldType;
            if (!dictionaryType.IsGenericType || dictionaryType.GetGenericTypeDefinition() != typeof(Dictionary<,>))
            {
                Debug.LogError("Selected field is not a Dictionary!");
                return false;
            }

            keyType = dictionaryType.GetGenericArguments()[0];
            var valueType = dictionaryType.GetGenericArguments()[1];
            if (!keyType.IsEnum || valueType != typeof(Sprite))
            {
                Debug.LogError("Dictionary must be Dictionary<Enum, Sprite>!");
                return false;
            }

            return true;
        }

        private List<Sprite> LoadSpritesFromFolder()
        {
            var guids = AssetDatabase.FindAssets("t:Sprite", new[] { SearchFolder });
            return guids
                .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
        }

        private static string NormalizeName(string input, MatchMode mode, char trimSeparator, int trimAfterNth)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            string result = input;
            if (mode.HasFlag(MatchMode.AllLowerCase))
                result = result.ToLower();
            if (mode.HasFlag(MatchMode.TrimAfterNthSeparator))
                result = TrimAfterNthOccurrence(result, trimSeparator, trimAfterNth);
            if (mode.HasFlag(MatchMode.IgnoreSpecialCharacters))
                result = Regex.Replace(result, @"[^a-zA-Z0-9]", "");
            return result;
        }

        private static string TrimAfterNthOccurrence(string input, char separator, int n)
        {
            if (n <= 0) return input;
            int count = 0;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == separator)
                {
                    count++;
                    if (count == n)
                        return input.Substring(i + 1);
                }
            }
            return input;
        }

        private static bool IsMatch(string enumName, string spriteName) => spriteName.Contains(enumName);

        private void FillValueTypeToDictionary(object dictionary, Type dictionaryType, Type keyType, List<Sprite> sprites)
        {
            var setItem = dictionaryType.GetMethod("set_Item");
            var containsKey = dictionaryType.GetMethod("ContainsKey");
            var getItem = dictionaryType.GetMethod("get_Item");

            foreach (var enumValue in Enum.GetValues(keyType))
            {
                string normEnum = NormalizeName(enumValue.ToString(), keyMatchMode, KeyTrimSeparator, KeyTrimAfterNth);

                if (!OverrideExistingValues)
                {
                    var hasKey = (bool)containsKey.Invoke(dictionary, new[] { enumValue });
                    if (hasKey)
                    {
                        var existing = getItem.Invoke(dictionary, new[] { enumValue });
                        if (existing != null)
                        {
                            Debug.Log($"<color=yellow>Skipping '{normEnum}' - Already has sprite (override disabled)</color>");
                            setItem.Invoke(dictionary, new[] { enumValue, existing });
                            continue;
                        }
                    }
                }

                Sprite matchedSprite = null;
                foreach (var sprite in sprites)
                {
                    string normSprite = NormalizeName(sprite.name, valueMatchMode, ValueTrimSeparator, ValueTrimAfterNth);
                    if (IsMatch(normEnum, normSprite))
                    {
                        Debug.Log($"<color=cyan>Match found:'{normSprite}' Contains '{normEnum}'</color>");
                        matchedSprite = sprite;
                        break;
                    }
                }

                setItem.Invoke(dictionary, new[] { enumValue, matchedSprite });
                if (matchedSprite == null)
                    Debug.Log($"<color=red>No match found for '{normEnum}' - Added with null sprite</color>");
            }
        }

        private static List<string> CollectMissingValues(FieldInfo field, ScriptableObject target)
        {
            var missingValues = new List<string>();
            var dictionary = field.GetValue(target);
            var dictionaryType = field.FieldType;
            var keyType = dictionaryType.GetGenericArguments()[0];

            if (dictionary == null)
            {
                foreach (var enumValue in Enum.GetValues(keyType))
                    missingValues.Add(enumValue.ToString());
                return missingValues;
            }

            var containsKeyMethod = dictionaryType.GetMethod("ContainsKey");
            var getItemMethod = dictionaryType.GetMethod("get_Item");

            foreach (var enumValue in Enum.GetValues(keyType))
            {
                var hasKey = (bool)containsKeyMethod.Invoke(dictionary, new[] { enumValue });
                if (!hasKey)
                {
                    missingValues.Add(enumValue.ToString());
                    continue;
                }
                var value = getItemMethod.Invoke(dictionary, new[] { enumValue });
                if (value == null)
                    missingValues.Add(enumValue.ToString());
            }

            return missingValues;
        }

        private IEnumerable<string> GetDictionaryFields()
        {
            if (TargetSO == null) return Enumerable.Empty<string>();

            return TargetSO.GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType.IsGenericType &&
                            f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
                            f.FieldType.GetGenericArguments()[0].IsEnum &&
                            f.FieldType.GetGenericArguments()[1] == typeof(Sprite))
                .Select(f => f.Name);
        }
    }
}
