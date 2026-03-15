using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;

namespace Feeder
{
    public class RecursiveBacktrackerAlgor : IMazeAlgorithms
    {
        private System.Random _random;
        int total;
        int visitedCount = 1;

        public RecursiveBacktrackerAlgor(int? seed = null)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            maze.ResetVisited();
            RandomizeStartingCell(maze);

            total = maze.Rows * maze.Cols;

            var stack = new Stack<MazeCell>();

            while (visitedCount < total)
            {
                var neighbors = maze.GetNeighbors(maze.currentMazeCell, includeVisited: false);
                if (neighbors.Count > 0)
                {
                    // choose random
                    var pair = neighbors[_random.Next(neighbors.Count)];
                    var next = pair.cell;

                    // push current, remove wall, move
                    stack.Push(maze.currentMazeCell);
                    maze.RemoveWallBetween(maze.currentMazeCell, next);
                    next.isVisited = true;
                    visitedCount++;
                    maze.currentMazeCell = next;
                }
                else
                {
                    if (stack.Count == 0) break;
                    maze.currentMazeCell = stack.Pop();
                }

                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);
            }
        }
        private void RandomizeStartingCell(Maze maze)
        {
            int startR = _random.Next(0, maze.Rows);
            int startC = _random.Next(0, maze.Cols);
            maze.currentMazeCell = maze.GetCell(startR, startC);
            maze.currentMazeCell.isVisited = true;
        }
    }
}
