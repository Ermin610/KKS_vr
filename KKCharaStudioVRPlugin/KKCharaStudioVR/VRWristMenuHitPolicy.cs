namespace KKCharaStudioVR;

/// <summary>
/// Wrist menu layout is authored top-left down (x right, y down), but the canvas
/// RectTransform keeps Unity's centred pivot. A raw InverseTransformPoint result
/// is relative to that centre, so hit tests go through the rect edges.
/// </summary>
internal static class VRWristMenuHitPolicy
{
    internal static float LogicalX(float localX, float rectXMin)
    {
        return localX - rectXMin;
    }

    internal static float LogicalY(float localY, float rectYMax)
    {
        return rectYMax - localY;
    }

    internal static bool InsideBand(float logicalY, float top, float height)
    {
        return logicalY >= top && logicalY <= top + height;
    }

    internal static bool InsideViewport(
        float localX,
        float localY,
        float rectXMin,
        float rectYMax,
        float width,
        float top,
        float height)
    {
        float x = LogicalX(localX, rectXMin);
        float y = LogicalY(localY, rectYMax);
        return x >= 0f && x <= width && InsideBand(y, top, height);
    }

    /// <summary>
    /// Menu-space y of a point placed at <paramref name="contentY"/> inside a
    /// scroll content that is scrolled down by <paramref name="scrollOffset"/>.
    /// </summary>
    internal static float ContentToMenuY(float contentY, float viewportTop, float scrollOffset)
    {
        return viewportTop + contentY - scrollOffset;
    }
}
