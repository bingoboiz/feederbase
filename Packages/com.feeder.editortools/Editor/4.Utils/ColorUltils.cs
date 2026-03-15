
using System.Runtime.CompilerServices;
using UnityEngine;

public struct ColorUltils
{
    public static Color lightCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.4f, 0.8f, 0.8f, 1f);
        }
    }
    public static Color veryLightCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.6f, 0.9f, 0.9f, 1f);
        }
    }
    public static Color paleBlueCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.4f, 0.8f, 0.8f, 1f);
        }
    }
    public static Color iceBlue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.7f, 0.85f, 0.9f, 1f);
        }
    }
    public static Color mutedCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.4f, 0.75f, 0.75f, 1f);
        }
    }
    public static Color whitishCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.8f, 0.95f, 0.95f, 1f);
        }
    }
    public static Color darkerLightCyan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.3f, 0.6f, 0.6f, 1f);
        }
    }
    public static Color seafoamGreen
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return new Color(0.55f, 0.8f, 0.75f, 1f);
        }
    }

}
