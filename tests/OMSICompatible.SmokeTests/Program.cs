using System.Text;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Models;
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
    var vehicleModelDirectory =
        Path.Combine(
            vehicleDirectory,
            "model");
    var programDirectory =
        Path.Combine(
            root,
            "program");

    Directory.CreateDirectory(mapDirectory);
    Directory.CreateDirectory(sceneryDirectory);
    Directory.CreateDirectory(splineDirectory);
    Directory.CreateDirectory(vehicleDirectory);
    Directory.CreateDirectory(vehicleScriptDirectory);
    Directory.CreateDirectory(vehicleModelDirectory);
    Directory.CreateDirectory(programDirectory);

    File.WriteAllText(
        Path.Combine(
            root,
            "options.cfg"),
        Lines(
            "GENERAL ------------------------",
            "[language]",
            "PTBR",
            "[ticketselling]",
            "2",
            "[no_collision_pedastrians]",
            "[autoCenter]",
            "[nopreview]",
            "[font_typewriter]",
            "Courier New",
            "MULTITHREADING ------------------------",
            "[no_multithreading_calculate]",
            "[no_multithreading_texload]",
            "GRAPHICS ------------------------",
            "[performance_realreflexions]",
            "economy",
            "[performance_reflTexSize]",
            "9",
            "[texmax256]",
            "[texture_uselow]",
            "[no_tex_low_high_switch]",
            "[no_lightmap_terr]",
            "[no_reflmap]",
            "[maxFPS]",
            "45",
            "SOUND ------------------------",
            "[sound_maxcount]",
            "333",
            "[sound_vol_master]",
            "0.5",
            "[sound_noreverb]",
            "GAME CONTROLERS ------------------------",
            "[gamectrleron]",
            "AI ------------------------",
            "[AIMaxCountRandom]",
            "77",
            "123",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "[AIUnschedFactor]",
            "65",
            "[AIMaxCountParked]",
            "35",
            "[AIPassFactor]",
            "120",
            "[AIMaxCountScheduled]",
            "44",
            "[AIPriorityScheduled]",
            "3"));

    var importedOptions =
        OmsiRuntimeOptions.ImportFromOmsi(
            root);

    Require(
        importedOptions.Language == "PTBR" &&
        importedOptions.TicketSalesMode == 2 &&
        !importedOptions.UserVehiclePedestrianCollisions &&
        importedOptions.AutomaticSteeringCenter &&
        !importedOptions.ShowVehiclePreview &&
        importedOptions.ReducedMultithreading &&
        importedOptions.GameControllerEnabled &&
        importedOptions.TargetFps == 45 &&
        importedOptions.RealTimeReflectionTextureSize == 512 &&
        importedOptions.LimitTexturesTo256 &&
        importedOptions.OnlyLowResolutionTextures &&
        !importedOptions.LowResolutionTexturesAtDistance &&
        !importedOptions.MaterialTerrainLightMap &&
        !importedOptions.MaterialReflectionMap &&
        importedOptions.MaximumSoundCount == 333 &&
        importedOptions.MasterVolumePercent == 50 &&
        !importedOptions.ReverbEffects &&
        importedOptions.MaximumUnscheduledTraffic == 77 &&
        importedOptions.MaximumPeople == 123 &&
        importedOptions.RoadTrafficFactorPercent == 65 &&
        importedOptions.ParkedCarsPercent == 35 &&
        importedOptions.PassengerFactorPercent == 120 &&
        importedOptions.MaximumScheduledTraffic == 44 &&
        importedOptions.ScheduledTrafficPriority == 3,
        "OMSI options.cfg compatibility import did not preserve the expected OMSI 2 settings.");

    var legacyDirectXPath =
        Path.Combine(
            sceneryDirectory,
            "legacy-uv.x");

    WriteSyntheticDirectX(
        legacyDirectXPath);

    var legacyDirectXGeometry =
        new OmsiDirectXTextGeometryReader()
            .Read(
                legacyDirectXPath);

    Require(
        legacyDirectXGeometry.IsLoaded &&
        legacyDirectXGeometry.ErrorCode is null &&
        legacyDirectXGeometry.Uvs.Length == 6 &&
        Math.Abs(
            legacyDirectXGeometry.Uvs[1]) <
            0.0001f &&
        Math.Abs(
            legacyDirectXGeometry.Uvs[5] -
            1.0f) <
            0.0001f,
        "Legacy DirectX .x V texture coordinates must remain in native Direct3D orientation.");

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
            "[object]",
            "0",
            @"Sceneryobjects\Synthetic\tree.sco",
            "1002",
            "15",
            "25",
            "0",
            "0",
            "0",
            "0",
            "3",
            "tree.tga",
            "12",
            "1",
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

    Directory.CreateDirectory(
        Path.Combine(
            sceneryDirectory,
            "texture"));

    File.WriteAllBytes(
        Path.Combine(
            sceneryDirectory,
            "texture",
            "tree.tga"),
        [0x00]);

    File.WriteAllText(
        Path.Combine(
            sceneryDirectory,
            "tree.sco"),
        Lines(
            "[friendlyname]",
            "Synthetic Tree",
            "[tree]",
            "tree.tga",
            "8",
            "14",
            "0.8",
            "1.2",
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
            "1",
            "(S.L.mesh_visible)",
            "0.5",
            "(S.L.mesh_alpha)",
            "0.75",
            "(S.L.lights_stand)",
            "1",
            "(S.L.cockpit_light_test)",
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
            "\"Linha 342P\"",
            "(S.$.IBIS_line)",
            "(L.$.IBIS_line)",
            "\" - PENHA\"",
            "$+",
            "(S.$.number)",
            "(L.$.number)",
            "$length",
            "(S.L.string_length)",
            "(M.V.GetRouteIndex)",
            "(S.L.callback_result)",
            "\"125\"",
            "$StrToFloat",
            "(S.L.parsed_number)",
            "(L.S.GetTime)",
            "\"announcement.wav\"",
            "(T.F.ev_IBIS_Ansagen)",
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
            "parsed_number",
            "mesh_visible",
            "mesh_alpha",
            "lights_stand",
            "cockpit_light_test"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleScriptDirectory,
            "strings.txt"),
        Lines(
            "IBIS_line",
            "Matrix_SchildFrnt"),
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
        Path.Combine(
            vehicleModelDirectory,
            "model.cfg"),
        Lines(
            "[texttexture]",
            "IBIS_line",
            "IBIS-2_5x7",
            "256",
            "64",
            "0",
            "50",
            "50",
            "50",
            "[CTCTexture]",
            "body",
            "regen.tga",
            "[CTCTexture]",
            "detail",
            "detail.bmp",
            "[LOD]",
            "0.1",
            "[mesh]",
            "triangle.o3d",
            "[mesh_ident]",
            "steering_parent",
            "[viewpoint]",
            "3",
            "[visible]",
            "mesh_visible",
            "1",
            "[matl]",
            "regen.tga",
            "0",
            "[matl_alpha]",
            "2",
            "[alphascale]",
            "mesh_alpha",
            "[useTextTexture]",
            "0",
            "[matl_freetex]",
            "regen.tga",
            "Matrix_SchildFrnt",
            "[matl_envmap]",
            "envmap.bmp",
            "0.5",
            "[matl_envmap_mask]",
            "envmask.bmp",
            "[matl_bumpmap]",
            "bump.bmp",
            "0.05",
            "[matl_transmap]",
            "[matl_allcolor]",
            "0.8",
            "0.7",
            "0.6",
            "0.9",
            "0.4",
            "0.4",
            "0.4",
            "0.1",
            "0.1",
            "0.1",
            "0.02",
            "0.03",
            "0.04",
            "8",
            "[matl_lightmap]",
            "panel_lm.bmp",
            "lights_stand",
            "[matl_change]",
            "regen.tga",
            "0",
            "cockpit_light_test",
            "[matl_item]",
            "[matl_alpha]",
            "1",
            "[matl_transmap]",
            "panel_mask.bmp",
            "[matl_noZwrite]",
            "[matl_allcolor]",
            "1",
            "1",
            "1",
            "1",
            "1",
            "1",
            "1",
            "0",
            "0",
            "0",
            "0.24",
            "0.23",
            "0.2",
            "0",
            "[matl_lightmap]",
            "panel_item_lm.bmp",
            "lights_stand",
            "[matl_nightmap]",
            "panel_n.bmp",
            "[light_enh]",
            "0.1",
            "2.2",
            "1.3",
            "255",
            "128",
            "0",
            "0.03",
            "cockpit_light_test",
            "1.5",
            "0.01",
            "3",
            "0.05",
            "[light_enh_2]",
            "0.998",
            "5.634",
            "0.827",
            "0",
            "1",
            "0",
            "0",
            "0",
            "1",
            "0",
            "0",
            "200",
            "200",
            "255",
            "0.4",
            "50",
            "150",
            "lights_stand",
            "3.0",
            "0.15",
            "1",
            "1",
            "0.1",
            "D_Scheinwerfer.bmp",
            "[newanim]",
            "origin_from_mesh",
            "origin_rot_y",
            "90",
            "anim_rot",
            "Axle_Steering_0_L",
            "1680",
            "offset",
            "2",
            "delay",
            "10",
            "maxspeed",
            "360",
            "[LOD]",
            "0",
            "[mesh]",
            "triangle.o3d",
            "[animparent]",
            "steering_parent",
            "[viewpoint]",
            "1"),
        Encoding.Unicode);

    WriteSyntheticO3d(
        Path.Combine(
            vehicleModelDirectory,
            "triangle.o3d"));

    var zeroKeyO3dPath =
        Path.Combine(
            vehicleModelDirectory,
            "triangle-zero-key.o3d");

    WriteSyntheticO3d(
        zeroKeyO3dPath,
        extendedHeader: true,
        protectionKey: 0);

    var zeroKeyGeometry =
        OmsiO3dGeometryReader.ReadFile(
            zeroKeyO3dPath);

    Require(
        zeroKeyGeometry.IsLoaded &&
        zeroKeyGeometry.ErrorCode is null &&
        zeroKeyGeometry.Positions.Length == 9 &&
        Math.Abs(
            zeroKeyGeometry.Positions[0] -
            -1.0f) < 0.0001 &&
        Math.Abs(
            zeroKeyGeometry.Positions[3] -
            1.0f) < 0.0001 &&
        Math.Abs(
            zeroKeyGeometry.Positions[8] -
            1.0f) < 0.0001 &&
        Math.Abs(
            zeroKeyGeometry.Normals[2] -
            1.0f) < 0.0001 &&
        Math.Abs(
            zeroKeyGeometry.Uvs[5] -
            1.0f) < 0.0001,
        "OMSI v5 zero-key encrypted vertex stream was not decoded back to the original geometry.");

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "panel_lm.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "panel_n.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "panel_item_lm.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "panel_mask.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "envmap.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "envmask.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "bump.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleModelDirectory,
            "regen.tga"),
        [0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleDirectory,
            "skin_body.tga"),
        [0x00]);

    File.WriteAllBytes(
        Path.Combine(
            vehicleDirectory,
            "skin_detail.bmp"),
        [0x42, 0x4D]);

    File.WriteAllText(
        Path.Combine(
            vehicleDirectory,
            "synthetic-repaints.cti"),
        Lines(
            "[item]",
            "Blue Fleet",
            "body",
            "skin_body.tga",
            "[item]",
            "Blue Fleet",
            "detail",
            "skin_detail.bmp",
            "[setvar]",
            "mesh_visible",
            "1"),
        Encoding.Unicode);

    File.WriteAllBytes(
        Path.Combine(
            vehicleDirectory,
            "Preview.png"),
        [0x89, 0x50, 0x4E, 0x47]);

    File.WriteAllText(
        Path.Combine(vehicleDirectory, "Synthetic.bus"),
        Lines(
            "[model]",
            @"model\model.cfg",
            "[friendlyname]",
            "Synthetic Coachworks",
            "Camera Bus",
            "Test Skin",
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
            "[mass]",
            "10.9",
            "[momentofintertia]",
            "300",
            "80",
            "300",
            "[schwerpunkt]",
            "1.3",
            "[rollwiderstand]",
            "1000",
            "[ai_deltaheight]",
            "-0.12",
            "[rot_pnt_long]",
            "-2.7",
            "[inv_min_turnradius]",
            "0.13",
            "[newachse]",
            "achse_long",
            "3.1",
            "achse_maxwidth",
            "2.4",
            "achse_minwidth",
            "1.76",
            "achse_raddurchmesser",
            "0.94",
            "achse_feder",
            "240",
            "achse_maxforce",
            "90",
            "achse_daempfer",
            "20",
            "achse_antrieb",
            "0",
            "[newachse]",
            "achse_long",
            "-2.7",
            "achse_maxwidth",
            "2.4",
            "achse_minwidth",
            "1.4",
            "achse_raddurchmesser",
            "0.94",
            "achse_feder",
            "280",
            "achse_maxforce",
            "116",
            "achse_daempfer",
            "20",
            "achse_antrieb",
            "1"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleDirectory,
            "Ignored.ovh"),
        "not a bus",
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleDirectory,
            "Ignored.cfg"),
        "not a bus",
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleDirectory,
            "Ignored.bus.bak"),
        "not a bus",
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
    Require(
        buses.Count == 1 &&
        string.Equals(
            Path.GetExtension(
                buses[0].FilePath),
            ".bus",
            StringComparison.OrdinalIgnoreCase),
        $"Bus discovery must return only real .bus files; found {buses.Count}.");

    var bus = buses[0];

    Require(
        bus.Carroceria == "Synthetic Coachworks" &&
        bus.Modelo == "Camera Bus" &&
        bus.Skin == "Test Skin" &&
        bus.SelectionLabel ==
            "Synthetic Coachworks — Camera Bus — Test Skin" &&
        !string.IsNullOrWhiteSpace(
            bus.PreviewImagePath) &&
        string.Equals(
            Path.GetFileName(
                bus.PreviewImagePath),
            "Preview.png",
            StringComparison.OrdinalIgnoreCase),
        "OMSI [friendlyname] lines must map to body/model/skin selection metadata and resolve the vehicle preview.");

    Require(
        Math.Abs(
            bus.Physics.MassTonnes!.Value -
            10.9) <
        0.0001 &&
        Math.Abs(
            bus.Physics.CenterOfGravityHeightMeters!.Value -
            1.3) <
        0.0001 &&
        Math.Abs(
            bus.Physics.RollingResistanceNewtons!.Value -
            1000.0) <
        0.0001 &&
        Math.Abs(
            bus.Physics.MomentOfInertiaZ!.Value -
            300.0) <
        0.0001 &&
        Math.Abs(
            bus.Physics.TrackWidthMeters!.Value -
            2.4) <
        0.0001 &&
        Math.Abs(
            bus.Physics.AverageWheelDiameterMeters!.Value -
            0.94) <
        0.0001 &&
        bus.Physics.Axles.Count ==
            2 &&
        Math.Abs(
            bus.Physics.Axles[0]
                .SpringRateKilonewtonsPerMeter!.Value -
            240.0) <
        0.0001 &&
        Math.Abs(
            bus.Physics.Axles[1]
                .DamperRateKilonewtonSecondsPerMeter!.Value -
            20.0) <
        0.0001,
        "OMSI vehicle dynamics must parse mass, inertia, center of gravity, rolling resistance, track width and suspension from the .bus file.");

    var repaints =
        OmsiVehicleRepaintCatalog.Discover(
            bus);

    Require(
        repaints.Count == 1 &&
        repaints[0].Name == "Blue Fleet" &&
        repaints[0].TextureOverrides.Count == 2 &&
        repaints[0].SetVariables.TryGetValue(
            "mesh_visible",
            out var repaintVisible) &&
        Math.Abs(
            repaintVisible -
            1.0) <
        0.0001,
        "OMSI CTI repaint items with the same skin name must be merged, including [setvar] values.");

    var repaintedVehicleAsset =
        OmsiVehicleAssetLoader.Load(
            contentRoot,
            bus,
            repaint:
                repaints[0]);

    Require(
        repaintedVehicleAsset.Meshes
            .SelectMany(
                static mesh =>
                    mesh.Materials)
            .Any(
                material =>
                    string.Equals(
                        Path.GetFileName(
                            material.TexturePath),
                        "skin_body.tga",
                        StringComparison.OrdinalIgnoreCase)),
        "Selected OMSI CTI repaint must replace the matching vehicle material texture.");

    var vehicleAsset =
        OmsiVehicleAssetLoader.Load(
            contentRoot,
            bus);

    Require(
        vehicleAsset.Meshes.Count == 2 &&
        vehicleAsset.RenderableMeshCount == 2 &&
        vehicleAsset.Meshes[0].MeshIdentifier ==
            "steering_parent" &&
        vehicleAsset.Meshes[1].AnimationParent ==
            "steering_parent" &&
        Math.Abs(
            vehicleAsset.Meshes[0].LodThreshold!.Value -
            0.1) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[1].LodThreshold!.Value) < 0.0001 &&
        vehicleAsset.Meshes[0].VisibilityConditions.Count == 1 &&
        vehicleAsset.Meshes[0].VisibilityConditions[0].VariableName ==
            "mesh_visible" &&
        Math.Abs(
            vehicleAsset.Meshes[0].VisibilityConditions[0].Value -
            1.0) < 0.0001 &&
        vehicleAsset.Meshes[1].VisibilityConditions.Count == 0 &&
        vehicleAsset.Meshes[0].Animations.Count == 1 &&
        vehicleAsset.Meshes[0].Animations[0].Kind ==
            OmsiVehicleAnimationKind.Rotation &&
        vehicleAsset.Meshes[0].Animations[0].VariableName ==
            "Axle_Steering_0_L" &&
        Math.Abs(
            vehicleAsset.Meshes[0].Animations[0].Delta -
            1680.0) < 0.0001 &&
        vehicleAsset.Meshes[0].Animations[0].OriginFromMesh &&
        Math.Abs(
            vehicleAsset.Meshes[0].Animations[0].OriginRotationY -
            90.0) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Animations[0].Offset -
            2.0) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Animations[0].Delay!.Value -
            10.0) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Animations[0].MaxSpeed!.Value -
            360.0) < 0.0001 &&
        vehicleAsset.Meshes[0].LightEffects is
            { Count: 2 } &&
        !vehicleAsset.Meshes[0].LightEffects![0].Enhanced &&
        vehicleAsset.Meshes[0].LightEffects![0].Red == 255 &&
        vehicleAsset.Meshes[0].LightEffects![0].Green == 128 &&
        vehicleAsset.Meshes[0].LightEffects![0].BrightnessVariable ==
            "cockpit_light_test" &&
        Math.Abs(
            vehicleAsset.Meshes[0].LightEffects![0].BrightnessFactor -
            1.5) < 0.0001 &&
        vehicleAsset.Meshes[0].LightEffects![1].Enhanced &&
        vehicleAsset.Meshes[0].LightEffects![1].Omni == 0 &&
        vehicleAsset.Meshes[0].LightEffects![1].Rotating == 0 &&
        vehicleAsset.Meshes[0].LightEffects![1].BrightnessVariable ==
            "lights_stand" &&
        vehicleAsset.Meshes[0].LightEffects![1].BitmapSource ==
            "D_Scheinwerfer.bmp" &&
        Math.Abs(
            vehicleAsset.Meshes[0].LightEffects![1].OuterConeAngleDegrees -
            150.0) < 0.0001 &&
        vehicleAsset.TextTextures.Count == 1 &&
        vehicleAsset.TextTextures[0].Index == 0 &&
        vehicleAsset.TextTextures[0].StringVariable ==
            "IBIS_line" &&
        vehicleAsset.TextTextures[0].FontName ==
            "IBIS-2_5x7" &&
        vehicleAsset.TextTextures[0].Width == 256 &&
        vehicleAsset.TextTextures[0].Height == 64 &&
        vehicleAsset.Meshes[0].Materials.Count == 1 &&
        vehicleAsset.Meshes[0].Materials[0].TextTextureIndex == 0 &&
        vehicleAsset.Meshes[0].Materials[0].FreeTextures.Count == 1 &&
        vehicleAsset.Meshes[0].Materials[0].FreeTextures[0].VariableName ==
            "Matrix_SchildFrnt" &&
        vehicleAsset.Meshes[0].Materials[0].AlphaScaleVariable ==
            "mesh_alpha" &&
        vehicleAsset.Meshes[0].Materials[0].HasTransMapDirective &&
        vehicleAsset.Meshes[0].Materials[0].AlphaMode == 0 &&
        string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].TransMapTexturePath) &&
        vehicleAsset.Meshes[0].Materials[0].LightMapVariable ==
            "lights_stand" &&
        !string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].LightMapTexturePath) &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].LightMapTexturePath!) ==
            "panel_lm.bmp" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeVariable ==
            "cockpit_light_test" &&
        vehicleAsset.Meshes[0].Materials[0].BaseAllColor is not null &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].BaseAllColor!.DiffuseR -
            0.8) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].BaseAllColor!.EmissiveB -
            0.04) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].BaseAllColor!.Power -
            8.0) < 0.0001 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeAllColor is not null &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeAllColor!.EmissiveR -
            0.24) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeAllColor!.EmissiveG -
            0.23) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeAllColor!.EmissiveB -
            0.2) < 0.0001 &&
        !string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeTexturePath) &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeTexturePath!) ==
            "panel_n.bmp" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets is
            { Count: 1 } &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].VariableName ==
            "cockpit_light_test" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items.Count == 1 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].ItemIndex == 1 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].AlphaMode == 1 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].HasTransMapDirective &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].NoZWrite &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].TransMapTexturePath!) ==
            "panel_mask.bmp" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].LightMapVariable ==
            "lights_stand" &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].LightMapTexturePath!) ==
            "panel_item_lm.bmp" &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].MaterialChangeTexturePath!) ==
            "panel_n.bmp" &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].EnvMapStrength -
            0.5) < 0.0001 &&
        !string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].EnvMapTexturePath) &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].EnvMapTexturePath!) ==
            "envmap.bmp" &&
        !string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].EnvMapMaskTexturePath) &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].EnvMapMaskTexturePath!) ==
            "envmask.bmp" &&
        Math.Abs(
            vehicleAsset.Meshes[0].Materials[0].BumpMapStrength -
            0.05) < 0.0001 &&
        !string.IsNullOrWhiteSpace(
            vehicleAsset.Meshes[0].Materials[0].BumpMapTexturePath) &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].BumpMapTexturePath!) ==
            "bump.bmp" &&
        vehicleAsset.Meshes[1].Animations.Count == 0 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M41 -
            1.25f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M42 -
            2.5f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M43 -
            3.75f) < 0.0001 &&
        vehicleAsset.ProtectedMeshCount == 0 &&
        vehicleAsset.FailedMeshCount == 0,
        "Synthetic OMSI bus model.cfg/O3D geometry did not load end-to-end.");

    Require(
        vehicleAsset.Meshes[0].Positions.Length == 9 &&
        vehicleAsset.Meshes[0].Normals.Length == 9 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Normals[2] -
            1.0f) < 0.0001 &&
        vehicleAsset.Meshes[0].Indices.Length == 3 &&
        vehicleAsset.Meshes[1].Positions.Length == 9 &&
        vehicleAsset.Meshes[1].Normals.Length == 9 &&
        vehicleAsset.Meshes[1].Indices.Length == 3,
        "Synthetic OMSI vehicle geometry counts are incorrect.");

    Require(
        vehicleAsset.Meshes[0].Uvs.Length == 6 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Uvs[5] -
            1.0f) < 0.0001,
        "OMSI O3D V texture coordinates must remain in native Direct3D orientation.");

    Require(
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[0] -
            -1.0f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[1]) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[2]) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[3] -
            1.0f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[4]) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].Positions[5]) < 0.0001,
        "OMSI O3D 0x79 must not bake the animation/source transform into static mesh vertices.");

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

    var fileSoundTriggers =
        new List<(string Trigger, string File)>();

    scriptRuntime.FileSoundTriggerRequested +=
        (trigger, file) =>
            fileSoundTriggers.Add(
                (trigger, file));

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
        fileSoundTriggers.Count == 1 &&
        fileSoundTriggers[0].Trigger ==
            "ev_IBIS_Ansagen" &&
        fileSoundTriggers[0].File ==
            "announcement.wav",
        "OMSI T.F file sound trigger execution failed.");
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

    Require(
        Math.Abs(
            scriptRuntime.GetSystem(
                "getTime") -
            10.0) < 0.0001 &&
        Math.Abs(
            scriptRuntime.GetLocal(
                "ENGINE_SPEED") -
            660.0) < 0.0001,
        "OMSI variable names must be case-insensitive.");

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
        world.Objects.Count == 2,
        $"Expected 2 objects, found {world.Objects.Count}.");
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
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\tree.sco",
            out var editorOnlyTreeAsset) &&
        editorOnlyTreeAsset.OnlyEditor &&
        editorOnlyTreeAsset.Tree is not null &&
        editorOnlyTreeAsset.IsRenderable,
        "[tree] scenery must remain runtime-renderable even when its helper mesh is [onlyeditor].");

    Require(
        world.Dependencies.RequiredCount == 3,
        "Expected three primary dependencies.");
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

static void WriteSyntheticO3d(
    string path,
    bool extendedHeader = false,
    uint protectionKey = uint.MaxValue)
{
    using var stream =
        File.Create(path);

    using var writer =
        new BinaryWriter(stream);

    writer.Write((byte)0x84);
    writer.Write((byte)0x19);

    if (extendedHeader)
    {
        writer.Write((byte)5);
        writer.Write((byte)0);
        writer.Write(protectionKey);
    }
    else
    {
        writer.Write((byte)3);
    }

    writer.Write((byte)0x17);

    if (extendedHeader)
    {
        writer.Write((uint)3);
    }
    else
    {
        writer.Write((ushort)3);
    }

    void Vertex(
        BinaryWriter writer,
        float x,
        float y,
        float z,
        float u,
        float v)
    {
        var encodeZeroKey =
            extendedHeader &&
            protectionKey == 0;

        if (encodeZeroKey)
        {
            // For a zero-key/options=0 stream with integer positions the
            // first salt remains zero. Encode the known triangle with the
            // inverse of that first-step vertex permutation so the reader
            // must restore the original values.
            writer.Write(y);
            writer.Write(x);
            writer.Write(z);

            writer.Write(0.0f);
            writer.Write(-1.0f);
            writer.Write(0.0f);
        }
        else
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);

            writer.Write(0.0f);
            writer.Write(0.0f);
            writer.Write(1.0f);
        }

        writer.Write(u);
        writer.Write(v);
    }

    Vertex(writer, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f);
    Vertex(writer, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f);
    Vertex(writer, 0.0f, 0.0f, 1.0f, 0.5f, 1.0f);

    writer.Write((byte)0x49);

    if (extendedHeader)
    {
        writer.Write((uint)1);
    }
    else
    {
        writer.Write((ushort)1);
    }

    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)2);
    writer.Write((ushort)0);

    writer.Write((byte)0x26);
    writer.Write((ushort)1);

    writer.Write(0.7f);
    writer.Write(0.7f);
    writer.Write(0.7f);
    writer.Write(1.0f);

    writer.Write(0.0f);
    writer.Write(0.0f);
    writer.Write(0.0f);

    writer.Write(0.0f);
    writer.Write(0.0f);
    writer.Write(0.0f);

    writer.Write(0.0f);

    var textureName =
        Encoding.Latin1.GetBytes(
            "regen.tga");

    writer.Write(
        (byte)textureName.Length);
    writer.Write(
        textureName);

    writer.Write((byte)0x79);

    var transform =
        new float[]
        {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            1.25f, 2.5f, 3.75f, 1
        };

    foreach (var value in
             transform)
    {
        writer.Write(
            value);
    }
}

static void WriteSyntheticDirectX(string path)
{
    File.WriteAllText(
        path,
        string.Join(
            Environment.NewLine,
            [
                "xof 0303txt 0032",
                "Mesh {",
                "3;",
                "-1.0;0.0;0.0;,",
                "1.0;0.0;0.0;,",
                "0.0;1.0;0.0;;",
                "1;",
                "3;0,1,2;;",
                "MeshTextureCoords {",
                "3;",
                "0.0;0.0;,",
                "1.0;0.0;,",
                "0.5;1.0;;",
                "}",
                "MeshMaterialList {",
                "1;",
                "1;",
                "0;;",
                "Material {",
                "1.0;1.0;1.0;1.0;;",
                "0.0;",
                "0.0;0.0;0.0;;",
                "0.0;0.0;0.0;;",
                "}",
                "}",
                "}"
            ]) +
        Environment.NewLine,
        Encoding.Latin1);
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
