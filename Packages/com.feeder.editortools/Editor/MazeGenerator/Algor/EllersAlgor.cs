using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;

/// <summary>
/// Simplified Eller's algorithm. Produces perfect maze row-by-row.
/// Note: this is a concise, readable variant rather than the absolutely optimal/complex version.
/// </summary>

namespace Feeder
{
    public class EllersAlgor : IMazeAlgorithms
    {
        private System.Random _random;
        public EllersAlgor(int? seed = null)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            int rows = maze.Rows;
            int cols = maze.Cols;

            int[] sets = new int[cols];
            int nextSetId = 1;

            // init first row sets
            for (int c = 0; c < cols; c++) sets[c] = nextSetId++;

            for (int r = 0; r < rows; r++)
            {
                // 1) Join adjacent cells randomly (except last row we'll handle later)
                for (int c = 0; c < cols - 1; c++)
                {
                    // if different sets, randomly join them
                    if (sets[c] != sets[c + 1] && (_random.Next(0, 2) == 0 || r == rows - 1))
                    {
                        // join: unify sets by replacing set ids
                        int old = sets[c + 1];
                        int keep = sets[c];
                        for (int k = 0; k < cols; k++) if (sets[k] == old) sets[k] = keep;
                        // remove right wall
                        var a = maze.GetCell(r, c);
                        var b = maze.GetCell(r, c + 1);
                        maze.RemoveWallBetween(a, b);
                    }
                }

                // visualize current row
                for (int c = 0; c < cols; c++) { var cc = maze.GetCell(r, c); cc.isVisited = true; }
                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);

                if (r == rows - 1) break; // last row done (we already joined everything horizontally)

                // 2) Create vertical connections: for each set, ensure at least one cell connects down
                var newSets = new int[cols];
                for (int c = 0; c < cols; c++) newSets[c] = 0;

                // group indices by set
                var groups = new Dictionary<int, List<int>>();
                for (int c = 0; c < cols; c++)
                {
                    if (!groups.ContainsKey(sets[c])) groups[sets[c]] = new List<int>();
                    groups[sets[c]].Add(c);
                }

                foreach (var kv in groups)
                {
                    var members = kv.Value;
                    // ensure at least one member creates a down connection
                    int atLeastOne = _random.Next(members.Count);
                    for (int i = 0; i < members.Count; i++)
                    {
                        int c = members[i];
                        bool carveDown = (i == atLeastOne) || (_random.Next(0, 2) == 0);
                        if (carveDown)
                        {
                            // remove wall between (r,c) and (r+1,c)
                            var a = maze.GetCell(r, c);
                            var b = maze.GetCell(r + 1, c);
                            maze.RemoveWallBetween(a, b);

                            // assign same set id to cell in next row
                            newSets[c] = sets[c];
                        }
                    }
                }

                // 3) For cells in next row that didn't get a set, assign new set ids
                for (int c = 0; c < cols; c++)
                {
                    if (newSets[c] == 0) newSets[c] = nextSetId++;
                }

                // move to next row
                sets = newSets;

                // optional visualization
                for (int c = 0; c < cols; c++) { var cc = maze.GetCell(r + 1, c); cc.isVisited = true; }
                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);
            }

            maze.currentMazeCell = null;
            window?.Repaint();
        }
    }

}
