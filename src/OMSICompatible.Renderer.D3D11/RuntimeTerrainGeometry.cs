using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeTerrainGeometry(
    RuntimeTerrainVertex[] Vertices,
    Vector3 Center,
    float HorizontalSpan,
    float MinimumHeight,
    float MaximumHeight)
{
    public static RuntimeTerrainGeometry Empty { get; } = new(
        [],
        Vector3.Zero,
        300.0f,
        0.0f,
        0.0f);
}

internal static class RuntimeTerrainGeometryBuilder
{
    private const double TileSizeMeters = 300.0;
    private const int VertexBudget = 900_000;
    private const int MinimumCellsPerAxis = 4;
    private const int MaximumCellsPerAxis = 64;

    public static RuntimeTerrainGeometry Build(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        var terrainTiles = tiles
            .Where(static tile =>
                tile.Terrain is
                {
                    CellCount: > 0,
                    Heights.Count: > 0
                })
            .ToArray();

        if (terrainTiles.Length == 0)
        {
            return RuntimeTerrainGeometry.Empty;
        }

        var minimumTileX = terrainTiles.Min(static tile => tile.X);
        var maximumTileX = terrainTiles.Max(static tile => tile.X);
        var minimumTileY = terrainTiles.Min(static tile => tile.Y);
        var maximumTileY = terrainTiles.Max(static tile => tile.Y);

        var minimumHeight = terrainTiles.Min(
            static tile => tile.Terrain!.MinimumHeight);
        var maximumHeight = terrainTiles.Max(
            static tile => tile.Terrain!.MaximumHeight);

        var verticesPerTileBudget = Math.Max(
            6,
            VertexBudget / terrainTiles.Length);

        var cellsPerAxisBudget = Math.Clamp(
            (int)Math.Sqrt(verticesPerTileBudget / 6.0),
            MinimumCellsPerAxis,
            MaximumCellsPerAxis);

        var vertices = new List<RuntimeTerrainVertex>(
            Math.Min(
                VertexBudget,
                terrainTiles.Length *
                cellsPerAxisBudget *
                cellsPerAxisBudget *
                6));

        foreach (var tile in terrainTiles)
        {
            AppendTile(
                tile,
                cellsPerAxisBudget,
                minimumHeight,
                maximumHeight,
                vertices);
        }

        var minimumX = (float)(minimumTileX * TileSizeMeters);
        var maximumX = (float)((maximumTileX + 1) * TileSizeMeters);
        var minimumZ = (float)(minimumTileY * TileSizeMeters);
        var maximumZ = (float)((maximumTileY + 1) * TileSizeMeters);

        var center = new Vector3(
            (minimumX + maximumX) * 0.5f,
            (minimumHeight + maximumHeight) * 0.5f,
            (minimumZ + maximumZ) * 0.5f);

        var horizontalSpan = MathF.Max(
            maximumX - minimumX,
            maximumZ - minimumZ);

        return new RuntimeTerrainGeometry(
            vertices.ToArray(),
            center,
            MathF.Max(horizontalSpan, 300.0f),
            minimumHeight,
            maximumHeight);
    }

    private static void AppendTile(
        RuntimeTileInfo tile,
        int cellsPerAxisBudget,
        float globalMinimumHeight,
        float globalMaximumHeight,
        List<RuntimeTerrainVertex> output)
    {
        var terrain = tile.Terrain;
        if (terrain is null || terrain.CellCount <= 0)
        {
            return;
        }

        var sampleCount = terrain.CellCount + 1;
        if (terrain.Heights.Count != sampleCount * sampleCount)
        {
            return;
        }

        var step = Math.Max(
            1,
            (int)Math.Ceiling(
                terrain.CellCount /
                (double)cellsPerAxisBudget));

        var spacing = TileSizeMeters / terrain.CellCount;
        var originX = tile.X * TileSizeMeters;
        var originZ = tile.Y * TileSizeMeters;

        for (var row = 0; row < terrain.CellCount; row += step)
        {
            var nextRow = Math.Min(row + step, terrain.CellCount);

            for (var column = 0; column < terrain.CellCount; column += step)
            {
                var nextColumn = Math.Min(
                    column + step,
                    terrain.CellCount);

                var h00 = Height(
                    terrain,
                    sampleCount,
                    row,
                    column);
                var h10 = Height(
                    terrain,
                    sampleCount,
                    row,
                    nextColumn);
                var h01 = Height(
                    terrain,
                    sampleCount,
                    nextRow,
                    column);
                var h11 = Height(
                    terrain,
                    sampleCount,
                    nextRow,
                    nextColumn);

                if (!float.IsFinite(h00) ||
                    !float.IsFinite(h10) ||
                    !float.IsFinite(h01) ||
                    !float.IsFinite(h11))
                {
                    continue;
                }

                var x0 = (float)(originX + column * spacing);
                var x1 = (float)(originX + nextColumn * spacing);
                var z0 = (float)(originZ + row * spacing);
                var z1 = (float)(originZ + nextRow * spacing);

                var color = TerrainColor(
                    (h00 + h10 + h01 + h11) * 0.25f,
                    globalMinimumHeight,
                    globalMaximumHeight);

                // Same winding used by the native Map Studio terrain path.
                AddTriangle(
                    new Vector3(x0, h00, z0),
                    new Vector3(x1, h11, z1),
                    new Vector3(x1, h10, z0),
                    color,
                    output);

                AddTriangle(
                    new Vector3(x0, h00, z0),
                    new Vector3(x0, h01, z1),
                    new Vector3(x1, h11, z1),
                    color,
                    output);
            }
        }
    }

    private static float Height(
        RuntimeTerrainInfo terrain,
        int sampleCount,
        int row,
        int column)
    {
        return terrain.Heights[
            row * sampleCount +
            column];
    }

    private static Color4 TerrainColor(
        float height,
        float minimumHeight,
        float maximumHeight)
    {
        var range = maximumHeight - minimumHeight;
        var normalized = range > 0.001f
            ? Math.Clamp(
                (height - minimumHeight) / range,
                0.0f,
                1.0f)
            : 0.5f;

        return new Color4(
            0.16f + normalized * 0.18f,
            0.34f + normalized * 0.30f,
            0.17f + normalized * 0.15f,
            1.0f);
    }

    private static void AddTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color4 color,
        ICollection<RuntimeTerrainVertex> output)
    {
        output.Add(new RuntimeTerrainVertex(a, color));
        output.Add(new RuntimeTerrainVertex(b, color));
        output.Add(new RuntimeTerrainVertex(c, color));
    }
}

internal readonly struct RuntimeTerrainVertex
{
    public const uint SizeInBytes = 28;

    public RuntimeTerrainVertex(
        Vector3 position,
        Color4 color)
    {
        Position = position;
        Color = color;
    }

    public readonly Vector3 Position;

    public readonly Color4 Color;
}
