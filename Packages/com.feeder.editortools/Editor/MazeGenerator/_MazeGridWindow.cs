using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using System.Linq;

namespace Feeder
{
    public class MazeGridWindow : OdinEditorWindow
    {
        [MenuItem("Tools/Feeder/Maze Grid Window")]
        private static void OpenWindow() => GetWindow<MazeGridWindow>("Maze Generator").Show();

        [Title("Maze Settings")]
        [ValueDropdown("GetAvailableAlgorithms", DropdownTitle = "Algorithm")]
        public GeneratingAlgorithms algorithm = GeneratingAlgorithms.Backtracking;

        [MinValue(1), MaxValue(20), OnValueChanged("ResetMaze")] public int rows = 10;
        [MinValue(1), MaxValue(20), OnValueChanged("ResetMaze")] public int cols = 10;
        [MinValue(0), MaxValue(1)] public float delay = 0.05f;

        [SerializeField]
        [OnValueChanged("RandomChangeSeed")]
        private bool useRandomSeed = true;
        [ShowIf("useRandomSeed")]
        [ReadOnly]
        [SerializeField]
        [LabelText("Current Seed")]
        private int rngSeed = 12345;

        [HideIf("useRandomSeed")]
        [SerializeField]
        [LabelText("Custom Seed")]
        [OnValueChanged("ChangeSeed")]
        private int customSeed = 12345;

        [TableMatrix(HorizontalTitle = "Maze Matrix", SquareCells = true, DrawElementMethod = "DrawMazeCell", ResizableColumns = false, IsReadOnly = true)]
        [ShowInInspector]
        public MazeCell[,] MazeMatrix => maze != null ? maze.Cells : null;
        private EditorCoroutine runMazeCoroutine;
        private MazeCell currentCell => maze.currentMazeCell;
        private Maze maze;

        protected override void OnEnable()
        {
            base.OnEnable();
            ResetMaze();
        }

        protected override void OnDisable()
        {
            if (runMazeCoroutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(runMazeCoroutine);
                runMazeCoroutine = null;
            }
            base.OnDisable();
        }

        public MazeCell DrawMazeCell(Rect rect, MazeCell drawCell)
        {
            if (drawCell == null) drawCell = new MazeCell();

            // Cell background
            if (drawCell == currentCell)
            {
                EditorGUI.DrawRect(rect, Color.cyan);
            }
            else if (drawCell.isVisited)
            {
                EditorGUI.DrawRect(rect, ColorUltils.paleBlueCyan);
            }
            else
            {
                EditorGUI.DrawRect(rect, Color.gray);
            }

            // Wall thickness
            float thickness = 4f;
            Color wallColor = Color.black;

            // Draw walls
            if (drawCell.hasTopWall)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), wallColor);

            if (drawCell.hasBottomWall)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), wallColor);

            if (drawCell.hasLeftWall)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), wallColor);

            if (drawCell.hasRightWall)
                EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), wallColor);

            return drawCell;
        }

        [Button("Generate Maze")]
        public void GenerateMaze()
        {
            if (maze == null) ResetMaze();

            runMazeCoroutine = EditorCoroutineUtility.StartCoroutineOwnerless(
                activeGenerator.Generate(maze, this, delay));
        }

        private IMazeAlgorithms activeGenerator
        {
            get
            {
                return algorithm switch
                {
                    GeneratingAlgorithms.AldousBroder => new AldousBroderAlgor(rngSeed),
                    GeneratingAlgorithms.Backtracking => new RecursiveBacktrackerAlgor(rngSeed),
                    GeneratingAlgorithms.BinaryTree => new BinaryTreeAlgor(rngSeed),
                    GeneratingAlgorithms.CellularAutomaton => new CellularAutomatonAlgor(rngSeed),
                    GeneratingAlgorithms.DungeonRooms => new DungeonRoomsAlgor(rngSeed),
                    GeneratingAlgorithms.Ellers => new EllersAlgor(rngSeed),
                    GeneratingAlgorithms.GrowingTree => new GrowingTreeAlgor(rngSeed),
                    //GeneratingAlgorithms.HuntAndKill => new HuntAndKillAlgor(rngSeed),

                    _ => throw new NotImplementedException($"Algorithm {algorithm} not implemented"),
                };
            }
        }

        [Button("Reset Maze")]
        public void ResetMaze()
        {
            if (runMazeCoroutine != null)
            {
                EditorCoroutineUtility.StopCoroutine(runMazeCoroutine);
                runMazeCoroutine = null;
            }

            // Create new maze and subscribe repaint
            maze = new Maze(rows, cols);
            maze.ResetVisited();
            Repaint();
            RandomChangeSeed();
        }

        //This method is used for the ValueDropdown GeneratingAlgorithms
        private IEnumerable<GeneratingAlgorithms> GetAvailableAlgorithms()
        {
            return Enum.GetValues(typeof(GeneratingAlgorithms)).Cast<GeneratingAlgorithms>();
        }

        private void ChangeSeed()
        {
            rngSeed = customSeed;
        }

        private void RandomChangeSeed()
        {
            if (useRandomSeed) rngSeed = UnityEngine.Random.Range(0, 1000);
            else customSeed = rngSeed;
        }
    }
}
