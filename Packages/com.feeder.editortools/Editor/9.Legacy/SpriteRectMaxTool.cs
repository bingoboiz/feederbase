using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Feeder
{
    public class SpriteRectMaxTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Sprite Rect Max Tool")]
        private static void OpenWindow()
        {
            GetWindow<SpriteRectMaxTool>("Sprite Rect Max Tool").Show();
        }

        [Title("Target Sprites")]
        [InfoBox("Kéo Sprite assets vào đây, sau đó bấm Apply Max Rect để reset về full texture size (X:0, Y:0, W:full, H:full).")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public List<Sprite> targetSprites = new List<Sprite>();

        [PropertySpace(SpaceBefore = 10)]
        [Button("Apply Max Rect", ButtonSizes.Large)]
        private void ApplyMaxRect()
        {
            if (targetSprites == null || targetSprites.Count == 0)
            {
                Debug.LogWarning("[SpriteRectMaxTool] Chưa có sprite nào trong danh sách.");
                return;
            }

            var factory = new SpriteDataProviderFactories();
            factory.Init();

            int count = 0;
            var processed = new HashSet<string>();

            foreach (var sprite in targetSprites)
            {
                if (sprite == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(assetPath)) continue;

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;

                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                var fullRect = new Rect(0, 0, w, h);

                var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
                if (dataProvider == null) continue;

                dataProvider.InitSpriteEditorDataProvider();
                SpriteRect[] spriteRects = dataProvider.GetSpriteRects();
                bool changed = false;

                if (importer.spriteImportMode == SpriteImportMode.Single)
                {
                    if (spriteRects.Length == 0)
                    {
                        dataProvider.SetSpriteRects(new[]
                        {
                            new SpriteRect
                            {
                                name      = sprite.name,
                                rect      = fullRect,
                                pivot     = new Vector2(0.5f, 0.5f),
                                alignment = SpriteAlignment.Center
                            }
                        });
                        changed = true;
                        count++;
                    }
                    else if (spriteRects[0].rect != fullRect)
                    {
                        spriteRects[0].rect = fullRect;
                        dataProvider.SetSpriteRects(spriteRects);
                        changed = true;
                        count++;
                    }
                }
                else if (importer.spriteImportMode == SpriteImportMode.Multiple)
                {
                    for (int i = 0; i < spriteRects.Length; i++)
                    {
                        if (spriteRects[i].name != sprite.name) continue;
                        if (spriteRects[i].rect == fullRect) break;
                        spriteRects[i].rect = fullRect;
                        dataProvider.SetSpriteRects(spriteRects);
                        changed = true;
                        count++;
                        break;
                    }
                }

                if (!changed) continue;

                dataProvider.Apply();
                importer.SaveAndReimport();
                processed.Add(assetPath);
            }

            Debug.Log($"[SpriteRectMaxTool] Đã reset {count} sprite(s) trên {processed.Count} texture(s).");
        }
    }
}
