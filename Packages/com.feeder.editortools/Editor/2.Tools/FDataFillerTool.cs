using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FDataFillerTool : FTargetScriptableObjectToolBase
    {
        protected override string GetDescription()
        {
            return "Tự điền Dictionary<Enum, Sprite> trong ScriptableObject bằng cách khớp mờ tên enum với sprite trong thư mục. Dùng Match Threshold để tinh chỉnh độ nhạy khớp.";
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

        [BoxGroup("Settings")]
        [LabelText("Match Threshold (0–1)")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _matchThreshold = 0.8f;

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
                "Match Threshold    ngưỡng độ khớp tối thiểu (0–1), thường để 0.8–0.9\n" +
                "Override           ghi đè sprite đã có hay bỏ qua\n" +
                "Report Missing     liệt kê những enum chưa có sprite tương ứng"
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

        private void FillValueTypeToDictionary(object dictionary, Type dictionaryType, Type keyType, List<Sprite> sprites)
        {
            var setItem = dictionaryType.GetMethod("set_Item");
            var containsKey = dictionaryType.GetMethod("ContainsKey");
            var getItem = dictionaryType.GetMethod("get_Item");

            string[] normalizedSprites = new string[sprites.Count];
            for (int i = 0; i < sprites.Count; i++)
                normalizedSprites[i] = sprites[i] != null ? FuzzyMatchUtils.Normalize(sprites[i].name) : null;

            var usedIndices = new HashSet<int>();

            foreach (var enumValue in Enum.GetValues(keyType))
            {
                if (!OverrideExistingValues)
                {
                    var hasKey = (bool)containsKey.Invoke(dictionary, new[] { enumValue });
                    if (hasKey)
                    {
                        var existing = getItem.Invoke(dictionary, new[] { enumValue });
                        if (existing != null)
                        {
                            Debug.Log($"<color=yellow>Skipping '{enumValue}' - Already has sprite (override disabled)</color>");
                            continue;
                        }
                    }
                }

                string normEnum = FuzzyMatchUtils.Normalize(enumValue.ToString());
                int bestIndex = -1;
                float bestScore = 0f;

                for (int i = 0; i < sprites.Count; i++)
                {
                    if (usedIndices.Contains(i) || normalizedSprites[i] == null) continue;
                    float score = FuzzyMatchUtils.Similarity(normEnum, normalizedSprites[i]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }

                Sprite matchedSprite = null;
                if (bestIndex >= 0 && bestScore >= _matchThreshold)
                {
                    matchedSprite = sprites[bestIndex];
                    usedIndices.Add(bestIndex);
                    Debug.Log($"<color=cyan>Match: '{enumValue}' → '{matchedSprite.name}' ({bestScore * 100f:F1}%)</color>");
                }
                else
                {
                    Debug.Log($"<color=red>No match for '{enumValue}' (best: {bestScore * 100f:F1}%)</color>");
                }

                setItem.Invoke(dictionary, new[] { enumValue, matchedSprite });
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
