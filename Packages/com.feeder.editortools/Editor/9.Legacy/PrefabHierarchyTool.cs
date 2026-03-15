using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using UnityEditor;

public class PrefabHierarchyTool : SerializedScriptableObject
{
    public struct HierarchyNodeInfo
    {
        public string Path;
        public string ChildSignature;

        public HierarchyNodeInfo(string path, string childSignature)
        {
            Path = path;
            ChildSignature = childSignature;
        }
    }

    [OnValueChanged("OnPrefabChanged")]
    [LabelText("Base Prefab")]
    public GameObject basePrefab;

    [Tooltip("This is the speed of the player in meters per second")]
    [InfoBox("Locate where the model will be create inside base prefab", InfoMessageType.Warning)]
    [ValueDropdown("GetPrefabTransforms", IsUniqueList = false, DropdownWidth = 400, DropdownHeight = 300, DrawDropdownForListElements = false)]
    public Transform locateModel;

    // cache list of transforms
    private List<ValueDropdownItem<Transform>> prefabTransforms;

    private void OnPrefabChanged()
    {
        prefabTransforms = null; // reset cache when prefab changes
    }

    private IEnumerable<ValueDropdownItem<Transform>> GetPrefabTransforms()
    {
        if (basePrefab == null) yield break;

        if (prefabTransforms == null)
        {
            prefabTransforms = new List<ValueDropdownItem<Transform>>();
            var root = basePrefab.transform;
            AddTransformToDropdown(prefabTransforms, root, root.name);
        }

        foreach (var item in prefabTransforms)
        {
            yield return item;
        }
    }

    private void AddTransformToDropdown(List<ValueDropdownItem<Transform>> list, Transform tr, string path)
    {
        list.Add(new ValueDropdownItem<Transform>(path, tr));

        foreach (Transform child in tr)
        {
            AddTransformToDropdown(list, child, path + "/" + child.name);
        }
    }

    public static List<HierarchyNodeInfo> CollectHierarchyInfo(Transform root, bool includeRoot)
    {
        var results = new List<HierarchyNodeInfo>();
        if (root == null) return results;

        if (includeRoot)
        {
            AddHierarchyInfo(results, root, root.name);
            return results;
        }

        foreach (Transform child in root)
        {
            AddHierarchyInfo(results, child, child.name);
        }

        return results;
    }

    private static void AddHierarchyInfo(List<HierarchyNodeInfo> results, Transform node, string path)
    {
        results.Add(new HierarchyNodeInfo(path, BuildChildSignature(node)));

        foreach (Transform child in node)
        {
            AddHierarchyInfo(results, child, path + "/" + child.name);
        }
    }

    private static string BuildChildSignature(Transform node)
    {
        var childNames = new List<string>();
        foreach (Transform child in node)
        {
            childNames.Add(child.name);
        }

        childNames.Sort();
        return string.Join("|", childNames);
    }
}
