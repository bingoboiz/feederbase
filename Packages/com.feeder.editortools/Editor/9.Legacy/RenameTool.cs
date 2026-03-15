using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder {
    public class RenameTool : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Rename Tool")]
        private static void OpenWindow()
        {
            GetWindow<RenameTool>("Rename Tool").Show();
        }

        [Title("Target Prefabs")]
        [InfoBox("Drag your Prefabs/GameObjects here")]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        public List<GameObject> targetObjects = new List<GameObject>();

        [Title("Rename Options")]
        [PropertySpace(SpaceBefore = 10, SpaceAfter = 6)]
        [LabelText("Pattern"), Tooltip("use {index} and {original}")]
        public string pattern = "{original}";

        [LabelText("Start From"), MinValue(0)]
        public int startNumber = 0;

        [LabelText("Step"), MinValue(1)]
        public int step = 1;

        [Title("Common Substring Replace")]
        [PropertySpace(SpaceBefore = 8, SpaceAfter = 4)]
        [DisplayAsString, InlineButton("DetectCommon", "Detect"), LabelText("Detected Common"), Tooltip("common substring among all names")]
        public string commonSubstring = string.Empty;

        private void DetectCommon()
        {
            commonSubstring = FindCommonSubstringOfAll(targetObjects);
        }

        [ToggleLeft, LabelText("Replace detected common substring"), Tooltip("replace the detected common substring with a custom value during rename")]
        public bool replaceCommonSubstring = false;

        [EnableIf("replaceCommonSubstring"), LabelText("Replacement"), Tooltip("custom replacement for detected common substring")]
        public string commonReplacement = string.Empty;

        [ToggleLeft, LabelText("Use only detected common as base"), Tooltip("remove all other parts and build name from detected common or its replacement")]
        public bool useOnlyCommonAsBase = false;

        [PropertySpace(SpaceBefore = 8, SpaceAfter = 6)]
        [Button("Rename Objects", ButtonSizes.Large)]
        private void RenameObjects()
        {
            if (targetObjects == null || targetObjects.Count == 0)
                return;

            int current = startNumber;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var go in targetObjects)
            {
                if (go == null) continue;

                string original = go.name;
                string baseName = original;

                // handle common substring usage and replacement
                if (useOnlyCommonAsBase)
                {
                    string chosen = !string.IsNullOrEmpty(commonReplacement) ? commonReplacement : commonSubstring;
                    baseName = chosen ?? string.Empty;
                }
                else if (replaceCommonSubstring && !string.IsNullOrEmpty(commonSubstring))
                {
                    baseName = baseName.Replace(commonSubstring, commonReplacement);
                }

                string newName = ApplyPattern(pattern, baseName, current);
                current += step;

                if (string.IsNullOrEmpty(newName))
                {
                    continue;
                }

                if (newName != go.name)
                {
                    RenameObject(go, newName);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        private static string FindCommonSubstringOfAll(List<GameObject> objects)
        {
            if (objects == null || objects.Count == 0) return string.Empty;

            // collect names and guard against nulls
            var names = new List<string>();
            foreach (var go in objects)
            {
                if (go != null && !string.IsNullOrEmpty(go.name))
                    names.Add(go.name);
            }

            if (names.Count == 0) return string.Empty;

            string first = names[0];
            if (names.Count == 1) return first;

            string best = string.Empty;
            int len = first.Length;

            // search longest-first to stop early when found
            for (int subLen = len; subLen > 0; subLen--)
            {
                bool foundAtThisLength = false;
                for (int start = 0; start + subLen <= len; start++)
                {
                    string candidate = first.Substring(start, subLen);
                    bool inAll = true;
                    for (int i = 1; i < names.Count; i++)
                    {
                        if (!names[i].Contains(candidate))
                        {
                            inAll = false;
                            break;
                        }
                    }
                    if (inAll)
                    {
                        best = candidate;
                        foundAtThisLength = true;
                        break;
                    }
                }
                if (foundAtThisLength)
                    break;
            }

            return best;
        }

        private static string ApplyPattern(string namePattern, string originalName, int index)
        {
            return namePattern?
                .Replace("{index}", index.ToString())
                .Replace("{original}", originalName);
        }

        private static void RenameObject(GameObject targetObject, string newName)
        {
            var assetPath = AssetDatabase.GetAssetPath(targetObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(targetObject);
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                var renameError = AssetDatabase.RenameAsset(assetPath, newName);
                if (!string.IsNullOrEmpty(renameError))
                {
                    throw new System.Exception(renameError);
                }

                var instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
                if (instanceRoot != null)
                {
                    Undo.RecordObject(instanceRoot, "Rename Objects");
                    instanceRoot.name = newName;
                    EditorUtility.SetDirty(instanceRoot);
                }

                /*AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();*/
                return;
            }

            Undo.RecordObject(targetObject, "Rename Objects");
            targetObject.name = newName;
            EditorUtility.SetDirty(targetObject);
        }
    }
}
