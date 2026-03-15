using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public static class FDataPersistenceService
    {
        /// <summary>Gets the single persisted container (stored in UserSettings/Feeder).</summary>
        public static FDataContainer GetOrCreateDataContainer() => FDataContainer.instance;

        /// <summary>Persists container to disk so refs survive tool close and Unity restart.</summary>
        public static void SaveData(FDataContainer data)
        {
            if (data == null)
                return;
            data.SaveData();
        }
    }
}
