using System.Collections.Generic;
using UnityEditor;

namespace Feeder
{
    /// <summary>A single hit in a Used-By / Uses traversal, with its depth from the target roots.</summary>
    public struct FRefResult
    {
        public FRefAsset asset;
        public int depth;
        public string parentGuid;
        public string rootGuid;
    }

    /// <summary>
    /// Breadth-first traversal of the dependency graph from a set of target GUIDs.
    /// Target roots themselves are excluded from the result.
    /// </summary>
    public static class FRefQuery
    {
        public static List<FRefResult> FindUsedBy(IEnumerable<string> targetGuids, bool recursive, int maxDepth)
        {
            return Traverse(targetGuids, usedBy: true, recursive: recursive, maxDepth: maxDepth);
        }

        public static List<FRefResult> FindUses(IEnumerable<string> targetGuids, bool recursive, int maxDepth)
        {
            return Traverse(targetGuids, usedBy: false, recursive: recursive, maxDepth: maxDepth);
        }

        private static List<FRefResult> Traverse(IEnumerable<string> roots, bool usedBy, bool recursive, int maxDepth)
        {
            var result = new List<FRefResult>();
            var db = FRefDatabase.instance;
            if (!db.IsReady) return result;
            db.EnsureUsedBy();

            var visited = new HashSet<string>();
            var queue = new Queue<(string guid, int depth, string parentGuid, string rootGuid)>();

            foreach (string g in ExpandTargets(roots))
                if (visited.Add(g)) queue.Enqueue((g, 0, null, g));

            while (queue.Count > 0)
            {
                var (guid, depth, parentGuid, rootGuid) = queue.Dequeue();
                var asset = db.Get(guid);
                if (asset == null) continue;

                if (depth > 0)
                    result.Add(new FRefResult
                    {
                        asset = asset,
                        depth = depth,
                        parentGuid = parentGuid,
                        rootGuid = rootGuid
                    });

                // Stop expanding past depth 1 when non-recursive, or past the depth cap.
                if (!recursive && depth >= 1) continue;
                if (maxDepth > 0 && depth >= maxDepth) continue;

                IEnumerable<string> next = usedBy
                    ? (asset.usedBy != null ? asset.usedBy.Keys : null)
                    : asset.useGuids;
                if (next == null) continue;

                foreach (string n in next)
                    if (visited.Add(n)) queue.Enqueue((n, depth + 1, guid, rootGuid));
            }

            return result;
        }

        /// <summary>Expands folder targets into the GUIDs of the assets they contain.</summary>
        private static IEnumerable<string> ExpandTargets(IEnumerable<string> guids)
        {
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    foreach (string sub in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                        yield return sub;
                }
                else
                {
                    yield return g;
                }
            }
        }
    }
}
