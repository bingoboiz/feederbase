using Sirenix.OdinInspector;
using UnityEngine;

namespace Feeder
{
    /// <summary>Base panel for Feeder Asset Cache modes.</summary>
    public abstract class FRefModeBase : SerializedScriptableObject
    {
        public string Description => GetDescription() ?? string.Empty;

        protected virtual string GetDescription() => null;

        internal virtual void OnHostSelectionChanged() { }

        protected static string[] TargetGuids => FRefData.GetTargetGuids();

        protected static FRefDatabase Db => FRefDatabase.instance;

        protected static FAssetCacheSettings Settings => FAssetCacheSettings.instance;

        protected static bool RequireDatabaseReady()
        {
            if (Db.IsReady) return true;
            UnityEditor.EditorUtility.DisplayDialog(
                "Feeder Asset Cache",
                "Database is not ready. Open Scan Database and click Scan / Refresh Database first.",
                "OK");
            return false;
        }
    }
}
