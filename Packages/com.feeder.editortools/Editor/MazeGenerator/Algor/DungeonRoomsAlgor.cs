using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon style rooms: place random rectangular rooms then connect them with straight tunnels.
/// Rooms carve by removing walls between adjacent cells inside each room.
/// </summary>

namespace Feeder
{
    public class DungeonRoomsAlgor : IMazeAlgorithms
    {
        private System.Random _random;
        private int roomCount;
        private int minRoomSize;
        private int maxRoomSize;

        public DungeonRoomsAlgor(int? seed = null, int roomCount = 8, int minRoomSize = 3, int maxRoomSize = 7)
        {
            _random = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            this.roomCount = Mathf.Max(1, roomCount);
            this.minRoomSize = Mathf.Max(2, minRoomSize);
            this.maxRoomSize = Mathf.Max(minRoomSize, maxRoomSize);
        }

        public IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds)
        {
            if (maze == null) yield break;

            int rows = maze.Rows;
            int cols = maze.Cols;

            var rooms = new List<RectInt>();

            // try to place rooms (simple random placement, allow some overlap)
            for (int i = 0; i < roomCount; i++)
            {
                int w = _random.Next(minRoomSize, maxRoomSize + 1);
                int h = _random.Next(minRoomSize, maxRoomSize + 1);
                int x = _random.Next(1, cols - w - 1);
                int y = _random.Next(1, rows - h - 1);

                var room = new RectInt(x, y, w, h);
                rooms.Add(room);

                // carve room: remove internal walls between cells inside the room
                for (int ry = y; ry < y + h; ry++)
                {
                    for (int cx = x; cx < x + w; cx++)
                    {
                        var cell = maze.GetCell(ry, cx);
                        maze.currentMazeCell = cell;
                        cell.isVisited = true;
                        // remove walls to right and down inside the room
                        if (cx < x + w - 1) maze.RemoveWallBetween(cell, maze.GetCell(ry, cx + 1));
                        if (ry < y + h - 1) maze.RemoveWallBetween(cell, maze.GetCell(ry + 1, cx));
                    }
                }

                window?.Repaint();
                yield return new EditorWaitForSeconds(delaySeconds);
            }

            // connect rooms: use centers and connect in order by nearest neighbor (simple MST-like)
            var centers = new List<(int r, int c)>();
            foreach (var rct in rooms)
            {
                int centerC = rct.x + rct.width / 2;
                int centerR = rct.y + rct.height / 2;
                centers.Add((centerR, centerC));
            }

            // naive connect: sort by x then connect sequentially (simple, fast)
            centers.Sort((a, b) => a.c.CompareTo(b.c));
            for (int i = 0; i < centers.Count - 1; i++)
            {
                var a = centers[i];
                var b = centers[i + 1];

                // carve L-shaped tunnel: horizontal then vertical
                int r = a.r;
                for (int c = Mathf.Min(a.c, b.c); c <= Mathf.Max(a.c, b.c); c++)
                {
                    var cell = maze.GetCell(r, c);
                    maze.currentMazeCell = cell;
                    cell.isVisited = true;
                    // connect to left neighbor
                    if (c > 0) maze.RemoveWallBetween(cell, maze.GetCell(r, c - 1));
                    window?.Repaint();
                    yield return new EditorWaitForSeconds(delaySeconds);
                }

                int c2 = b.c;
                for (int rr = Mathf.Min(a.r, b.r); rr <= Mathf.Max(a.r, b.r); rr++)
                {
                    var cell = maze.GetCell(rr, c2);
                    maze.currentMazeCell = cell;
                    cell.isVisited = true;
                    if (rr > 0) maze.RemoveWallBetween(cell, maze.GetCell(rr - 1, c2));
                    window?.Repaint();
                    yield return new EditorWaitForSeconds(delaySeconds);
                }
            }

            maze.currentMazeCell = null;
            window?.Repaint();
        }
    }

}
