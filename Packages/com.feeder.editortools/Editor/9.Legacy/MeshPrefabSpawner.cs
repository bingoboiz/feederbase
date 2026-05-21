using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder {
    public class MeshPrefabSpawner : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Mesh Prefab Spawner")]
        public static void OpenWindow()
        {
            var window = GetWindow<MeshPrefabSpawner>();
            window.titleContent = new GUIContent("Mesh Prefab Spawner");
        }

        [Title("Search Settings")]
        public Mesh targetMesh;

        [Title("Prefab to Spawn")]
        public GameObject prefab;

        [Title("Found Objects (read-only)")]
        [ReadOnly]
        public List<GameObject> foundObjects = new List<GameObject>();

        [Button("Find MeshRenderers Using Mesh")]
        public void FindMeshRenderers()
        {
            foundObjects.Clear();

            if (targetMesh == null)
            {
                Debug.LogWarning("Please assign a target mesh.");
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        MeshFilter mf = mr.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh == targetMesh)
                            foundObjects.Add(mr.gameObject);
                    }
                }
            }

            if (foundObjects.Count == 0)
                Debug.LogWarning($"No MeshRenderer found using mesh '{targetMesh.name}' in loaded scenes.");
            else
                Debug.Log($"Found {foundObjects.Count} object(s) using mesh '{targetMesh.name}'.");
        }

        [Button("Spawn Prefabs at Found Objects")]
        public void SpawnPrefabs()
        {
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                Debug.LogWarning("Please assign a valid prefab from the Project window.");
                return;
            }

            if (foundObjects.Count == 0)
            {
                Debug.LogWarning("No found objects. Run 'Find MeshRenderers Using Mesh' first.");
                return;
            }

            int spawned = 0;
            for (int i = 0; i < foundObjects.Count; i++)
            {
                GameObject target = foundObjects[i];
                if (target == null) continue;

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target.scene);
                if (instance == null)
                {
                    Debug.LogError("Failed to instantiate prefab.");
                    return;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Spawn Prefab at Mesh");

                instance.transform.SetParent(target.transform.parent, false);
                instance.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
                instance.name = prefab.name + "_" + (i + 1);

                Undo.DestroyObjectImmediate(target);
                spawned++;
            }

            Debug.Log($"Spawned {spawned} prefab(s).");
            foundObjects.Clear();
        }
    }
}
