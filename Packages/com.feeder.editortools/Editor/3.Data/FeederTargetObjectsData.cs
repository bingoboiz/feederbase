using System.Collections.Generic;
using UnityEngine;

namespace Feeder
{
    /// <summary>Persistent data for Feeder tools. Stores TargetPrefabs so refs survive domain reload and Unity restart.</summary>
    public sealed class FeederTargetPrefabsData : ScriptableObject
    {
        [SerializeField]
        private List<GameObject> targetPrefabs = new List<GameObject>();

        /// <summary>Shared list used by all FTargetPrefabsToolBase tools. Persisted in this asset.</summary>
        public List<GameObject> TargetPrefabs
        {
            get
            {
                if (targetPrefabs == null)
                    targetPrefabs = new List<GameObject>();
                return targetPrefabs;
            }
        }

        private void OnValidate()
        {
            if (targetPrefabs == null)
                targetPrefabs = new List<GameObject>();
        }
    }
}
