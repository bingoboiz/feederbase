using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    [FilePath("UserSettings/Feeder/FBuildEstimateCache.asset",
              FilePathAttribute.Location.ProjectFolder)]
    public sealed class FBuildEstimateCache : ScriptableSingleton<FBuildEstimateCache>
    {
        [SerializeField] public List<FBuildEstimateRow> rows = new List<FBuildEstimateRow>();
        [SerializeField] public bool hasData;
        [SerializeField] public int sceneCount;
        [SerializeField] public long totalBefore;
        [SerializeField] public long totalEstimate;

        public bool HasData => hasData || (rows != null && rows.Count > 0);

        public void SaveCache()
        {
            if (rows == null)
                rows = new List<FBuildEstimateRow>();

            hasData = true;
            Save(true);
        }
    }

    [Serializable]
    public class FBuildEstimateRow
    {
        public string path;
        public string fileName;
        public FRefAssetType type;
        public long sizeBefore;
        public long sizeEstimate;
        public double percent;
    }
}
