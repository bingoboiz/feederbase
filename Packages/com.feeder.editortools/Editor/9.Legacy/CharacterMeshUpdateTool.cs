using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder {
    public class CharacterMeshUpdateTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Character Mesh Update")]
        private static void OpenWindow()
        {
            GetWindow<CharacterMeshUpdateTool>("Character Mesh Update").Show();
        }

        [Title("Mesh Transfer Tool")]
        [InfoBox("This tool allows you to transfer skinned mesh and mesh renderers from one armature to another. " +
                 "Select the source gameobjects, choose the new armature and parent, then click Transfer.", InfoMessageType.Info)]
        
        [Space(10)]
        [LabelText("Source GameObjects")]
        [InfoBox("Drag and drop the gameobjects to search for renderers", InfoMessageType.None)]
        public GameObject[] sourceGameObjects = new GameObject[0];

        [Space(10)]
        [Title("Find Options")]
        [LabelText("Only Active GameObjects")]
        public bool onlyActiveGameObjects;

        [LabelText("Only Skinned Mesh")]
        public bool onlySkinnedMesh;

        [LabelText("Only Mesh")]
        public bool onlyMesh;

        [LabelText("Exclude Duplicate Names")]
        public bool excludeDuplicateNames;

        [Space(10)]
        [Title("Transfer Settings")]
        [LabelText("New Armature (Hips)")]
        [InfoBox("The root bone of the new armature (usually the Hips bone)", InfoMessageType.None)]
        [Required]
        public Transform newArmature;

        [Space(5)]
        [LabelText("New Parent")]
        [InfoBox("The parent transform where the skinned mesh renderers will be moved to", InfoMessageType.None)]
        [Required]
        public Transform newParent;

        [Space(10)]
        [Title("Preview")]
        [LabelText("Found Skinned Mesh Renderers")]
        [ReadOnly]
        public SkinnedMeshRenderer[] foundSkinnedMeshRenderers = new SkinnedMeshRenderer[0];

        [LabelText("Found Mesh Renderers")]
        [ReadOnly]
        public MeshRenderer[] foundMeshRenderers = new MeshRenderer[0];

        [PropertySpace]
        [Button("Preview Find Mesh", ButtonSizes.Medium)]
        [GUIColor(0.6f, 1f, 0.6f)]
        private void PreviewFindMesh()
        {
            if (!ValidateFindOptions())
            {
                return;
            }

            CollectRenderersFromSources();
            EditorUtility.DisplayDialog("Preview Complete",
                $"Found {foundSkinnedMeshRenderers.Length} SkinnedMeshRenderer(s) and {foundMeshRenderers.Length} MeshRenderer(s).",
                "OK");
        }

        [PropertySpace]
        [Button("Transfer Meshes", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.8f, 1f)]
        [EnableIf("@newArmature != null && newParent != null && sourceGameObjects != null && sourceGameObjects.Length > 0")]
        private void TransferMeshes()
        {
            if (sourceGameObjects == null || sourceGameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", "Please select at least one source gameobject.", "OK");
                return;
            }

            if (newArmature == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a new armature.", "OK");
                return;
            }

            if (newParent == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a new parent transform.", "OK");
                return;
            }

            if (!ValidateFindOptions())
            {
                return;
            }

            CollectRenderersFromSources();

            int successCount = 0;
            int failCount = 0;
            var armatureMap = BuildArmatureMap(newArmature);

            foreach (var skinnedMeshRenderer in foundSkinnedMeshRenderers)
            {
                if (skinnedMeshRenderer == null) continue;

                try
                {
                    TransferSkinnedMeshRenderer(skinnedMeshRenderer, armatureMap, newParent);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to transfer {skinnedMeshRenderer.name}: {e.Message}");
                    failCount++;
                }
            }

            foreach (var meshRenderer in foundMeshRenderers)
            {
                if (meshRenderer == null) continue;

                try
                {
                    TransferMeshRenderer(meshRenderer, armatureMap);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to transfer {meshRenderer.name}: {e.Message}");
                    failCount++;
                }
            }

            // Show results
            if (failCount == 0)
            {
                EditorUtility.DisplayDialog("Success", 
                    $"Successfully transferred {successCount} renderer(s).", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Transfer Complete", 
                    $"Transferred {successCount} renderer(s) successfully.\n" +
                    $"Failed to transfer {failCount} renderer(s).\n" +
                    "Check the console for error details.", "OK");
            }

            // Mark scene as dirty
            EditorUtility.SetDirty(newParent);
        }

        [PropertySpace]
        [Button("Clear All", ButtonSizes.Medium)]
        [GUIColor(1f, 0.6f, 0.6f)]
        private void ClearAll()
        {
            sourceGameObjects = new GameObject[0];
            onlyActiveGameObjects = false;
            onlySkinnedMesh = false;
            onlyMesh = false;
            excludeDuplicateNames = false;
            foundSkinnedMeshRenderers = new SkinnedMeshRenderer[0];
            foundMeshRenderers = new MeshRenderer[0];
            newArmature = null;
            newParent = null;
        }

        private bool ValidateFindOptions()
        {
            if (onlySkinnedMesh && onlyMesh)
            {
                EditorUtility.DisplayDialog("Error", "Please select only one: Only Skinned Mesh or Only Mesh.", "OK");
                return false;
            }

            return true;
        }

        private void CollectRenderersFromSources()
        {
            var sources = sourceGameObjects
                .Where(source => source != null);

            SkinnedMeshRenderer[] skinnedRenderers;
            MeshRenderer[] meshRenderers;

            if (onlyMesh)
            {
                skinnedRenderers = new SkinnedMeshRenderer[0];
            }
            else
            {
                skinnedRenderers = sources
                    .SelectMany(source => source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    .Distinct()
                    .ToArray();
            }

            if (onlySkinnedMesh)
            {
                meshRenderers = new MeshRenderer[0];
            }
            else
            {
                meshRenderers = sources
                    .SelectMany(source => source.GetComponentsInChildren<MeshRenderer>(true))
                    .Distinct()
                    .ToArray();
            }

            if (onlyActiveGameObjects)
            {
                skinnedRenderers = skinnedRenderers
                    .Where(renderer => renderer?.gameObject.activeInHierarchy == true)
                    .ToArray();
                meshRenderers = meshRenderers
                    .Where(renderer => renderer?.gameObject.activeInHierarchy == true)
                    .ToArray();
            }

            if (excludeDuplicateNames)
            {
                skinnedRenderers = RemoveDuplicateNames(skinnedRenderers);
                meshRenderers = RemoveDuplicateNames(meshRenderers);
            }

            foundSkinnedMeshRenderers = skinnedRenderers;
            foundMeshRenderers = meshRenderers;
        }

        private static SkinnedMeshRenderer[] RemoveDuplicateNames(SkinnedMeshRenderer[] renderers)
        {
            var uniqueNames = new System.Collections.Generic.HashSet<string>();
            return renderers
                .Where(renderer => renderer?.name != null)
                .Where(renderer => uniqueNames.Add(renderer.name))
                .ToArray();
        }

        private static MeshRenderer[] RemoveDuplicateNames(MeshRenderer[] renderers)
        {
            var uniqueNames = new System.Collections.Generic.HashSet<string>();
            return renderers
                .Where(renderer => renderer?.name != null)
                .Where(renderer => uniqueNames.Add(renderer.name))
                .ToArray();
        }

        private static Transform[] BuildArmatureMap(Transform armatureRoot)
        {
            return armatureRoot.GetComponentsInChildren<Transform>(true);
        }

        private static void TransferSkinnedMeshRenderer(
            SkinnedMeshRenderer skinnedMeshRenderer,
            Transform[] armatureMap,
            Transform newParentTransform)
        {
            var rootBoneName = skinnedMeshRenderer.rootBone?.name;
            if (string.IsNullOrEmpty(rootBoneName))
            {
                throw new System.Exception("Root bone is missing.");
            }

            var newBones = new Transform[skinnedMeshRenderer.bones.Length];

            for (var x = 0; x < skinnedMeshRenderer.bones.Length; x++)
            {
                var oldBoneName = skinnedMeshRenderer.bones[x]?.name;
                if (string.IsNullOrEmpty(oldBoneName))
                {
                    throw new System.Exception("Bone is missing.");
                }

                var newBone = armatureMap.FirstOrDefault(transformChild => transformChild.name == oldBoneName);
                if (newBone == null)
                {
                    throw new System.Exception($"Bone not found: {oldBoneName}");
                }

                newBones[x] = newBone;
            }

            var matchingRootBone = armatureMap.FirstOrDefault(transformChild => transformChild.name == rootBoneName);
            if (matchingRootBone == null)
            {
                throw new System.Exception($"Root bone not found: {rootBoneName}");
            }

            skinnedMeshRenderer.rootBone = matchingRootBone;
            skinnedMeshRenderer.bones = newBones;

            skinnedMeshRenderer.transform.SetParent(newParentTransform, false);
            skinnedMeshRenderer.transform.localPosition = Vector3.zero;
        }

        private static void TransferMeshRenderer(MeshRenderer meshRenderer, Transform[] armatureMap)
        {
            var parentName = meshRenderer.transform.parent?.name;
            if (string.IsNullOrEmpty(parentName))
            {
                throw new System.Exception("Parent bone is missing.");
            }

            var targetParent = armatureMap.FirstOrDefault(transformChild => transformChild.name == parentName);
            if (targetParent == null)
            {
                throw new System.Exception($"Parent bone not found: {parentName}");
            }

            meshRenderer.transform.SetParent(targetParent, false);
        }
    }
}
