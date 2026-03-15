using System;
using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using Sirenix.Serialization;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FUnpackEviromentTool : FTargetObjectsToolBase
    {
        private const string CommonFolderName = "_Common";
        private const string ModelsFolderName = "Models";
        private const string MaterialsFolderName = "Materials";
        private const string TexturesFolderName = "Textures";

        protected override string GetDescription()
        {
            return "Unpack environment: extract meshes, materials and textures from MeshRenderers into per-target folders; shared assets go to Common.";
        }

        [Title("Settings")]
        [LabelText("Save Folder")]
        [FolderPath(AbsolutePath = false, RequireExistingPath = true)]
        [ShowInInspector, OdinSerialize]
        private string saveFolderPath;

        [LabelText("Skip TextMeshPro")]
        [ShowInInspector, OdinSerialize]
        private bool skipTextMeshPro = true;

        [LabelText("Skip Disabled GameObjects")]
        [ShowInInspector, OdinSerialize]
        private bool skipDisabledGameObjects = true;

        [Button(ButtonSizes.Large), GUIColor(0.3f, 0.8f, 1f)]
        public void Unpack()
        {
            ValidateInput();

            Dictionary<Mesh, HashSet<string>> meshToTargetNames = new Dictionary<Mesh, HashSet<string>>();
            Dictionary<Material, HashSet<string>> materialToTargetNames = new Dictionary<Material, HashSet<string>>();
            Dictionary<Texture, HashSet<string>> textureToTargetNames = new Dictionary<Texture, HashSet<string>>();
            List<MeshFilterEntry> entries = CollectEntries(meshToTargetNames, materialToTargetNames, textureToTargetNames);

            EnsureFolderStructure(meshToTargetNames, materialToTargetNames, textureToTargetNames);

            Dictionary<Mesh, Mesh> meshMap = new Dictionary<Mesh, Mesh>();
            Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();
            Dictionary<Texture, Texture> textureMap;
            Dictionary<string, Texture> texturePathToSource = new Dictionary<string, Texture>();

            // Phase 1: create all texture assets first (no material dependency).
            AssetDatabase.StartAssetEditing();
            try
            {
                textureMap = CreateAllTextureAssets(textureToTargetNames, texturePathToSource);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            LoadTexturesAfterRefresh(textureMap, texturePathToSource);

            // Phase 2: create all material assets and assign only the new textures.
            AssetDatabase.StartAssetEditing();
            try
            {
                CreateAllMaterialAssets(materialToTargetNames, textureMap, materialMap);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // Phase 3: create meshes and assign new materials to TargetObjects (no reference to old assets).
            HashSet<GameObject> processedObjects = new HashSet<GameObject>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (MeshFilterEntry entry in entries)
                {
                    if (!processedObjects.Add(entry.GameObject))
                        continue;

                    string targetName = entry.TargetRootName;
                    Mesh newMesh = GetOrCreateMesh(entry.SourceMesh, entry.GameObject, targetName, meshToTargetNames, meshMap);
                    Material[] newMaterials = GetNewMaterialsForEntry(entry.SourceMaterials, materialMap);

                    Undo.RecordObject(entry.MeshFilter, "Unpack Environment Mesh");
                    Undo.RecordObject(entry.MeshRenderer, "Unpack Environment Materials");

                    entry.MeshFilter.sharedMesh = newMesh;
                    entry.MeshRenderer.sharedMaterials = newMaterials;

                    if (PrefabUtility.IsPartOfPrefabInstance(entry.GameObject))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(entry.MeshFilter);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(entry.MeshRenderer);
                    }

                    EditorUtility.SetDirty(entry.MeshFilter);
                    EditorUtility.SetDirty(entry.MeshRenderer);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"<color=green>Unpacked {entries.Count} mesh object(s).</color>");
        }

        private struct MeshFilterEntry
        {
            public GameObject GameObject;
            public string TargetRootName;
            public MeshFilter MeshFilter;
            public MeshRenderer MeshRenderer;
            public Mesh SourceMesh;
            public Material[] SourceMaterials;
        }

        private List<MeshFilterEntry> CollectEntries(
            Dictionary<Mesh, HashSet<string>> meshToTargetNames,
            Dictionary<Material, HashSet<string>> materialToTargetNames,
            Dictionary<Texture, HashSet<string>> textureToTargetNames)
        {
            List<MeshFilterEntry> entries = new List<MeshFilterEntry>();

            for (int i = 0; i < TargetObjects.Count; i++)
            {
                GameObject target = TargetObjects[i];
                if (target == null)
                {
                    Debug.LogWarning($"[FUnpackEviromentTool] Skipping null at TargetObjects[{i}].");
                    continue;
                }

                if (PrefabUtility.IsPartOfPrefabAsset(target))
                    throw new InvalidOperationException($"prefab asset is not supported: {target.name}");

                string targetName = ValidateAssetName(target.name);
                MeshFilter[] meshFilters = target.GetComponentsInChildren<MeshFilter>(true);

                foreach (MeshFilter meshFilter in meshFilters)
                {
                    GameObject child = meshFilter.gameObject;
                    if (skipDisabledGameObjects && !child.activeInHierarchy)
                        continue;
                    if (skipTextMeshPro && HasTextMeshProComponent(child))
                        continue;

                    MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
                    if (meshRenderer == null)
                        continue;

                    Mesh sourceMesh = meshFilter.sharedMesh;
                    if (sourceMesh == null)
                        continue;

                    Material[] sourceMaterials = meshRenderer.sharedMaterials;
                    if (sourceMaterials == null || sourceMaterials.Length == 0)
                        continue;

                    AddToSet(meshToTargetNames, sourceMesh, targetName);
                    foreach (Material mat in sourceMaterials)
                    {
                        if (mat == null)
                            continue;
                        AddToSet(materialToTargetNames, mat, targetName);
                        CollectTexturesFromMaterial(mat, textureToTargetNames, targetName);
                    }

                    entries.Add(new MeshFilterEntry
                    {
                        GameObject = child,
                        TargetRootName = targetName,
                        MeshFilter = meshFilter,
                        MeshRenderer = meshRenderer,
                        SourceMesh = sourceMesh,
                        SourceMaterials = sourceMaterials
                    });
                }
            }

            return entries;
        }

        private static void CollectTexturesFromMaterial(Material material, Dictionary<Texture, HashSet<string>> textureToTargetNames, string targetName)
        {
            string[] propNames = material.GetTexturePropertyNames();
            if (propNames == null)
                return;
            foreach (string propName in propNames)
            {
                Texture tex = material.GetTexture(propName);
                if (tex == null)
                    continue;
                AddToSet(textureToTargetNames, tex, targetName);
            }
        }

        private static void AddToSet<T>(Dictionary<T, HashSet<string>> map, T key, string targetName) where T : UnityEngine.Object
        {
            if (key == null)
                return;
            if (!map.TryGetValue(key, out HashSet<string> set))
            {
                set = new HashSet<string>();
                map.Add(key, set);
            }
            set.Add(targetName);
        }

        private void EnsureFolderStructure(
            Dictionary<Mesh, HashSet<string>> meshToTargetNames,
            Dictionary<Material, HashSet<string>> materialToTargetNames,
            Dictionary<Texture, HashSet<string>> textureToTargetNames)
        {
            HashSet<string> allTargetNames = new HashSet<string>();
            foreach (HashSet<string> set in meshToTargetNames.Values)
                foreach (string n in set)
                    allTargetNames.Add(n);

            EnsureFolder(saveFolderPath, CommonFolderName);
            string commonPath = $"{saveFolderPath}/{CommonFolderName}";
            EnsureFolder(commonPath, ModelsFolderName);
            EnsureFolder(commonPath, MaterialsFolderName);
            EnsureFolder(commonPath, TexturesFolderName);

            foreach (string targetName in allTargetNames)
            {
                string targetPath = $"{saveFolderPath}/{targetName}";
                EnsureFolder(saveFolderPath, targetName);
                EnsureFolder(targetPath, ModelsFolderName);
                EnsureFolder(targetPath, MaterialsFolderName);
                EnsureFolder(targetPath, TexturesFolderName);
            }
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            string childPath = $"{parentPath}/{folderName}";
            if (AssetDatabase.IsValidFolder(childPath))
                return;
            string guid = AssetDatabase.CreateFolder(parentPath, folderName);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException($"failed to create folder: {childPath}");
        }

        private string GetAssetFolder(string assetTargetName, HashSet<string> targetNames, string subFolder)
        {
            bool isCommon = targetNames.Count > 1;
            if (isCommon)
                return $"{saveFolderPath}/{CommonFolderName}/{subFolder}";
            return $"{saveFolderPath}/{assetTargetName}/{subFolder}";
        }

        private Mesh GetOrCreateMesh(Mesh sourceMesh, GameObject contextGo, string targetName,
            Dictionary<Mesh, HashSet<string>> meshToTargetNames, Dictionary<Mesh, Mesh> meshMap)
        {
            if (meshMap.TryGetValue(sourceMesh, out Mesh cached))
                return cached;

            HashSet<string> targetNames = meshToTargetNames[sourceMesh];
            string firstTarget = GetFirstTargetName(targetNames);
            string folder = GetAssetFolder(firstTarget, targetNames, ModelsFolderName);
            string assetName = BuildAssetName(contextGo, sourceMesh.name, "Mesh");
            Mesh meshCopy = CreateStrippedMesh(sourceMesh);
            meshCopy.name = assetName;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{assetName}.asset");
            AssetDatabase.CreateAsset(meshCopy, assetPath);
            meshMap.Add(sourceMesh, meshCopy);
            return meshCopy;
        }

        private Dictionary<Texture, Texture> CreateAllTextureAssets(
            Dictionary<Texture, HashSet<string>> textureToTargetNames,
            Dictionary<string, Texture> texturePathToSource)
        {
            Dictionary<Texture, Texture> textureMap = new Dictionary<Texture, Texture>();

            foreach (Texture sourceTexture in textureToTargetNames.Keys)
            {
                if (!(sourceTexture is Texture2D sourceTex2D))
                {
                    Debug.LogWarning($"[FUnpackEviromentTool] Skipping non-Texture2D: {sourceTexture.name}");
                    continue;
                }

                HashSet<string> targetNames = textureToTargetNames[sourceTexture];
                string firstTarget = GetFirstTargetName(targetNames);
                string folder = GetAssetFolder(firstTarget, targetNames, TexturesFolderName);
                string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(sourceTex2D.name) ? "Texture" : sourceTex2D.name);

                (Texture2D newTex, string assetPath) = DuplicateTexture2DToAsset(sourceTex2D, folder, assetName);
                if (assetPath == null)
                    continue;

                if (newTex != null)
                    textureMap.Add(sourceTexture, newTex);
                else
                    texturePathToSource.Add(assetPath, sourceTexture);
            }

            return textureMap;
        }

        private static void LoadTexturesAfterRefresh(Dictionary<Texture, Texture> textureMap, Dictionary<string, Texture> texturePathToSource)
        {
            foreach (KeyValuePair<string, Texture> kv in texturePathToSource)
            {
                Texture2D loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(kv.Key);
                if (loaded == null)
                {
                    Debug.LogWarning($"[FUnpackEviromentTool] Failed to load texture after refresh: {kv.Key}");
                    continue;
                }
                textureMap.Add(kv.Value, loaded);
            }
        }

        private void CreateAllMaterialAssets(
            Dictionary<Material, HashSet<string>> materialToTargetNames,
            Dictionary<Texture, Texture> textureMap,
            Dictionary<Material, Material> materialMap)
        {
            foreach (Material sourceMaterial in materialToTargetNames.Keys)
            {
                HashSet<string> targetNames = materialToTargetNames[sourceMaterial];
                string firstTarget = GetFirstTargetName(targetNames);
                string folder = GetAssetFolder(firstTarget, targetNames, MaterialsFolderName);
                string assetName = BuildAssetNameForMaterial(sourceMaterial.name, firstTarget);

                Material newMat = new Material(sourceMaterial);
                newMat.name = assetName;

                string[] texPropNames = sourceMaterial.GetTexturePropertyNames();
                if (texPropNames != null)
                {
                    foreach (string propName in texPropNames)
                    {
                        Texture srcTex = sourceMaterial.GetTexture(propName);
                        if (srcTex == null)
                            continue;
                        if (textureMap.TryGetValue(srcTex, out Texture newTex) && newTex != null)
                            newMat.SetTexture(propName, newTex);
                    }
                }

                string matPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{assetName}.mat");
                AssetDatabase.CreateAsset(newMat, matPath);
                materialMap.Add(sourceMaterial, newMat);
            }
        }

        private static Material[] GetNewMaterialsForEntry(Material[] sourceMaterials, Dictionary<Material, Material> materialMap)
        {
            Material[] result = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                Material src = sourceMaterials[i];
                result[i] = src != null && materialMap.TryGetValue(src, out Material newMat) ? newMat : null;
            }
            return result;
        }

        // when CopyAsset is used, texture is null until Refresh; path is always set.
        private static (Texture2D texture, string assetPath) DuplicateTexture2DToAsset(Texture2D source, string folderPath, string assetName)
        {
            string extension = ".png";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{assetName}{extension}");

            string sourcePath = AssetDatabase.GetAssetPath(source);
            bool isMainAsset = !string.IsNullOrEmpty(sourcePath) && AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) == source;

            if (isMainAsset && !string.IsNullOrEmpty(sourcePath))
            {
                bool copied = AssetDatabase.CopyAsset(sourcePath, assetPath);
                if (copied)
                    return (null, assetPath);
            }

            Texture2D duplicated = CopyTexture2DViaRenderTexture(source);
            if (duplicated == null)
                return (null, null);
            duplicated.name = assetName;
            AssetDatabase.CreateAsset(duplicated, assetPath);
            return (duplicated, assetPath);
        }

        private static Texture2D CopyTexture2DViaRenderTexture(Texture2D source)
        {
            int w = source.width;
            int h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private static string GetFirstTargetName(HashSet<string> targetNames)
        {
            foreach (string n in targetNames)
                return n;
            throw new InvalidOperationException("target names set is empty.");
        }

        private static Mesh CreateStrippedMesh(Mesh source)
        {
            if (source == null)
                throw new InvalidOperationException("source mesh is null.");

            Mesh mesh = new Mesh
            {
                name = source.name,
                vertices = source.vertices,
                normals = source.normals,
                uv = source.uv,
                subMeshCount = source.subMeshCount
            };

            for (int i = 0; i < source.subMeshCount; i++)
                mesh.SetTriangles(source.GetTriangles(i), i);

            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool HasTextMeshProComponent(GameObject go)
        {
            if (go == null)
                return false;
            return go.GetComponent<TextMeshProUGUI>() != null || go.GetComponent<TextMeshPro>() != null;
        }

        private void ValidateInput()
        {
            if (TargetObjects == null || TargetObjects.Count == 0)
                throw new InvalidOperationException("target objects is empty.");

            if (string.IsNullOrWhiteSpace(saveFolderPath) || !AssetDatabase.IsValidFolder(saveFolderPath))
                throw new InvalidOperationException("save folder path is invalid.");
        }

        private static string BuildAssetName(GameObject target, string sourceName, string suffix)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                throw new InvalidOperationException("source name is invalid.");

            string targetName = ValidateAssetName(target != null ? target.name : "Unknown");
            string baseName = ValidateAssetName(sourceName);
            string endName = ValidateAssetName(suffix);
            return $"{targetName}_{baseName}_{endName}";
        }

        private static string BuildAssetNameForMaterial(string sourceName, string contextName)
        {
            string baseName = ValidateAssetName(string.IsNullOrWhiteSpace(sourceName) ? "Material" : sourceName);
            string ctx = ValidateAssetName(contextName);
            return $"{ctx}_{baseName}";
        }

        private static string BuildAssetNameForTexture(string sourceName, string materialName, string propertyName)
        {
            string baseName = ValidateAssetName(string.IsNullOrWhiteSpace(sourceName) ? "Texture" : sourceName);
            string prop = ValidateAssetName(propertyName);
            return $"{baseName}_{prop}";
        }

        private static string ValidateAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("asset name is empty.");

            foreach (char ch in Path.GetInvalidFileNameChars())
            {
                if (name.IndexOf(ch) >= 0)
                    throw new InvalidOperationException($"asset name contains invalid character: {ch}");
            }

            return name;
        }
    }
}
