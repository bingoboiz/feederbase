using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Feeder
{
    /// <summary>A reference to a target asset found inside an open scene.</summary>
    public struct FSceneRefResult
    {
        public Object obj;       // GameObject or Component using the target
        public string scenePath; // scene the object lives in
        public string targetPath; // asset being referenced
        public string propertyPath; // serialized property holding the reference
    }

    /// <summary>Extra Find Reference modes: duplicate detection, unused assets, and scene references.</summary>
    public static class FRefScanner
    {
        // ----- Duplicate -----

        /// <summary>Groups of assets with byte-identical content (size pre-filter, then MD5).</summary>
        public static List<List<FRefAsset>> FindDuplicates()
        {
            var result = new List<List<FRefAsset>>();
            var db = FRefDatabase.instance;
            if (!db.IsReady) return result;

            var bySize = new Dictionary<long, List<FRefAsset>>();
            foreach (var a in db.AllAssets)
            {
                if (a.fileSize <= 0) continue;
                if (a.type == FRefAssetType.Folder || a.type == FRefAssetType.Script) continue;
                if (!bySize.TryGetValue(a.fileSize, out var l)) bySize[a.fileSize] = l = new List<FRefAsset>();
                l.Add(a);
            }

            foreach (var kv in bySize)
            {
                if (kv.Value.Count < 2) continue;

                var byHash = new Dictionary<string, List<FRefAsset>>();
                foreach (var a in kv.Value)
                {
                    string h = ComputeHash(a.path);
                    if (h == null) continue;
                    if (!byHash.TryGetValue(h, out var l)) byHash[h] = l = new List<FRefAsset>();
                    l.Add(a);
                }

                foreach (var hg in byHash.Values)
                    if (hg.Count > 1) result.Add(hg);
            }

            return result;
        }

        private static string ComputeHash(string path)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return System.Convert.ToBase64String(md5.ComputeHash(fs));
                }
            }
            catch
            {
                return null;
            }
        }

        // ----- Unused -----

        /// <summary>
        /// Assets that nothing in the project references. Heuristic: skips folders, scenes
        /// (build roots), scripts (referenced by type, not GUID), and assets under Resources/ or
        /// Editor/ (loaded dynamically / editor-only). Tweak as needed for your project.
        /// </summary>
        public static List<FRefAsset> FindUnused()
        {
            var result = new List<FRefAsset>();
            var db = FRefDatabase.instance;
            if (!db.IsReady) return result;
            db.EnsureUsedBy();

            foreach (var a in db.AllAssets)
            {
                if (a.type == FRefAssetType.Folder) continue;
                if (a.type == FRefAssetType.Scene) continue;
                if (a.type == FRefAssetType.Script) continue;
                if (a.path.Contains("/Resources/")) continue;
                if (a.path.Contains("/Editor/")) continue;
                if (a.usedBy == null || a.usedBy.Count == 0) result.Add(a);
            }

            return result;
        }

        // ----- Scene -----

        /// <summary>
        /// Finds GameObjects/Components in currently open scene(s) that reference any target asset,
        /// by walking serialized object-reference properties.
        /// </summary>
        public static List<FSceneRefResult> FindInScenes(IEnumerable<string> targetGuids)
        {
            var result = new List<FSceneRefResult>();
            var targets = new HashSet<string>(targetGuids);
            if (targets.Count == 0) return result;
            var seen = new HashSet<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = t.gameObject;
                        CheckObject(go, scene.path, targets, result, seen, true);
                        foreach (var comp in go.GetComponents<Component>())
                        {
                            if (comp == null) continue; // missing script
                            CheckObject(comp, scene.path, targets, result, seen, true);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Finds asset references held by currently open scene(s), without filtering by TargetAssets.</summary>
        public static List<FSceneRefResult> FindAssetsUsedByOpenScenes()
        {
            var result = new List<FSceneRefResult>();
            var seen = new HashSet<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = t.gameObject;
                        CheckObject(go, scene.path, null, result, seen, false);
                        foreach (var comp in go.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            CheckObject(comp, scene.path, null, result, seen, false);
                        }
                    }
                }
            }

            return result;
        }

        private static void CheckObject(Object inspect, string scenePath,
            HashSet<string> targets, List<FSceneRefResult> result, HashSet<string> seen, bool filterTargets)
        {
            var so = new SerializedObject(inspect);
            SerializedProperty p = so.GetIterator();

            while (p.NextVisible(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!filterTargets && p.propertyPath == "m_Script") continue;
                Object value = p.objectReferenceValue;
                if (value == null) continue;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long _)) continue;
                if (filterTargets && (targets == null || !targets.Contains(guid))) continue;

                string targetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(targetPath)) continue;

                string key = $"{scenePath}|{inspect.GetInstanceID()}|{p.propertyPath}|{targetPath}";
                if (!seen.Add(key)) continue;

                result.Add(new FSceneRefResult
                {
                    obj = inspect,
                    scenePath = scenePath,
                    targetPath = targetPath,
                    propertyPath = p.propertyPath
                });
            }

            so.Dispose();
        }
    }
}
