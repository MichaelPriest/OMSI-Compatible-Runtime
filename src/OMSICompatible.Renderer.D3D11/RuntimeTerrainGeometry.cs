using System.Numerics;
using Vortice.Mathematics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeTerrainBatch(
    uint StartVertex,
    uint VertexCount,
    string? TexturePath,
    string? MaskTexturePath,
    string? DetailTexturePath,
    bool AdditiveLightmap,
    int? TerrainLayerIndex);

internal sealed record RuntimeTerrainGeometry(
    RuntimeTerrainVertex[] Vertices,
    IReadOnlyList<RuntimeTerrainBatch> Batches,
    Vector3 Center,
    float HorizontalSpan,
    float MinimumHeight,
    float MaximumHeight)
{
    public int TexturedBatchCount =>
        Batches.Count(
            static batch =>
                !string.IsNullOrWhiteSpace(
                    batch.TexturePath));

    public int MaskedLayerCount =>
        Batches.Count(
            static batch =>
                !string.IsNullOrWhiteSpace(
                    batch.MaskTexturePath));

    public static RuntimeTerrainGeometry Empty { get; } =
        new(
            [],
            Array.Empty<RuntimeTerrainBatch>(),
            Vector3.Zero,
            300.0f,
            0.0f,
            0.0f);
}

internal static class RuntimeTerrainGeometryBuilder
{
    private const double TileSizeMeters = 300.0;
    private const int VertexBudget = 1_200_000;
    private const int MinimumCellsPerAxis = 4;
    private const int MaximumCellsPerAxis = 64;

    public static RuntimeTerrainGeometry Build(
        IReadOnlyList<RuntimeTileInfo> tiles,
        IReadOnlyList<RuntimeGroundTextureInfo> groundTextures)
    {
        var terrainTiles =
            tiles
                .Where(
                    static tile =>
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

        var minimumTileX =
            terrainTiles.Min(
                static tile => tile.X);

        var maximumTileX =
            terrainTiles.Max(
                static tile => tile.X);

        var minimumTileY =
            terrainTiles.Min(
                static tile => tile.Y);

        var maximumTileY =
            terrainTiles.Max(
                static tile => tile.Y);

        var minimumHeight =
            terrainTiles.Min(
                static tile =>
                    tile.Terrain!.MinimumHeight);

        var maximumHeight =
            terrainTiles.Max(
                static tile =>
                    tile.Terrain!.MaximumHeight);

        var approximateLayersPerTile =
            Math.Max(
                1,
                1 +
                (int)Math.Ceiling(
                    terrainTiles.Average(
                        static tile =>
                            tile.TerrainMasks.Count)));

        var verticesPerTileBudget =
            Math.Max(
                6,
                VertexBudget /
                Math.Max(
                    1,
                    terrainTiles.Length *
                    approximateLayersPerTile));

        var cellsPerAxisBudget =
            Math.Clamp(
                (int)Math.Sqrt(
                    verticesPerTileBudget /
                    6.0),
                MinimumCellsPerAxis,
                MaximumCellsPerAxis);

        var vertices =
            new List<RuntimeTerrainVertex>(
                Math.Min(
                    VertexBudget,
                    terrainTiles.Length *
                    cellsPerAxisBudget *
                    cellsPerAxisBudget *
                    6));

        var batches =
            new List<RuntimeTerrainBatch>();

        var baseGround =
            groundTextures
                .FirstOrDefault(
                    static layer =>
                        layer.LayerIndex == 0);

        foreach (var tile in terrainTiles)
        {
            var terrain =
                tile.Terrain!;

            AppendTileLayer(
                tile,
                terrain,
                vertices,
                batches,
                baseGround?.MainTexturePath,
                maskTexturePath: null,
                baseGround?.DetailTexturePath,
                baseGround?.MainTextureRepeating ??
                1.0,
                baseGround?.DetailTextureRepeating ??
                1.0,
                heightOffset: 0.0f,
                fallbackToHeightColor:
                    baseGround?.MainTexturePath is null,
                additiveLightmap: false,
                terrainLayerIndex: 0,
                cellsPerAxisBudget,
                minimumHeight,
                maximumHeight);

            var overlayOrdinal = 0;

            foreach (var mask in
                     tile.TerrainMasks
                         .OrderBy(
                             static item =>
                                 item.LayerIndex))
            {
                var ground =
                    groundTextures
                        .FirstOrDefault(
                            layer =>
                                layer.LayerIndex ==
                                mask.LayerIndex);

                if (ground is null ||
                    string.IsNullOrWhiteSpace(
                        ground.MainTexturePath) ||
                    !File.Exists(mask.Path))
                {
                    continue;
                }

                overlayOrdinal++;

                AppendTileLayer(
                    tile,
                    terrain,
                    vertices,
                    batches,
                    ground.MainTexturePath,
                    mask.Path,
                    ground.DetailTexturePath,
                    ground.MainTextureRepeating,
                    ground.DetailTextureRepeating,
                    heightOffset:
                        Math.Min(
                            overlayOrdinal,
                            16) *
                        0.002f,
                    fallbackToHeightColor: false,
                    additiveLightmap: false,
                    terrainLayerIndex:
                        mask.LayerIndex,
                    cellsPerAxisBudget,
                    minimumHeight,
                    maximumHeight);
            }

            if (!string.IsNullOrWhiteSpace(
                    tile.LightmapPath) &&
                File.Exists(
                    tile.LightmapPath))
            {
                AppendTileLayer(
                    tile,
                    terrain,
                    vertices,
                    batches,
                    tile.LightmapPath,
                    maskTexturePath: null,
                    detailTexturePath: null,
                    repeating: 1.0,
                    detailRepeating: 1.0,
                    heightOffset: 0.04f,
                    fallbackToHeightColor: false,
                    additiveLightmap: true,
                    terrainLayerIndex: null,
                    cellsPerAxisBudget,
                    minimumHeight,
                    maximumHeight);
            }
        }

        var minimumX =
            (float)(
                minimumTileX *
                TileSizeMeters);

        var maximumX =
            (float)(
                (maximumTileX + 1) *
                TileSizeMeters);

        var minimumZ =
            (float)(
                minimumTileY *
                TileSizeMeters);

        var maximumZ =
            (float)(
                (maximumTileY + 1) *
                TileSizeMeters);

        var center =
            new Vector3(
                (minimumX + maximumX) *
                0.5f,
                (minimumHeight + maximumHeight) *
                0.5f,
                (minimumZ + maximumZ) *
                0.5f);

        var horizontalSpan =
            MathF.Max(
                maximumX - minimumX,
                maximumZ - minimumZ);

        return new RuntimeTerrainGeometry(
            vertices.ToArray(),
            batches.ToArray(),
            center,
            MathF.Max(
                horizontalSpan,
                300.0f),
            minimumHeight,
            maximumHeight);
    }

    private static void AppendTileLayer(
        RuntimeTileInfo tile,
        RuntimeTerrainInfo terrain,
        List<RuntimeTerrainVertex> vertices,
        List<RuntimeTerrainBatch> batches,
        string? texturePath,
        string? maskTexturePath,
        string? detailTexturePath,
        double repeating,
        double detailRepeating,
        float heightOffset,
        bool fallbackToHeightColor,
        bool additiveLightmap,
        int? terrainLayerIndex,
        int cellsPerAxisBudget,
        float globalMinimumHeight,
        float globalMaximumHeight)
    {
        var sampleCount =
            terrain.CellCount +
            1;

        if (terrain.Heights.Count !=
            sampleCount *
            sampleCount)
        {
            return;
        }

        var step =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    terrain.CellCount /
                    (double)cellsPerAxisBudget));

        var spacing =
            TileSizeMeters /
            terrain.CellCount;

        var originX =
            tile.X *
            TileSizeMeters;

        var originZ =
            tile.Y *
            TileSizeMeters;

        var layerStart =
            vertices.Count;

        for (var row = 0;
             row < terrain.CellCount;
             row += step)
        {
            var nextRow =
                Math.Min(
                    row + step,
                    terrain.CellCount);

            for (var column = 0;
                 column < terrain.CellCount;
                 column += step)
            {
                var nextColumn =
                    Math.Min(
                        column + step,
                        terrain.CellCount);

                var h00 =
                    Height(
                        terrain,
                        sampleCount,
                        row,
                        column);

                var h10 =
                    Height(
                        terrain,
                        sampleCount,
                        row,
                        nextColumn);

                var h01 =
                    Height(
                        terrain,
                        sampleCount,
                        nextRow,
                        column);

                var h11 =
                    Height(
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

                var localX0 =
                    column *
                    spacing;

                var localX1 =
                    nextColumn *
                    spacing;

                var localZ0 =
                    row *
                    spacing;

                var localZ1 =
                    nextRow *
                    spacing;

                var x0 =
                    originX +
                    localX0;

                var x1 =
                    originX +
                    localX1;

                var z0 =
                    originZ +
                    localZ0;

                var z1 =
                    originZ +
                    localZ1;

                var uv00 =
                    CreateUv(
                        localX0,
                        localZ0,
                        repeating);

                var uv10 =
                    CreateUv(
                        localX1,
                        localZ0,
                        repeating);

                var uv01 =
                    CreateUv(
                        localX0,
                        localZ1,
                        repeating);

                var uv11 =
                    CreateUv(
                        localX1,
                        localZ1,
                        repeating);

                var maskUv00 =
                    CreateMaskUv(
                        localX0,
                        localZ0);

                var maskUv10 =
                    CreateMaskUv(
                        localX1,
                        localZ0);

                var maskUv01 =
                    CreateMaskUv(
                        localX0,
                        localZ1);

                var maskUv11 =
                    CreateMaskUv(
                        localX1,
                        localZ1);

                var detailUv00 =
                    CreateUv(
                        localX0,
                        localZ0,
                        detailRepeating);

                var detailUv10 =
                    CreateUv(
                        localX1,
                        localZ0,
                        detailRepeating);

                var detailUv01 =
                    CreateUv(
                        localX0,
                        localZ1,
                        detailRepeating);

                var detailUv11 =
                    CreateUv(
                        localX1,
                        localZ1,
                        detailRepeating);

                var color =
                    fallbackToHeightColor
                        ? TerrainColor(
                            (h00 + h10 + h01 + h11) *
                            0.25f,
                            globalMinimumHeight,
                            globalMaximumHeight)
                        : new Color4(
                            1,
                            1,
                            1,
                            1);

                AppendTriangle(
                    new Vector3(
                        (float)x0,
                        h00 + heightOffset,
                        (float)z0),
                    uv00,
                    maskUv00,
                    detailUv00,
                    new Vector3(
                        (float)x1,
                        h11 + heightOffset,
                        (float)z1),
                    uv11,
                    maskUv11,
                    detailUv11,
                    new Vector3(
                        (float)x1,
                        h10 + heightOffset,
                        (float)z0),
                    uv10,
                    maskUv10,
                    detailUv10,
                    color,
                    vertices);

                AppendTriangle(
                    new Vector3(
                        (float)x0,
                        h00 + heightOffset,
                        (float)z0),
                    uv00,
                    maskUv00,
                    detailUv00,
                    new Vector3(
                        (float)x0,
                        h01 + heightOffset,
                        (float)z1),
                    uv01,
                    maskUv01,
                    detailUv01,
                    new Vector3(
                        (float)x1,
                        h11 + heightOffset,
                        (float)z1),
                    uv11,
                    maskUv11,
                    detailUv11,
                    color,
                    vertices);
            }
        }

        var count =
            vertices.Count -
            layerStart;

        if (count <= 0)
        {
            return;
        }

        batches.Add(
            new RuntimeTerrainBatch(
                (uint)layerStart,
                (uint)count,
                texturePath,
                maskTexturePath,
                detailTexturePath,
                additiveLightmap,
                terrainLayerIndex));
    }

    private static float Height(
        RuntimeTerrainInfo terrain,
        int sampleCount,
        int row,
        int column) =>
        terrain.Heights[
            row *
            sampleCount +
            column];

    private static Vector2 CreateUv(
        double localX,
        double localZ,
        double repeating)
    {
        var safeRepeating =
            double.IsFinite(repeating) &&
            repeating > 0
                ? repeating
                : 1.0;

        // Runtime world X is mirrored relative to OMSI source X to keep
        // the renderer ground plane right-handed. Sample terrain textures
        // from the corresponding source-side U coordinate so lightmaps,
        // masks and base/detail textures remain in the same place as OMSI.
        return new Vector2(
            (float)(
                (1.0 -
                 localX /
                     TileSizeMeters) *
                safeRepeating),
            (float)(
                localZ /
                TileSizeMeters *
                safeRepeating));
    }

    private static Vector2 CreateMaskUv(
        double localX,
        double localZ) =>
        new(
            (float)(
                1.0 -
                localX /
                    TileSizeMeters),
            (float)(
                localZ /
                TileSizeMeters));

    private static Color4 TerrainColor(
        float height,
        float minimumHeight,
        float maximumHeight)
    {
        var range =
            maximumHeight -
            minimumHeight;

        var normalized =
            range > 0.001f
                ? Math.Clamp(
                    (height -
                     minimumHeight) /
                    range,
                    0.0f,
                    1.0f)
                : 0.5f;

        return new Color4(
            0.16f +
            normalized *
            0.18f,
            0.34f +
            normalized *
            0.30f,
            0.17f +
            normalized *
            0.15f,
            1.0f);
    }

    private static void AppendTriangle(
        Vector3 a,
        Vector2 uvA,
        Vector2 maskUvA,
        Vector2 detailUvA,
        Vector3 b,
        Vector2 uvB,
        Vector2 maskUvB,
        Vector2 detailUvB,
        Vector3 c,
        Vector2 uvC,
        Vector2 maskUvC,
        Vector2 detailUvC,
        Color4 color,
        ICollection<RuntimeTerrainVertex> output)
    {
        output.Add(
            new RuntimeTerrainVertex(
                a,
                color,
                uvA,
                maskUvA,
                detailUvA));

        output.Add(
            new RuntimeTerrainVertex(
                b,
                color,
                uvB,
                maskUvB,
                detailUvB));

        output.Add(
            new RuntimeTerrainVertex(
                c,
                color,
                uvC,
                maskUvC,
                detailUvC));
    }
}

internal readonly struct RuntimeTerrainVertex
{
    public const uint SizeInBytes = 52;

    public RuntimeTerrainVertex(
        Vector3 position,
        Color4 color,
        Vector2 uv,
        Vector2 maskUv,
        Vector2 detailUv)
    {
        Position = position;
        Color = color;
        Uv = uv;
        MaskUv = maskUv;
        DetailUv = detailUv;
    }

    public readonly Vector3 Position;
    public readonly Color4 Color;
    public readonly Vector2 Uv;
    public readonly Vector2 MaskUv;
    public readonly Vector2 DetailUv;
}
