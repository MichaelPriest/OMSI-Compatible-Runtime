using System.Text;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using OMSICompatible.World;

var root = Path.Combine(
    Path.GetTempPath(),
    "omsi-compatible-runtime-smoke-" + Guid.NewGuid().ToString("N"));

try
{
    var mapDirectory = Path.Combine(root, "maps", "SyntheticMap");
    var sceneryDirectory = Path.Combine(root, "Sceneryobjects", "Synthetic");
    var splineDirectory = Path.Combine(root, "Splines", "Synthetic");
    var vehicleDirectory = Path.Combine(root, "Vehicles", "Synthetic");

    Directory.CreateDirectory(mapDirectory);
    Directory.CreateDirectory(sceneryDirectory);
    Directory.CreateDirectory(splineDirectory);
    Directory.CreateDirectory(vehicleDirectory);

    File.WriteAllText(
        Path.Combine(mapDirectory, "global.cfg"),
        Lines(
            "[map]",
            "0",
            "0",
            "tile_0_0.map"),
        Encoding.Unicode);

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
            "2.5"),
        Encoding.Unicode);

    // This tile exists on disk but is intentionally not declared in global.cfg.
    File.WriteAllText(
        Path.Combine(mapDirectory, "tile_9_9.map"),
        "[object]\n");

    WriteTerrain(
        Path.Combine(mapDirectory, "tile_0_0.map.terrain"));

    File.WriteAllBytes(
        Path.Combine(mapDirectory, "tile_0_0.map.LM.bmp"),
        [0x42, 0x4D]);

    File.WriteAllText(
        Path.Combine(sceneryDirectory, "object.sco"),
        Lines("[friendlyname]", "Synthetic Object"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(splineDirectory, "road.sli"),
        Lines("[friendlyname]", "Synthetic Road"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(vehicleDirectory, "Synthetic.bus"),
        Lines(
            "[friendlyname]",
            "Synthetic",
            "Camera Bus",
            "[add_camera_driver]",
            "0",
            "4.5",
            "1.8",
            "-0.06",
            "55",
            "0",
            "-8",
            "[add_camera_driver]",
            "-0.7",
            "4.6",
            "1.9",
            "-0.06",
            "48",
            "-30",
            "4",
            "[view_schedule]",
            "[view_ticketselling]",
            "[add_camera_pax]",
            "0.8",
            "-2.0",
            "2.1",
            "-0.06",
            "45",
            "25",
            "0",
            "[set_camera_std]",
            "1",
            "[set_camera_outside_center]",
            "0",
            "-2.5",
            "1.2",
            "[rot_pnt_long]",
            "-2.7",
            "[inv_min_turnradius]",
            "0.13",
            "[newachse]",
            "achse_long",
            "3.1",
            "achse_raddurchmesser",
            "0.94",
            "achse_antrieb",
            "0",
            "[newachse]",
            "achse_long",
            "-2.7",
            "achse_raddurchmesser",
            "0.94",
            "achse_antrieb",
            "1"),
        Encoding.Unicode);

    if (!OmsiContentRoot.TryCreate(
            root,
            out var contentRoot,
            out var contentError) ||
        contentRoot is null)
    {
        throw new InvalidOperationException(
            $"Content root validation failed: {contentError}");
    }

    var maps = MapDiscovery.Discover(contentRoot);
    Require(maps.Count == 1, $"Expected 1 map, found {maps.Count}.");

    var buses = BusDiscovery.Discover(contentRoot);
    Require(buses.Count == 1, $"Expected 1 bus, found {buses.Count}.");

    var bus = buses[0];
    Require(
        bus.DriverCameras.Count == 2,
        "Synthetic driver cameras were not parsed.");
    Require(
        bus.PassengerCameras.Count == 1,
        "Synthetic passenger camera was not parsed.");
    Require(
        bus.StandardDriverCameraIndex == 1,
        "Standard driver camera index was not preserved.");
    Require(
        bus.ScheduleDriverCameraIndex == 1,
        "Schedule camera marker was not preserved.");
    Require(
        bus.TicketSellingDriverCameraIndex == 1,
        "Ticket-selling camera marker was not preserved.");
    Require(
        bus.OutsideCameraCenter is
        {
            X: 0,
            Y: -2.5,
            Z: 1.2
        },
        "Outside camera center was not preserved.");
    Require(
        bus.Physics.Axles.Count == 2,
        "Synthetic vehicle axles were not parsed.");
    Require(
        Math.Abs(
            (bus.Physics.WheelBaseMeters ?? 0.0) -
            5.8) < 0.0001,
        "Vehicle wheelbase was not derived from OMSI axles.");
    Require(
        bus.Physics.MaximumSteeringAngleDegrees is > 35.0 and < 40.0,
        "Vehicle steering angle was not derived from OMSI turn radius.");

    var map = maps[0];
    var world = WorldLoader.Load(contentRoot, map);

    Require(
        world.Tiles.Count == 1,
        $"Expected 1 active tile, found {world.Tiles.Count}.");
    Require(
        world.Objects.Count == 1,
        $"Expected 1 object, found {world.Objects.Count}.");
    Require(
        world.Splines.Count == 1,
        $"Expected 1 spline, found {world.Splines.Count}.");
    Require(
        world.PlacementParseIssueCount == 0,
        "Synthetic placements should parse without issues.");

    var worldObject = world.Objects[0];
    Require(worldObject.Id == 1001, "Object ID was not preserved.");
    Require(
        worldObject.Position.X == 10.5,
        "Object X position was not preserved.");
    Require(
        worldObject.Position.Y == 3.0,
        "Object height axis was not normalized.");
    Require(
        worldObject.Position.Z == 20.25,
        "Object horizontal Y axis was not normalized to renderer Z.");
    Require(
        worldObject.HeadingDegrees == 90,
        "Object heading was not preserved.");

    var worldSpline = world.Splines[0];
    Require(worldSpline.Id == 2001, "Spline ID was not preserved.");
    Require(
        worldSpline.Position.X == 5.0 &&
        worldSpline.Position.Y == 7.0 &&
        worldSpline.Position.Z == 6.0,
        "Spline axes were not normalized to renderer coordinates.");
    Require(
        worldSpline.LengthMeters == 100,
        "Spline length was not preserved.");
    Require(
        worldSpline.RadiusMeters == 0,
        "Spline radius was not preserved.");
    Require(
        worldSpline.GradientStartPercent == 1.5,
        "Spline start gradient was not preserved.");

    Require(
        world.Dependencies.RequiredCount == 2,
        "Expected two primary dependencies.");
    Require(
        world.Dependencies.MissingCount == 0,
        "Synthetic dependencies should resolve.");

    var tile = world.Tiles[0];
    Require(
        tile.Resources.TerrainPath is not null,
        "Terrain companion file was not discovered.");
    Require(
        tile.Resources.LightmapPath is not null,
        "Lightmap companion file was not discovered.");

    var terrain = tile.Terrain
        ?? throw new InvalidOperationException("Terrain grid was not loaded.");

    Require(
        terrain.CellCount == 1,
        "Terrain cell count was not preserved.");
    Require(
        terrain.Heights.Count == 4,
        "Terrain height sample count is incorrect.");
    Require(
        terrain.MinimumHeight == 0.0f &&
        terrain.MaximumHeight == 3.0f,
        "Terrain elevation range is incorrect.");
    Require(
        world.TerrainParseIssueCount == 0,
        "Synthetic terrain should parse without issues.");

    Console.WriteLine("OMSI Compatible Runtime smoke test passed.");
    Console.WriteLine(
        $"tiles={world.Tiles.Count}; " +
        $"objects={world.Objects.Count}; " +
        $"splines={world.Splines.Count}; " +
        $"dependencies={world.Dependencies.RequiredCount}; " +
        $"terrainSamples={terrain.Heights.Count}");

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

static void WriteTerrain(string path)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);

    writer.Write(1);
    writer.Write(0.0f);
    writer.Write(1.0f);
    writer.Write(2.0f);
    writer.Write(3.0f);
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
