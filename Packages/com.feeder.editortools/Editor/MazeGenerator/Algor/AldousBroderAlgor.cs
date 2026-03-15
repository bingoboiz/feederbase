using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor;

/// <summary>
/// Aldous-Broder algorithm: random walk until all cells visited.
/// Very simple: pick a random neighbor each step; if neighbor not visited, remove wall and mark visited.
/// </summary>

namespace Feeder
{
    public class AldousBroderAlgor : IMazeAlgorithms
    {
        private System.Random _random;

        public AldousBroderAlgor(int? seed = null)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            // Reset visited flags first
            maze.ResetVisited();

            int rows = maze.Rows;
            int cols = maze.Cols;
            int total = rows * cols;

            // start at a random cell
            int startR = _random.Next(0, rows);
            int startC = _random.Next(0, cols);
            var current = maze.GetCell(startR, startC);
            if (current == null) yield break;

            current.isVisited = true;
            maze.currentMazeCell = current;
            int visitedCount = 1;

            // loop until all visited
            while (visitedCount < total)
            {
                // get all neighbors (including visited)
                var neighbors = maze.GetNeighbors(current, includeVisited: true);
                if (neighbors == null || neighbors.Count == 0)
                {
                    // shouldn't happen in a rectangular grid, but guard anyway
                    break;
                }

                var pair = neighbors[_random.Next(neighbors.Count)];
                var next = pair.cell;

                // if the chosen neighbor is not visited, carve and mark
                if (!next.isVisited)
                {
                    maze.RemoveWallBetween(current, next);
                    next.isVisited = true;
                    visitedCount++;
                }

                // move current
                current = next;
                maze.currentMazeCell = current;

                // repaint and wait
                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);
            }

            // done
            maze.currentMazeCell = null;
            window?.Repaint();
        }
    }
}

