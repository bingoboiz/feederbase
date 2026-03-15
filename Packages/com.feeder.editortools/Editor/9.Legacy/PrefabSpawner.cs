using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder {
    public class PrefabSpawner : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Spawner")]
        public static void OpenWindow()
        {
            var window = GetWindow<PrefabSpawner>();
            window.titleContent = new GUIContent("Prefab Spawner");
        }

        [Title("Prefab to Spawn")]
        public GameObject prefab;

        [Title("Spawn Points")]
        public List<Transform> spawnPoints = new List<Transform>();

        [Button("Spawn Prefabs")]
        public void SpawnPrefabs()
        {
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                Debug.LogWarning("Please assign a valid prefab from the Project window.");
                return;
            }

            if (spawnPoints.Count == 0)
            {
                Debug.LogWarning("No spawn points assigned!");
                return;
            }

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] == null) continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (instance == null)
                {
                    Debug.LogError("Failed to instantiate prefab.");
                    return;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Spawn Prefab");

                instance.transform.SetParent(spawnPoints[i].parent, false);

                instance.transform.SetPositionAndRotation(spawnPoints[i].position, spawnPoints[i].rotation);

                instance.name = prefab.name + "_" + (i + 1);

                Undo.DestroyObjectImmediate(spawnPoints[i].gameObject);
            }

            spawnPoints.Clear();
        }
    }

}
