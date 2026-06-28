using System;
using System.Collections.Generic;

namespace Feeder
{
    /// <summary>
    /// One node in the Find Reference dependency graph. Self-contained, no FindReference2 dependency.
    /// Forward dependencies (<see cref="useGuids"/>) are serialized; the reverse map
    /// (<see cref="usedBy"/>) is rebuilt in memory after load.
    /// </summary>
    [Serializable]
    public class FRefAsset
    {
        public string guid;
        public string path;
        public FRefAssetType type;
        public long fileSize;

        /// <summary>GUIDs of assets this asset references (direct dependencies).</summary>
        public List<string> useGuids = new List<string>();

        /// <summary>GUID -> asset of everything that references this asset. Rebuilt, never serialized.</summary>
        [NonSerialized] public Dictionary<string, FRefAsset> usedBy;
    }
}
