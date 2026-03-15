using UnityEngine;

namespace Feeder
{
    public readonly struct PrefabVariantCreatorConfig
    {
        public GameObject BasePrefab { get; }
        public Transform LocateModel { get; }
        public string SaveFolderPath { get; }

        public PrefabVariantCreatorConfig(
            GameObject basePrefab,
            Transform locateModel,
        string saveFolderPath)
        {
            BasePrefab = basePrefab;
            LocateModel = locateModel;
            SaveFolderPath = saveFolderPath;
        }
    }
}
