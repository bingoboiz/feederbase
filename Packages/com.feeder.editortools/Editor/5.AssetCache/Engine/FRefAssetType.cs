using System;
using System.IO;
using UnityEditor;

namespace Feeder
{
    /// <summary>Coarse asset classification used for grouping/sorting in the Find Reference tool.</summary>
    public enum FRefAssetType
    {
        Unknown,
        Folder,
        Script,
        Scene,
        Prefab,
        Material,
        Texture,
        Model,
        Audio,
        Animation,
        AnimatorController,
        ScriptableObject,
        Shader,
        ShaderGraph,
        Font,
        Atlas,
        Other
    }

    public static class FRefAssetTypeUtil
    {
        public static FRefAssetType FromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return FRefAssetType.Unknown;
            if (AssetDatabase.IsValidFolder(path)) return FRefAssetType.Folder;

            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return FRefAssetType.Other;
            ext = ext.ToLowerInvariant();

            switch (ext)
            {
                case ".cs": case ".dll": case ".asmdef": case ".asmref":
                    return FRefAssetType.Script;
                case ".unity":
                    return FRefAssetType.Scene;
                case ".prefab":
                    return FRefAssetType.Prefab;
                case ".mat": case ".physicmaterial": case ".physicsmaterial2d":
                    return FRefAssetType.Material;
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".tif":
                case ".tiff": case ".psd": case ".bmp": case ".exr": case ".gif":
                case ".hdr": case ".rendertexture": case ".cubemap": case ".texture2darray":
                    return FRefAssetType.Texture;
                case ".fbx": case ".obj": case ".dae": case ".3ds": case ".blend": case ".max":
                    return FRefAssetType.Model;
                case ".wav": case ".mp3": case ".ogg": case ".aiff": case ".aif": case ".flac":
                    return FRefAssetType.Audio;
                case ".anim": case ".playable": case ".signal":
                    return FRefAssetType.Animation;
                case ".controller": case ".overridecontroller": case ".mask":
                    return FRefAssetType.AnimatorController;
                case ".asset": case ".preset": case ".mixer": case ".guiskin": case ".terrainlayer":
                    return FRefAssetType.ScriptableObject;
                case ".shader": case ".compute": case ".cginc": case ".hlsl": case ".shadervariants":
                    return FRefAssetType.Shader;
                case ".shadergraph": case ".shadersubgraph":
                    return FRefAssetType.ShaderGraph;
                case ".ttf": case ".otf": case ".fontsettings":
                    return FRefAssetType.Font;
                case ".spriteatlas": case ".spriteatlasv2":
                    return FRefAssetType.Atlas;
                default:
                    return FRefAssetType.Other;
            }
        }
    }
}
