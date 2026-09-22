namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeTerrainSampler
{
    private const double TileSizeMeters = 300.0;

    private readonly Dictionary<(int X, int Y), RuntimeTerrainInfo>
        _terrainByTile;

    public RuntimeTerrainSampler(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        _terrainByTile = tiles
            .Where(static tile => tile.Terrain is not null)
            .ToDictionary(
                static tile => (tile.X, tile.Y),
                static tile => tile.Terrain!);
    }

    public bool TrySample(
        double worldX,
        double worldZ,
        out float height)
    {
        var tileX = (int)Math.Floor(
            worldX / TileSizeMeters);
        var tileY = (int)Math.Floor(
            worldZ / TileSizeMeters);

        if (!_terrainByTile.TryGetValue(
                (tileX, tileY),
                out var terrain) ||
            terrain.CellCount <= 0)
        {
            height = 0.0f;
            return false;
        }

        var localX =
            worldX -
            tileX * TileSizeMeters;
        var localZ =
            worldZ -
            tileY * TileSizeMeters;

        var spacing =
            TileSizeMeters /
            terrain.CellCount;

        var gridX = Math.Clamp(
            localX / spacing,
            0.0,
            terrain.CellCount);
        var gridZ = Math.Clamp(
            localZ / spacing,
            0.0,
            terrain.CellCount);

        var x0 = Math.Clamp(
            (int)Math.Floor(gridX),
            0,
            terrain.CellCount);
        var z0 = Math.Clamp(
            (int)Math.Floor(gridZ),
            0,
            terrain.CellCount);
        var x1 = Math.Min(
            x0 + 1,
            terrain.CellCount);
        var z1 = Math.Min(
            z0 + 1,
            terrain.CellCount);

        var tx = (float)(gridX - x0);
        var tz = (float)(gridZ - z0);

        var sampleCount =
            terrain.CellCount + 1;

        var h00 = Read(
            terrain,
            sampleCount,
            z0,
            x0);
        var h10 = Read(
            terrain,
            sampleCount,
            z0,
            x1);
        var h01 = Read(
            terrain,
            sampleCount,
            z1,
            x0);
        var h11 = Read(
            terrain,
            sampleCount,
            z1,
            x1);

        if (!float.IsFinite(h00) ||
            !float.IsFinite(h10) ||
            !float.IsFinite(h01) ||
            !float.IsFinite(h11))
        {
            height = 0.0f;
            return false;
        }

        var top =
            h00 + (h10 - h00) * tx;
        var bottom =
            h01 + (h11 - h01) * tx;

        height =
            top + (bottom - top) * tz;

        return true;
    }

    private static float Read(
        RuntimeTerrainInfo terrain,
        int sampleCount,
        int row,
        int column)
    {
        var index =
            row * sampleCount +
            column;

        return index >= 0 &&
               index < terrain.Heights.Count
            ? terrain.Heights[index]
            : 0.0f;
    }
}
