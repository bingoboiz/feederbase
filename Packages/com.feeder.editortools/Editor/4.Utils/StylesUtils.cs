using UnityEditor;
using UnityEngine;

public static class StylesUtils
{
    private static GUIStyle s_DescriptionStyle;

    public static void DrawDescription(string text)
    {
        EnsureStyles();

        var rect = GUILayoutUtility.GetRect(
            GUIContent.none,
            GUIStyle.none,
            GUILayout.ExpandWidth(true),
            GUILayout.Height(36)
        );

        // Background
        EditorGUI.DrawRect(rect, FBlue);

        // Padding
        var padded = new Rect(
            rect.x + 8,
            rect.y + 6,
            rect.width - 16,
            rect.height - 12
        );

        EditorGUI.LabelField(padded, text, s_DescriptionStyle);
    }

    private static void EnsureStyles()
    {
        if (s_DescriptionStyle == null)
        {
            s_DescriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                stretchWidth = true
            };
            s_DescriptionStyle.normal.textColor = EditorStyles.label.normal.textColor;
        }
    }

    public static Color FBlue = new(0.2f, 0.5f, 1f, 0.42f);
}
