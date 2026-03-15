using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    /// <summary>
    /// Shared layout helpers for content drawn inside OdinMenuEditorWindow (e.g. menu tree width).
    /// Caches the host window so width stays correct when focus moves to another window.
    /// </summary>
    public static class FOdinMenuLayoutUtils
    {
        private static EditorWindow s_cachedMenuWindow;

        /// <summary>
        /// Gets the OdinMenuEditorWindow that is currently (or was last) hosting this inspector.
        /// Uses focus/mouseOver first, then cached reference so it works when another window is focused.
        /// </summary>
        public static OdinMenuEditorWindow GetHostMenuWindow()
        {
            var focused = EditorWindow.focusedWindow;
            var mouseOver = EditorWindow.mouseOverWindow;

            if (focused is OdinMenuEditorWindow menuFocused)
            {
                s_cachedMenuWindow = menuFocused;
                return menuFocused;
            }

            if (mouseOver is OdinMenuEditorWindow menuMouse)
            {
                s_cachedMenuWindow = menuMouse;
                return menuMouse;
            }

            if (s_cachedMenuWindow != null && s_cachedMenuWindow is OdinMenuEditorWindow cached)
                return cached;

            return null;
        }

        /// <summary>
        /// Width of the inspector content area (window width minus menu pane). Use when laying out content.
        /// </summary>
        public static float GetContentViewWidth()
        {
            var menuWin = GetHostMenuWindow();
            if (menuWin != null)
                return Mathf.Max(0f, menuWin.position.width - menuWin.MenuWidth);
            return EditorGUIUtility.currentViewWidth;
        }

        /// <summary>
        /// Content width minus horizontal margin on both sides (for centered/symmetric layout).
        /// </summary>
        public static float GetContentWidthWithMargin(float horizontalMargin)
        {
            var viewWidth = GetContentViewWidth();
            return Mathf.Max(0f, viewWidth - horizontalMargin * 2f);
        }

        /// <summary>
        /// Rect of the inspector content area (excluding menu). Origin is content top-left.
        /// </summary>
        public static Rect GetContentRect()
        {
            var menuWin = GetHostMenuWindow();
            if (menuWin == null)
                return new Rect(0, 0, EditorGUIUtility.currentViewWidth, 0);

            var w = menuWin.position.width - menuWin.MenuWidth;
            var h = menuWin.position.height;
            return new Rect(0, 0, Mathf.Max(0f, w), h);
        }

        /// <summary>
        /// Clears the cached host window (e.g. after window close). Called automatically when cache is invalid.
        /// </summary>
        public static void ClearCache()
        {
            s_cachedMenuWindow = null;
        }
    }
}
