namespace AiMeter.Windowing;

/// <summary>
/// Pure window-position math. Kept free of WPF/Win32 dependencies so it can be unit
/// tested directly; the window supplies the real work-area bounds at call time.
/// </summary>
public static class ScreenMath
{
    /// <summary>
    /// Clamps a window's top-left so it stays within the given work-area bounds,
    /// preferring to keep the top-left corner visible when the window is larger than the
    /// work area.
    /// </summary>
    public static (double Left, double Top) ClampToBounds(
        double left, double top, double width, double height,
        double boundsLeft, double boundsTop, double boundsWidth, double boundsHeight)
    {
        var maxLeft = boundsLeft + Math.Max(0, boundsWidth - width);
        var maxTop = boundsTop + Math.Max(0, boundsHeight - height);

        var clampedLeft = Math.Clamp(left, boundsLeft, Math.Max(boundsLeft, maxLeft));
        var clampedTop = Math.Clamp(top, boundsTop, Math.Max(boundsTop, maxTop));

        return (clampedLeft, clampedTop);
    }
}