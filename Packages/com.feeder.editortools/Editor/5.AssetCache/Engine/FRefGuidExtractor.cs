using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace Feeder
{
    /// <summary>
    /// Extracts referenced asset GUIDs from a file. Independent re-implementation of an FR2-style
    /// parser: text/YAML assets are streamed line-by-line and every 32-char hex GUID is collected;
    /// binary assets fall back to Unity's own dependency database.
    ///
    /// Assumes the project uses Force Text serialization (Unity default). For binary-serialized
    /// YAML extensions the AssetDatabase fallback still covers them.
    /// </summary>
    public static class FRefGuidExtractor
    {
        // Extensions whose main file is human-readable YAML/text we can scan ourselves.
        private static readonly HashSet<string> YamlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".mat", ".asset", ".controller", ".overridecontroller", ".anim",
            ".physicmaterial", ".physicsmaterial2d", ".spriteatlas", ".spriteatlasv2", ".guiskin",
            ".fontsettings", ".mixer", ".rendertexture", ".terrainlayer", ".shadervariants",
            ".lighting", ".preset", ".playable", ".signal", ".mask", ".brush", ".uxml", ".uss",
            ".inputactions", ".scenetemplate", ".shadergraph", ".shadersubgraph", ".cubemap"
        };

        /// <summary>
        /// Collects all GUIDs referenced by the asset at <paramref name="path"/> into
        /// <paramref name="result"/>. The asset's own GUID is not removed here; the caller does that.
        /// </summary>
        public static void ExtractGuids(string path, HashSet<string> result)
        {
            if (string.IsNullOrEmpty(path)) return;

            // The .meta sidecar (always YAML) can carry importer references (e.g. model externalObjects).
            string meta = path + ".meta";
            if (File.Exists(meta)) ScanYaml(meta, result);

            string ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && YamlExtensions.Contains(ext))
            {
                ScanYaml(path, result);
            }
            else
            {
                // Binary asset (textures, models, audio, fonts, ...): rely on Unity's dependency graph.
                foreach (string dep in AssetDatabase.GetDependencies(path, false))
                {
                    if (dep == path) continue;
                    string g = AssetDatabase.AssetPathToGUID(dep);
                    if (!string.IsNullOrEmpty(g)) result.Add(g);
                }
            }
        }

        private static void ScanYaml(string file, HashSet<string> result)
        {
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true, 1 << 16))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length < 32) continue;
                        // Skip lines that contain non-GUID 32-hex blobs to avoid false positives.
                        if (line.IndexOf("spriteID:", StringComparison.Ordinal) >= 0) continue;
                        if (line.IndexOf("Hash:", StringComparison.Ordinal) >= 0) continue;
                        ExtractHexRuns(line, result);
                    }
                }
            }
            catch
            {
                // Unreadable/locked file - skip silently, it just yields no references.
            }
        }

        /// <summary>Adds every standalone 32-char hex run (a Unity GUID) found in the line.</summary>
        private static void ExtractHexRuns(string line, HashSet<string> result)
        {
            int count = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (IsHex(line[i]))
                {
                    count++;
                    if (count == 32)
                    {
                        bool nextHex = (i + 1 < line.Length) && IsHex(line[i + 1]);
                        if (!nextHex) result.Add(line.Substring(i - 31, 32));
                    }
                }
                else
                {
                    count = 0;
                }
            }
        }

        private static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
