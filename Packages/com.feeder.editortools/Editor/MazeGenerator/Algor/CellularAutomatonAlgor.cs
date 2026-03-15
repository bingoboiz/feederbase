using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cellular automaton to produce cave-like open areas.
/// Uses maze.currentMazeCell for highlight and maze.RemoveWallBetween to carve corridors between floor cells.
/// NOTE: Uses cell.isVisited as 'isFloor' marker while building.
/// </summary>

namespace Feeder
{
    public class CellularAutomatonAlgor : IMazeAlgorithms
    {
        private System.Random _random;
        private int fillPercent;
        private int iterations;

        /// <summary>
        /// fillPercent: initial chance (0-100) for a cell to be floor.
        /// iterations: number of CA smoothing passes.
        /// </summary>
        public CellularAutomatonAlgor(int? seed = null, int fillPercent = 45, int iterations = 4)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            this.fillPercent = Mathf.Clamp(fillPercent, 0, 100);
            this.iterations = Mathf.Max(1, iterations);
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            int rows = maze.Rows;
            int cols = maze.Cols;

            // bool map: true = floor, false = wall
            bool[,] map = new bool[rows, cols];

            // Step 1: Initial random fill (để lại viền làm tường)
            for (int r = 1; r < rows - 1; r++)
            {
                for (int c = 1; c < cols - 1; c++)
                {
                    map[r, c] = (_random.Next(0, 100) < fillPercent);
                }
            }

            for (int r = 0; r < rows; r++)
            {
                map[r, 0] = false;
                map[r, cols - 1] = false;
            }
            for (int c = 0; c < cols; c++)
            {
                map[0, c] = false;
                map[rows - 1, c] = false;
            }

            // Visualize initial state
            UpdateMazeVisualization(maze, map, window);
            yield return new EditorWaitForSeconds(delaySeconds);

            // Step 2: Smoothing passes với quy tắc phù hợp cho mê cung
            for (int it = 0; it < iterations; it++)
            {
                bool[,] nextMap = new bool[rows, cols];

                // Copy borders
                for (int r = 0; r < rows; r++)
                {
                    nextMap[r, 0] = false;
                    nextMap[r, cols - 1] = false;
                }
                for (int c = 0; c < cols; c++)
                {
                    nextMap[0, c] = false;
                    nextMap[rows - 1, c] = false;
                }

                // Apply cellular automata rules
                for (int r = 1; r < rows - 1; r++)
                {
                    for (int c = 1; c < cols - 1; c++)
                    {
                        int wallCount = CountWallNeighbors(map, r, c);

                        if (map[r, c])
                        {
                            nextMap[r, c] = wallCount < 5;
                        }
                        else
                        {
                            nextMap[r, c] = wallCount < 4;
                        }
                    }
                }

                map = nextMap;

                // Visualize progress
                UpdateMazeVisualization(maze, map, window);
                yield return new EditorWaitForSeconds(delaySeconds);
            }

            // Step 3: Carve connections between floor cells
            for (int r = 1; r < rows - 1; r++)
            {
                for (int c = 1; c < cols - 1; c++)
                {
                    if (!map[r, c]) continue;

                    var cell = maze.GetCell(r, c);
                    maze.currentMazeCell = cell;
                    cell.isVisited = true;

                    // Remove walls to adjacent floor cells
                    if (r > 0 && map[r - 1, c])
                        maze.RemoveWallBetween(cell, maze.GetCell(r - 1, c));
                    if (c < cols - 1 && map[r, c + 1])
                        maze.RemoveWallBetween(cell, maze.GetCell(r, c + 1));
                    if (r < rows - 1 && map[r + 1, c])
                        maze.RemoveWallBetween(cell, maze.GetCell(r + 1, c));
                    if (c > 0 && map[r, c - 1])
                        maze.RemoveWallBetween(cell, maze.GetCell(r, c - 1));

                    window?.Repaint();
                    yield return new EditorWaitForSeconds(delaySeconds * 0.5f);
                }
            }

            maze.currentMazeCell = null;
            window?.Repaint();
        }

        private int CountWallNeighbors(bool[,] map, int r, int c)
        {
            int wallCount = 0;

            int[] dr = { -1, 0, 1, 0, -1, -1, 1, 1 }; 
            int[] dc = { 0, 1, 0, -1, -1, 1, -1, 1 };

            for (int i = 0; i < 8; i++) 
            {
                int rr = r + dr[i];
                int cc = c + dc[i];

                if (rr >= 0 && rr < map.GetLength(0) && cc >= 0 && cc < map.GetLength(1))
                {
                    if (!map[rr, cc]) wallCount++;
                }
                else
                {
                    wallCount++; 
                }
            }

            return wallCount;
        }

        private void UpdateMazeVisualization(Maze maze, bool[,] map, EditorWindow window)
        {
            for (int r = 0; r < map.GetLength(0); r++)
            {
                for (int c = 0; c < map.GetLength(1); c++)
                {
                    maze.GetCell(r, c).isVisited = map[r, c];
                }
            }
            window?.Repaint();
        }
    }
}
    