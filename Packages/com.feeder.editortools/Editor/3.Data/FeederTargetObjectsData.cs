using System.Collections.Generic;
using UnityEngine;

namespace Feeder
{
    /// <summary>Persistent data for Feeder tools. Stores TargetObjects so refs survive domain reload and Unity restart.</summary>
    public sealed class FeederTargetObjectsData : ScriptableObject
    {
        [SerializeField]
        private List<GameObject> targetObjects = new List<GameObject>();

        /// <summary>Shared list used by all FTargetObjectsToolBase tools. Persisted in this asset.</summary>
        public List<GameObject> TargetObjects
        {
            get
            {
                if (targetObjects == null)
                    targetObjects = new List<GameObject>();
                return targetObjects;
            }
        }

        private void OnValidate()
        {
            if (targetObjects == null)
                targetObjects = new List<GameObject>();
        }
    }
}
