using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    /// <summary>
    /// Self-contained bidirectional asset dependency index for the Find Reference tool.
    /// Built time-sliced over <see cref="EditorApplication.update"/> so the editor stays responsive,
    /// then persisted to UserSettings so reopening is instant. No FindReference2 dependency.
    /// </summary>
    [FilePath("UserSettings/Feeder/FRefDatabase.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class FRefDatabase : ScriptableSingleton<FRefDatabase>
    {
        [SerializeField] private List<FRefAsset> assets = new List<FRefAsset>();
        [SerializeField] private bool isReady;

        // Rebuilt in memory, never serialized.
        [System.NonSerialized] private Dictionary<string, FRefAsset> map;
        [System.NonSerialized] private bool usedByBuilt;

        // Time-sliced build state.
        [System.NonSerialized] private bool isBuilding;
        [System.NonSerialized] private float progress;
        [System.NonSerialized] private string[] buildPaths;
        [System.NonSerialized] private int buildIndex;

        private const int TimeBudgetMs = 30;

        public bool IsReady => isReady;
        public bool IsBuilding => isBuilding;
        public float Progress => progress;
        public int AssetCount => assets != null ? assets.Count : 0;
        public IReadOnlyList<FRefAsset> AllAssets => assets;

        public FRefAsset Get(string guid)
        {
            EnsureMap();
            return (guid != null && map.TryGetValue(guid, out var a)) ? a : null;
        }

        /// <summary>Ensures the reverse (used-by) map is available, rebuilding it lazily after a domain reload.</summary>
        public void EnsureUsedBy()
        {
            EnsureMap();
            if (!usedByBuilt) BuildUsedBy();
        }

        // ----- Build -----

        public void StartBuild()
        {
            if (isBuilding) return;

            buildPaths = AssetDatabase.GetAllAssetPaths();
            buildIndex = 0;
            assets.Clear();
            map = new Dictionary<string, FRefAsset>(buildPaths.Length);
            isReady = false;
            usedByBuilt = false;
            isBuilding = true;
            progress = 0f;

            EditorApplication.update += BuildTick;
        }

        public void CancelBuild()
        {
            if (!isBuilding) return;
            FinishBuild(false);
        }

        private void BuildTick()
        {
            if (!isBuilding)
            {
                EditorApplication.update -= BuildTick;
                return;
            }

            int n = buildPaths != null ? buildPaths.Length : 0;
            var sw = Stopwatch.StartNew();
            while (buildIndex < n && sw.ElapsedMilliseconds < TimeBudgetMs)
            {
                ProcessPath(buildPaths[buildIndex]);
                buildIndex++;
            }

            progress = n == 0 ? 1f : (float)buildIndex / n;

            bool cancel = EditorUtility.DisplayCancelableProgressBar(
                "Feeder Asset Cache",
                $"Scanning assets {buildIndex}/{n}",
                progress);

            if (cancel) { FinishBuild(false); return; }
            if (buildIndex >= n) FinishBuild(true);
        }

        private void ProcessPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.IsValidFolder(path)) return; // folders handled via expansion in queries

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return;

            var asset = new FRefAsset
            {
                guid = guid,
                path = path,
                type = FRefAssetTypeUtil.FromPath(path),
                useGuids = new List<string>()
            };

            var set = new HashSet<string>();
            FRefGuidExtractor.ExtractGuids(path, set);
            set.Remove(guid); // never reference self
            asset.useGuids.AddRange(set);

            try { asset.fileSize = new FileInfo(path).Length; }
            catch { asset.fileSize = 0; }

            assets.Add(asset);
            map[guid] = asset;
        }

        private void FinishBuild(bool completed)
        {
            EditorApplication.update -= BuildTick;
            isBuilding = false;
            buildPaths = null;
            EditorUtility.ClearProgressBar();

            if (completed)
            {
                BuildUsedBy();
                isReady = true;
                progress = 1f;
                Save(true);
            }

            foreach (var w in Resources.FindObjectsOfTypeAll<FFindReferenceWindow>())
                w.Repaint();
        }

        // ----- In-memory maps -----

        private void EnsureMap()
        {
            if (map != null) return;
            map = new Dictionary<string, FRefAsset>(assets.Count);
            foreach (var a in assets)
                if (!string.IsNullOrEmpty(a.guid)) map[a.guid] = a;
        }

        private void BuildUsedBy()
        {
            EnsureMap();
            foreach (var a in assets) a.usedBy = null;

            foreach (var a in assets)
            {
                foreach (string g in a.useGuids)
                {
                    if (!map.TryGetValue(g, out var target)) continue;
                    if (target.usedBy == null) target.usedBy = new Dictionary<string, FRefAsset>();
                    target.usedBy[a.guid] = a;
                }
            }
            usedByBuilt = true;
        }
    }
}
