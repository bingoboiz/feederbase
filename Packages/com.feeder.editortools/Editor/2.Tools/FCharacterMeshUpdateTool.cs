using System.Linq;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FCharacterMeshUpdateTool : FBaseTool
    {
        protected override string GetDescription()
        {
            return "Chuyển SkinnedMeshRenderer / MeshRenderer từ armature cũ sang armature mới. Kéo GameObject nguồn vào Source, chọn Armature đích và Parent, bấm Preview rồi Transfer.";
        }

        // ── Source ──────────────────────────────────────────────────────────

        [Title("Source")]
        [LabelText("Source GameObjects")]
        [Tooltip("Kéo và thả các GameObject chứa renderer cần chuyển vào đây")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public GameObject[] sourceGameObjects = new GameObject[0];

        // ── Find Options ─────────────────────────────────────────────────────

        [PropertySpace(SpaceBefore = 4)]
        [Title("Find Options")]
        [LabelText("Only Active GameObjects")]
        [Tooltip("Chỉ lấy renderer trên những GameObject đang active trong hierarchy")]
        public bool onlyActiveGameObjects;

        [LabelText("Only Skinned Mesh")]
        [Tooltip("Chỉ thu thập SkinnedMeshRenderer, bỏ qua MeshRenderer")]
        public bool onlySkinnedMesh;

        [LabelText("Only Mesh")]
        [Tooltip("Chỉ thu thập MeshRenderer, bỏ qua SkinnedMeshRenderer")]
        public bool onlyMesh;

        [LabelText("Exclude Duplicate Names")]
        [Tooltip("Nếu nhiều renderer cùng tên, chỉ giữ renderer đầu tiên")]
        public bool excludeDuplicateNames;

        // ── Transfer Settings ────────────────────────────────────────────────

        [PropertySpace(SpaceBefore = 4)]
        [Title("Transfer Settings")]
        [LabelText("New Armature (Hips)")]
        [Tooltip("Root bone của armature đích — thường là bone Hips")]
        [Required]
        public Transform newArmature;

        [PropertySpace(SpaceBefore = 2)]
        [LabelText("New Parent")]
        [Tooltip("Transform sẽ trở thành parent của các renderer sau khi chuyển")]
        [Required]
        public Transform newParent;

        // ── Guide ────────────────────────────────────────────────────────────

        [OnInspectorGUI]
        private void DrawGuide()
        {
            GUILayout.Space(6);
            StylesUtils.DrawInfoBox(
                "Hướng dẫn sử dụng:\n" +
                "  1. Kéo GameObject chứa renderer vào Source GameObjects\n" +
                "  2. Tuỳ chọn lọc: Only Active / Only Skinned / Only Mesh / Exclude Duplicates\n" +
                "  3. Chọn New Armature (bone Hips của armature đích)\n" +
                "  4. Chọn New Parent (transform sẽ chứa các renderer sau khi chuyển)\n" +
                "  5. Bấm Preview để xem trước, sau đó bấm Transfer để thực hiện"
            );
            GUILayout.Space(4);
        }

        // ── Preview ──────────────────────────────────────────────────────────

        [Title("Preview")]
        [LabelText("Found Skinned Mesh Renderers")]
        [ReadOnly]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = false, NumberOfItemsPerPage = 10)]
        public SkinnedMeshRenderer[] foundSkinnedMeshRenderers = new SkinnedMeshRenderer[0];

        [LabelText("Found Mesh Renderers")]
        [ReadOnly]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = false, NumberOfItemsPerPage = 10)]
        public MeshRenderer[] foundMeshRenderers = new MeshRenderer[0];

        // ── Actions ──────────────────────────────────────────────────────────

        [PropertySpace(SpaceBefore = 6)]
        [Button("Preview Find Mesh", ButtonSizes.Medium)]
        [GUIColor(0.6f, 1f, 0.6f)]
        private void PreviewFindMesh()
        {
            if (!ValidateFindOptions()) return;
            CollectRenderersFromSources();
            Debug.Log($"[CharacterMeshUpdate] Preview: {foundSkinnedMeshRenderers.Length} SkinnedMeshRenderer(s), {foundMeshRenderers.Length} MeshRenderer(s).");
        }

        [PropertySpace(SpaceBefore = 2)]
        [Button("Transfer Meshes", ButtonSizes.Large)]
        [GUIColor(0.4f, 0.8f, 1f)]
        [EnableIf("@newArmature != null && newParent != null && sourceGameObjects != null && sourceGameObjects.Length > 0")]
        private void TransferMeshes()
        {
            if (sourceGameObjects == null || sourceGameObjects.Length == 0)
                throw new System.InvalidOperationException("Source GameObjects is empty.");
            if (newArmature == null)
                throw new System.InvalidOperationException("New Armature is not assigned.");
            if (newParent == null)
                throw new System.InvalidOperationException("New Parent is not assigned.");
            if (!ValidateFindOptions()) return;

            CollectRenderersFromSources();

            var armatureMap = BuildArmatureMap(newArmature);
            int successCount = 0;
            int failCount = 0;

            Undo.SetCurrentGroupName("Transfer Meshes");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var smr in foundSkinnedMeshRenderers)
            {
                if (smr == null) continue;
                try
                {
                    TransferSkinnedMeshRenderer(smr, armatureMap, newParent);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CharacterMeshUpdate] Failed to transfer '{smr.name}': {e.Message}");
                    failCount++;
                }
            }

            foreach (var mr in foundMeshRenderers)
            {
                if (mr == null) continue;
                try
                {
                    TransferMeshRenderer(mr, armatureMap);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CharacterMeshUpdate] Failed to transfer '{mr.name}': {e.Message}");
                    failCount++;
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorUtility.SetDirty(newParent);

            if (failCount == 0)
                Debug.Log($"<color=green>[CharacterMeshUpdate] Successfully transferred {successCount} renderer(s).</color>");
            else
                Debug.LogWarning($"[CharacterMeshUpdate] Transferred {successCount} renderer(s). Failed: {failCount}. See above errors for details.");
        }

        [PropertySpace(SpaceBefore = 2)]
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

        // ── Private helpers ──────────────────────────────────────────────────

        private bool ValidateFindOptions()
        {
            if (onlySkinnedMesh && onlyMesh)
            {
                Debug.LogError("[CharacterMeshUpdate] 'Only Skinned Mesh' and 'Only Mesh' cannot both be enabled at the same time.");
                return false;
            }
            return true;
        }

        private void CollectRenderersFromSources()
        {
            var sources = sourceGameObjects.Where(s => s != null);

            SkinnedMeshRenderer[] skinnedRenderers;
            MeshRenderer[] meshRenderers;

            skinnedRenderers = onlyMesh
                ? new SkinnedMeshRenderer[0]
                : sources.SelectMany(s => s.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Distinct().ToArray();

            meshRenderers = onlySkinnedMesh
                ? new MeshRenderer[0]
                : sources.SelectMany(s => s.GetComponentsInChildren<MeshRenderer>(true)).Distinct().ToArray();

            if (onlyActiveGameObjects)
            {
                skinnedRenderers = skinnedRenderers.Where(r => r?.gameObject.activeInHierarchy == true).ToArray();
                meshRenderers = meshRenderers.Where(r => r?.gameObject.activeInHierarchy == true).ToArray();
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
            var seen = new System.Collections.Generic.HashSet<string>();
            return renderers.Where(r => r?.name != null && seen.Add(r.name)).ToArray();
        }

        private static MeshRenderer[] RemoveDuplicateNames(MeshRenderer[] renderers)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            return renderers.Where(r => r?.name != null && seen.Add(r.name)).ToArray();
        }

        private static Transform[] BuildArmatureMap(Transform armatureRoot)
        {
            return armatureRoot.GetComponentsInChildren<Transform>(true);
        }

        private static void TransferSkinnedMeshRenderer(
            SkinnedMeshRenderer smr,
            Transform[] armatureMap,
            Transform newParentTransform)
        {
            var rootBoneName = smr.rootBone?.name;
            if (string.IsNullOrEmpty(rootBoneName))
                throw new System.Exception("Root bone is missing.");

            var newBones = new Transform[smr.bones.Length];
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var boneName = smr.bones[i]?.name;
                if (string.IsNullOrEmpty(boneName))
                    throw new System.Exception($"Bone at index {i} is missing.");

                var newBone = armatureMap.FirstOrDefault(t => t.name == boneName);
                if (newBone == null)
                    throw new System.Exception($"Bone not found in new armature: '{boneName}'");

                newBones[i] = newBone;
            }

            var matchingRootBone = armatureMap.FirstOrDefault(t => t.name == rootBoneName);
            if (matchingRootBone == null)
                throw new System.Exception($"Root bone not found in new armature: '{rootBoneName}'");

            Undo.RecordObject(smr, "Transfer SkinnedMeshRenderer");
            Undo.RecordObject(smr.transform, "Transfer SkinnedMeshRenderer");

            smr.rootBone = matchingRootBone;
            smr.bones = newBones;
            smr.transform.SetParent(newParentTransform, false);
            smr.transform.localPosition = Vector3.zero;
        }

        private static void TransferMeshRenderer(MeshRenderer mr, Transform[] armatureMap)
        {
            var parentName = mr.transform.parent?.name;
            if (string.IsNullOrEmpty(parentName))
                throw new System.Exception("Parent bone is missing.");

            var targetParent = armatureMap.FirstOrDefault(t => t.name == parentName);
            if (targetParent == null)
                throw new System.Exception($"Parent bone not found in new armature: '{parentName}'");

            Undo.RecordObject(mr.transform, "Transfer MeshRenderer");
            mr.transform.SetParent(targetParent, false);
        }
    }
}
