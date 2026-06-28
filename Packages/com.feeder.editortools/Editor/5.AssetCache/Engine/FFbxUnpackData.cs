using System;
using System.Collections.Generic;
using UnityEngine;

namespace Feeder
{
    [Serializable]
    public struct FFbxSubAssetKey : IEquatable<FFbxSubAssetKey>
    {
        public string Guid;
        public long LocalFileId;

        public FFbxSubAssetKey(string guid, long localFileId)
        {
            Guid = guid;
            LocalFileId = localFileId;
        }

        public bool IsValid => !string.IsNullOrEmpty(Guid) && LocalFileId != 0;

        public bool Equals(FFbxSubAssetKey other)
        {
            return string.Equals(Guid, other.Guid, StringComparison.OrdinalIgnoreCase)
                   && LocalFileId == other.LocalFileId;
        }

        public override bool Equals(object obj)
        {
            return obj is FFbxSubAssetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Guid != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Guid) : 0) * 397)
                       ^ LocalFileId.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{Guid}:{LocalFileId}";
        }
    }

    [Serializable]
    public sealed class FFbxSubAssetInfo
    {
        public FFbxSubAssetKey Key;
        public UnityEngine.Object SourceObject;
        public string Name;
        public string TypeName;
        public string SourcePath;
        public string ExtractedPath;
        public UnityEngine.Object ExtractedObject;

        public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;
        public string DisplayType => string.IsNullOrEmpty(TypeName) ? "Object" : TypeName;
    }

    [Serializable]
    public sealed class FFbxReferenceHit
    {
        public string AssetPath;
        public string ReferenceKind;
        public FFbxSubAssetKey SourceKey;
        public string SourceName;
        public string SourceType;
        public int Count;
        public bool Writable;
        public string Status;
    }

    [Serializable]
    public sealed class FFbxUnpackApplyResult
    {
        public int ExtractedCount;
        public int TouchedAssetCount;
        public int ReplacedReferenceCount;
        public int RemainingReferenceCount;
        public readonly List<string> Logs = new List<string>();
    }

    [Serializable]
    public sealed class FFbxUnpackPlan
    {
        public string SourceFbxPath;
        public string SourceFbxGuid;
        public string SaveFolderPath;
        public string RootOutputFolderPath;
        public readonly List<FFbxSubAssetInfo> SubAssets = new List<FFbxSubAssetInfo>();
        public readonly List<string> CandidateAssetPaths = new List<string>();
        public readonly List<FFbxReferenceHit> ReferenceHits = new List<FFbxReferenceHit>();
        public readonly List<string> Warnings = new List<string>();
        public readonly Dictionary<FFbxSubAssetKey, UnityEngine.Object> ExtractedAssetMap =
            new Dictionary<FFbxSubAssetKey, UnityEngine.Object>();

        public bool HasPreview => !string.IsNullOrEmpty(SourceFbxPath);
        public bool HasReferences => ReferenceHits.Count > 0;
    }
}
