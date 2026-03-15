using System;
using UnityEngine;

namespace Feeder
{
    public static class HierarchyPathResolver
    {
        public static Transform ResolveTargetByPath(Transform root, string path)
        {
            if (root == null)
                throw new InvalidOperationException("root is null.");
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("path is empty.");

            var current = root;
            var parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                current = current?.Find(parts[i]);
                if (current == null)
                    throw new InvalidOperationException($"path '{path}' not found.");
            }

            return current;
        }
    }
}
