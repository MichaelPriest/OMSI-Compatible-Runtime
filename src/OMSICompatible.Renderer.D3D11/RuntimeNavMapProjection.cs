using System.Drawing;
using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Projection used by both the in-game mini navigator and the enlarged city
/// map: north-up or heading-up, reversible under pan and mouse-wheel zoom.
/// Everything uses the same X/Z coordinate system as streamed OMSI splines.
/// </summary>
internal static class RuntimeNavMapProjection
{
    public static PointF ToScreen(
        Vector2 world,
        Vector2 viewCenter,
        PointF screenCenter,
        float scale,
        float headingRadians)
    {
        var delta = world - viewCenter;
        var sin = MathF.Sin(headingRadians);
        var cos = MathF.Cos(headingRadians);
        var right = delta.X * cos - delta.Y * sin;
        var forward = delta.X * sin + delta.Y * cos;
        return new PointF(
            screenCenter.X + right * scale,
            screenCenter.Y - forward * scale);
    }

    public static Vector2 ToWorld(
        PointF screen,
        Vector2 viewCenter,
        PointF screenCenter,
        float scale,
        float headingRadians)
    {
        if (!float.IsFinite(scale) || scale <= 0.0f)
        {
            return viewCenter;
        }

        var right = (screen.X - screenCenter.X) / scale;
        var forward = (screenCenter.Y - screen.Y) / scale;
        var sin = MathF.Sin(headingRadians);
        var cos = MathF.Cos(headingRadians);
        return viewCenter + new Vector2(
            right * cos + forward * sin,
            -right * sin + forward * cos);
    }

    public static Vector2 Pan(
        Vector2 startingCenter,
        PointF start,
        PointF current,
        float scale,
        float headingRadians)
    {
        var screenCenter = new PointF(0, 0);
        var before = ToWorld(
            start, Vector2.Zero, screenCenter, scale, headingRadians);
        var after = ToWorld(
            current, Vector2.Zero, screenCenter, scale, headingRadians);
        return startingCenter + before - after;
    }

    public static Vector2 ZoomAtCursor(
        Vector2 oldCenter,
        PointF cursor,
        PointF screenCenter,
        float previousScale,
        float newScale,
        float headingRadians)
    {
        var anchored = ToWorld(
            cursor, oldCenter, screenCenter, previousScale, headingRadians);
        var projected = ToWorld(
            cursor, oldCenter, screenCenter, newScale, headingRadians);
        return oldCenter + anchored - projected;
    }
}
