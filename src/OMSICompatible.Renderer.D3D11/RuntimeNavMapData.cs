using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal readonly record struct RuntimeNavRoadSection(
    Vector2 A, Vector2 B, int SegmentIndex)
{
    public Vector2 Midpoint => (A + B) * 0.5f;
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
            var midpoint = section.Midpoint;
            if (!float.IsFinite(midpoint.X) || !float.IsFinite(midpoint.Y))
                continue;

            var key = Cell(midpoint);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = [];
                _cells.Add(key, list);
            }

            list.Add(section);
            Count++;
        }
    }

    public int Count { get; }

    private static (int X, int Y) Cell(Vector2 p) =>
        ((int)MathF.Floor(p.X / CellSize),
         (int)MathF.Floor(p.Y / CellSize));

    public IEnumerable<RuntimeNavRoadSection> Nearby(Vector2 center, float radiusMeters)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(radiusMeters) || radiusMeters < 0.0f)
            yield break;

        var radius = Math.Clamp(radiusMeters, 0.0f, 12080.0f);
        var from = Cell(center - new Vector2(radius));
        var to = Cell(center + new Vector2(radius));
        for (var x = from.X; x <= to.X; x++)
        for (var z = from.Y; z <= to.Y; z++)
        {
            if (!_cells.TryGetValue((x, z), out var list))
                continue;

            foreach (var road in list)
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
internal static class RuntimeNavStopProjector
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
                    stop.Name.Trim(), x, z, i == stops.Count - 1));
            }
        }

        return result.ToArray();
    }
}
