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

        /// <summary>Non-null when extraction failed for this sub-asset (used to abort Apply).</summary>
        public string ExtractError;

        /// <summary>How many project references point at this exact sub-asset (filled by the preview scan).</summary>
        public int UsedByCount;

        public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;
        public string DisplayType => string.IsNullOrEmpty(TypeName) ? "Object" : TypeName;

        public string ExtractStatus
        {
            get
            {
                if (!string.IsNullOrEmpty(ExtractError)) return "Failed";
                if (!string.IsNullOrEmpty(ExtractedPath)) return "Extracted";
                return UsedByCount > 0 ? "Pending" : "Unused";
            }
        }
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
        public string SourceFbxPath;
        public bool Aborted;
        public int ExtractedCount;
        public int TouchedAssetCount;
        public int ReplacedReferenceCount;
        public int RemainingReferenceCount;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> RemainingReferrers = new List<string>();
        public readonly List<string> Logs = new List<string>();

        public bool Verified => !Aborted && RemainingReferenceCount == 0 && Errors.Count == 0;
    }

    /// <summary>Aggregated result for unpacking a batch of FBX files in one Apply.</summary>
    public sealed class FFbxUnpackBatchResult
    {
        public readonly List<FFbxUnpackApplyResult> Results = new List<FFbxUnpackApplyResult>();

        public int TotalExtracted => Sum(r => r.ExtractedCount);
        public int TotalTouched => Sum(r => r.TouchedAssetCount);
        public int TotalReplaced => Sum(r => r.ReplacedReferenceCount);
        public int TotalRemaining => Sum(r => r.RemainingReferenceCount);
        public int AbortedCount => Count(r => r.Aborted);
        public int FullySeveredCount => Count(r => r.Verified);

        public bool AllVerified
        {
            get
            {
                if (Results.Count == 0) return false;
                foreach (FFbxUnpackApplyResult r in Results)
                    if (!r.Verified) return false;
                return true;
            }
        }

        private int Sum(Func<FFbxUnpackApplyResult, int> selector)
        {
            int total = 0;
            foreach (FFbxUnpackApplyResult r in Results) total += selector(r);
            return total;
        }

        private int Count(Func<FFbxUnpackApplyResult, bool> predicate)
        {
            int total = 0;
            foreach (FFbxUnpackApplyResult r in Results) if (predicate(r)) total++;
            return total;
        }
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

        public string SourceFbxName =>
            string.IsNullOrEmpty(SourceFbxPath) ? "(none)" : System.IO.Path.GetFileName(SourceFbxPath);

        /// <summary>All reference hits that point at one specific sub-asset (for the "where used" panel).</summary>
        public List<FFbxReferenceHit> GetHitsForKey(FFbxSubAssetKey key)
        {
            var list = new List<FFbxReferenceHit>();
            foreach (FFbxReferenceHit hit in ReferenceHits)
                if (hit.SourceKey.Equals(key))
                    list.Add(hit);
            return list;
        }
    }
}
