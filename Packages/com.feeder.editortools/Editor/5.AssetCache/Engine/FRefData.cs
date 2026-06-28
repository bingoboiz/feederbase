using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder
{
    /// <summary>
    /// Shared target helpers for Find Reference. The actual list is Feeder's common
    /// FDataContainer.TargetAssets, so this tool stays in sync with the main Feeder menu window.
    /// </summary>
    [FilePath("UserSettings/Feeder/FRefData.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class FRefData : ScriptableSingleton<FRefData>
    {
        // Keep this Unity-native shell so legacy serialized FRefData assets/layout state remain loadable.
        public static List<Object> Targets => Data.TargetAssets;

        private static FDataContainer Data => FDataPersistenceService.GetOrCreateDataContainer();

        public static void SaveData()
        {
            var data = Data;
            data.SyncAllFromAssets();
            FDataPersistenceService.SaveData(data);
        }

        /// <summary>Distinct asset GUIDs of all non-null targets (folders included).</summary>
        public static string[] GetTargetGuids()
        {
            var result = new List<string>();
            foreach (var o in Targets)
            {
                if (o == null) continue;
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid)) result.Add(guid);
            }
            return result.Distinct().ToArray();
        }

        public static bool IsAssetTarget(Object obj)
        {
            if (obj == null) return false;
            if (!AssetDatabase.Contains(obj)) return false;
            return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(obj));
        }

        public static bool Contains(Object obj)
        {
            if (obj == null) return false;
            return Targets.Contains(obj);
        }

        public static int AddAssets(IEnumerable<Object> objects, bool replace)
        {
            if (objects == null) return 0;

            var targets = Targets;
            if (replace) targets.Clear();

            int added = 0;
            foreach (var obj in objects)
            {
                if (!IsAssetTarget(obj)) continue;
                if (targets.Contains(obj)) continue;
                targets.Add(obj);
                added++;
            }

            if (added > 0 || replace)
                SaveData();

            return added;
        }
    }
}
