using UnityEditor;
using UnityEngine;
using System.Collections;

namespace Feeder
{
    public interface IMazeAlgorithms
    {
        IEnumerator Generate(Maze maze, EditorWindow window, float delaySeconds);
    }
}
