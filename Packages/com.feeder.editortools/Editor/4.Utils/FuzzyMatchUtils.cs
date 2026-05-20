using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Feeder
{
    public static class FuzzyMatchUtils
    {
        // Splits PascalCase / camelCase boundaries: "FlowerChoker" → ["Flower", "Choker"]
        private static readonly Regex CamelSplit = new Regex(
            @"(?<=[a-z\d])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
            RegexOptions.Compiled);

        // Splits on common separators: _, -, space, dot
        private static readonly Regex SepSplit = new Regex(
            @"[_\-\s\.]+",
            RegexOptions.Compiled);

        /// <summary>
        /// Normalizes a name for fuzzy comparison:
        /// splits by delimiters and camelCase, lowercases all tokens,
        /// sorts tokens alphabetically (neutralizes reordering), then concatenates.
        /// </summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var byDelimiter = SepSplit.Split(s);
            var tokens = new List<string>();
            for (int i = 0; i < byDelimiter.Length; i++)
            {
                var part = byDelimiter[i];
                if (string.IsNullOrEmpty(part)) continue;
                var subParts = CamelSplit.Split(part);
                for (int j = 0; j < subParts.Length; j++)
                {
                    if (!string.IsNullOrEmpty(subParts[j]))
                        tokens.Add(subParts[j].ToLowerInvariant());
                }
            }
            tokens.Sort(StringComparer.Ordinal);
            return string.Concat(tokens);
        }

        /// <summary>
        /// Returns similarity in [0, 1] between two already-normalized strings.
        /// Uses Levenshtein distance: 1 - dist / max(len_a, len_b).
        /// </summary>
        public static float Similarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1f;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
            int dist = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            return 1f - (float)dist / maxLen;
        }

        // Two-row rolling Levenshtein (O(n) space instead of O(m*n))
        private static int LevenshteinDistance(string a, string b)
        {
            int m = a.Length, n = b.Length;
            var prev = new int[n + 1];
            var curr = new int[n + 1];
            for (int j = 0; j <= n; j++) prev[j] = j;
            for (int i = 1; i <= m; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= n; j++)
                {
                    curr[j] = a[i - 1] == b[j - 1]
                        ? prev[j - 1]
                        : 1 + Math.Min(prev[j - 1], Math.Min(prev[j], curr[j - 1]));
                }
                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[n];
        }
    }
}
