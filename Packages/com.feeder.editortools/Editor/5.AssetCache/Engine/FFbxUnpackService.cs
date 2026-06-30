using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Feeder
{
    public static class FFbxUnpackService
    {
        private const string ModelsFolderName = "Models";
        private const string AnimationsFolderName = "Animations";
        private const string MaterialsFolderName = "Materials";
        private const string TexturesFolderName = "Textures";
        private const string AvatarFolderName = "Avatar";
        private const string PrefabsFolderName = "Prefabs";

        private static readonly HashSet<string> SupportedReferenceExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".prefab", ".unity", ".controller", ".overridecontroller", ".mat", ".asset", ".anim", ".playable"
            };

        public static FFbxUnpackPlan BuildPreview(Object sourceFbx, string saveFolderPath)
        {
            ValidateFbxInput(sourceFbx, saveFolderPath, out string sourcePath, out string sourceGuid);

            var plan = new FFbxUnpackPlan
            {
                SourceFbxPath = sourcePath,
                SourceFbxGuid = sourceGuid,
                SaveFolderPath = saveFolderPath,
                RootOutputFolderPath = $"{saveFolderPath}/{ValidateAssetName(Path.GetFileNameWithoutExtension(sourcePath))}"
            };

            if (AssetDatabase.IsValidFolder(plan.RootOutputFolderPath))
                plan.Warnings.Add(
                    $"Output folder already exists ({plan.RootOutputFolderPath}); re-applying can create duplicate assets with fresh GUIDs.");

            LoadSourceSubAssets(plan);
            CollectCandidatePaths(plan);
            ScanReferenceHits(plan);
            return plan;
        }

        /// <summary>Builds one preview plan per FBX. Invalid entries and duplicates are skipped.</summary>
        public static List<FFbxUnpackPlan> BuildPreviewBatch(IEnumerable<Object> fbxList, string saveFolderPath)
        {
            var plans = new List<FFbxUnpackPlan>();
            if (fbxList == null) return plans;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Object fbx in fbxList)
            {
                if (fbx == null) continue;
                string path = AssetDatabase.GetAssetPath(fbx);
                if (string.IsNullOrEmpty(path) ||
                    !Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seen.Add(path)) continue;

                try
                {
                    plans.Add(BuildPreview(fbx, saveFolderPath));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FBX Unpack] Preview failed for {path}: {ex.Message}");
                }
            }

            return plans;
        }

        /// <summary>Applies a batch of preview plans sequentially and aggregates the result.</summary>
        public static FFbxUnpackBatchResult ApplyBatch(IList<FFbxUnpackPlan> plans)
        {
            var batch = new FFbxUnpackBatchResult();
            if (plans == null) return batch;

            try
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    FFbxUnpackPlan plan = plans[i];
                    if (plan == null || !plan.HasPreview) continue;

                    EditorUtility.DisplayProgressBar(
                        "FBX Unpack",
                        $"Unpacking {plan.SourceFbxName} ({i + 1}/{plans.Count})",
                        plans.Count == 0 ? 1f : (float)i / plans.Count);

                    try
                    {
                        batch.Results.Add(Apply(plan));
                    }
                    catch (Exception ex)
                    {
                        var failed = new FFbxUnpackApplyResult { SourceFbxPath = plan.SourceFbxPath, Aborted = true };
                        failed.Errors.Add(ex.Message);
                        failed.Logs.Add($"Apply threw for {plan.SourceFbxName}: {ex.Message}");
                        batch.Results.Add(failed);
                        Debug.LogException(ex);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return batch;
        }

        public static FFbxUnpackApplyResult Apply(FFbxUnpackPlan plan)
        {
            if (plan == null || !plan.HasPreview)
                throw new InvalidOperationException("Preview plan is empty. Run Preview first.");

            Object sourceFbx = AssetDatabase.LoadAssetAtPath<Object>(plan.SourceFbxPath);
            ValidateFbxInput(sourceFbx, plan.SaveFolderPath, out string sourcePath, out string sourceGuid);
            if (!string.Equals(sourcePath, plan.SourceFbxPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(sourceGuid, plan.SourceFbxGuid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Source FBX changed after preview. Run Preview again.");

            var result = new FFbxUnpackApplyResult { SourceFbxPath = plan.SourceFbxPath };
            try
            {
                EnsureOutputFolders(plan);
                ExtractAllAssets(plan, result);

                // Transactional guarantee: every generated asset must replace the original. If any sub-asset
                // failed to extract, roll back the partial output and never remap against an incomplete map.
                if (result.Errors.Count > 0)
                {
                    RollbackExtraction(plan, result);
                    result.Aborted = true;
                    result.Logs.Add(
                        $"Aborted: {result.Errors.Count} sub-asset(s) failed to extract. No references were changed and partial output was removed.");
                    return result;
                }

                string[] pathsToTouch = plan.ReferenceHits
                    .Where(h => h.Writable)
                    .Select(h => h.AssetPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(GetWriteOrder)
                    .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                for (int i = 0; i < pathsToTouch.Length; i++)
                {
                    string path = pathsToTouch[i];
                    EditorUtility.DisplayProgressBar(
                        "FBX Unpack",
                        $"Remapping {Path.GetFileName(path)} ({i + 1}/{pathsToTouch.Length})",
                        pathsToTouch.Length == 0 ? 1f : (float)i / pathsToTouch.Length);

                    int changed = RemapAssetPath(path, plan);
                    if (changed <= 0) continue;

                    result.TouchedAssetCount++;
                    result.ReplacedReferenceCount += changed;
                    result.Logs.Add($"Remapped {changed} reference(s): {path}");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                VerifyUnreferenced(plan, result);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return result;
        }

        /// <summary>Deletes assets created during a failed Apply so the project is left clean.</summary>
        private static void RollbackExtraction(FFbxUnpackPlan plan, FFbxUnpackApplyResult result)
        {
            foreach (FFbxSubAssetInfo info in plan.SubAssets)
            {
                if (string.IsNullOrEmpty(info.ExtractedPath)) continue;
                AssetDatabase.DeleteAsset(info.ExtractedPath);
                info.ExtractedPath = null;
                info.ExtractedObject = null;
            }

            plan.ExtractedAssetMap.Clear();
            result.ExtractedCount = 0;
            AssetDatabase.Refresh();
        }

        /// <summary>Authoritative post-remap check: which candidate assets still depend on the source FBX.</summary>
        private static void VerifyUnreferenced(FFbxUnpackPlan plan, FFbxUnpackApplyResult result)
        {
            var referrers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in plan.CandidateAssetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                foreach (string dep in AssetDatabase.GetDependencies(path, true))
                {
                    if (!string.Equals(dep, plan.SourceFbxPath, StringComparison.OrdinalIgnoreCase)) continue;
                    referrers.Add(path);
                    break;
                }
            }

            result.RemainingReferrers.Clear();
            result.RemainingReferrers.AddRange(referrers.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            result.RemainingReferenceCount = referrers.Count;

            if (referrers.Count == 0)
                result.Logs.Add("Verified via AssetDatabase.GetDependencies: no project asset depends on the source FBX anymore.");
            else
                result.Logs.Add($"Still referenced by {referrers.Count} asset(s) after remap (see remaining list).");
        }

        public static List<FFbxReferenceHit> ScanRemainingReferences(FFbxUnpackPlan plan)
        {
            if (plan == null || !plan.HasPreview)
                return new List<FFbxReferenceHit>();
            ScanReferenceHits(plan);
            return plan.ReferenceHits;
        }

        private static void ValidateFbxInput(Object sourceFbx, string saveFolderPath, out string sourcePath, out string sourceGuid)
        {
            if (sourceFbx == null)
                throw new InvalidOperationException("Source FBX is empty.");

            sourcePath = AssetDatabase.GetAssetPath(sourceFbx);
            if (string.IsNullOrEmpty(sourcePath) ||
                !Path.GetExtension(sourcePath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Source asset must be an .fbx asset.");

            sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            if (string.IsNullOrEmpty(sourceGuid))
                throw new InvalidOperationException("Could not resolve FBX GUID.");

            if (string.IsNullOrWhiteSpace(saveFolderPath) || !AssetDatabase.IsValidFolder(saveFolderPath))
                throw new InvalidOperationException("Save folder path is invalid.");

            if (!FRefDatabase.instance.IsReady)
                throw new InvalidOperationException("Asset Cache database is not ready. Run Scan / Refresh Database first.");
        }

        private static void LoadSourceSubAssets(FFbxUnpackPlan plan)
        {
            plan.SubAssets.Clear();
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath(plan.SourceFbxPath);
            if (objects == null || objects.Length == 0)
            {
                plan.Warnings.Add("No sub-assets found in the source FBX.");
                return;
            }

            var seen = new HashSet<FFbxSubAssetKey>();
            foreach (Object obj in objects)
            {
                if (!IsSupportedSourceSubAsset(obj)) continue;
                if (!TryGetKey(obj, out FFbxSubAssetKey key)) continue;
                if (!seen.Add(key)) continue;

                plan.SubAssets.Add(new FFbxSubAssetInfo
                {
                    Key = key,
                    SourceObject = obj,
                    Name = obj.name,
                    TypeName = GetDisplayTypeName(obj),
                    SourcePath = plan.SourceFbxPath
                });
            }

            plan.SubAssets.Sort((a, b) =>
            {
                int type = string.Compare(a.DisplayType, b.DisplayType, StringComparison.OrdinalIgnoreCase);
                return type != 0 ? type : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static bool IsSupportedSourceSubAsset(Object obj)
        {
            return obj is Mesh ||
                   obj is AnimationClip ||
                   obj is Material ||
                   obj is Texture2D ||
                   obj is Avatar ||
                   obj is GameObject;
        }

        private static void CollectCandidatePaths(FFbxUnpackPlan plan)
        {
            plan.CandidateAssetPaths.Clear();

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var db = FRefDatabase.instance;
            db.EnsureUsedBy();

            FRefAsset fbx = db.Get(plan.SourceFbxGuid);
            if (fbx?.usedBy != null)
            {
                foreach (FRefAsset user in fbx.usedBy.Values)
                    AddCandidatePath(paths, user.path);
            }

            foreach (FRefResult result in FRefQuery.FindUsedBy(new[] { plan.SourceFbxGuid }, false, 1))
                AddCandidatePath(paths, result.asset != null ? result.asset.path : null);

            plan.CandidateAssetPaths.AddRange(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        }

        private static void AddCandidatePath(HashSet<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!SupportedReferenceExtensions.Contains(Path.GetExtension(path))) return;
            paths.Add(path);
        }

        private static void ScanReferenceHits(FFbxUnpackPlan plan)
        {
            plan.ReferenceHits.Clear();
            var byKeyAndPath = new Dictionary<string, FFbxReferenceHit>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in plan.CandidateAssetPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!File.Exists(ToFullPath(path))) continue;

                foreach (FFbxSubAssetInfo source in plan.SubAssets)
                {
                    int count = CountSerializedReferences(path, source.Key);
                    if (count <= 0) continue;

                    string mapKey = $"{path}|{source.Key}";
                    if (!byKeyAndPath.TryGetValue(mapKey, out FFbxReferenceHit hit))
                    {
                        hit = new FFbxReferenceHit
                        {
                            AssetPath = path,
                            ReferenceKind = GetReferenceKind(path),
                            SourceKey = source.Key,
                            SourceName = source.DisplayName,
                            SourceType = source.DisplayType,
                            Writable = SupportedReferenceExtensions.Contains(Path.GetExtension(path)),
                            Status = "Preview"
                        };
                        byKeyAndPath.Add(mapKey, hit);
                    }
                    hit.Count += count;
                }
            }

            plan.ReferenceHits.AddRange(byKeyAndPath.Values
                .OrderBy(h => h.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.SourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.SourceName, StringComparer.OrdinalIgnoreCase));

            foreach (FFbxSubAssetInfo info in plan.SubAssets)
            {
                int total = 0;
                foreach (FFbxReferenceHit hit in plan.ReferenceHits)
                    if (hit.SourceKey.Equals(info.Key))
                        total += hit.Count;
                info.UsedByCount = total;
            }
        }

        private static int CountSerializedReferences(string assetPath, FFbxSubAssetKey key)
        {
            string fullPath = ToFullPath(assetPath);
            if (!File.Exists(fullPath)) return 0;

            int count = 0;
            string localId = key.LocalFileId.ToString();
            try
            {
                foreach (string line in File.ReadLines(fullPath))
                {
                    if (line.IndexOf(key.Guid, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (line.IndexOf(localId, StringComparison.Ordinal) < 0) continue;
                    count++;
                }
            }
            catch
            {
                return 0;
            }
            return count;
        }

        private static void EnsureOutputFolders(FFbxUnpackPlan plan)
        {
            EnsureFolder(plan.SaveFolderPath, ValidateAssetName(Path.GetFileNameWithoutExtension(plan.SourceFbxPath)));
            EnsureFolder(plan.RootOutputFolderPath, ModelsFolderName);
            EnsureFolder(plan.RootOutputFolderPath, AnimationsFolderName);
            EnsureFolder(plan.RootOutputFolderPath, MaterialsFolderName);
            EnsureFolder(plan.RootOutputFolderPath, TexturesFolderName);
            EnsureFolder(plan.RootOutputFolderPath, AvatarFolderName);
            EnsureFolder(plan.RootOutputFolderPath, PrefabsFolderName);
        }

        private static void ExtractAllAssets(FFbxUnpackPlan plan, FFbxUnpackApplyResult result)
        {
            plan.ExtractedAssetMap.Clear();

            string fbxName = plan.SourceFbxName;
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting textures", 0.05f);
            ExtractByType<Texture2D>(plan, result);
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting materials", 0.15f);
            ExtractByType<Material>(plan, result);
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting meshes", 0.30f);
            ExtractByType<Mesh>(plan, result);
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting animations", 0.45f);
            ExtractByType<AnimationClip>(plan, result);
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting avatar", 0.55f);
            ExtractByType<Avatar>(plan, result);
            EditorUtility.DisplayProgressBar("FBX Unpack", $"{fbxName}: extracting prefab", 0.65f);
            ExtractByType<GameObject>(plan, result);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (FFbxSubAssetInfo info in plan.SubAssets)
            {
                if (string.IsNullOrEmpty(info.ExtractedPath)) continue;
                Object loaded = AssetDatabase.LoadAssetAtPath<Object>(info.ExtractedPath);
                if (loaded == null) continue;

                info.ExtractedObject = loaded;
                plan.ExtractedAssetMap[info.Key] = loaded;
            }
        }

        private static void ExtractByType<T>(FFbxUnpackPlan plan, FFbxUnpackApplyResult result)
            where T : Object
        {
            var extractedThisPhase = new List<FFbxSubAssetInfo>();
            foreach (FFbxSubAssetInfo info in plan.SubAssets)
            {
                if (!(info.SourceObject is T)) continue;
                if (!string.IsNullOrEmpty(info.ExtractedPath)) continue;
                ExtractOne(plan, info, result);
                if (!string.IsNullOrEmpty(info.ExtractedPath))
                    extractedThisPhase.Add(info);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (FFbxSubAssetInfo info in extractedThisPhase)
            {
                Object loaded = AssetDatabase.LoadAssetAtPath<Object>(info.ExtractedPath);
                if (loaded == null) continue;

                info.ExtractedObject = loaded;
                plan.ExtractedAssetMap[info.Key] = loaded;
            }
        }

        private static void ExtractOne(FFbxUnpackPlan plan, FFbxSubAssetInfo info, FFbxUnpackApplyResult result)
        {
            info.ExtractError = null;
            try
            {
                switch (info.SourceObject)
                {
                    case Texture2D texture:
                        info.ExtractedPath = ExtractTexture(plan, info, texture);
                        break;
                    case Material material:
                        info.ExtractedPath = ExtractMaterial(plan, info, material);
                        break;
                    case Mesh mesh:
                        info.ExtractedPath = ExtractMesh(plan, info, mesh);
                        break;
                    case AnimationClip clip:
                        info.ExtractedPath = ExtractClip(plan, info, clip);
                        break;
                    case Avatar avatar:
                        info.ExtractedPath = ExtractAvatar(plan, info, avatar);
                        break;
                    case GameObject go:
                        info.ExtractedPath = ExtractPrefab(plan, info, go);
                        break;
                }

                if (!string.IsNullOrEmpty(info.ExtractedPath))
                {
                    result.ExtractedCount++;
                    result.Logs.Add($"Extracted {info.DisplayType}: {info.DisplayName} -> {info.ExtractedPath}");
                }
            }
            catch (Exception ex)
            {
                string message = $"Failed to extract {info.DisplayType} '{info.DisplayName}': {ex.Message}";
                info.ExtractError = message;
                plan.Warnings.Add(message);
                result.Errors.Add(message);
                result.Logs.Add(message);
                Debug.LogException(ex);
            }
        }

        private static string ExtractTexture(FFbxUnpackPlan plan, FFbxSubAssetInfo info, Texture2D source)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            bool isMainAsset = !string.IsNullOrEmpty(sourcePath)
                               && !string.Equals(sourcePath, plan.SourceFbxPath, StringComparison.OrdinalIgnoreCase)
                               && AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath) == source;

            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Texture" : source.name);
            if (isMainAsset)
            {
                string dest = AssetDatabase.GenerateUniqueAssetPath(
                    $"{plan.RootOutputFolderPath}/{TexturesFolderName}/{assetName}{Path.GetExtension(sourcePath)}");
                if (AssetDatabase.CopyAsset(sourcePath, dest))
                    return dest;
            }

            Texture2D copy = CopyTexture2DViaRenderTexture(source);
            string pngPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{plan.RootOutputFolderPath}/{TexturesFolderName}/{assetName}.png");
            File.WriteAllBytes(ToFullPath(pngPath), copy.EncodeToPNG());
            Object.DestroyImmediate(copy);
            AssetDatabase.ImportAsset(pngPath);
            return pngPath;
        }

        private static string ExtractMaterial(FFbxUnpackPlan plan, FFbxSubAssetInfo info, Material source)
        {
            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Material" : source.name);
            var copy = new Material(source) { name = assetName };
            foreach (string propName in source.GetTexturePropertyNames())
            {
                Texture texture = source.GetTexture(propName);
                if (texture == null) continue;
                if (TryGetKey(texture, out FFbxSubAssetKey textureKey) &&
                    plan.ExtractedAssetMap.TryGetValue(textureKey, out Object replacement) &&
                    replacement is Texture replacementTexture)
                    copy.SetTexture(propName, replacementTexture);
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{plan.RootOutputFolderPath}/{MaterialsFolderName}/{assetName}.mat");
            AssetDatabase.CreateAsset(copy, path);
            return path;
        }

        private static string ExtractMesh(FFbxUnpackPlan plan, FFbxSubAssetInfo info, Mesh source)
        {
            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Mesh" : source.name);
            Mesh copy = CopyMesh(source);
            copy.name = assetName;
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{plan.RootOutputFolderPath}/{ModelsFolderName}/{assetName}.asset");
            AssetDatabase.CreateAsset(copy, path);
            return path;
        }

        private static string ExtractClip(FFbxUnpackPlan plan, FFbxSubAssetInfo info, AnimationClip source)
        {
            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Clip" : source.name);

            // A fresh AnimationClip cannot be filled from a model-imported clip with EditorUtility.CopySerialized
            // ("Source and Destination Types do not match"). Copy the curve/event data explicitly instead.
            var copy = new AnimationClip
            {
                name = assetName,
                legacy = source.legacy,
                frameRate = source.frameRate,
                wrapMode = source.wrapMode,
                localBounds = source.localBounds
            };

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
                AnimationUtility.SetEditorCurve(copy, binding, AnimationUtility.GetEditorCurve(source, binding));

            foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
                AnimationUtility.SetObjectReferenceCurve(copy, binding, AnimationUtility.GetObjectReferenceCurve(source, binding));

            AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(source));
            AnimationUtility.SetAnimationEvents(copy, AnimationUtility.GetAnimationEvents(source));

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{plan.RootOutputFolderPath}/{AnimationsFolderName}/{assetName}.anim");
            AssetDatabase.CreateAsset(copy, path);
            return path;
        }

        private static string ExtractAvatar(FFbxUnpackPlan plan, FFbxSubAssetInfo info, Avatar source)
        {
            Avatar avatar = BuildAvatarFromModel(plan.SourceFbxPath, source);
            if (avatar == null)
            {
                plan.Warnings.Add($"Avatar '{info.DisplayName}' could not be rebuilt and was skipped.");
                return null;
            }

            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Avatar" : source.name);
            avatar.name = assetName;
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{plan.RootOutputFolderPath}/{AvatarFolderName}/{assetName}.asset");
            AssetDatabase.CreateAsset(avatar, path);
            return path;
        }

        private static string ExtractPrefab(FFbxUnpackPlan plan, FFbxSubAssetInfo info, GameObject source)
        {
            string assetName = ValidateAssetName(string.IsNullOrWhiteSpace(source.name) ? "Model" : source.name);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
                instance = Object.Instantiate(source);

            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                RemapHierarchyReferences(instance, plan.ExtractedAssetMap);

                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{plan.RootOutputFolderPath}/{PrefabsFolderName}/{assetName}.prefab");
                PrefabUtility.SaveAsPrefabAsset(instance, path, out bool success);
                if (!success)
                    throw new InvalidOperationException($"SaveAsPrefabAsset failed: {path}");
                return path;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static int RemapAssetPath(string path, FFbxUnpackPlan plan)
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
                return RemapPrefabAsset(path, plan);
            if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase))
                return RemapSceneAsset(path, plan);

            return RemapRegularAsset(path, plan.ExtractedAssetMap);
        }

        private static int RemapPrefabAsset(string prefabPath, FFbxUnpackPlan plan)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool dirty = false;
            int changed = 0;
            try
            {
                changed += ReplaceNestedFbxInstances(root, plan);
                int refs = RemapHierarchyReferences(root, plan.ExtractedAssetMap);
                changed += refs;
                dirty = changed > 0;
                if (dirty)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool _);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changed;
        }

        private static int RemapSceneAsset(string scenePath, FFbxUnpackPlan plan)
        {
            Scene alreadyLoaded = FindLoadedScene(scenePath);
            bool openedByTool = !alreadyLoaded.IsValid();
            Scene scene = openedByTool
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive)
                : alreadyLoaded;

            int changed = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                changed += ReplaceNestedFbxInstances(root, plan);
                changed += RemapHierarchyReferences(root, plan.ExtractedAssetMap);
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (openedByTool)
                EditorSceneManager.CloseScene(scene, true);

            return changed;
        }

        private static int RemapRegularAsset(string path, Dictionary<FFbxSubAssetKey, Object> map)
        {
            int changed = 0;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null) return 0;

            foreach (Object asset in assets)
            {
                if (asset == null) continue;
                changed += RemapSerializedObjectReferences(asset, map);

                switch (asset)
                {
                    case AnimatorController controller:
                        changed += RemapAnimatorController(controller, map);
                        break;
                    case AnimatorOverrideController overrideController:
                        changed += RemapOverrideController(overrideController, map);
                        break;
                }
            }

            if (changed > 0)
            {
                foreach (Object asset in assets)
                    if (asset != null)
                        EditorUtility.SetDirty(asset);
            }
            return changed;
        }

        private static int RemapHierarchyReferences(GameObject root, Dictionary<FFbxSubAssetKey, Object> map)
        {
            int changed = 0;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = transform.gameObject;
                changed += RemapSerializedObjectReferences(go, map);
                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    changed += RemapSerializedObjectReferences(component, map);
                    changed += RemapKnownComponent(component, map);
                }
            }
            return changed;
        }

        private static int RemapKnownComponent(Component component, Dictionary<FFbxSubAssetKey, Object> map)
        {
            int changed = 0;
            switch (component)
            {
                case MeshFilter meshFilter:
                    if (TryResolve(meshFilter.sharedMesh, map, out Mesh mesh))
                    {
                        meshFilter.sharedMesh = mesh;
                        changed++;
                    }
                    break;
                case SkinnedMeshRenderer skinned:
                    if (TryResolve(skinned.sharedMesh, map, out Mesh skinnedMesh))
                    {
                        skinned.sharedMesh = skinnedMesh;
                        changed++;
                    }
                    changed += RemapMaterials(skinned, map);
                    break;
                case MeshRenderer meshRenderer:
                    changed += RemapMaterials(meshRenderer, map);
                    break;
                case MeshCollider meshCollider:
                    if (TryResolve(meshCollider.sharedMesh, map, out Mesh colliderMesh))
                    {
                        meshCollider.sharedMesh = colliderMesh;
                        changed++;
                    }
                    break;
                case Animator animator:
                    if (TryResolve(animator.avatar, map, out Avatar avatar))
                    {
                        animator.avatar = avatar;
                        changed++;
                    }
                    break;
            }

            if (changed > 0)
            {
                EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            return changed;
        }

        private static int RemapMaterials(Renderer renderer, Dictionary<FFbxSubAssetKey, Object> map)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!TryResolve(materials[i], map, out Material material)) continue;
                materials[i] = material;
                changed = true;
            }
            if (!changed) return 0;
            renderer.sharedMaterials = materials;
            return 1;
        }

        private static int RemapSerializedObjectReferences(Object target, Dictionary<FFbxSubAssetKey, Object> map)
        {
            if (target == null) return 0;

            int changed = 0;
            var so = new SerializedObject(target);
            SerializedProperty property = so.GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object value = property.objectReferenceValue;
                if (value == null) continue;
                if (!TryGetKey(value, out FFbxSubAssetKey key)) continue;
                if (!map.TryGetValue(key, out Object replacement) || replacement == null) continue;
                if (value == replacement) continue;

                property.objectReferenceValue = replacement;
                changed++;
            }

            if (changed > 0)
                so.ApplyModifiedProperties();
            so.Dispose();
            return changed;
        }

        private static int ReplaceNestedFbxInstances(GameObject root, FFbxUnpackPlan plan)
        {
            GameObject replacementPrefab = GetReplacementPrefab(plan);
            if (replacementPrefab == null) return 0;

            var toReplace = new List<GameObject>();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = transform.gameObject;
                GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (nearestRoot != go) continue;

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (string.Equals(path, plan.SourceFbxPath, StringComparison.OrdinalIgnoreCase))
                    toReplace.Add(go);
            }

            int changed = 0;
            foreach (GameObject oldRoot in toReplace)
            {
                Transform oldTransform = oldRoot.transform;
                Transform parent = oldTransform.parent;
                int sibling = oldTransform.GetSiblingIndex();
                string objectName = oldRoot.name;
                bool active = oldRoot.activeSelf;
                Vector3 localPosition = oldTransform.localPosition;
                Quaternion localRotation = oldTransform.localRotation;
                Vector3 localScale = oldTransform.localScale;

                var replacement = PrefabUtility.InstantiatePrefab(replacementPrefab, oldRoot.scene) as GameObject;
                if (replacement == null)
                {
                    // Never fall back to a scene-less Object.Instantiate: it leaks copies into the active scene.
                    plan.Warnings.Add($"Could not instantiate replacement prefab for '{objectName}'; left the original FBX instance in place.");
                    continue;
                }

                replacement.name = objectName;
                replacement.SetActive(active);
                replacement.transform.SetParent(parent, false);
                replacement.transform.SetSiblingIndex(sibling);
                replacement.transform.localPosition = localPosition;
                replacement.transform.localRotation = localRotation;
                replacement.transform.localScale = localScale;

                Object.DestroyImmediate(oldRoot);
                changed++;
            }

            return changed;
        }

        private static GameObject GetReplacementPrefab(FFbxUnpackPlan plan)
        {
            foreach (FFbxSubAssetInfo info in plan.SubAssets)
            {
                if (!(info.SourceObject is GameObject)) continue;
                Object replacement;
                if (plan.ExtractedAssetMap.TryGetValue(info.Key, out replacement))
                    return replacement as GameObject;
            }
            return null;
        }

        private static int RemapAnimatorController(AnimatorController controller, Dictionary<FFbxSubAssetKey, Object> map)
        {
            bool changed = false;
            foreach (AnimatorControllerLayer layer in controller.layers)
                changed |= RemapStateMachine(layer.stateMachine, map);
            if (changed)
                EditorUtility.SetDirty(controller);
            return changed ? 1 : 0;
        }

        private static bool RemapStateMachine(AnimatorStateMachine stateMachine, Dictionary<FFbxSubAssetKey, Object> map)
        {
            if (stateMachine == null) return false;
            bool changed = false;

            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                Motion remapped = RemapMotion(childState.state.motion, map, ref changed);
                if (remapped != childState.state.motion)
                {
                    childState.state.motion = remapped;
                    changed = true;
                }
            }

            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
                changed |= RemapStateMachine(child.stateMachine, map);

            return changed;
        }

        private static Motion RemapMotion(Motion motion, Dictionary<FFbxSubAssetKey, Object> map, ref bool changed)
        {
            if (motion == null) return null;

            if (TryResolve(motion, map, out AnimationClip clip))
                return clip;

            if (motion is BlendTree tree)
            {
                ChildMotion[] children = tree.children;
                bool treeChanged = false;
                for (int i = 0; i < children.Length; i++)
                {
                    Motion remapped = RemapMotion(children[i].motion, map, ref changed);
                    if (remapped == children[i].motion) continue;
                    children[i].motion = remapped;
                    treeChanged = true;
                }
                if (treeChanged)
                {
                    tree.children = children;
                    EditorUtility.SetDirty(tree);
                    changed = true;
                }
            }

            return motion;
        }

        private static int RemapOverrideController(AnimatorOverrideController controller, Dictionary<FFbxSubAssetKey, Object> map)
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);
            controller.GetOverrides(overrides);

            bool changed = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip key = overrides[i].Key;
                AnimationClip current = overrides[i].Value;
                AnimationClip effective = current != null ? current : key;
                if (!TryResolve(effective, map, out AnimationClip replacement)) continue;

                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(key, replacement);
                changed = true;
            }

            if (!changed) return 0;
            controller.ApplyOverrides(overrides);
            EditorUtility.SetDirty(controller);
            return 1;
        }

        private static bool TryResolve<T>(Object source, Dictionary<FFbxSubAssetKey, Object> map, out T replacement)
            where T : Object
        {
            replacement = null;
            if (source == null) return false;
            if (!TryGetKey(source, out FFbxSubAssetKey key)) return false;
            if (!map.TryGetValue(key, out Object obj)) return false;
            replacement = obj as T;
            return replacement != null;
        }

        private static bool TryGetKey(Object obj, out FFbxSubAssetKey key)
        {
            key = default;
            if (obj == null) return false;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                return false;
            if (string.IsNullOrEmpty(guid) || localId == 0)
                return false;
            key = new FFbxSubAssetKey(guid, localId);
            return true;
        }

        private static Avatar BuildAvatarFromModel(string fbxPath, Avatar source)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null || source == null) return null;

            GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null)
                instance = Object.Instantiate(model);

            try
            {
                return source.isHuman
                    ? AvatarBuilder.BuildHumanAvatar(instance, source.humanDescription)
                    : AvatarBuilder.BuildGenericAvatar(instance, string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Mesh CopyMesh(Mesh source)
        {
            var mesh = new Mesh { indexFormat = source.indexFormat };
            mesh.vertices = source.vertices;
            mesh.normals = source.normals;
            mesh.tangents = source.tangents;
            mesh.colors = source.colors;
            mesh.uv = source.uv;
            mesh.uv2 = source.uv2;
            mesh.uv3 = source.uv3;
            mesh.uv4 = source.uv4;
            mesh.uv5 = source.uv5;
            mesh.uv6 = source.uv6;
            mesh.uv7 = source.uv7;
            mesh.uv8 = source.uv8;
            mesh.bindposes = source.bindposes;
            mesh.boneWeights = source.boneWeights;
            mesh.subMeshCount = source.subMeshCount;

            for (int i = 0; i < source.subMeshCount; i++)
                mesh.SetTriangles(source.GetTriangles(i), i);

            if (source.blendShapeCount > 0)
            {
                var deltaV = new Vector3[source.vertexCount];
                var deltaN = new Vector3[source.vertexCount];
                var deltaT = new Vector3[source.vertexCount];
                for (int s = 0; s < source.blendShapeCount; s++)
                {
                    string shapeName = source.GetBlendShapeName(s);
                    for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                    {
                        float weight = source.GetBlendShapeFrameWeight(s, f);
                        source.GetBlendShapeFrameVertices(s, f, deltaV, deltaN, deltaT);
                        mesh.AddBlendShapeFrame(shapeName, weight, deltaV, deltaN, deltaT);
                    }
                }
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D CopyTexture2DViaRenderTexture(Texture2D source)
        {
            int w = source.width;
            int h = source.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        private static Scene FindLoadedScene(string scenePath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return default;
        }

        private static int GetWriteOrder(string path)
        {
            string ext = Path.GetExtension(path);
            if (ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase)) return 0;
            if (ext.Equals(".unity", StringComparison.OrdinalIgnoreCase)) return 1;
            return 2;
        }

        private static string GetReferenceKind(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".prefab": return "Prefab";
                case ".unity": return "Scene";
                case ".controller": return "Animator Controller";
                case ".overridecontroller": return "Animator Override";
                case ".mat": return "Material";
                case ".anim": return "Animation";
                case ".playable": return "Timeline";
                case ".asset": return "Asset";
                default: return "Asset";
            }
        }

        private static string GetDisplayTypeName(Object obj)
        {
            if (obj is Texture2D) return "Texture2D";
            if (obj is AnimationClip) return "AnimationClip";
            if (obj is GameObject) return "GameObject";
            return obj != null ? obj.GetType().Name : "Object";
        }

        private static void EnsureFolder(string parentPath, string folderName)
        {
            string childPath = $"{parentPath}/{folderName}";
            if (AssetDatabase.IsValidFolder(childPath)) return;
            string guid = AssetDatabase.CreateFolder(parentPath, folderName);
            if (string.IsNullOrEmpty(guid))
                throw new InvalidOperationException($"Failed to create folder: {childPath}");
        }

        private static string ValidateAssetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";
            foreach (char ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');
            name = name.Trim();
            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, assetPath));
        }
    }
}
