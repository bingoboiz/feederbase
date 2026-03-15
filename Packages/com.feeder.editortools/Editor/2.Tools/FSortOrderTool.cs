using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Feeder
{
    public sealed class FSortOrderTool : FTargetAssetsToolBase
    {
        [System.Serializable]
        private sealed class SortOrderMappingRow
        {
            [TableColumnWidth(180)]
            public string EnumName;

            [TableColumnWidth(280)]
            [AssetSelector(Paths = "Assets")]
            public UnityEngine.Object Asset;
        }

        protected override string GetDescription()
        {
            return "Match TargetAssets to an enum by name (asset.name.Contains(enumName)). Analyze shows mapping table; Apply Sort overwrites TargetAssets in enum order (null when no match).";
        }

        [PropertySpace(SpaceBefore = 8)]
        [LabelText("Enum Type")]
        [ValueDropdown(nameof(GetEnumTypeDropdown))]
        [ShowInInspector]
        [SerializeField]
        private string _selectedEnumTypeName;

        private System.Type ResolveSelectedEnumType()
        {
            if (string.IsNullOrEmpty(_selectedEnumTypeName))
                return null;
            var t = System.Type.GetType(_selectedEnumTypeName, false);
            if (t != null && t.IsEnum)
                return t;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = assembly?.GetType(_selectedEnumTypeName, false);
                if (t != null && t.IsEnum)
                    return t;
            }
            return null;
        }

        private IEnumerable<ValueDropdownItem<string>> GetEnumTypeDropdown()
        {
            var list = new List<(string display, string value)>();
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null) continue;
                System.Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }
                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t?.IsEnum != true) continue;
                    var fullName = t.FullName ?? t.Name;
                    var qualified = t.AssemblyQualifiedName ?? fullName;
                    list.Add((fullName, qualified));
                }
            }
            list.Sort((a, b) => string.Compare(a.display, b.display, StringComparison.Ordinal));
            foreach (var pair in list)
                yield return new ValueDropdownItem<string>(pair.display, pair.value);
        }

        [PropertySpace(SpaceBefore = 10)]
        [ShowIf(nameof(HasMapping))]
        [TableList(ShowIndexLabels = true, IsReadOnly = false, NumberOfItemsPerPage = 15, AlwaysExpanded = true, ShowPaging = true)]
        [LabelText("Enum → Asset mapping")]
        [SerializeField]
        private List<SortOrderMappingRow> _mappingRows = new List<SortOrderMappingRow>();

        private bool HasMapping => _mappingRows?.Count > 0;

        [PropertySpace(SpaceBefore = 6)]
        [HorizontalGroup("Actions")]
        [Button("Analyze", ButtonSizes.Medium)]
        private void Analyze()
        {
            var enumType = ResolveSelectedEnumType();
            if (enumType == null)
                throw new InvalidOperationException("Select an enum type first.");
            if (TargetAssets == null)
                throw new InvalidOperationException("TargetAssets is null.");
            _mappingRows ??= new List<SortOrderMappingRow>();
            _mappingRows.Clear();
            var values = System.Enum.GetValues(enumType);
            var usedAssets = new HashSet<System.Object>();
            for (int i = 0; i < values.Length; i++)
            {
                var enumVal = values.GetValue(i);
                var enumName = enumVal?.ToString() ?? "";
                var row = new SortOrderMappingRow { EnumName = enumName, Asset = null };
                if (!string.IsNullOrEmpty(enumName))
                {
                    for (int j = 0; j < TargetAssets.Count; j++)
                    {
                        var asset = TargetAssets[j];
                        if (asset == null || usedAssets.Contains(asset))
                            continue;
                        if (asset.name?.Contains(enumName) == true)
                        {
                            row.Asset = asset;
                            usedAssets.Add(asset);
                            break;
                        }
                    }
                }
                _mappingRows.Add(row);
            }
        }

        [HorizontalGroup("Actions")]
        [Button("Apply Sort", ButtonSizes.Medium)]
        private void ApplySort()
        {
            if (_mappingRows == null || _mappingRows.Count == 0)
                throw new InvalidOperationException("Run Analyze first and ensure an enum type is selected.");
            var data = GetDataContainer();
            data.TargetAssets.Clear();
            for (int i = 0; i < _mappingRows.Count; i++)
                data.TargetAssets.Add(_mappingRows[i].Asset);
            FDataPersistenceService.SaveData(data);
        }
    }
}
