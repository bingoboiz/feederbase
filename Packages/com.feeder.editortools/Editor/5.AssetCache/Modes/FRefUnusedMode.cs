using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;

namespace Feeder
{
    /// <summary>"Unused": assets that nothing else in the project references.</summary>
    public sealed class FRefUnusedMode : FRefModeBase
    {
        [PropertyOrder(0), ValueDropdown(nameof(GroupModes))]
        public string GroupBy = "Type";

        [NonSerialized] private List<FRefResult> _results;
        [NonSerialized] private string _searchText;

        private static string[] GroupModes => FRefResultGUI.GroupModes;

        protected override string GetDescription() =>
            "Tìm asset không được asset nào tham chiếu. Bỏ qua Scene, Script, Resources/ và Editor/. Không cần target.";

        [Button("Find Unused", ButtonSizes.Large), GUIColor(0.3f, 0.8f, 0.3f), PropertyOrder(1)]
        private void Find()
        {
            if (!RequireDatabaseReady()) return;
            var unused = FRefScanner.FindUnused();
            _results = new List<FRefResult>(unused.Count);
            foreach (var asset in unused)
                _results.Add(new FRefResult { asset = asset, depth = 0 });
        }

        [OnInspectorGUI, PropertyOrder(2)]
        private void DrawResults()
        {
            FRefResultGUI.DrawGroupedResults(_results, GroupBy, ref _searchText);
        }
    }
}
