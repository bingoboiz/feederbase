using System.Collections.Generic;
using System.IO;
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
    [InitializeOnLoad]
    public static class FRefData
    {
        private const string LegacyAssetPath = "UserSettings/Feeder/FRefData.asset";

        static FRefData()
        {
            RemoveLegacyAsset();
        }

        public static List<Object> Targets => Data.TargetAssets;

        private static FDataContainer Data => FDataPersistenceService.GetOrCreateDataContainer();

        private static void RemoveLegacyAsset()
        {
            try
            {
                if (!File.Exists(LegacyAssetPath))
                    return;

                File.Delete(LegacyAssetPath);

                string metaPath = LegacyAssetPath + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Could not remove legacy Find Reference data asset at {LegacyAssetPath}: {ex.Message}");
            }
        }

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
