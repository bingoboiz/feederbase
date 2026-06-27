using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Feeder
{
    /// <summary>"Used By": every asset that references the target assets.</summary>
    public sealed class FRefUsedByMode : FRefModeBase
    {
        [ToggleLeft, PropertyOrder(0)]
        public bool Recursive;

        [PropertyOrder(0), EnableIf(nameof(Recursive)), MinValue(0)]
        [Tooltip("0 = không giới hạn độ sâu khi Recursive bật.")]
        public int MaxDepth;

        [PropertyOrder(0), ValueDropdown(nameof(GroupModes))]
        public string GroupBy = "Type";

        [NonSerialized] private List<FRefResult> _results;
        [NonSerialized] private string _searchText;

        private static string[] GroupModes => FRefResultGUI.GroupModes;

        protected override string GetDescription() =>
            "Tìm tất cả asset đang tham chiếu tới các asset trong TargetAssets.";

        [Button("Find Used By", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.3f), PropertyOrder(1)]
        private void Find()
        {
            if (!RequireDatabaseReady()) return;
            string[] guids = TargetGuids;
            if (guids.Length == 0)
            {
                UnityEditor.EditorUtility.DisplayDialog("Feeder Asset Cache", "Please add assets to TargetAssets first.", "OK");
                return;
            }

            _results = FRefQuery.FindUsedBy(guids, Recursive, MaxDepth);
        }

        [OnInspectorGUI, PropertyOrder(2)]
        private void DrawResults()
        {
            FRefResultGUI.DrawGroupedResults(_results, GroupBy, ref _searchText);
        }
    }
}
