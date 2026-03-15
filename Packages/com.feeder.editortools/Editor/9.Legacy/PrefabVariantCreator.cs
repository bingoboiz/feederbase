using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System.Collections.Generic;

namespace Feeder
{
    public class PrefabVariantCreatorWindow : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Legacy/Prefab Variant Creator")]
        private static void ShowWindow()
        {
            GetWindow<PrefabVariantCreatorWindow>("Prefab Variant Creator");
        }

        [InlineEditor(InlineEditorModes.GUIOnly, InlineEditorObjectFieldModes.Hidden)]
        public PrefabHierarchyTool toolAsset;
        protected override void Initialize()
        {
            if (toolAsset == null)
            {
                toolAsset = CreateInstance<PrefabHierarchyTool>();
            }
        }

        [LabelText("Save Folder")]
        [FolderPath(AbsolutePath = false, RequireExistingPath = true)]
        public string saveFolderPath;

        public float offsetY = 0.1f;
        public bool checkingMissingScripts = true;

        public bool autoModelsScale = false;
        [ShowIf("autoModelsScale")]
        public bool useColliderBounds = false;

        private string folderPath;
        private Transform holder;

        [Title("Models")]
        [InfoBox("Drag the prefabs/gameobjects here")]
        [ListDrawerSettings(
            ShowFoldout = true,
            DraggableItems = true,
            ShowIndexLabels = true,
            NumberOfItemsPerPage = 10)]
        public List<GameObject> models = new List<GameObject>();

        [Button(ButtonSizes.Large), GUIColor(0.3f, 0.8f, 1f)]
        public void CreateVariants()
        {
            folderPath = saveFolderPath;
            if (!IsReadyToRunTool()) return;

            for (int i = 0; i < models.Count; i++)
            {
                var sceneObject = models[i];
                if (sceneObject == null) continue;

                string indexedName = $"{sceneObject.name}";
                string variantAssetPath = $"{folderPath}/{indexedName}.prefab";

                // Create temporary prefab instance from base prefab
                GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(toolAsset.basePrefab);

                GameObject clone = null;
                if (toolAsset?.locateModel == null)
                {
                    clone = Instantiate(sceneObject, temp.transform, true);
                    clone.name = indexedName;
                    clone.transform.localPosition = Vector3.zero;
                    MoveChildrenToParentAndDestroyRoot(clone.transform, temp.transform);
                }
                else
                {
                    string holderPath = AnimationUtility.CalculateTransformPath(toolAsset.locateModel, toolAsset.basePrefab.transform);
                    holder = temp.transform.Find(holderPath);
                    if (holder == null)
                    {
                        Debug.LogError($"<color=red>Base prefab is missing 'Holder' child!</color>");
                        DestroyImmediate(temp);
                        continue;
                    }

                    // Clone into Holder instead of prefab root
                    clone = Instantiate(sceneObject, holder, true);
                    clone.name = indexedName;
                    clone.transform.localPosition = Vector3.zero;
                }

                if (autoModelsScale && useColliderBounds)
                {
                    Collider col = toolAsset.basePrefab.GetComponent<Collider>();
                    if (col == null)
                    {
                        Debug.LogError($"<color=red>Base prefab {toolAsset.basePrefab.name} has no Collider!</color>");
                    }
                    else
                    {
                        Vector3 localSize = Vector3.zero;

                        if (col is BoxCollider box)
                            localSize = box.size;
                        else if (col is SphereCollider sphere)
                            localSize = Vector3.one * (sphere.radius * 2f);
                        else if (col is CapsuleCollider capsule)
                            localSize = new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f);
                        else
                            Debug.LogWarning($"Collider type {col.GetType().Name} not fully supported.");

                        // Convert to world size
                        Vector3 colSize = Vector3.Scale(localSize, col.transform.lossyScale);

                        // Get bounds of all meshes in the cloned object
                        if (TryGetCombinedWorldBounds(clone.transform, out Bounds meshBounds))
                        {
                            Vector3 meshSize = meshBounds.size;
                            Debug.Log($"<color=cyan>Collider Size: {colSize}, Mesh Size: {meshSize}</color>");

                            if (meshSize.x > 0 && meshSize.y > 0 && meshSize.z > 0)
                            {
                                float scaleX = colSize.x / meshSize.x;
                                float scaleY = colSize.y / meshSize.y;
                                float scaleZ = colSize.z / meshSize.z;

                                float uniformScale = Mathf.Min(scaleX, scaleY, scaleZ);
                                clone.transform.localScale *= uniformScale;

                                Debug.Log($"<color=cyan>Scaling {clone.name} by {uniformScale}</color>");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"<color=yellow>Could not compute bounds for {clone.name}</color>");
                        }
                    }
                }

                if (checkingMissingScripts) RemoveMissingScripts(temp);

                if (toolAsset?.locateModel != null)
                {
                    PlaceNameAboveMesh(temp, holder);
                }

                SavePrefab(variantAssetPath, temp);
            }
            AssetDatabase.Refresh();
            Debug.Log($"<color=green>Prefab variants created successfully in: {folderPath}</color>");
        }

        private void RemoveMissingScripts(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
        }

        private void PlaceNameAboveMesh(GameObject prefabVariant, Transform holder)
        {
            if (prefabVariant == null)
            {
                Debug.Log($"<color=red>PrefabVariant is null</color>");
                return;
            }

            if (holder == null || holder.childCount < 2)
            {
                Debug.Log($"<color=red>{prefabVariant.name} must contain Holder with at least 2 children</color>");
                return;
            }

            Transform nameChild = holder.GetChild(0);
            Transform meshChild = holder.GetChild(1);

            if (!TryGetCombinedWorldBounds(meshChild, out Bounds combined))
            {
                Debug.Log($"<color=red>{meshChild.name} has no active MeshRenderer</color>");
                return;
            }

            // Convert bounds minY into local Holder space
            Vector3 minWorld = new Vector3(combined.center.x, combined.min.y, combined.center.z);
            float minLocalY = holder.InverseTransformPoint(minWorld).y;

            // If mesh is below ground, move it up
            if (minLocalY < 0f)
            {
                Vector3 meshLocalPos = meshChild.localPosition;
                meshLocalPos.y -= minLocalY; // push it up so minY = 0
                meshChild.localPosition = meshLocalPos;

                // recompute bounds after shifting
                TryGetCombinedWorldBounds(meshChild, out combined);
            }

            // World position of the top of the combined bounds
            Vector3 topWorld = new Vector3(combined.center.x, combined.max.y, combined.center.z);

            // Convert to local Y relative to Holder
            float topLocalY = holder.InverseTransformPoint(topWorld).y;

            // Update local position of the name object
            Vector3 lp = nameChild.localPosition;
            lp.y = topLocalY + offsetY;
            nameChild.localPosition = lp;
        }

        private bool TryGetCombinedWorldBounds(Transform root, out Bounds combined)
        {
            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);

            combined = default;
            bool initialized = false;

            foreach (var r in meshRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                if (!initialized)
                {
                    combined = r.bounds; // first valid bounds
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(r.bounds); // expand combined bounds
                }
            }

            return initialized;
        }

        private void SavePrefab(string path, GameObject finalPrefab)
        {
            PrefabUtility.SaveAsPrefabAsset(finalPrefab, path);
            DestroyImmediate(finalPrefab);
        }

        private bool IsReadyToRunTool()
        {
            if (toolAsset?.basePrefab == null)
            {
                EditorUtility.DisplayDialog("Error", "You must assign a Base Prefab!", "OK");
                return false;
            }

            if (string.IsNullOrEmpty(saveFolderPath))
            {
                EditorUtility.DisplayDialog("Error", "You must select a folder to save the prefab variants!", "OK");
                return false;
            }

            if (models == null || models.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "The Scene Object List is empty!", "OK");
                return false;
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("Error", "The selected save path is not a valid folder!", "OK");
                return false;
            }

            else return true;
        }

        private void MoveChildrenToParentAndDestroyRoot(Transform sourceRoot, Transform targetParent)
        {
            if (sourceRoot == null || targetParent == null) return;

            for (int i = sourceRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = sourceRoot.GetChild(i);
                child.SetParent(targetParent, true);
            }

            DestroyImmediate(sourceRoot.gameObject);
        }

        [Button(ButtonSizes.Large), GUIColor("orange")]
        public void ClearList()
        {
            if (EditorUtility.DisplayDialog("Clear List?",
                "Are you sure you want to clear all scene objects from the list?",
                "Yes", "No"))
            {
                models.Clear();
            }
        }
    }

}
