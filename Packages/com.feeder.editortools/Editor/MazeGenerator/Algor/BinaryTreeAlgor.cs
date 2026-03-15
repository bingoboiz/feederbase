using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;

/// <summary>
/// Binary Tree algorithm: for each cell choose either right or down (if available).
/// This implementation marks cells visited as it iterates so the editor can highlight them.
/// </summary>

namespace Feeder
{
    public class BinaryTreeAlgor : IMazeAlgorithms
    {
        private System.Random _random;

        public BinaryTreeAlgor(int? seed = null)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            // Reset visited flags only (keep walls intact or let ResetVisited behaviour define)
            maze.ResetVisited();

            int rows = maze.Rows;
            int cols = maze.Cols;
            int total = rows * cols;
            int visitedCount = 0;

            // Iterate columns then rows (or rows then cols) — consistency matters for visuals
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var current = maze.GetCell(r, c);
                    if (current == null) continue;

                    // mark visited (for highlight)
                    if (!current.isVisited)
                    {
                        current.isVisited = true;
                        visitedCount++;
                    }

                    maze.currentMazeCell = current;

                    var choices = new List<MazeCell>(2);
                    if (maze.InBounds(r, c + 1)) choices.Add(maze.GetCell(r, c + 1)); // right
                    if (maze.InBounds(r + 1, c)) choices.Add(maze.GetCell(r + 1, c)); // down

                    if (choices.Count > 0)
                    {
                        var chosen = choices[_random.Next(choices.Count)];
                        // remove wall between current and chosen
                        maze.RemoveWallBetween(current, chosen);
                    }

                    // repaint and wait a frame to animate in editor
                    window?.Repaint();
                    yield return new EditorWaitForSeconds(delaySeconds);
                }
            }

            // final repaint
            maze.currentMazeCell = null;
            window?.Repaint();
        }
    }

}
