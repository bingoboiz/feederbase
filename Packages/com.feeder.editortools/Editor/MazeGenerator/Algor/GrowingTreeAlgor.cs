using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Growing Tree algorithm: a generalization of backtracker. You can control selection policy.
/// 0 = newest (stack-like), 1 = random, 2 = middle-biased (mix).
/// </summary>

namespace Feeder
{
    public class GrowingTreeAlgor : IMazeAlgorithms
    {
        private System.Random _random;
        private int policy; // 0 newest, 1 random, 2 mixed

        public GrowingTreeAlgor(int? seed = null, int policy = 2)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            this.policy = Mathf.Clamp(policy, 0, 2);
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            int rows = maze.Rows;
            int cols = maze.Cols;
            int total = rows * cols;
            int visited = 0;

            var list = new List<MazeCell>();

            // start
            int sr = _random.Next(0, rows);
            int sc = _random.Next(0, cols);
            var start = maze.GetCell(sr, sc);
            start.isVisited = true;
            visited++;
            maze.currentMazeCell = start;
            list.Add(start);

            while (list.Count > 0 && visited < total)
            {
                // selection
                int index = SelectIndex(list.Count);
                var cell = list[index];

                // find unvisited neighbors
                var neighbors = maze.GetNeighbors(cell, includeVisited: false);
                if (neighbors.Count > 0)
                {
                    var chosen = neighbors[_random.Next(neighbors.Count)].cell;
                    maze.RemoveWallBetween(cell, chosen);
                    chosen.isVisited = true;
                    visited++;
                    maze.currentMazeCell = chosen;
                    list.Add(chosen);
                }
                else
                {
                    // remove index
                    list.RemoveAt(index);
                }

                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);
            }

            maze.currentMazeCell = null;
            window?.Repaint();
        }

        private int SelectIndex(int count)
        {
            if (count <= 0) return -1;
            switch (policy)
            {
                case 0: // newest
                    return count - 1;
                case 1: // random
                    return _random.Next(0, count);
                case 2: // mixed: mostly newest but sometimes random
                default:
                    if (_random.NextDouble() < 0.7) return count - 1;
                    return _random.Next(0, count);
            }
        }
    }

}
