using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FAssetGroupTool : FTargetAssetsToolBase
    {
        private const string Tab = "GroupMode";

        [System.Serializable]
        private sealed class AssetGroup
        {
            public string GroupName;
            public List<Object> Assets = new List<Object>();
        }

        private sealed class PatternInfo
        {
            public string Token;
            public List<Object> Assets = new List<Object>();
        }

        protected override string GetDescription()
        {
            return "Tự động phát hiện các token chung trong tên asset (hỗ trợ CamelCase, _/-/. delimiter). " +
                   "Mỗi nhóm có nút Apply để gán asset vào TargetAssets cho các tool khác xử lý.";
        }

        [SerializeField, HideInInspector]
        private List<AssetGroup> _groups = new List<AssetGroup>();

        // ── pattern analysis ──
        [System.NonSerialized] private List<PatternInfo> _candidatePatterns = new List<PatternInfo>();
        [System.NonSerialized] private bool _patternsAnalyzed;
        [System.NonSerialized] private int _patternMinCount = 2;
        [System.NonSerialized] private bool _splitCamelCase = true;
        [System.NonSerialized] private Vector2 _patternScrollPos;

        // ── keyword input ──
        [System.NonSerialized] private string _kwGroupName = "";
        [System.NonSerialized] private string _kwKeyword = "";

        // ── ui state ──
        [System.NonSerialized] private Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();

        private static readonly StringBuilder s_Builder = new StringBuilder(32);

        private void OnEnable()
        {
            if (_candidatePatterns == null) _candidatePatterns = new List<PatternInfo>();
            if (_foldouts == null) _foldouts = new Dictionary<int, bool>();
            _splitCamelCase = true;
        }

        protected override void OnTargetAssetsChanged()
        {
            _patternsAnalyzed = false;
            _candidatePatterns.Clear();
            _foldouts.Clear();
            _groups.Clear();
        }

        // ───────────────────────────── Auto Pattern Tab ─────────────────────────────

        [PropertySpace(SpaceBefore = 10)]
        [TabGroup(Tab, "Auto Pattern")]
        [ShowInInspector]
        [LabelText("Split CamelCase")]
        [PropertyOrder(5)]
        private bool SplitCamelCase
        {
            get => _splitCamelCase;
            set => _splitCamelCase = value;
        }

        [TabGroup(Tab, "Auto Pattern")]
        [ShowInInspector]
        [LabelText("Min Asset Count")]
        [PropertyRange(2, 20)]
        [PropertyOrder(6)]
        private int PatternMinCount
        {
            get => _patternMinCount;
            set => _patternMinCount = value;
        }

        [TabGroup(Tab, "Auto Pattern")]
        [Button("Analyze Patterns", ButtonSizes.Medium)]
        [GUIColor(0.3f, 0.8f, 0.3f)]
        [PropertyOrder(10)]
        private void AnalyzePatterns()
        {
            _candidatePatterns.Clear();
            _patternsAnalyzed = false;

            List<Object> assets = TargetAssets;
            if (assets.Count == 0)
            {
                Debug.LogWarning("[Asset Group] No target assets to analyze.");
                return;
            }

            // key = lowercase, value preserves first-seen casing
            Dictionary<string, PatternInfo> tokenMap = new Dictionary<string, PatternInfo>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == null) continue;
                List<string> tokens = TokenizeName(assets[i].name);

                // track per-asset to avoid counting same token twice for one asset
                HashSet<string> seenInThisAsset = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < tokens.Count; j++)
                {
                    string t = tokens[j];
                    if (t.Length < 2) continue;
                    if (IsNumericOnly(t)) continue;
                    if (!seenInThisAsset.Add(t)) continue;

                    if (!tokenMap.TryGetValue(t, out PatternInfo info))
                    {
                        info = new PatternInfo { Token = t };
                        tokenMap[t] = info;
                    }
                    info.Assets.Add(assets[i]);
                }
            }

            foreach (PatternInfo info in tokenMap.Values)
            {
                if (info.Assets.Count >= _patternMinCount)
                    _candidatePatterns.Add(info);
            }

            _candidatePatterns.Sort((a, b) => b.Assets.Count.CompareTo(a.Assets.Count));
            _patternsAnalyzed = true;

            Debug.Log($"<color=cyan>[Asset Group] Found {_candidatePatterns.Count} patterns (min={_patternMinCount}) across {assets.Count} assets</color>");
        }

        [TabGroup(Tab, "Auto Pattern")]
        [OnInspectorGUI]
        [PropertyOrder(20)]
        private void DrawPatternsSection()
        {
            if (!_patternsAnalyzed) return;

            GUILayout.Space(8);

            if (_candidatePatterns.Count == 0)
            {
                EditorGUILayout.HelpBox($"No patterns found with min count = {_patternMinCount}. Try lowering the threshold.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Discovered Patterns ({_candidatePatterns.Count})", EditorStyles.boldLabel);
            GUILayout.Space(4);

            _patternScrollPos = EditorGUILayout.BeginScrollView(_patternScrollPos, GUILayout.MaxHeight(280));
            for (int i = 0; i < _candidatePatterns.Count; i++)
            {
                PatternInfo p = _candidatePatterns[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(p.Token, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField($"{p.Assets.Count} assets", GUILayout.Width(68));

                Color origBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
                if (GUILayout.Button("Add Group", GUILayout.Width(80), GUILayout.Height(18)))
                    AddPatternAsGroup(p);
                GUI.backgroundColor = origBg;

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            Color bg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("Add All as Groups", GUILayout.Height(26)))
                AddAllPatternsAsGroups();
            GUI.backgroundColor = bg;
        }

        private void AddPatternAsGroup(PatternInfo pattern)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (string.Equals(_groups[i].GroupName, pattern.Token, System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"<color=yellow>[Asset Group] Group '{pattern.Token}' already exists.</color>");
                    return;
                }
            }
            _groups.Add(new AssetGroup { GroupName = pattern.Token, Assets = new List<Object>(pattern.Assets) });
        }

        private void AddAllPatternsAsGroups()
        {
            int added = 0;
            for (int i = 0; i < _candidatePatterns.Count; i++)
            {
                PatternInfo p = _candidatePatterns[i];
                bool exists = false;
                for (int j = 0; j < _groups.Count; j++)
                {
                    if (string.Equals(_groups[j].GroupName, p.Token, System.StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    _groups.Add(new AssetGroup { GroupName = p.Token, Assets = new List<Object>(p.Assets) });
                    added++;
                }
            }
            Debug.Log($"<color=cyan>[Asset Group] Added {added} groups from patterns.</color>");
        }

        // ───────────────────────────── Keyword Tab ──────────────────────────────────

        [PropertySpace(SpaceBefore = 10)]
        [TabGroup(Tab, "Keyword")]
        [LabelText("Group Name")]
        [ShowInInspector]
        [PropertyOrder(10)]
        private string KwGroupName
        {
            get => _kwGroupName;
            set => _kwGroupName = value;
        }

        [TabGroup(Tab, "Keyword")]
        [LabelText("Keyword")]
        [ShowInInspector]
        [PropertyOrder(11)]
        private string KwKeyword
        {
            get => _kwKeyword;
            set => _kwKeyword = value;
        }

        [PropertySpace(SpaceBefore = 4)]
        [TabGroup(Tab, "Keyword")]
        [Button("Add Group", ButtonSizes.Medium)]
        [GUIColor(0.3f, 0.8f, 0.3f)]
        [PropertyOrder(12)]
        private void AddKeywordGroup()
        {
            if (string.IsNullOrEmpty(_kwGroupName) || string.IsNullOrEmpty(_kwKeyword))
            {
                Debug.LogWarning("[Asset Group] Group name and keyword must not be empty.");
                return;
            }

            AssetGroup group = new AssetGroup { GroupName = _kwGroupName, Assets = new List<Object>() };
            string lowerKeyword = _kwKeyword.ToLowerInvariant();
            List<Object> assets = TargetAssets;

            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] == null) continue;
                if (assets[i].name.ToLowerInvariant().Contains(lowerKeyword))
                    group.Assets.Add(assets[i]);
            }

            _groups.Add(group);
            Debug.Log($"<color=cyan>[Asset Group] Added '{_kwGroupName}': {group.Assets.Count} assets matched '{_kwKeyword}'</color>");
        }

        [PropertySpace(SpaceBefore = 2)]
        [TabGroup(Tab, "Keyword")]
        [Button("Clear All Groups", ButtonSizes.Small)]
        [GUIColor(1f, 0.5f, 0.5f)]
        [PropertyOrder(13)]
        private void ClearKeywordGroupsButton()
        {
            _groups.Clear();
            _foldouts.Clear();
        }

        // ───────────────────────────── Groups Display ───────────────────────────────

        [PropertyOrder(100)]
        [OnInspectorGUI]
        private void DrawGroupsSection()
        {
            if (_groups.Count == 0) return;

            GUILayout.Space(12);
            EditorGUILayout.LabelField($"Groups ({_groups.Count})", EditorStyles.boldLabel);
            GUILayout.Space(4);

            for (int i = 0; i < _groups.Count; i++)
                DrawSingleGroup(_groups[i], i);

            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();

            Color originalBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
            if (GUILayout.Button("Apply All", GUILayout.Height(26)))
                ApplyAllGroupsToTargetAssets();

            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button("Clear Groups", GUILayout.Height(26)))
            {
                _groups.Clear();
                _foldouts.Clear();
            }

            GUI.backgroundColor = originalBg;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSingleGroup(AssetGroup group, int index)
        {
            bool expanded;
            _foldouts.TryGetValue(index, out expanded);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            string label = group.Assets.Count == 1
                ? $"{group.GroupName} (1 asset)"
                : $"{group.GroupName} ({group.Assets.Count} assets)";

            expanded = EditorGUILayout.Foldout(expanded, label, true);
            _foldouts[index] = expanded;

            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 1f, 0.5f);
            if (GUILayout.Button("Apply", GUILayout.Width(55), GUILayout.Height(18)))
                ApplyGroupToTargetAssets(group);
            GUI.backgroundColor = originalBg;

            EditorGUILayout.EndHorizontal();

            if (!expanded) return;

            EditorGUI.indentLevel++;
            for (int j = 0; j < group.Assets.Count; j++)
                EditorGUILayout.ObjectField(group.Assets[j], typeof(Object), true);
            EditorGUI.indentLevel--;
            GUILayout.Space(2);
        }

        private void ApplyGroupToTargetAssets(AssetGroup group)
        {
            FDataContainer data = GetDataContainer();
            data.TargetAssets.Clear();
            data.TargetAssets.AddRange(group.Assets);
            FDataPersistenceService.SaveData(data);
            Debug.Log($"<color=cyan>[Asset Group] Applied '{group.GroupName}' → {group.Assets.Count} assets to TargetAssets</color>");
        }

        private void ApplyAllGroupsToTargetAssets()
        {
            FDataContainer data = GetDataContainer();
            data.TargetAssets.Clear();
            for (int i = 0; i < _groups.Count; i++)
                data.TargetAssets.AddRange(_groups[i].Assets);
            FDataPersistenceService.SaveData(data);
            Debug.Log($"<color=cyan>[Asset Group] Applied all {_groups.Count} groups → {data.TargetAssets.Count} assets to TargetAssets</color>");
        }

        // ───────────────────────────── Tokenization ─────────────────────────────────

        private List<string> TokenizeName(string name)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrEmpty(name)) return tokens;

            s_Builder.Length = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' || c == '-' || c == '.' || c == ' ')
                {
                    FlushToken(tokens);
                    continue;
                }

                // CamelCase split: split before uppercase when previous is lowercase,
                // or when previous is uppercase and next is lowercase (e.g. "HPRecovery" → "HP"+"Recovery")
                if (_splitCamelCase && i > 0 && char.IsUpper(c))
                {
                    bool prevLower = char.IsLower(name[i - 1]);
                    bool prevUpperNextLower = char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (prevLower || prevUpperNextLower)
                        FlushToken(tokens);
                }

                s_Builder.Append(c);
            }
            FlushToken(tokens);
            return tokens;
        }

        private static void FlushToken(List<string> tokens)
        {
            if (s_Builder.Length == 0) return;
            tokens.Add(s_Builder.ToString());
            s_Builder.Length = 0;
        }

        private static bool IsNumericOnly(string token)
        {
            for (int i = 0; i < token.Length; i++)
            {
                if (!char.IsDigit(token[i])) return false;
            }
            return token.Length > 0;
        }
    }
}
