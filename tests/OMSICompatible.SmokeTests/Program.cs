using OmsiCompat.Core;
using OmsiCompat.Map;
using OMSICompatible.World;

var root = Path.Combine(
    Path.GetTempPath(),
    "omsi-compatible-runtime-smoke-" + Guid.NewGuid().ToString("N"));

try
{
    var mapDirectory = Path.Combine(root, "maps", "SyntheticMap");
    var sceneryDirectory = Path.Combine(root, "Sceneryobjects", "Synthetic");
    var splineDirectory = Path.Combine(root, "Splines", "Synthetic");

    Directory.CreateDirectory(mapDirectory);
    Directory.CreateDirectory(sceneryDirectory);
    Directory.CreateDirectory(splineDirectory);

    File.WriteAllText(
        Path.Combine(mapDirectory, "global.cfg"),
        Lines(
            "[map]",
            "0",
            "0",
            "tile_0_0.map"));

    File.WriteAllText(
        Path.Combine(mapDirectory, "tile_0_0.map"),
        Lines(
            "[object]",
            "0",
            @"Sceneryobjects\Synthetic\object.sco",
            "1001",
            "10.5",
            "20.25",
            "3",
            "90",
            "2",
            "1",
            "0",
            "",
            "[spline]",
            "0",
            @"Splines\Synthetic\road.sli",
            "2001",
            "-1",
            "-1",
            "5",
            "7",
            "6",
            "45",
            "100",
            "0",
            "1.5",
            "2.5"));

    // This tile exists on disk but is intentionally not declared in global.cfg.
    File.WriteAllText(
        Path.Combine(mapDirectory, "tile_9_9.map"),
        "[object]\n");

    File.WriteAllBytes(
        Path.Combine(mapDirectory, "tile_0_0.map.terrain"),
        [0x01, 0x02, 0x03]);

    File.WriteAllBytes(
        Path.Combine(mapDirectory, "tile_0_0.map.LM.bmp"),
        [0x42, 0x4D]);

    File.WriteAllText(
        Path.Combine(sceneryDirectory, "object.sco"),
        Lines("[friendlyname]", "Synthetic Object"));

    File.WriteAllText(
        Path.Combine(splineDirectory, "road.sli"),
        Lines("[friendlyname]", "Synthetic Road"));

    Require(
        OmsiContentRoot.TryCreate(root, out var contentRoot, out var contentError) &&
        contentRoot is not null,
        $"Content root validation failed: {contentError}");

    var maps = MapDiscovery.Discover(contentRoot);
    Require(maps.Count == 1, $"Expected 1 map, found {maps.Count}.");

    var map = maps[0];
    var world = WorldLoader.Load(contentRoot, map);

    Require(world.Tiles.Count == 1, $"Expected 1 active tile, found {world.Tiles.Count}.");
    Require(world.Objects.Count == 1, $"Expected 1 object, found {world.Objects.Count}.");
    Require(world.Splines.Count == 1, $"Expected 1 spline, found {world.Splines.Count}.");
    Require(world.PlacementParseIssueCount == 0, "Synthetic placements should parse without issues.");

    var worldObject = world.Objects[0];
    Require(worldObject.Id == 1001, "Object ID was not preserved.");
    Require(worldObject.Position.X == 10.5, "Object X position was not preserved.");
    Require(worldObject.HeadingDegrees == 90, "Object heading was not preserved.");

    var worldSpline = world.Splines[0];
    Require(worldSpline.Id == 2001, "Spline ID was not preserved.");
    Require(worldSpline.LengthMeters == 100, "Spline length was not preserved.");
    Require(worldSpline.RadiusMeters == 0, "Spline radius was not preserved.");
    Require(worldSpline.GradientStartPercent == 1.5, "Spline start gradient was not preserved.");

    Require(world.Dependencies.RequiredCount == 2, "Expected two primary dependencies.");
    Require(world.Dependencies.MissingCount == 0, "Synthetic dependencies should resolve.");

    var resources = world.Tiles[0].Resources;
    Require(resources.TerrainPath is not null, "Terrain companion file was not discovered.");
    Require(resources.LightmapPath is not null, "Lightmap companion file was not discovered.");

    Console.WriteLine("OMSI Compatible Runtime smoke test passed.");
    Console.WriteLine(
        $"tiles={world.Tiles.Count}; objects={world.Objects.Count}; splines={world.Splines.Count}; dependencies={world.Dependencies.RequiredCount}");

    return 0;
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static string Lines(params string[] values)
{
    return string.Join(Environment.NewLine, values) + Environment.NewLine;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
