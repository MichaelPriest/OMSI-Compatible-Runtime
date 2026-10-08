using System.Numerics;
using OMSICompatible.Renderer.Common;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Low-profile cyan chevrons projected onto the streamed OMSI road meshes.
/// Inspired by Forza Horizon's repeated navigation line (original geometry).
/// No fake ground, UI-space arrows, proprietary textures or depth bypass.
/// </summary>
internal static class RuntimeNavigationGuidanceGeometry
{
    private const float StemLengthMeters = 2.05f;
    private const float HalfWidthMeters = 0.88f;
    private const float HaloStrokeMeters = 0.53f;
    private const float CoreStrokeMeters = 0.29f;
    private const float HighlightStrokeMeters = 0.095f;

    public static RuntimeObjectVertex[] Build(
        IReadOnlyList<RuntimeNavigationGuidancePointInfo> points,
        RuntimeSplineSurfaceSampler? splineRoad = null,
        RuntimeSplineSurfaceSampler? sceneryRoad = null)
    {
        if (points.Count == 0)
        {
            return [];
        }

        // If a streamed road mesh has not been loaded yet, do not invent
        // unsupported floating markers over terrain or the vehicle.
        if (splineRoad is not null &&
            sceneryRoad is not null &&
            splineRoad.TriangleCount == 0 &&
            sceneryRoad.TriangleCount == 0)
        {
            return [];
        }

        var output = new List<RuntimeObjectVertex>(points.Count * 72);
        foreach (var point in points)
        {
            AppendChevron(point, splineRoad, sceneryRoad, output);
        }

        return output.ToArray();
    }

    private static void AppendChevron(
        RuntimeNavigationGuidancePointInfo point,
        RuntimeSplineSurfaceSampler? splineRoad,
        RuntimeSplineSurfaceSampler? sceneryRoad,
        ICollection<RuntimeObjectVertex> output)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            !double.IsFinite(point.Z) || !double.IsFinite(point.HeadingDegrees) ||
            !double.IsFinite(point.PitchDegrees))
        {
            return;
        }

        var yaw = (float)(point.HeadingDegrees * Math.PI / 180.0);
        var pitch = (float)(point.PitchDegrees * Math.PI / 180.0);
        var flatForward = new Vector3(MathF.Sin(yaw), 0.0f, MathF.Cos(yaw));
        var forward = Vector3.Normalize(new Vector3(
            flatForward.X * MathF.Cos(pitch),
            MathF.Sin(pitch),
            flatForward.Z * MathF.Cos(pitch)));
        var right = Vector3.Normalize(new Vector3(forward.Z, 0.0f, -forward.X));
        var middle = new Vector3((float)point.X, (float)point.Y, (float)point.Z);

        // Route points are traffic-lane centers, not the map's camera/HUD.
        // Raise only a few centimetres so the decals clear the asphalt
        // without the old floating billboard look.
        var leftTail = middle - forward * 0.92f - right * HalfWidthMeters;
        var rightTail = middle - forward * 0.92f + right * HalfWidthMeters;
        var tip = middle + forward * (StemLengthMeters - 0.92f);

        var rejoin = point.Kind.Equals("rejoin", StringComparison.OrdinalIgnoreCase);
        var haloColor = rejoin
            ? new Color4(1.0f, 0.43f, 0.04f, 0.20f)
            : new Color4(0.03f, 0.55f, 1.0f, 0.22f);
        var coreColor = rejoin
            ? new Color4(1.0f, 0.61f, 0.11f, 0.87f)
            : new Color4(0.035f, 0.75f, 1.0f, 0.82f);
        var highlightColor = rejoin
            ? new Color4(1.0f, 0.83f, 0.34f, 0.57f)
            : new Color4(0.50f, 0.95f, 1.0f, 0.65f);

        // Check both arms against the same real surface first; never draw
        // an isolated half-chevron over the edge of a bridge or junction.
        if (!TrySample(leftTail, splineRoad, sceneryRoad, 0.038f, out _) ||
            !TrySample(rightTail, splineRoad, sceneryRoad, 0.038f, out _) ||
            !TrySample(tip, splineRoad, sceneryRoad, 0.038f, out _))
        {
            return;
        }

        // A broad, subtle outer glow, a saturated cyan stripe and a narrow
        // highlight approximate the FH5 racing-line shape without using its
        // copyrighted textures. The route stays depth-tested against buses.
        foreach (var layer in new[]
        {
            (Width: HaloStrokeMeters, Offset: 0.038f, Color: haloColor),
            (Width: CoreStrokeMeters, Offset: 0.052f, Color: coreColor),
            (Width: HighlightStrokeMeters, Offset: 0.065f, Color: highlightColor)
        })
        {
            AppendStroke(leftTail, tip, layer.Width, layer.Offset,
                layer.Color, splineRoad, sceneryRoad, output);
            AppendStroke(rightTail, tip, layer.Width, layer.Offset,
                layer.Color, splineRoad, sceneryRoad, output);
        }
    }

    private static void AppendStroke(
        Vector3 from,
        Vector3 to,
        float width,
        float heightOffset,
        Color4 color,
        RuntimeSplineSurfaceSampler? splineRoad,
        RuntimeSplineSurfaceSampler? sceneryRoad,
        ICollection<RuntimeObjectVertex> output)
    {
        var delta = to - from;
        var lengthSquared = delta.X * delta.X + delta.Z * delta.Z;
        if (lengthSquared < 0.001f)
        {
            return;
        }

        var side = new Vector3(-delta.Z, 0.0f, delta.X) *
            (width * 0.5f / MathF.Sqrt(lengthSquared));
        var tipScale = 0.24f;
        if (!TrySample(from - side, splineRoad, sceneryRoad, heightOffset, out var a) ||
            !TrySample(from + side, splineRoad, sceneryRoad, heightOffset, out var b) ||
            !TrySample(to + side * tipScale, splineRoad, sceneryRoad, heightOffset, out var c) ||
            !TrySample(to - side * tipScale, splineRoad, sceneryRoad, heightOffset, out var d))
        {
            return;
        }

        AddDoubleSidedTriangle(a, b, c, color, output);
        AddDoubleSidedTriangle(a, c, d, color, output);
    }

    private static bool TrySample(
        Vector3 requested,
        RuntimeSplineSurfaceSampler? splineRoad,
        RuntimeSplineSurfaceSampler? sceneryRoad,
        float offset,
        out Vector3 placed)
    {
        placed = requested;

        if (splineRoad is null && sceneryRoad is null)
        {
            placed.Y += offset;
            return true;
        }

        // Pick the actual spline/road-object triangle under this vertex,
        // never the terrain under an overpass. Limit the vertical mismatch
        // to avoid projecting the bridge route onto the road below.
        var best = float.NegativeInfinity;
        var limit = requested.Y + 0.9f;

        if (splineRoad?.TrySampleBelow(requested.X, requested.Z,
                limit, out var splineHeight) == true)
        {
            best = splineHeight;
        }
        if (sceneryRoad?.TrySampleBelow(requested.X, requested.Z,
                limit, out var sceneryHeight) == true)
        {
            best = Math.Max(best, sceneryHeight);
        }

        if (!float.IsFinite(best) ||
            MathF.Abs(best - requested.Y) > 1.20f)
        {
            return false;
        }

        placed.Y = best + offset;
        return true;
    }

    private static void AddDoubleSidedTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeObjectVertex> output)
    {
        var uv = Vector2.Zero;
        output.Add(new RuntimeObjectVertex(a, color, uv));
        output.Add(new RuntimeObjectVertex(b, color, uv));
        output.Add(new RuntimeObjectVertex(c, color, uv));
        output.Add(new RuntimeObjectVertex(c, color, uv));
        output.Add(new RuntimeObjectVertex(b, color, uv));
        output.Add(new RuntimeObjectVertex(a, color, uv));
    }
}
