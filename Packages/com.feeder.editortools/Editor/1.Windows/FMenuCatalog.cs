using System;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace Feeder
{
    internal static class FMenuCatalog
    {
        private const string AssetRoot = "Asset/";
        private const string PrefabRoot = "Prefab/";
        private const string OptimizedRoot = "Optimized/";

        public const string BatchSortOrderToolPath = AssetRoot + "Sort Order Tool";
        public const string BatchRenameToolPath = AssetRoot + "Rename Tool";
        public const string ScriptableObjectsFillerToolPath = AssetRoot + "Scriptable Objects Filler Tool";
        public const string CharacterMeshUpdaterToolPath = AssetRoot + "Character Mesh Updater Tool";
        public const string DataClonerToolPath = AssetRoot + "Data Cloner Tool";
        
        public const string BatchComponentModifyToolPath = PrefabRoot + "Component Modify Tool";
        public const string BatchPrefabModifyToolPath = PrefabRoot + "Prefab Modify Tool";
        public const string BatchPrefabVariantCreatorToolPath = PrefabRoot + "Prefab Variant Creator Tool";
        public const string BatchNameOffsetToolPath = PrefabRoot + "Name Offset Tool";
        public const string ModelScaleToColliderToolPath = PrefabRoot + "Model Scale To Collider Tool";
        public const string MeshBoxColliderFitterToolPath = PrefabRoot + "Mesh Box Collider Fitter Tool";
        public const string BatchComponentReplacerToolPath = PrefabRoot + "Component Replacer Tool";
        public const string BatchMissingComponentToolPath = PrefabRoot + "Missing Component Handler Tool";

        public const string UnpackMeshToolPath = OptimizedRoot + "Unpack Mesh Tool";
        public const string RepackModelsToolPath = OptimizedRoot + "Repack Models Tool";
        public const string DeduplicateMeshToolPath = OptimizedRoot + "Deduplicate Mesh Tool";
        public const string DeduplicateTextureToolPath = OptimizedRoot + "Deduplicate Texture Tool";
        public const string DeduplicateMaterialToolPath = OptimizedRoot + "Deduplicate Material Tool";

        public static void AddTools(OdinMenuTree tree)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            tree.Add(BatchSortOrderToolPath, ScriptableObject.CreateInstance<FSortOrderTool>(), FeederIconCatalog.SortOrderToolIcon);
            tree.Add(BatchRenameToolPath, ScriptableObject.CreateInstance<FRenameTool>(), FeederIconCatalog.RenameToolIcon);
            tree.Add(BatchComponentModifyToolPath, ScriptableObject.CreateInstance<FComponentModifyTool>(), FeederIconCatalog.ComponentModifyToolIcon);
            tree.Add(BatchPrefabModifyToolPath, ScriptableObject.CreateInstance<FPrefabModifyTool>(), FeederIconCatalog.PrefabModifyToolIcon);
            tree.Add(BatchPrefabVariantCreatorToolPath, ScriptableObject.CreateInstance<FPrefabVariantCreatorTool>(), FeederIconCatalog.PrefabVariantCreatorToolIcon);
            tree.Add(BatchNameOffsetToolPath, ScriptableObject.CreateInstance<FNameOffsetTool>(), FeederIconCatalog.NameOffsetToolIcon);
            tree.Add(ModelScaleToColliderToolPath, ScriptableObject.CreateInstance<FModelScaleToColliderTool>(), FeederIconCatalog.ModelScaleToColliderToolIcon);
            tree.Add(MeshBoxColliderFitterToolPath, ScriptableObject.CreateInstance<FMeshBoxColliderFitterTool>(), FeederIconCatalog.MeshBoxColliderFitterToolIcon);
            tree.Add(BatchComponentReplacerToolPath, ScriptableObject.CreateInstance<FComponentReplacerTool>(), FeederIconCatalog.ComponentReplacerToolIcon);
            tree.Add(BatchMissingComponentToolPath, ScriptableObject.CreateInstance<FMissingComponentHandlerTool>(), FeederIconCatalog.MissingScriptHandlerToolIcon);

            tree.Add(ScriptableObjectsFillerToolPath, ScriptableObject.CreateInstance<FScriptableObjectsFillerTool>(), FeederIconCatalog.ScriptableObjectsFillerToolIcon);
            tree.Add(CharacterMeshUpdaterToolPath, ScriptableObject.CreateInstance<FCharacterMeshUpdateTool>(), FeederIconCatalog.CharacterMeshUpdaterToolIcon);
            tree.Add(DataClonerToolPath, ScriptableObject.CreateInstance<FDataClonerTool>(), FeederIconCatalog.DataClonerToolIcon);

            tree.Add(UnpackMeshToolPath, ScriptableObject.CreateInstance<FUnpackMeshTool>(), FeederIconCatalog.DefaultToolIcon);
            tree.Add(DeduplicateTextureToolPath, ScriptableObject.CreateInstance<FDeduplicateTextureTool>(), FeederIconCatalog.DefaultToolIcon);
            tree.Add(DeduplicateMaterialToolPath, ScriptableObject.CreateInstance<FDeduplicateMaterialTool>(), FeederIconCatalog.DefaultToolIcon);
            tree.Add(DeduplicateMeshToolPath, ScriptableObject.CreateInstance<FDeduplicateMeshTool>(), FeederIconCatalog.DefaultToolIcon);
            tree.Add(RepackModelsToolPath, ScriptableObject.CreateInstance<FRepackModelsTool>(), FeederIconCatalog.DefaultToolIcon);
        }
    }
}
