using System;
using System.Collections.Generic;
using UnityEngine;

namespace Feeder
{
    public enum CellDirection { Up, Right, Down, Left }

    [Serializable]
    public struct CellPos
    {
        public int row;
        public int col;
        public CellPos(int r, int c) { row = r; col = c; }
    }

    [Serializable]
    public class MazeCell
    {
        public CellPos pos;
        public bool hasTopWall = true;
        public bool hasRightWall = true;
        public bool hasBottomWall = true;
        public bool hasLeftWall = true;
        public bool isVisited = false;
    }

    public class Maze
    {
        public readonly int Rows;
        public readonly int Cols;
        private MazeCell[,] _cells;
        public MazeCell[,] Cells => _cells;
        public MazeCell currentMazeCell;
        public Maze(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0) throw new ArgumentException("invalid size");
            Rows = rows; Cols = cols;
            _cells = new MazeCell[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    _cells[r, c] = new MazeCell { pos = new CellPos(r, c) };
                }
        }

        public MazeCell GetCell(int row, int col)
        {
            if (!InBounds(row, col)) return null;
            return _cells[row, col];
        }

        public bool InBounds(int r, int c) => r >= 0 && r < Rows && c >= 0 && c < Cols;

        // return neighbor cells with their direction
        public List<(MazeCell cell, CellDirection dir)> GetNeighbors(MazeCell cell, bool includeVisited = true)
        {
            var list = new List<(MazeCell cell, CellDirection direction)>(4);
            var r = cell.pos.row;
            var c = cell.pos.col;

            if (InBounds(r - 1, c)) list.Add((_cells[r - 1, c], CellDirection.Up));
            if (InBounds(r, c + 1)) list.Add((_cells[r, c + 1], CellDirection.Right));
            if (InBounds(r + 1, c)) list.Add((_cells[r + 1, c], CellDirection.Down));
            if (InBounds(r, c - 1)) list.Add((_cells[r, c - 1], CellDirection.Left));

            if (!includeVisited)
                list.RemoveAll(x => x.cell.isVisited);

            return list;
        }

        // Remove walls between two adjacent cells
        public void RemoveWallBetween(MazeCell a, MazeCell b)
        {
            if (a == null || b == null) return;
            int dr = b.pos.row - a.pos.row;
            int dc = b.pos.col - a.pos.col;

            if (dr == 0 && dc == -1) { a.hasTopWall = false; b.hasBottomWall = false; } // b is left
            else if (dr == 0 && dc == 1) { a.hasBottomWall = false; b.hasTopWall = false; } // b is right
            else if (dr == -1 && dc == 0) { a.hasLeftWall = false; b.hasRightWall = false; } // b is up
            else if (dr == 1 && dc == 0) { a.hasRightWall = false; b.hasLeftWall = false; } // b is down
            else
            {
                Debug.LogWarning("Cells not adjacent in RemoveWallBetween");
            }
        }

        // reset visited
        public void ResetVisited()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c].isVisited = false;
        }
    }

    public enum GeneratingAlgorithms : byte
    {
        AldousBroder = 1,
        Backtracking = 2,
        BinaryTree = 3,
        CellularAutomaton = 4,
        DungeonRooms = 5,
        Ellers = 6,
        GrowingTree = 7,
        HuntAndKill = 8,
        Kruskals = 9,
        Prims = 10,
        RecursiveDivision = 11,
        Sidewinder = 12,
        Wilsons = 13,
        Custom = 255
    }
}

