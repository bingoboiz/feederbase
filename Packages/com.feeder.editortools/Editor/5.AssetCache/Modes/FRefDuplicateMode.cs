using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;

namespace Feeder
{
    /// <summary>"Duplicate": assets with byte-identical content grouped together.</summary>
    public sealed class FRefDuplicateMode : FRefModeBase
    {
        [NonSerialized] private List<List<FRefAsset>> _groups;
        [NonSerialized] private string _searchText;

        protected override string GetDescription() =>
            "Tìm các asset có nội dung trùng lặp (cùng kích thước + cùng MD5). Không cần target.";

        [Button("Find Duplicates", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.3f), PropertyOrder(1)]
        private void Find()
        {
            if (!RequireDatabaseReady()) return;
            try
            {
                EditorUtility.DisplayProgressBar("Feeder Asset Cache", "Comparing asset contents...", 0.5f);
                _groups = FRefScanner.FindDuplicates();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [OnInspectorGUI, PropertyOrder(2)]
        private void DrawResults()
        {
            if (_groups == null)
            {
                EditorGUILayout.HelpBox("Bấm Find Duplicates để quét.", MessageType.Info);
                return;
            }

            if (_groups.Count == 0)
            {
                EditorGUILayout.HelpBox("Không có asset trùng lặp.", MessageType.Info);
                return;
            }

            List<List<FRefAsset>> filtered = FilterGroups(_groups, _searchText);
            int totalAssets = _groups.Sum(g => g.Count);
            int shownAssets = filtered.Sum(g => g.Count);
            _searchText = FRefResultGUI.DrawSearchToolbar(_searchText, shownAssets, totalAssets);

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox("Không có nhóm trùng lặp khớp bộ lọc hiện tại.", MessageType.Info);
                return;
            }

            FRefResultGUI.DrawAssetPathActions(filtered.SelectMany(g => g).Select(a => a.path));
            EditorGUILayout.LabelField($"Số nhóm trùng: {filtered.Count}", EditorStyles.boldLabel);

            for (int i = 0; i < filtered.Count; i++)
            {
                var group = filtered[i];
                EditorGUILayout.LabelField($"Nhóm {i + 1} - {group.Count} bản ({group[0].fileSize} bytes)", EditorStyles.miniBoldLabel);
                foreach (var asset in group)
                    FRefResultGUI.DrawAssetRow(asset.path, 1);
            }
        }

        private static List<List<FRefAsset>> FilterGroups(List<List<FRefAsset>> groups, string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return groups;

            var filtered = new List<List<FRefAsset>>();
            foreach (var group in groups)
                if (group.Any(asset => FRefResultGUI.AssetPathMatchesSearch(asset.path, searchText)))
                    filtered.Add(group);

            return filtered;
        }
    }
}
