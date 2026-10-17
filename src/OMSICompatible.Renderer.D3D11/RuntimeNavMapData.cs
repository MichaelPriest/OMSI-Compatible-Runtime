using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal readonly record struct RuntimeNavRoadSection(
    Vector2 A, Vector2 B, int SegmentIndex)
{
    public Vector2 Midpoint => (A + B) * 0.5f;

    public float DistanceSquaredTo(Vector2 point)
    {
        var delta = B - A;
        var lengthSquared = delta.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return Vector2.DistanceSquared(point, A);
        var t = Math.Clamp(
            Vector2.Dot(point - A, delta) / lengthSquared, 0.0f, 1.0f);
        return Vector2.DistanceSquared(point, A + delta * t);
    }
}

/// <summary>
/// 240-metre spatial grid; the full city map only draws the roads nearby,
/// instead of scanning every loaded OMSI road on each frame.
/// </summary>
internal sealed class RuntimeNavRoadIndex
{
    private const float CellSize = 240.0f;
    private readonly Dictionary<(int X, int Y), List<RuntimeNavRoadSection>> _cells = [];

    public RuntimeNavRoadIndex(IEnumerable<RuntimeNavRoadSection> roads)
    {
        foreach (var section in roads)
        {
            if (!float.IsFinite(section.A.X) || !float.IsFinite(section.A.Y) ||
                !float.IsFinite(section.B.X) || !float.IsFinite(section.B.Y))
                continue;

            // Traverse only grid cells crossed by the road segment. Indexing
            // the entire bounding rectangle makes long diagonal junctions
            // allocate O(width * height) entries and can stall map loading.
            foreach (var cell in CellsAlong(section.A, section.B))
            {
                if (!_cells.TryGetValue(cell, out var list))
                {
                    list = [];
                    _cells.Add(cell, list);
                }
                list.Add(section);
            }
            Count++;
        }
    }

    public int Count { get; }

    private static (int X, int Y) Cell(Vector2 p) =>
        ((int)MathF.Floor(p.X / CellSize),
         (int)MathF.Floor(p.Y / CellSize));

    private static IEnumerable<(int X, int Y)> CellsAlong(Vector2 a, Vector2 b)
    {
        var cell = Cell(a);
        var end = Cell(b);
        yield return cell;
        if (cell == end)
            yield break;

        var delta = b - a;
        var stepX = Math.Sign(delta.X);
        var stepY = Math.Sign(delta.Y);
        var tDeltaX = stepX == 0 ? float.PositiveInfinity : CellSize / MathF.Abs(delta.X);
        var tDeltaY = stepY == 0 ? float.PositiveInfinity : CellSize / MathF.Abs(delta.Y);
        var boundaryX = stepX > 0
            ? (cell.X + 1) * CellSize
            : cell.X * CellSize;
        var boundaryY = stepY > 0
            ? (cell.Y + 1) * CellSize
            : cell.Y * CellSize;
        var tMaxX = stepX == 0 ? float.PositiveInfinity : (boundaryX - a.X) / delta.X;
        var tMaxY = stepY == 0 ? float.PositiveInfinity : (boundaryY - a.Y) / delta.Y;

        // Amanatides-Woo grid traversal: work is proportional to the number
        // of cells crossed, not the area of the segment's bounding rectangle.
        while (cell != end)
        {
            if (tMaxX < tMaxY)
            {
                cell = (cell.X + stepX, cell.Y);
                tMaxX += tDeltaX;
            }
            else if (tMaxY < tMaxX)
            {
                cell = (cell.X, cell.Y + stepY);
                tMaxY += tDeltaY;
            }
            else
            {
                // Supercover the exact corner so a small viewport touching
                // either adjacent cell can still discover this road segment.
                yield return (cell.X + stepX, cell.Y);
                yield return (cell.X, cell.Y + stepY);
                cell = (cell.X + stepX, cell.Y + stepY);
                tMaxX += tDeltaX;
                tMaxY += tDeltaY;
                yield return cell;
                continue;
            }

            yield return cell;
        }
    }

    public IEnumerable<RuntimeNavRoadSection> Nearby(Vector2 center, float radiusMeters)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(radiusMeters) || radiusMeters < 0.0f)
            yield break;

        var radius = Math.Clamp(radiusMeters, 0.0f, 12080.0f);
        var from = Cell(center - new Vector2(radius));
        var to = Cell(center + new Vector2(radius));
        var seen = new HashSet<RuntimeNavRoadSection>();
        for (var x = from.X; x <= to.X; x++)
        for (var z = from.Y; z <= to.Y; z++)
        {
            if (!_cells.TryGetValue((x, z), out var list))
                continue;

            foreach (var road in list)
                if (seen.Add(road))
                    yield return road;
        }
    }
}

/// <summary>
/// Real OMSI road-vehicle path geometry and traffic speeds, not placeholder
/// roads, pedestrian paths, metro tracks or a static traffic overlay.
/// </summary>
internal static class RuntimeNavTrafficMap
{
    public static RuntimeNavRoadSection[] Build(RuntimeTrafficPathNetworkInfo network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var result = new List<RuntimeNavRoadSection>();
        foreach (var lane in network.Segments)
        {
            // OMSI path types: 0=road vehicles, 1=pedestrians,
            // 2=rails, 3=aircraft.
            if (lane.Type != 0 || lane.Points.Count < 2)
                continue;

            for (var i = 1; i < lane.Points.Count; i++)
            {
                var a = lane.Points[i - 1];
                var b = lane.Points[i];
                if (!double.IsFinite(a.X) || !double.IsFinite(a.Z) ||
                    !double.IsFinite(b.X) || !double.IsFinite(b.Z))
                    continue;

                var from = new Vector2((float)a.X, (float)a.Z);
                var to = new Vector2((float)b.X, (float)b.Z);
                if (!float.IsFinite(from.X) || !float.IsFinite(from.Y) ||
                    !float.IsFinite(to.X) || !float.IsFinite(to.Y) ||
                    Vector2.DistanceSquared(from, to) < 0.0004f)
                    continue;

                result.Add(new RuntimeNavRoadSection(from, to, lane.Index));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Do not consider one car stopped at a light a traffic jam; only
    /// colour lanes with at least two genuine, slow OMSI AI vehicles.
    /// </summary>
    public static Dictionary<int, float> EstimateCongestion(
        IReadOnlyList<RuntimeTrafficAgentInfo> agents,
        IReadOnlyDictionary<int, double> speedLimits)
    {
        var grouped = new Dictionary<int, (int Count, double SpeedSum)>();
        foreach (var agent in agents)
        {
            if ((unchecked((uint)agent.AgentIndex) & 0x60000000u) ==
                    0x60000000u ||
                !speedLimits.ContainsKey(agent.SegmentIndex) ||
                !double.IsFinite(agent.SpeedMetersPerSecond) ||
                agent.SpeedMetersPerSecond < 0.0)
                continue;

            grouped.TryGetValue(agent.SegmentIndex, out var value);
            grouped[agent.SegmentIndex] =
                (value.Count + 1, value.SpeedSum + agent.SpeedMetersPerSecond);
        }

        var result = new Dictionary<int, float>();
        foreach (var (index, group) in grouped)
        {
            if (group.Count < 2)
                continue;

            var speedLimit = speedLimits[index];
            if (!double.IsFinite(speedLimit) || speedLimit <= 0.0)
                speedLimit = 35.0;

            var ratio = group.SpeedSum / group.Count * 3.6 / speedLimit;
            if (ratio >= 0.55)
                continue;

            result[index] = (float)Math.Clamp(
                (0.55 - ratio) / 0.55, 0.0, 1.0);
        }

        return result;
    }
}

/// <summary>
/// Project stops from the selected OMSI timetable on the selected route's
/// geometry. Distances are normalized to its actual length, never guessed
/// from a name or generated at arbitrary map coordinates.
/// </summary>
public static class RuntimeNavStopProjector
{
    public static RuntimeNavigationStopInfo[] Place(
        IReadOnlyList<RuntimeTrafficPathPointInfo> route,
        IReadOnlyList<RuntimeNavigationStopDistanceInfo> stops)
    {
        if (route.Count < 2 || stops.Count == 0)
            return [];

        var distance = new double[route.Count];
        for (var i = 1; i < route.Count; i++)
        {
            var a = route[i - 1];
            var b = route[i];
            distance[i] = distance[i - 1] +
                Math.Sqrt(Math.Pow(b.X - a.X, 2) +
                          Math.Pow(b.Y - a.Y, 2) +
                          Math.Pow(b.Z - a.Z, 2));
        }

        var total = distance[^1];
        var lastStop = stops
            .Where(s => double.IsFinite(s.RouteDistanceMeters))
            .Select(s => s.RouteDistanceMeters)
            .DefaultIfEmpty(0.0)
            .Max();

        if (!double.IsFinite(total) || total <= 0.01 ||
            lastStop <= 0.01)
            return [];

        // The final timing entry may be an actual pass-through, not a stop.
        var terminusIndex = -1;
        for (var i = stops.Count - 1; i >= 0; i--)
        {
            if (stops[i].Stops &&
                !string.IsNullOrWhiteSpace(stops[i].Name) &&
                double.IsFinite(stops[i].RouteDistanceMeters))
            {
                terminusIndex = i;
                break;
            }
        }

        var result = new List<RuntimeNavigationStopInfo>();
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            if (!stop.Stops || string.IsNullOrWhiteSpace(stop.Name) ||
                !double.IsFinite(stop.RouteDistanceMeters))
                continue;

            var at = Math.Clamp(stop.RouteDistanceMeters / lastStop * total,
                0.0, total);
            var index = Array.BinarySearch(distance, at);
            if (index < 0) index = ~index;
            index = Math.Clamp(index, 1, route.Count - 1);

            var a = route[index - 1];
            var b = route[index];
            var span = distance[index] - distance[index - 1];
            var weight = span <= 0.001
                ? 0.0
                : Math.Clamp((at - distance[index - 1]) / span, 0.0, 1.0);

            var x = a.X + (b.X - a.X) * weight;
            var z = a.Z + (b.Z - a.Z) * weight;
            if (double.IsFinite(x) && double.IsFinite(z))
            {
                result.Add(new RuntimeNavigationStopInfo(
                    stop.Name.Trim(), x, z, i == terminusIndex));
            }
        }

        return result.ToArray();
    }
}
