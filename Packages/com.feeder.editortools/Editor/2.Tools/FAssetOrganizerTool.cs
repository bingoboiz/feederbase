using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public sealed class FAssetOrganizerTool : FTargetAssetsToolBase
    {
        private enum TransferMode { Move, Copy }

        protected override string GetDescription()
            => "Di chuyển TargetAssets (theo đúng thứ tự) vào thư mục đích. Kết hợp với Sort Order Tool để sắp xếp trước rồi dùng tool này để gom vào folder mong muốn.";

        [PropertyOrder(-800)]
        [FolderPath(AbsolutePath = false)]
        [LabelText("Destination Folder")]
        [ShowInInspector]
        private string DestinationFolder
        {
            get => FDataPersistenceService.GetOrCreateDataContainer().AssetOrganizerFolder;
            set
            {
                var c = FDataPersistenceService.GetOrCreateDataContainer();
                c.AssetOrganizerFolder = value;
                FDataPersistenceService.SaveData(c);
            }
        }

        [PropertyOrder(-790)]
        [LabelText("Mode")]
        [ShowInInspector]
        [SerializeField]
        private TransferMode _mode = TransferMode.Move;

        [PropertyOrder(0)]
        [Button("Organize Assets", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.8f, 0.3f)]
        private void OrganizeAssets()
        {
            var destFolder = DestinationFolder;
            if (string.IsNullOrEmpty(destFolder))
            {
                Debug.LogWarning("[Asset Organizer] Chưa chọn thư mục đích.");
                return;
            }

            var assets = TargetAssets;
            if (assets == null || assets.Count == 0)
            {
                Debug.LogWarning("[Asset Organizer] TargetAssets trống.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(destFolder))
            {
                Directory.CreateDirectory(destFolder);
                AssetDatabase.Refresh();
            }

            int successCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    if (asset == null) continue;

                    string sourcePath = AssetDatabase.GetAssetPath(asset);
                    if (string.IsNullOrEmpty(sourcePath)) continue;

                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = $"{destFolder}/{fileName}";

                    if (sourcePath == destPath) continue;

                    if (_mode == TransferMode.Copy)
                    {
                        if (AssetDatabase.CopyAsset(sourcePath, destPath))
                            successCount++;
                        else
                            Debug.LogWarning($"[Asset Organizer] Copy thất bại: {fileName}");
                    }
                    else
                    {
                        string error = AssetDatabase.MoveAsset(sourcePath, destPath);
                        if (string.IsNullOrEmpty(error))
                            successCount++;
                        else
                            Debug.LogWarning($"[Asset Organizer] Move thất bại '{fileName}': {error}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"<color=cyan>[Asset Organizer] {_mode} {successCount}/{assets.Count} asset → '{destFolder}'</color>");
        }
    }
}
