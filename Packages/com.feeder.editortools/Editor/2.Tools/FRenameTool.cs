using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FRenameTool : FTargetAssetsToolBase
    {
        protected override string GetDescription()
        {
            return "Rename assets by pattern or find & replace. Drag assets into TargetAssets first.";
        }

        [System.Serializable]
        private class AssetRenameEntry
        {
            public string AssetPath;
            public string OldName;
            public string NewName;
        }

        [PropertySpace(SpaceBefore = 10)]
        [TabGroup("RenameMode", "Change Pattern")]
        [LabelText("Input Pattern"), Tooltip("use {number}, {variant}, {EnumType} (numeric), {(s)EnumType} (string)")]
        [ShowInInspector, ReadOnly, EnableGUI] private string inputPattern = "";

        [PropertySpace(SpaceBefore = 6, SpaceAfter = 6)]
        [TabGroup("RenameMode", "Change Pattern")]
        [LabelText("Output Pattern"), Tooltip("use {number}, {variant}, {EnumType} (numeric), {(s)EnumType} (string)")]
        public string outputPattern = "";

        [TabGroup("RenameMode", "Change Pattern")]
        [Button("Analyze Pattern", ButtonSizes.Medium)]
        private void AnalyzePattern()
        {
            inputPattern = BuildPatternFromAssets(TargetAssets);
        }

        [TabGroup("RenameMode", "Change Pattern")]
        [Button("Apply Rename", ButtonSizes.Large)]
        private void ApplyRename()
        {
            if ((TargetAssets?.Count ?? 0) == 0)
                throw new System.InvalidOperationException("TargetAssets is empty.");
            if (string.IsNullOrEmpty(inputPattern))
                throw new System.InvalidOperationException("inputPattern is empty.");
            if (string.IsNullOrEmpty(outputPattern))
                throw new System.InvalidOperationException("outputPattern is empty.");

            var assetEntries = new Dictionary<string, AssetRenameEntry>(TargetAssets.Count);
            BuildRenamePlan(TargetAssets, inputPattern, outputPattern, assetEntries);

            if (assetEntries.Count == 0)
                return;

            ApplyAssetRenames(assetEntries);
            RefreshInputPattern();
        }

        [PropertySpace(SpaceBefore = 10)]
        [TabGroup("RenameMode", "Find & Replace")]
        [LabelText("Find")]
        public string findString = "";

        [PropertySpace(SpaceBefore = 6, SpaceAfter = 6)]
        [TabGroup("RenameMode", "Find & Replace")]
        [LabelText("Replace With")]
        public string replaceString = "";

        [TabGroup("RenameMode", "Find & Replace")]
        [Button("Apply Find & Replace", ButtonSizes.Large)]
        private void ApplyFindAndReplace()
        {
            if ((TargetAssets?.Count ?? 0) == 0)
                throw new System.InvalidOperationException("TargetAssets is empty.");
            if (string.IsNullOrEmpty(findString))
                throw new System.InvalidOperationException("findString is empty.");

            var assetEntries = new Dictionary<string, AssetRenameEntry>(TargetAssets.Count);
            BuildFindReplacePlan(TargetAssets, findString, replaceString ?? "", assetEntries);

            if (assetEntries.Count == 0)
                return;

            ApplyAssetRenames(assetEntries);
            RefreshInputPattern();
        }

        protected override void OnTargetAssetsChanged()
        {
            RefreshInputPattern();
        }

        private void RefreshInputPattern()
        {
            if (TargetAssets?.Count > 0)
                inputPattern = BuildPatternFromAssets(TargetAssets);
            else
                inputPattern = "";
        }

        private static string BuildPatternFromAssets(List<Object> assets)
        {
            if (assets == null || assets.Count == 0)
                return "";

            var names = new List<string>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null) continue;
                var path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name)) continue;
                names.Add(name);
            }
            if (names.Count == 0)
                return "";
            return StringAnalyzeUtils.BuildPatternFromNames(names);
        }

        private static class EnumPatternResolver
        {
            private const string ReservedNumber = "number";
            private const string ReservedVariant = "variant";

            private sealed class EnumCacheEntry
            {
                public System.Type EnumType;
                public object[] Values;
            }

            // matches {EnumType} (numeric) or {(s)EnumType} (string); reserved: number, variant
            private static readonly System.Text.RegularExpressions.Regex EnumPlaceholderRegex =
                new System.Text.RegularExpressions.Regex(@"\{(\(s\))?([^}]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

            private static readonly System.Collections.Generic.Dictionary<string, EnumCacheEntry> EnumCache =
                new System.Collections.Generic.Dictionary<string, EnumCacheEntry>();

            private static bool IsReservedPlaceholder(string key)
            {
                var t = key?.Trim();
                return t == ReservedNumber || t == ReservedVariant;
            }

            /// <summary>True when pattern contains enum placeholder (not number/variant) so slot index drives enum.</summary>
            public static bool PatternUsesEnum(string pattern)
            {
                if (string.IsNullOrEmpty(pattern)) return false;
                foreach (System.Text.RegularExpressions.Match m in EnumPlaceholderRegex.Matches(pattern))
                {
                    var key = m.Groups[2].Value;
                    if (!IsReservedPlaceholder(key)) return true;
                }
                return false;
            }

            public static string Resolve(string pattern, int index)
            {
                if (string.IsNullOrEmpty(pattern))
                    throw new System.InvalidOperationException("outputPattern is empty.");

                if (!EnumPlaceholderRegex.IsMatch(pattern))
                    return pattern;

                return EnumPlaceholderRegex.Replace(pattern, match =>
                {
                    var isStringEnum = match.Groups[1].Success;
                    var enumTypeName = match.Groups[2].Value?.Trim();
                    if (string.IsNullOrEmpty(enumTypeName))
                        throw new System.InvalidOperationException("enum placeholder has empty type name.");
                    if (IsReservedPlaceholder(enumTypeName))
                        return match.Value;

                    var enumEntry = GetOrCreateEnumEntry(enumTypeName);

                    if (index < 0 || index >= enumEntry.Values.Length)
                        throw new System.InvalidOperationException($"index {index} is out of range for enum {enumEntry.EnumType.FullName}.");

                    var value = enumEntry.Values[index];
                    if (isStringEnum)
                        return value?.ToString() ?? "";
                    var underlyingType = System.Enum.GetUnderlyingType(enumEntry.EnumType);
                    var numeric = System.Convert.ChangeType(value, underlyingType);
                    return System.Convert.ToString(numeric, System.Globalization.CultureInfo.InvariantCulture);
                });
            }

            private static EnumCacheEntry GetOrCreateEnumEntry(string enumTypeName)
            {
                if (string.IsNullOrEmpty(enumTypeName))
                    throw new System.InvalidOperationException("enum type name is empty.");

                enumTypeName = enumTypeName.Trim();

                if (EnumCache.TryGetValue(enumTypeName, out var cachedEntry))
                    return cachedEntry;

                var enumType = FindEnumType(enumTypeName);
                if (enumType == null || !enumType.IsEnum)
                    throw new System.InvalidOperationException($"enum type not found: {enumTypeName}.");

                var valuesArray = System.Enum.GetValues(enumType);
                var values = new object[valuesArray.Length];
                for (int i = 0; i < valuesArray.Length; i++)
                {
                    var value = valuesArray.GetValue(i) ?? throw new System.InvalidOperationException($"enum value at index {i} is null.");
                    values[i] = value;
                }

                var entry = new EnumCacheEntry
                {
                    EnumType = enumType,
                    Values = values
                };

                EnumCache.Add(enumTypeName, entry);
                return entry;
            }

            private static System.Type FindEnumType(string enumTypeName)
            {
                var type = System.Type.GetType(enumTypeName, false);
                if (type != null && type.IsEnum)
                    return type;

                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    var assembly = assemblies[i] ?? throw new System.InvalidOperationException($"assembly at index {i} is null.");
                    var t = assembly.GetType(enumTypeName, false);
                    if (t != null && t.IsEnum)
                        return t;
                }

                var lastDotIndex = enumTypeName.LastIndexOf('.');
                var shortName = lastDotIndex >= 0 ? enumTypeName.Substring(lastDotIndex + 1) : enumTypeName;

                for (int i = 0; i < assemblies.Length; i++)
                {
                    var assembly = assemblies[i];
                    var types = assembly.GetTypes();
                    for (int j = 0; j < types.Length; j++)
                    {
                        var t = types[j];
                        if (t != null && t.IsEnum && t.Name == shortName)
                            return t;
                    }
                }

                return null;
            }
        }

        private static void BuildRenamePlan(
            List<Object> assets,
            string input,
            string output,
            Dictionary<string, AssetRenameEntry> assetEntries)
        {
            bool useEnumSlotIndex = EnumPatternResolver.PatternUsesEnum(output);
            int enumSlotIndex = 0;
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null)
                {
                    if (useEnumSlotIndex) enumSlotIndex++;
                    continue;
                }
                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(assetPath))
                {
                    if (useEnumSlotIndex) enumSlotIndex++;
                    Debug.LogWarning($"[FRenameTool] Skipping TargetAssets[{i}] (no asset path).");
                    continue;
                }

                var oldFileName = Path.GetFileNameWithoutExtension(assetPath);
                int indexForPattern = useEnumSlotIndex ? enumSlotIndex : i;
                var resolvedOutput = EnumPatternResolver.Resolve(output, indexForPattern);
                var newName = ModifyStringUtils.ApplyPattern(oldFileName, input, resolvedOutput, indexForPattern);
                if (string.IsNullOrEmpty(newName))
                    throw new System.InvalidOperationException($"rename result is empty at index {i}.");

                if (newName == oldFileName)
                {
                    if (useEnumSlotIndex) enumSlotIndex++;
                    continue;
                }

                if (!assetEntries.TryGetValue(assetPath, out var entry))
                {
                    entry = new AssetRenameEntry
                    {
                        AssetPath = assetPath,
                        OldName = oldFileName,
                        NewName = newName
                    };
                    assetEntries.Add(assetPath, entry);
                }
                else if (entry.NewName != newName)
                {
                    throw new System.InvalidOperationException($"conflicting rename for asset {assetPath}.");
                }
                if (useEnumSlotIndex) enumSlotIndex++;
            }
        }

        private static void BuildFindReplacePlan(
            List<Object> assets,
            string find,
            string replace,
            Dictionary<string, AssetRenameEntry> assetEntries)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                if (asset == null)
                {
                    Debug.LogWarning($"[FRenameTool] Skipping null at TargetAssets[{i}].");
                    continue;
                }
                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(assetPath))
                {
                    Debug.LogWarning($"[FRenameTool] Skipping TargetAssets[{i}] (no asset path).");
                    continue;
                }

                var oldFileName = Path.GetFileNameWithoutExtension(assetPath);
                var newName = oldFileName.Replace(find, replace);

                if (newName == oldFileName)
                    continue;

                if (!assetEntries.TryGetValue(assetPath, out var entry))
                {
                    entry = new AssetRenameEntry
                    {
                        AssetPath = assetPath,
                        OldName = oldFileName,
                        NewName = newName
                    };
                    assetEntries.Add(assetPath, entry);
                }
                else if (entry.NewName != newName)
                {
                    throw new System.InvalidOperationException($"conflicting rename for asset {assetPath}.");
                }
            }
        }

        private static void ApplyAssetRenames(Dictionary<string, AssetRenameEntry> assetEntries)
        {
            if (assetEntries.Count == 0)
                return;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var entry in assetEntries.Values)
                {
                    var renameError = AssetDatabase.RenameAsset(entry.AssetPath, entry.NewName);
                    if (!string.IsNullOrEmpty(renameError))
                        throw new System.Exception(renameError);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }
}
