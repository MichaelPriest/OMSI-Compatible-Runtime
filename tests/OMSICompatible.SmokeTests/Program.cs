using System.Text;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using OmsiCompat.Scripting;
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
    var vehicleScriptDirectory =
        Path.Combine(
            vehicleDirectory,
            "script");
    var programDirectory =
        Path.Combine(
            root,
            "program");

    Directory.CreateDirectory(mapDirectory);
    Directory.CreateDirectory(sceneryDirectory);
    Directory.CreateDirectory(splineDirectory);
    Directory.CreateDirectory(vehicleDirectory);
    Directory.CreateDirectory(vehicleScriptDirectory);
    Directory.CreateDirectory(programDirectory);

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
        Lines(
            "[friendlyname]",
            "Synthetic Object",
            "[onlyeditor]"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(splineDirectory, "road.sli"),
        Lines("[friendlyname]", "Synthetic Road"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            programDirectory,
            "varlist_roadvehicle.txt"),
        Lines(
            "Throttle",
            "Brake",
            "Velocity"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            programDirectory,
            "stringvarlist_roadvehicle.txt"),
        Lines(
            "number",
            "act_route"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            programDirectory,
            "varlist_system.txt"),
        Lines(
            "Timegap",
            "GetTime"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            programDirectory,
            "callbacklist_roadvehicle.txt"),
        Lines(
            "GetRouteIndex",
            "GetTTDelay"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            programDirectory,
            "callbacklist_scripttex.txt"),
        Lines(
            "STNewTex",
            "STTextOut"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleScriptDirectory,
            "main.osc"),
        Lines(
            "{init}",
            "650",
            "(S.L.engine_speed)",
            "{end}",
            "{frame}",
            "(L.L.engine_speed)",
            "10",
            "+",
            "(S.L.engine_speed)",
            "(L.L.engine_speed)",
            "(F.L.engine_curve)",
            "(S.L.engine_output)",
            "(M.L.helper)",
            ""Linha 342P"",
            "(S.$.IBIS_line)",
            "(L.$.IBIS_line)",
            "" - PENHA"",
            "$+",
            "(S.$.number)",
            "(L.$.number)",
            "$length",
            "(S.L.string_length)",
            "(M.V.GetRouteIndex)",
            "(S.L.callback_result)",
            ""125"",
            "$StrToFloat",
            "(S.L.parsed_number)",
            "(L.S.GetTime)",
            "(L.L.horn_timer)",
            "3",
            "+",
            ">",
            "{if}",
            "0",
            "(S.L.horn_timer)",
            "{endif}",
            "{end}",
            "{trigger:collision}",
            "(L.L.horn_timer)",
            "0",
            "=",
            "{if}",
            "(T.L.ev_AI_Horn)",
            "(L.S.GetTime)",
            "(S.L.horn_timer)",
            "{endif}",
            "{end}",
            "{macro:helper}",
            "(C.L.engine_idle)",
            "(S.L.idle_copy)",
            "{end}"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleScriptDirectory,
            "engine_varlist.txt"),
        Lines(
            "engine_speed",
            "engine_output",
            "idle_copy",
            "horn_timer",
            "string_length",
            "callback_result",
            "parsed_number"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleScriptDirectory,
            "strings.txt"),
        "IBIS_line",
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleScriptDirectory,
            "constants.txt"),
        Lines(
            "[const]",
            "engine_idle",
            "650",
            "[newcurve]",
            "engine_curve",
            "[pnt]",
            "0",
            "0",
            "[pnt]",
            "1000",
            "1"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(vehicleDirectory, "Synthetic.bus"),
        Lines(
            "[friendlyname]",
            "Synthetic",
            "Camera Bus",
            "[varnamelist]",
            "2",
            @"script\engine_varlist.txt",
            @"script\missing_varlist.txt",
            "[stringvarnamelist]",
            "1",
            @"script\strings.txt",
            "[script]",
            "1",
            @"script\main.osc",
            "[constfile]",
            "1",
            @"script\constants.txt",
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
            "[add_camera_reflexion]",
            "-1.3",
            "5.4",
            "2.0",
            "0",
            "52",
            "175",
            "-10",
            "[add_camera_reflexion_2]",
            "1.3",
            "5.8",
            "2.1",
            "0",
            "52",
            "200",
            "-12",
            "0.15",
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
        bus.ScriptManifest.ScriptFiles.Count == 1 &&
        bus.ScriptManifest.VariableLists.Count == 2 &&
        bus.ScriptManifest.StringVariableLists.Count == 1 &&
        bus.ScriptManifest.ConstantFiles.Count == 1,
        "Synthetic OMSI script manifest counts are incorrect.");
    Require(
        bus.ScriptManifest.RegisteredFileCount == 5 &&
        bus.ScriptManifest.MissingFileCount == 1,
        "Synthetic OMSI script manifest missing-file diagnostics are incorrect.");
    Require(
        bus.ScriptManifest.ScriptFiles[0].Exists &&
        !bus.ScriptManifest.VariableLists[1].Exists,
        "Synthetic OMSI script file resolution is incorrect.");
    var scriptCatalog =
        OmsiScriptCatalogLoader.Load(
            contentRoot,
            bus.ScriptManifest);

    Require(
        scriptCatalog.NumericVariables.Contains(
            "engine_speed") &&
        scriptCatalog.NumericVariables.Contains(
            "engine_output") &&
        scriptCatalog.NumericVariables.Contains(
            "idle_copy"),
        "Synthetic OMSI numeric variables were not loaded.");
    Require(
        scriptCatalog.StringVariables.Contains(
            "IBIS_line") &&
        scriptCatalog.StringVariables.Contains(
            "number"),
        "Synthetic/user and predefined OMSI string variables were not loaded.");
    Require(
        scriptCatalog.NumericVariables.Contains(
            "Throttle") &&
        scriptCatalog.NumericVariables.Contains(
            "Velocity"),
        "Predefined roadvehicle variables were not loaded from OMSI program data.");
    Require(
        scriptCatalog.SystemVariables.Contains(
            "Timegap") &&
        scriptCatalog.SystemVariables.Contains(
            "GetTime"),
        "Predefined OMSI system variables were not loaded.");
    Require(
        scriptCatalog.VehicleCallbacks.Contains(
            "GetRouteIndex") &&
        scriptCatalog.VehicleCallbacks.Contains(
            "GetTTDelay"),
        "Predefined roadvehicle callbacks were not loaded.");
    Require(
        scriptCatalog.ScriptTextureCallbacks.Contains(
            "STNewTex") &&
        scriptCatalog.ScriptTextureCallbacks.Contains(
            "STTextOut"),
        "Predefined script-texture callbacks were not loaded.");
    Require(
        scriptCatalog.Constants.TryGetValue(
            "engine_idle",
            out var engineIdle) &&
        Math.Abs(
            engineIdle -
            650.0) < 0.0001,
        "Synthetic OMSI constant was not loaded.");
    Require(
        scriptCatalog.Curves.TryGetValue(
            "engine_curve",
            out var engineCurve) &&
        Math.Abs(
            engineCurve.Evaluate(
                500.0) -
            0.5) < 0.0001,
        "Synthetic OMSI curve interpolation is incorrect.");
    Require(
        scriptCatalog.Program.InitBlocks.Count == 1 &&
        scriptCatalog.Program.FrameBlocks.Count == 1 &&
        scriptCatalog.Program.Macros.ContainsKey(
            "helper"),
        "Synthetic OMSI script entry points were not parsed.");

    var scriptRuntime =
        new OmsiScriptRuntime(
            scriptCatalog);

    var invokedSystemMacros =
        new List<string>();

    scriptRuntime.SystemMacroHandler =
        (name, context) =>
        {
            invokedSystemMacros.Add(
                name);

            if (!string.Equals(
                    name,
                    "GetRouteIndex",
                    StringComparison.Ordinal))
            {
                return false;
            }

            context.PushFloat(
                42.0);
            return true;
        };

    scriptRuntime.ExecuteInit();
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "engine_speed") -
            650.0) < 0.0001,
        "OMSI script {init} execution failed.");

    scriptRuntime.ExecuteFrame();
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "engine_speed") -
            660.0) < 0.0001,
        "OMSI script arithmetic/local variable execution failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "engine_output") -
            0.66) < 0.0001,
        "OMSI script curve execution failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "idle_copy") -
            650.0) < 0.0001,
        "OMSI script macro/constant execution failed.");
    Require(
        scriptRuntime.GetStringLocal(
            "IBIS_line") ==
            "Linha 342P",
        "Quoted OMSI string literal/tokenizer failed.");
    Require(
        scriptRuntime.GetStringLocal(
            "number") ==
            "Linha 342P - PENHA",
        "OMSI string stack concatenation failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "string_length") -
            18.0) < 0.0001,
        "OMSI string length operation failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "callback_result") -
            42.0) < 0.0001 &&
        invokedSystemMacros.Contains(
            "GetRouteIndex"),
        "OMSI synchronous system-macro bridge failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "parsed_number") -
            125.0) < 0.0001,
        "OMSI string-to-float conversion failed.");

    var requestedSoundTriggers =
        new List<string>();

    scriptRuntime.SoundTriggerRequested +=
        requestedSoundTriggers.Add;

    scriptRuntime.SetSystem(
        "GetTime",
        10.0);
    scriptRuntime.ExecuteTrigger(
        "collision");

    Require(
        requestedSoundTriggers.SequenceEqual(
            ["ev_AI_Horn"]),
        "OMSI sound trigger execution failed.");
    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "horn_timer") -
            10.0) < 0.0001,
        "OMSI system-variable load/store inside trigger failed.");

    scriptRuntime.SetSystem(
        "GetTime",
        14.0);
    scriptRuntime.ExecuteFrame();

    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "horn_timer")) < 0.0001,
        "OMSI {if} control flow / timer reset failed.");

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
        bus.ReflectionCameras.Count == 2,
        "Synthetic reflection cameras were not parsed.");
    Require(
        bus.ReflectionCameras[0].RuntimeTextureName ==
            "reflexion0.bmp" &&
        bus.ReflectionCameras[0].MaximumRenderDistanceMeters is null,
        "Permanent OMSI reflection camera metadata is incorrect.");
    Require(
        bus.ReflectionCameras[1].RuntimeTextureName ==
            "reflexion1.bmp" &&
        Math.Abs(
            (bus.ReflectionCameras[1].MaximumRenderDistanceMeters ?? 0.0) -
            0.15) < 0.0001,
        "Distance-limited OMSI reflection camera metadata is incorrect.");
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
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\object.sco",
            out var editorOnlyAsset) &&
        editorOnlyAsset.OnlyEditor &&
        !editorOnlyAsset.IsRenderable,
        "[onlyeditor] scenery must remain in the world but be hidden in game rendering.");

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
