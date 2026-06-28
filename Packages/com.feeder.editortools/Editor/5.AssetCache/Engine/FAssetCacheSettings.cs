using UnityEditor;
using UnityEngine;

namespace Feeder
{
    [FilePath("UserSettings/Feeder/FAssetCacheSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class FAssetCacheSettings : ScriptableSingleton<FAssetCacheSettings>
    {
        [SerializeField] private bool recursive;
        [SerializeField] private int maxDepth;

        public bool Recursive
        {
            get => recursive;
            set
            {
                if (recursive == value) return;
                recursive = value;
                Save(true);
            }
        }

        public int MaxDepth
        {
            get => Mathf.Max(0, maxDepth);
            set
            {
                int next = Mathf.Max(0, value);
                if (maxDepth == next) return;
                maxDepth = next;
                Save(true);
            }
        }
    }
}
