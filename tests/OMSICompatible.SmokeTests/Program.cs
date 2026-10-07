using System.Numerics;
using System.Text;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Models;
using OmsiCompat.Scenery;
using OmsiCompat.Physics.Ode;
using OmsiCompat.Vehicles;
using OmsiCompat.Scripting;
using OMSICompatible.World;
using OMSICompatible.Multiplayer;

var root = Path.Combine(
    Path.GetTempPath(),
    "omsi-compatible-runtime-smoke-" + Guid.NewGuid().ToString("N"));

try
{
    if (string.Equals(
            Environment.GetEnvironmentVariable(
                "OMSI_REQUIRE_ODE"),
            "1",
            StringComparison.Ordinal))
    {
        var odeInfo =
            OdeRuntime.Inspect();

        Require(
            odeInfo.Available &&
            odeInfo.Is64BitProcess &&
            odeInfo.SinglePrecision,
            $"ODE x64 single-precision backend unavailable: {odeInfo.Error ?? odeInfo.Configuration}");

        using var odeWorld =
            new OdeWorld();

        using var odeBody =
            new OdeRigidBody(
                odeWorld,
                new OdeRigidBodyParameters(
                    MassKilograms:
                        8_000.0f,
                    CenterOfMassX:
                        0.0f,
                    CenterOfMassY:
                        0.0f,
                    CenterOfMassZ:
                        0.0f,
                    InertiaXKilogramSquareMeters:
                        20_000.0f,
                    InertiaYKilogramSquareMeters:
                        45_000.0f,
                    InertiaZKilogramSquareMeters:
                        55_000.0f));

        odeBody.SetPosition(
            new Vector3(
                0.0f,
                0.0f,
                10.0f));

        Require(
            odeWorld.Step(
                1.0f / 120.0f),
            "ODE x64 QuickStep smoke test failed.");

        Require(
            odeBody.Position.Z <
                10.0f &&
            odeBody.LinearVelocity.Z <
                0.0f,
            "ODE rigid body did not respond to gravity with OMSI Z-up coordinates.");

        odeBody.SetGravityEnabled(
            false);
        odeBody.SetPosition(
            Vector3.Zero);
        odeBody.SetLinearVelocity(
            Vector3.Zero);
        odeBody.SetAngularVelocity(
            Vector3.Zero);
        odeBody.SetOrientation(
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitZ,
                0.25f));
        odeBody.AddWorldForce(
            new Vector3(
                8_000.0f,
                0.0f,
                0.0f));
        odeBody.AddWorldForceAtLocalPosition(
            new Vector3(
                0.0f,
                0.0f,
                8_000.0f),
            new Vector3(
                1.0f,
                0.0f,
                0.0f));
        odeBody.AddWorldTorque(
            new Vector3(
                0.0f,
                0.0f,
                55_000.0f));

        Require(
            odeWorld.Step(
                1.0f / 120.0f),
            "ODE x64 force/torque QuickStep smoke test failed.");

        Require(
            odeBody.LinearVelocity.X >
                0.0f &&
            Math.Abs(
                odeBody.AngularVelocity.Y) >
                0.0001f &&
            odeBody.AngularVelocity.Z >
                0.0f &&
            Math.Abs(
                odeBody.Orientation.Z) >
                0.01f,
            "ODE rigid body force-at-position, torque or quaternion bridge is invalid.");

        using var odeTrailerBody =
            new OdeRigidBody(
                odeWorld,
                new OdeRigidBodyParameters(
                    MassKilograms:
                        6_000.0f,
                    CenterOfMassX:
                        0.0f,
                    CenterOfMassY:
                        0.0f,
                    CenterOfMassZ:
                        0.0f,
                    InertiaXKilogramSquareMeters:
                        15_000.0f,
                    InertiaYKilogramSquareMeters:
                        30_000.0f,
                    InertiaZKilogramSquareMeters:
                        40_000.0f));

        odeTrailerBody.SetGravityEnabled(
            false);
        odeTrailerBody.SetPosition(
            new Vector3(
                0.0f,
                -5.0f,
                0.0f));

        using var odeArticulation =
            new OdeHingeJoint(
                odeWorld,
                odeBody,
                odeTrailerBody,
                new Vector3(
                    0.0f,
                    -2.5f,
                    0.0f),
                Vector3.UnitZ);

        odeArticulation.SetStops(
            -0.2617994f,
            0.2617994f,
            stopErp:
                0.35f,
            stopCfm:
                0.00001f);

        for (var hingeStep = 0;
             hingeStep < 240;
             hingeStep++)
        {
            odeTrailerBody.AddWorldTorque(
                new Vector3(
                    0.0f,
                    0.0f,
                    120_000.0f));

            Require(
                odeWorld.Step(
                    1.0f / 120.0f),
                "ODE articulated hinge stop QuickStep failed.");
        }

        Require(
            float.IsFinite(
                odeArticulation.AngleRadians) &&
            float.IsFinite(
                odeArticulation.AngularRateRadiansPerSecond) &&
            Math.Abs(
                odeArticulation.AngleRadians) <
                0.34f,
            $"ODE articulated hinge stop was exceeded: {odeArticulation.AngleRadians:0.0000} rad.");

        using var odeUniversalParent =
            new OdeRigidBody(
                odeWorld,
                new OdeRigidBodyParameters(
                    8_000.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    20_000.0f,
                    45_000.0f,
                    55_000.0f));

        using var odeUniversalTrailer =
            new OdeRigidBody(
                odeWorld,
                new OdeRigidBodyParameters(
                    6_000.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    15_000.0f,
                    30_000.0f,
                    40_000.0f));

        odeUniversalParent.SetGravityEnabled(
            false);
        odeUniversalTrailer.SetGravityEnabled(
            false);

        odeUniversalParent.SetPosition(
            new Vector3(
                20.0f,
                0.0f,
                0.0f));

        odeUniversalTrailer.SetPosition(
            new Vector3(
                20.0f,
                -5.0f,
                0.0f));

        using var odeUniversal =
            new OdeUniversalJoint(
                odeWorld,
                odeUniversalParent,
                odeUniversalTrailer,
                new Vector3(
                    20.0f,
                    -2.5f,
                    0.0f),
                Vector3.UnitZ,
                Vector3.UnitX);

        odeUniversal.SetYawStops(
            -0.35f,
            0.35f);

        odeUniversal.SetPitchStops(
            -0.20f,
            0.20f);

        odeUniversal.AddTorques(
            yawTorqueNewtonMeters:
                18_000.0f,
            pitchTorqueNewtonMeters:
                -9_000.0f);

        Require(
            odeWorld.Step(
                1.0f / 120.0f),
            "ODE universal joint torque API smoke test failed.");

        Require(
            float.IsFinite(
                odeUniversal.YawRateRadiansPerSecond) &&
            float.IsFinite(
                odeUniversal.PitchRateRadiansPerSecond),
            "ODE universal joint torque API returned invalid angular rates.");

        for (var universalStep = 0;
             universalStep < 240;
             universalStep++)
        {
            odeUniversalTrailer.AddWorldTorque(
                new Vector3(
                    45_000.0f,
                    0.0f,
                    120_000.0f));

            Require(
                odeWorld.Step(
                    1.0f / 120.0f),
                "ODE universal articulation QuickStep failed.");
        }

        Require(
            float.IsFinite(
                odeUniversal.YawAngleRadians) &&
            float.IsFinite(
                odeUniversal.PitchAngleRadians) &&
            Math.Abs(
                odeUniversal.YawAngleRadians) <
                0.45f &&
            Math.Abs(
                odeUniversal.PitchAngleRadians) <
                0.30f,
            $"ODE universal articulation stops were exceeded: yaw={odeUniversal.YawAngleRadians:0.0000}, pitch={odeUniversal.PitchAngleRadians:0.0000}.");

        var frontSuspension =
            OdeSuspensionTuning.FromSpringDamper(
                springNewtonsPerMeter:
                    240_000.0f,
                damperNewtonSecondsPerMeter:
                    20_000.0f,
                timeStepSeconds:
                    1.0f / 120.0f);

        Require(
            Math.Abs(
                frontSuspension.ErrorReductionParameter -
                0.09090909f) <
                0.0001f &&
            Math.Abs(
                frontSuspension.ConstraintForceMixing -
                0.0000454545f) <
                0.000001f,
            "OMSI axle spring/damper values were not converted to ODE ERP/CFM correctly.");
    }

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
    Directory.CreateDirectory(
        Path.Combine(
            sceneryDirectory,
            "script"));
    Directory.CreateDirectory(splineDirectory);
    Directory.CreateDirectory(vehicleDirectory);
    Directory.CreateDirectory(vehicleScriptDirectory);
    Directory.CreateDirectory(vehicleModelDirectory);
    Directory.CreateDirectory(programDirectory);

    var trainDirectory =
        Path.Combine(
            root,
            "trains");
    Directory.CreateDirectory(
        trainDirectory);

    var trainFrontVehiclePath =
        Path.Combine(
            vehicleDirectory,
            "rail_front.ovh");
    var trainRearVehiclePath =
        Path.Combine(
            vehicleDirectory,
            "rail_rear.ovh");

    File.WriteAllText(
        trainFrontVehiclePath,
        string.Empty);
    File.WriteAllText(
        trainRearVehiclePath,
        string.Empty);

    var trainConsistPath =
        Path.Combine(
            trainDirectory,
            "synthetic.zug");

    File.WriteAllText(
        trainConsistPath,
        Lines(
            @"Vehicles\Synthetic\rail_front.ovh",
            "0",
            @"Vehicles\Synthetic\rail_rear.ovh",
            "1"),
        Encoding.Unicode);

    var trainConsist =
        OmsiTrainConsistReader.ReadFile(
            root,
            trainConsistPath);

    Require(
        trainConsist.Vehicles.Count ==
            2 &&
        !trainConsist.Vehicles[0].Reverse &&
        trainConsist.Vehicles[0].Exists &&
        trainConsist.Vehicles[1].Reverse &&
        trainConsist.Vehicles[1].Exists,
        "OMSI .zug consist parsing did not preserve .ovh order/orientation or resolve vehicle files.");

    var signalRoutesPath =
        Path.Combine(
            mapDirectory,
            "signalroutes.cfg");

    File.WriteAllText(
        signalRoutesPath,
        Lines(
            "-----------------------",
            "Signal Routes File",
            "-----------------------",
            "0:",
            "[signalroute]",
            "",
            "[signal]",
            "195662",
            "0",
            "[entry]",
            "3001",
            "0",
            "237",
            "6",
            "[entry]",
            "3002",
            "0",
            "237",
            "15",
            "1:",
            "[signalroute]",
            "",
            "[future_extension]",
            "preserve-this-value"),
        Encoding.Unicode);

    var signalRoutes =
        OmsiSignalRoutesReader.ReadFile(
            signalRoutesPath);

    Require(
        signalRoutes.Sections.Count ==
            6 &&
        signalRoutes.Sections[0].RouteIndex ==
            0 &&
        signalRoutes.Sections[0].Name.Equals(
            "signalroute",
            StringComparison.OrdinalIgnoreCase) &&
        signalRoutes.Sections[1].RouteIndex ==
            0 &&
        signalRoutes.Sections[1].Name.Equals(
            "signal",
            StringComparison.OrdinalIgnoreCase) &&
        signalRoutes.Sections[1].Lines
            .Where(
                static line =>
                    !string.IsNullOrWhiteSpace(
                        line))
            .SequenceEqual(
                ["195662", "0"]) &&
        signalRoutes.Sections[2].RouteIndex ==
            0 &&
        signalRoutes.Sections[2].Name.Equals(
            "entry",
            StringComparison.OrdinalIgnoreCase) &&
        signalRoutes.Sections[2].Lines
            .Where(
                static line =>
                    !string.IsNullOrWhiteSpace(
                        line))
            .SequenceEqual(
                ["3001", "0", "237", "6"]) &&
        signalRoutes.Sections[3].RouteIndex ==
            0 &&
        signalRoutes.Sections[3].Name.Equals(
            "entry",
            StringComparison.OrdinalIgnoreCase) &&
        signalRoutes.Sections[3].Lines
            .Where(
                static line =>
                    !string.IsNullOrWhiteSpace(
                        line))
            .SequenceEqual(
                ["3002", "0", "237", "15"]) &&
        signalRoutes.Sections[4].RouteIndex ==
            1 &&
        signalRoutes.Sections[5].RouteIndex ==
            1 &&
        signalRoutes.Sections[5].Name.Equals(
            "future_extension",
            StringComparison.OrdinalIgnoreCase) &&
        signalRoutes.Sections[5].Lines
            .Where(
                static line =>
                    !string.IsNullOrWhiteSpace(
                        line))
            .Single() ==
            "preserve-this-value",
        "OMSI signalroutes.cfg parser did not preserve route indices, known sections and unknown future data.");

    var railNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    3001,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            50.0)
                    ],
                    [1],
                    []),
                new WorldTrafficPathSegment(
                    1,
                    3002,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            50.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            100.0)
                    ],
                    [],
                    [0]),
                new WorldTrafficPathSegment(
                    2,
                    4001,
                    0,
                    0,
                    0,
                    3.0,
                    [
                        new WorldVector3(
                            10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            100.0)
                    ],
                    [],
                    [])
            ],
            1,
            0,
            2,
            0,
            2,
            0,
            2,
            0);

    var resolvedSignalRoutes =
        WorldRailSignalRouteResolver.Resolve(
            railNetwork,
            signalRoutes);

    Require(
        resolvedSignalRoutes.Count ==
            2 &&
        resolvedSignalRoutes[0].RouteIndex ==
            0 &&
        resolvedSignalRoutes[0].ParsedEntryCount ==
            2 &&
        resolvedSignalRoutes[0].UnresolvedEntryCount ==
            0 &&
        resolvedSignalRoutes[0].SegmentIndices
            .SequenceEqual(
                [0, 1]) &&
        resolvedSignalRoutes[0].Signal is
            {
                ObjectId: 195662,
                SignalState: 0
            } &&
        resolvedSignalRoutes[1].RouteIndex ==
            1 &&
        resolvedSignalRoutes[1].ParsedEntryCount ==
            0,
        "OMSI signal route entries did not resolve against rail source IDs and local path indices.");

    var interlockingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    10,
                    6100,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            0.0)
                    ],
                    [],
                    []),
                new WorldTrafficPathSegment(
                    11,
                    6101,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            -10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [],
                    []),
                new WorldTrafficPathSegment(
                    12,
                    6102,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -10.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            20.0)
                    ],
                    [],
                    [])
            ],
            0,
            0,
            3,
            0,
            0,
            0,
            6,
            0);

    var interlocking =
        new WorldRailSignalRouteInterlocking(
            interlockingNetwork,
            [
                new WorldRailSignalRoute(
                    0,
                    [10],
                    1,
                    0),
                new WorldRailSignalRoute(
                    1,
                    [11],
                    1,
                    0),
                new WorldRailSignalRoute(
                    2,
                    [12],
                    1,
                    0)
            ]);

    Require(
        interlocking.TryReserve(
            0,
            100) &&
        !interlocking.TryReserve(
            1,
            200) &&
        interlocking.TryReserve(
            2,
            300),
        "OMSI rail interlocking did not block the geometrically conflicting signal route while allowing a clear parallel route.");

    interlocking.Release(
        0,
        100);

    Require(
        interlocking.TryReserve(
            1,
            200),
        "OMSI rail interlocking did not release a conflicting signal route after its owner cleared it.");

    interlocking.ReleaseAll(
        200);

    Require(
        !interlocking.TryReserve(
            0,
            100,
            new Dictionary<int, int>
            {
                [10] =
                    999
            }),
        "OMSI rail interlocking reserved an occupied signal route.");

    var railCatalog =
        new OmsiMapAiCatalog(
            [
                new OmsiAiVehicleDefinition(
                    "Rail",
                    @"trains\synthetic.zug",
                    trainConsistPath,
                    1.0)
            ],
            [],
            [],
            [],
            [
                new OmsiUnscheduledVehicleGroup(
                    0,
                    "Rail",
                    1)
            ]);

    var railSimulation =
        new WorldRailTrafficSimulation(
            railNetwork,
            railCatalog,
            maximumAgents:
                1);

    Require(
        railSimulation.Snapshot() is
            [{ SegmentIndex: 0 }],
        "OMSI rail simulation did not spawn the .zug consist on a type-2 rail path.");

    var roadSimulationWithRailOnly =
        new WorldTrafficSimulation(
            railNetwork,
            railCatalog,
            maximumAgents:
                1);

    Require(
        roadSimulationWithRailOnly.Snapshot().Count ==
            0,
        "OMSI road traffic simulation must not consume .zug train consists.");

    railSimulation.Step(
        6.0);

    var railState =
        railSimulation.Snapshot()
            .Single();

    Require(
        railState.SegmentIndex ==
            1 &&
        railState.Position.Z >
            50.0 &&
        railState.TraveledDistanceMeters >
            50.0,
        "OMSI rail simulation did not follow the connected type-2 rail path.");

    Require(
        railSimulation.TrySampleBehind(
            railState.AgentIndex,
            20.0,
            out var trailingRailSegmentIndex,
            out _,
            out var trailingRailPosition,
            out _) &&
        trailingRailSegmentIndex ==
            0 &&
        trailingRailPosition.Z <
            50.0,
        "OMSI rail consist trailing sample did not traverse back across the connected rail segment boundary.");

    var railGroupRoutingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    5001,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [1, 2],
                    []),
                new WorldTrafficPathSegment(
                    1,
                    5002,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            -20.0,
                            0.0,
                            40.0)
                    ],
                    [],
                    [0],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 0.0
                        }),
                new WorldTrafficPathSegment(
                    2,
                    5003,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            20.0,
                            0.0,
                            40.0)
                    ],
                    [],
                    [0],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 1.0
                        })
            ],
            0,
            0,
            3,
            0,
            2,
            0,
            2,
            0);

    var railGroupRoutingSimulation =
        new WorldRailTrafficSimulation(
            railGroupRoutingNetwork,
            railCatalog,
            maximumAgents:
                1);

    railGroupRoutingSimulation.Step(
        4.0);

    Require(
        railGroupRoutingSimulation.Snapshot() is
            [{ SegmentIndex: 2 }],
        "OMSI rail traffic ignored the unscheduled-group density routing weights.");

    var railSignalRouteNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    20,
                    7000,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [21],
                    []),
                new WorldTrafficPathSegment(
                    21,
                    7001,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [22, 23],
                    [20]),
                new WorldTrafficPathSegment(
                    22,
                    7002,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            -10.0,
                            0.0,
                            30.0)
                    ],
                    [],
                    [21],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 0.1
                        }),
                new WorldTrafficPathSegment(
                    23,
                    7003,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            30.0)
                    ],
                    [],
                    [21],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 10.0
                        })
            ],
            0,
            0,
            4,
            0,
            3,
            0,
            2,
            0);

    var railSignalRoutes =
        new OmsiSignalRoutesFile(
            "synthetic-signalroutes.cfg",
            [
                new OmsiSignalRouteSection(
                    7,
                    "signal",
                    1,
                    ["7777", "0"]),
                new OmsiSignalRouteSection(
                    7,
                    "entry",
                    4,
                    ["7001", "0", "237", "6"]),
                new OmsiSignalRouteSection(
                    7,
                    "entry",
                    9,
                    ["7002", "0", "237", "7"])
            ]);

    var railSignalRouteSimulation =
        new WorldRailTrafficSimulation(
            railSignalRouteNetwork,
            railCatalog,
            maximumAgents:
                1,
            signalRoutes:
                railSignalRoutes);

    railSignalRouteSimulation.Step(
        5.0);

    Require(
        railSignalRouteSimulation.Snapshot() is
            [{ SegmentIndex: 22 }],
        "OMSI rail traffic did not stay on the reserved signal-route path sequence.");

    Require(
        railSignalRouteSimulation.SignalRouteSnapshot() is
            [
                {
                    RouteIndex: 7,
                    Reserved: true,
                    ReservedAgentIndex: 0,
                    Signal:
                    {
                        ObjectId: 7777,
                        SignalState: 0
                    }
                }
            ],
        "OMSI rail signal route state did not expose the reserved route owner and signal object reference.");

    var railTailClearanceNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    30,
                    8000,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [31],
                    []),
                new WorldTrafficPathSegment(
                    31,
                    8001,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [32],
                    [30]),
                new WorldTrafficPathSegment(
                    32,
                    8002,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            50.0)
                    ],
                    [],
                    [31])
            ],
            0,
            0,
            3,
            0,
            2,
            0,
            2,
            0);

    var railTailClearanceRoutes =
        new OmsiSignalRoutesFile(
            "synthetic-tail-signalroutes.cfg",
            [
                new OmsiSignalRouteSection(
                    9,
                    "entry",
                    1,
                    ["8001", "0", "237", "6"])
            ]);

    var railTailClearanceSimulation =
        new WorldRailTrafficSimulation(
            railTailClearanceNetwork,
            railCatalog,
            maximumAgents:
                1,
            signalRoutes:
                railTailClearanceRoutes);

    railTailClearanceSimulation
        .SetConsistTrailingDistance(
            trainConsistPath,
            8.0);

    railTailClearanceSimulation.Step(
        2.0);

    Require(
        railTailClearanceSimulation.Snapshot() is
            [{ SegmentIndex: 32 }] &&
        railTailClearanceSimulation.IsSignalRouteReservedBy(
            9,
            0),
        "OMSI rail interlocking released the signal route before the consist tail cleared it.");

    railTailClearanceSimulation.Step(
        1.0);

    Require(
        !railTailClearanceSimulation.IsSignalRouteReservedBy(
            9,
            0),
        "OMSI rail interlocking did not release the signal route after the consist tail cleared it.");

    var railMergeHistoryNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    40,
                    9000,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [42],
                    [],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 0.0
                        }),
                new WorldTrafficPathSegment(
                    41,
                    9001,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [42],
                    [],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 1.0
                        }),
                new WorldTrafficPathSegment(
                    42,
                    9002,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            50.0)
                    ],
                    [],
                    [40, 41],
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] = 1.0
                        })
            ],
            0,
            0,
            3,
            0,
            2,
            0,
            2,
            0);

    var railMergeHistorySimulation =
        new WorldRailTrafficSimulation(
            railMergeHistoryNetwork,
            railCatalog,
            maximumAgents:
                1);

    railMergeHistorySimulation.Step(
        2.5);

    var railMergeHistoryState =
        railMergeHistorySimulation
            .Snapshot()
            .Single();

    Require(
        railMergeHistoryState.SegmentIndex ==
            42 &&
        railMergeHistorySimulation.TrySampleBehind(
            railMergeHistoryState.AgentIndex,
            10.0,
            out var railMergeTrailingSegmentIndex,
            out _,
            out _,
            out _) &&
        railMergeTrailingSegmentIndex ==
            41,
        "OMSI rail consist tail did not follow the actual traversed branch through a merge.");

    var railTwoTrainCatalog =
        new OmsiMapAiCatalog(
            [
                new OmsiAiVehicleDefinition(
                    "Rail",
                    @"trains\synthetic.zug",
                    trainConsistPath,
                    1.0),
                new OmsiAiVehicleDefinition(
                    "Rail",
                    @"trains\synthetic.zug",
                    trainConsistPath,
                    1.0)
            ],
            [],
            [],
            [],
            [
                new OmsiUnscheduledVehicleGroup(
                    0,
                    "Rail",
                    1)
            ]);

    var railTailOccupancyNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    50,
                    9100,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [51],
                    []),
                new WorldTrafficPathSegment(
                    51,
                    9101,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [52],
                    [50]),
                new WorldTrafficPathSegment(
                    52,
                    9102,
                    0,
                    2,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            50.0)
                    ],
                    [],
                    [51])
            ],
            0,
            0,
            3,
            0,
            2,
            0,
            2,
            0);

    var railTailOccupancySimulation =
        new WorldRailTrafficSimulation(
            railTailOccupancyNetwork,
            railTwoTrainCatalog,
            maximumAgents:
                2);

    railTailOccupancySimulation
        .SetConsistTrailingDistance(
            trainConsistPath,
            8.0);

    railTailOccupancySimulation.Step(
        1.2);

    var railTailOccupancyStates =
        railTailOccupancySimulation
            .Snapshot();

    Require(
        railTailOccupancyStates
            .Single(
                static agent =>
                    agent.AgentIndex ==
                    0)
            .SegmentIndex ==
            50 &&
        railTailOccupancyStates
            .Single(
                static agent =>
                    agent.AgentIndex ==
                    1)
            .SegmentIndex ==
            52,
        "OMSI rail traffic entered a path that was still occupied by another consist tail.");

    railTailOccupancySimulation.Step(
        0.6);

    Require(
        railTailOccupancySimulation
            .Snapshot()
            .Single(
                static agent =>
                    agent.AgentIndex ==
                    0)
            .SegmentIndex ==
            51,
        "OMSI rail traffic did not enter the path after the preceding consist tail cleared it.");

    var railBlockedBrakingSimulation =
        new WorldRailTrafficSimulation(
            railTailOccupancyNetwork,
            railTwoTrainCatalog,
            maximumAgents:
                2);

    railBlockedBrakingSimulation
        .SetConsistTrailingDistance(
            trainConsistPath,
            8.0);

    var railBlockedInitialSpeed =
        railBlockedBrakingSimulation
            .Snapshot()
            .Single(
                static agent =>
                    agent.AgentIndex ==
                    0)
            .SpeedMetersPerSecond;

    railBlockedBrakingSimulation.Step(
        0.5);

    var railBlockedBrakingState =
        railBlockedBrakingSimulation
            .Snapshot()
            .Single(
                static agent =>
                    agent.AgentIndex ==
                    0);

    Require(
        railBlockedBrakingState.SegmentIndex ==
            50 &&
        railBlockedBrakingState.SpeedMetersPerSecond >
            0.0 &&
        railBlockedBrakingState.SpeedMetersPerSecond <
            railBlockedInitialSpeed,
        "OMSI rail traffic did not brake progressively before an occupied path.");

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
            "[object]",
            "0",
            @"Sceneryobjects\Synthetic\masked.sco",
            "1003",
            "20",
            "30",
            "0",
            "0",
            "0",
            "0",
            "0",
            "",
            "[object]",
            "0",
            @"Sceneryobjects\Synthetic\collision_meta.sco",
            "1004",
            "35",
            "40",
            "0",
            "30",
            "0",
            "0",
            "0",
            "",
            "[spline]",
            "0",
            @"Splines\Synthetic\road.sli",
            "2001",
            "-1",
            "2002",
            "5",
            "7",
            "6",
            "45",
            "100",
            "0",
            "1.5",
            "2.5",
            "0",
            "0",
            "0",
            "0",
            "0",
            "",
            "[spline_terrain_align_2]",
            "4",
            "[rule]",
            "0",
            "priority",
            "160",
            "0",
            "[spline]",
            "0",
            @"Splines\Synthetic\road.sli",
            "2002",
            "2001",
            "-1",
            "75.7106781186548",
            "9",
            "76.7106781186548",
            "45",
            "50",
            "0",
            "0",
            "0",
            "[rule]",
            "0",
            "speedlimit",
            "10.000",
            "0",
            "[rule]",
            "0",
            "trafficdensity",
            "0.500",
            "0",
            "[rule]",
            "0",
            "no_cars",
            "0",
            "1",
            "[rule]",
            "0",
            "priority",
            "192",
            "0"),
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
            "[script]",
            "1",
            @"script\signal.osc",
            "[varnamelist]",
            "1",
            @"script\signal_varlist.txt",
            "[onlyeditor]",
            "[mesh]",
            "signal_missing.o3d",
            "[visible]",
            "signal_lamp",
            "1",
            "[traffic_lights_group]",
            "8",
            "[traffic_light]",
            "Main",
            "[phase]",
            "0",
            "2",
            "[phase]",
            "6",
            "6",
            "[approachdist]",
            "12",
            "[traffic_light_jump]",
            "0",
            "2",
            "1",
            "0",
            "[traffic_light_stop]",
            "0",
            "1",
            "1",
            "[path]",
            "1.5",
            "0",
            "0.1",
            "-90",
            "0",
            "3",
            "0",
            "0",
            "0",
            "2.5",
            "0",
            "1",
            "[use_traffic_light]",
            "0"),
        Encoding.Unicode);

    WriteSyntheticO3d(
        Path.Combine(
            sceneryDirectory,
            "masked.o3d"));

    File.WriteAllBytes(
        Path.Combine(
            sceneryDirectory,
            "regen.tga"),
        [0x00]);

    File.WriteAllBytes(
        Path.Combine(
            sceneryDirectory,
            "mask.bmp"),
        [0x42, 0x4D, 0x00, 0x00]);

    File.WriteAllText(
        Path.Combine(
            sceneryDirectory,
            "masked.sco"),
        Lines(
            "[friendlyname]",
            "Synthetic Masked Object",
            "[mesh]",
            "masked.o3d",
            "[matl]",
            "regen.tga",
            "0",
            "[matl_transmap]",
            "mask.bmp"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            sceneryDirectory,
            "script",
            "signal_varlist.txt"),
        Lines(
            "Signal",
            "NextSignal",
            "signal_lamp"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            sceneryDirectory,
            "script",
            "signal.osc"),
        Lines(
            "{frame}",
            "(L.L.Signal)",
            "(S.L.signal_lamp)"),
        Encoding.Unicode);

    WriteSyntheticCollisionO3d(
        Path.Combine(
            sceneryDirectory,
            "collision_low.o3d"));

    var collisionMetadataPath =
        Path.Combine(
            sceneryDirectory,
            "collision_meta.sco");

    File.WriteAllText(
        collisionMetadataPath,
        Lines(
            "[friendlyname]",
            "Synthetic Collision Metadata",
            "[fixed]",
            "[surface]",
            "[collision_mesh]",
            "collision_low.o3d",
            "[boundingbox]",
            "4",
            "2",
            "3",
            "0.5",
            "-0.25",
            "1.5"),
        Encoding.Unicode);

    var collisionMetadataDefinition =
        OmsiSceneryObjectReader.ReadFile(
            collisionMetadataPath);

    Require(
        collisionMetadataDefinition.Fixed &&
        collisionMetadataDefinition.Surface &&
        !collisionMetadataDefinition.NoCollision &&
        collisionMetadataDefinition.CollisionMeshSource ==
            "collision_low.o3d" &&
        collisionMetadataDefinition.BoundingBox is
            {
                LengthX: 4.0,
                WidthY: 2.0,
                HeightZ: 3.0,
                CenterX: 0.5,
                CenterY: -0.25,
                CenterZ: 1.5
            },
        "OMSI scenery collision metadata was not parsed correctly.");

    var noCollisionMetadataPath =
        Path.Combine(
            sceneryDirectory,
            "collision_none.sco");

    File.WriteAllText(
        noCollisionMetadataPath,
        Lines(
            "[friendlyname]",
            "Synthetic No Collision",
            "[nocollision]"),
        Encoding.Unicode);

    var noCollisionMetadataDefinition =
        OmsiSceneryObjectReader.ReadFile(
            noCollisionMetadataPath);

    Require(
        noCollisionMetadataDefinition.NoCollision &&
        !noCollisionMetadataDefinition.Fixed &&
        !noCollisionMetadataDefinition.Surface &&
        noCollisionMetadataDefinition.CollisionMeshSource is null &&
        noCollisionMetadataDefinition.BoundingBox is null,
        "OMSI [nocollision] metadata was not preserved independently.");

    var scenerySignalDefinition =
        OmsiSceneryObjectReader.ReadFile(
            Path.Combine(
                sceneryDirectory,
                "object.sco"));

    Require(
        scenerySignalDefinition.ScriptManifest is
            { RegisteredFileCount: 2, MissingFileCount: 0 } &&
        scenerySignalDefinition.ScriptManifest.ScriptFiles
            .Single()
            .Exists &&
        scenerySignalDefinition.ScriptManifest.VariableLists
            .Single()
            .Exists,
        "OMSI scenery script manifest did not resolve script/varnamelist files.");

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
        Lines(
            "[friendlyname]",
            "Synthetic Road",
            "[path]",
            "0",
            "-1.5",
            "0.1",
            "2.5",
            "0",
            "[path]",
            "1",
            "3.0",
            "0.25",
            "1.5",
            "2"),
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
            "(M.L.HeLpEr)",
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
            "\"\"",
            "(T.F.ev_DefaultConfiguredSound)",
            "(L.L.horn_timer)",
            "3",
            "+",
            ">",
            "{if}",
            "0",
            "(S.L.horn_timer)",
            "{endif}",
            "{end}",
            "{frame_ai}",
            "(L.L.ai_counter)",
            "1",
            "+",
            "(S.L.ai_counter)",
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
            "(C.L.ENGINE_IDLE)",
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
            "cockpit_light_test",
            "ai_counter"),
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
            "[smoothskin]",
            "[setbone]",
            "SyntheticBone",
            "0",
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
            "[matl_transmap]",
            @"\legacy\panel_mask.bmp",
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
            "[matl]",
            "regen.tga",
            "0",
            "[matl_transmap]",
            @"\legacy\panel_mask.bmp",
            "[animparent]",
            "steering_parent",
            "[viewpoint]",
            "1"),
        Encoding.Unicode);

    WriteSyntheticO3d(
        Path.Combine(
            vehicleModelDirectory,
            "triangle.o3d"),
        includeBones:
            true);

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
            "1",
            "[coupling_back]",
            "0",
            "-4.5",
            "0.5",
            "[couple_back]",
            "SyntheticTrailer.bus",
            "false"),
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

    var timetableDirectory =
        Path.Combine(
            maps[0].DirectoryPath,
            "TTData");

    Directory.CreateDirectory(
        timetableDirectory);

    File.WriteAllText(
        Path.Combine(
            maps[0].DirectoryPath,
            "Holidays.txt"),
        Lines(
            "[holiday]",
            "20260930",
            "Public Holiday",
            "[holidays]",
            "20261001",
            "20261010",
            "School Holidays"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "Busstops.cfg"),
        Lines(
            "[busstop]",
            "Terminal Central",
            "0",
            "42",
            "[busstop]",
            "Avenida Brasil",
            "0",
            "43"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "Linha100.ttp"),
        Lines(
            "[trip]",
            "Track100",
            "Centro",
            "100",
            "[station_typ2]",
            "42",
            "0",
            "[station]",
            "43",
            "75.5",
            "Avenida Brasil",
            "0"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "Linha200.ttp"),
        Lines(
            "[trip]",
            "",
            "Bairro",
            "200",
            "[station_typ2]",
            "42",
            "0",
            "[station_typ2]",
            "43",
            "0",
            "[profile]",
            "Weekday",
            "10",
            "[profile_man_dep_time]",
            "0",
            "1",
            "[profile_man_arr_time]",
            "1",
            "9"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "StnLinks.cfg"),
        Lines(
            "[StnLink]",
            "150",
            "42",
            "43",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "[StnLink_entry]",
            "3001",
            "0",
            "0",
            "100",
            "0",
            "0",
            "0",
            "[StnLink_entry]",
            "3002",
            "1",
            "0",
            "50",
            "0",
            "0",
            "0"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "Track100.ttr"),
        Lines(
            "[track_entry]",
            "3001",
            "0",
            "0",
            "0",
            "32.5",
            "0",
            "[track_entry]",
            "3002",
            "1",
            "0",
            "0",
            "20",
            "0"));

    File.WriteAllText(
        Path.Combine(
            timetableDirectory,
            "100.ttl"),
        Lines(
            "[userallowed]",
            "[priority]",
            "3",
            "[newtour]",
            "1",
            "Busses",
            "127",
            "[addtrip]",
            "Linha100",
            "0",
            "480.5",
            "[newtour]",
            "2",
            "Busses",
            ""));

    var timetableCatalog =
        OmsiTimetableCatalogReader.Read(
            maps[0]);

    Require(
        timetableCatalog.Trips.Count ==
            2 &&
        timetableCatalog.Trips[0].TrackName ==
            "Track100" &&
        timetableCatalog.Trips[0].Line ==
            "100" &&
        timetableCatalog.Trips[0].Destination ==
            "Centro" &&
        timetableCatalog.Trips[0].Stops.Count ==
            2 &&
        timetableCatalog.Trips[0].Stops[0].Name ==
            "Terminal Central" &&
        timetableCatalog.Trips[0].Stops[1].Name ==
            "Avenida Brasil" &&
        timetableCatalog.Tracks.TryGetValue(
            "Track100",
            out var syntheticTimetableTrack) &&
        syntheticTimetableTrack.Entries.Count ==
            2 &&
        syntheticTimetableTrack.Entries[0].ObjectId ==
            3001 &&
        syntheticTimetableTrack.Entries[0].TileCoordinate ==
            new OmsiTileCoordinate(
                0,
                0) &&
        syntheticTimetableTrack.Entries[1].PathId ==
            1 &&
        timetableCatalog.Lines.Count ==
            1 &&
        timetableCatalog.Lines[0].Name ==
            "100" &&
        timetableCatalog.Lines[0].UserAllowed &&
        timetableCatalog.Lines[0].Priority ==
            3 &&
        timetableCatalog.Lines[0].Tours.Count ==
            2 &&
        timetableCatalog.Lines[0].Tours[0].Number ==
            "1" &&
        timetableCatalog.Lines[0].Tours[0].AiGroup ==
            "Busses" &&
        timetableCatalog.Lines[0].Tours[0].Extra ==
            "127" &&
        timetableCatalog.Lines[0].Tours[0].DayMask ==
            127 &&
        timetableCatalog.Lines[0].Tours[1].Number ==
            "2" &&
        timetableCatalog.Lines[0].Tours[1].Extra ==
            string.Empty &&
        timetableCatalog.Lines[0].Tours[1].DayMask ==
            1023 &&
        timetableCatalog.Lines[0].Tours[0].Trips.Count ==
            1 &&
        timetableCatalog.Lines[0].Tours[0].Trips[0].TripName ==
            "Linha100" &&
        timetableCatalog.Lines[0].Tours[0].Trips[0].ProfileIndex ==
            0 &&
        Math.Abs(
            timetableCatalog.Lines[0].Tours[0].Trips[0].DepartureMinutes -
            480.5) <
            0.0001,
        "OMSI TTData parser did not preserve real trip, track, TTL tour, AI group, day mask, profile and departure data.");

    var parsedStationLinkTrip =
        timetableCatalog.Trips.Single(
            static trip =>
                trip.Name ==
                "Linha200");

    Require(
        parsedStationLinkTrip.TrackName ==
            string.Empty &&
        parsedStationLinkTrip.Destination ==
            "Bairro" &&
        parsedStationLinkTrip.Line ==
            "200" &&
        parsedStationLinkTrip.Stops.Count ==
            2 &&
        parsedStationLinkTrip.Stops[0].StopId ==
            42 &&
        parsedStationLinkTrip.Stops[1].StopId ==
            43 &&
        timetableCatalog.StationLinks.Count ==
            1 &&
        timetableCatalog.StationLinks[0].FromStopId ==
            42 &&
        timetableCatalog.StationLinks[0].ToStopId ==
            43 &&
        timetableCatalog.StationLinks[0].Entries.Count ==
            2 &&
        timetableCatalog.StationLinks[0].Entries[0].ObjectId ==
            3001 &&
        timetableCatalog.StationLinks[0].Entries[0].TileCoordinate ==
            new OmsiTileCoordinate(
                0,
                0) &&
        timetableCatalog.StationLinks[0].Entries[1].PathId ==
            1 &&
        parsedStationLinkTrip.Profiles.Count ==
            1 &&
        parsedStationLinkTrip.Profiles[0].Name ==
            "Weekday" &&
        parsedStationLinkTrip.Profiles[0].FactorMinutes ==
            10.0 &&
        parsedStationLinkTrip.Profiles[0].ManualDepartureTimes.Count ==
            1 &&
        parsedStationLinkTrip.Profiles[0].ManualDepartureTimes[0].StationIndex ==
            0 &&
        parsedStationLinkTrip.Profiles[0].ManualDepartureTimes[0].Minutes ==
            1.0 &&
        parsedStationLinkTrip.Profiles[0].ManualArrivalTimes.Count ==
            1 &&
        parsedStationLinkTrip.Profiles[0].ManualArrivalTimes[0].StationIndex ==
            1 &&
        parsedStationLinkTrip.Profiles[0].ManualArrivalTimes[0].Minutes ==
            9.0,
        "OMSI TTData parser did not preserve a bus trip, StnLinks.cfg and its selected TTP profile data.");

    var timingTrip =
        new OmsiTimetableTrip(
            "TimingTrip",
            "TimingTrip.ttp",
            string.Empty,
            "Terminal",
            "300",
            [
                new OmsiTimetableStop(
                    1,
                    "A",
                    null,
                    null),
                new OmsiTimetableStop(
                    2,
                    "B",
                    null,
                    null),
                new OmsiTimetableStop(
                    3,
                    "C",
                    null,
                    null)
            ],
            [
                new OmsiTimetableTripProfile(
                    "Timed",
                    10.0,
                    [
                        new OmsiTimetableProfileTime(
                            2,
                            8.0)
                    ],
                    [
                        new OmsiTimetableProfileTime(
                            0,
                            0.0)
                    ],
                    [
                        new OmsiTimetableProfileStopping(
                            1,
                            2)
                    ])
            ]);

    var timing =
        WorldLineAiTripTimingResolver.Resolve(
            timingTrip,
            0,
            [
                new OmsiTimetableStationLink(
                    100.0,
                    1,
                    2,
                    Array.Empty<OmsiTimetableTrackEntry>()),
                new OmsiTimetableStationLink(
                    300.0,
                    2,
                    3,
                    Array.Empty<OmsiTimetableTrackEntry>())
            ]);

    Require(
        timing.Stops.Count ==
            3 &&
        timing.Stops[0].DepartureSeconds ==
            0.0 &&
        Math.Abs(
            timing.Stops[1].ArrivalSeconds -
            120.0) <
            0.0001 &&
        !timing.Stops[1].Stops &&
        timing.Stops[2].ArrivalSeconds ==
            480.0 &&
        timing.DurationSeconds ==
            480.0,
        "LineAI TTP timing did not interpolate untimed stations by real StnLink lengths or honor pass-through stops.");

    var parsedTiming =
        WorldLineAiTripTimingResolver.Resolve(
            parsedStationLinkTrip,
            0,
            timetableCatalog.StationLinks);

    Require(
        parsedTiming.Stops.Count ==
            2 &&
        parsedTiming.Stops[0].DepartureSeconds ==
            60.0 &&
        parsedTiming.Stops[1].ArrivalSeconds ==
            540.0 &&
        parsedTiming.DurationSeconds ==
            540.0,
        "LineAI did not apply parsed TTP manual arrival/departure profile times.");

    var mapCalendar =
        OmsiMapCalendarReader.Read(
            maps[0]);

    var publicHolidayBits =
        OmsiTimetableDayMask.Resolve(
            new DateOnly(
                2026,
                9,
                30),
            mapCalendar);

    var schoolHolidayBits =
        OmsiTimetableDayMask.Resolve(
            new DateOnly(
                2026,
                10,
                2),
            mapCalendar);

    Require(
        mapCalendar.IsPublicHoliday(
            20260930) &&
        mapCalendar.IsSchoolHoliday(
            20261002) &&
        publicHolidayBits.DayBit ==
            (1 << 7) &&
        publicHolidayBits.SchoolBit ==
            (1 << 9) &&
        schoolHolidayBits.DayBit ==
            (1 << 4) &&
        schoolHolidayBits.SchoolBit ==
            (1 << 8),
        "OMSI Holidays.txt calendar/day-mask resolution is incorrect.");

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

    Require(
        bus.BackCoupling is
            { } backCoupling &&
        Math.Abs(
            backCoupling.Y +
            4.5) <
        0.0001 &&
        bus.CoupledBack is
            { } coupledBack &&
        coupledBack.DeclaredBusPath ==
            "SyntheticTrailer.bus" &&
        !coupledBack.Reverse,
        "OMSI [coupling_back]/[couple_back] metadata must be parsed from the leading .bus file.");

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
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeIsNightMap &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets is
            { Count: 1 } &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].VariableName ==
            "cockpit_light_test" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items.Count == 2 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].ItemIndex == 0 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].AlphaMode is null &&
        !vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].HasTransMapDirective &&
        !vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[0].MaterialChangeIsNightMap &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].ItemIndex == 1 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].AlphaMode == 1 &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].HasTransMapDirective &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].NoZWrite &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].TransMapTexturePath!) ==
            "panel_mask.bmp" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].LightMapVariable ==
            "lights_stand" &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].LightMapTexturePath!) ==
            "panel_item_lm.bmp" &&
        Path.GetFileName(
            vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].MaterialChangeTexturePath!) ==
            "panel_n.bmp" &&
        vehicleAsset.Meshes[0].Materials[0].MaterialChangeSets![0].Items[1].MaterialChangeIsNightMap &&
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
        vehicleAsset.Meshes[1].Materials.Count == 1 &&
        vehicleAsset.Meshes[1].Materials[0].HasTransMapDirective &&
        vehicleAsset.Meshes[1].Materials[0].AlphaMode == 1 &&
        Path.GetFileName(
            vehicleAsset.Meshes[1].Materials[0].TransMapTexturePath!) ==
            "panel_mask.bmp" &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M41 -
            1.25f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M42 -
            2.5f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SourceTransform.M43 -
            3.75f) < 0.0001 &&
        vehicleAsset.Meshes[0].ModelOrdinal == 0 &&
        vehicleAsset.Meshes[0].SkinBoneMeshOrdinals is
            { Count: 1 } &&
        vehicleAsset.Meshes[0].SkinBoneMeshOrdinals![0] == 0 &&
        vehicleAsset.Meshes[0].SkinWeights is
            { Length: 12 } &&
        Math.Abs(
            vehicleAsset.Meshes[0].SkinWeights![0] -
            0.5f) < 0.0001 &&
        Math.Abs(
            vehicleAsset.Meshes[0].SkinWeights![4] -
            0.75f) < 0.0001 &&
        vehicleAsset.ProtectedMeshCount == 0 &&
        vehicleAsset.FailedMeshCount == 0,
        "Synthetic OMSI bus model.cfg/O3D geometry did not load end-to-end.");

    var trailerSoundDirectory =
        Path.Combine(
            vehicleDirectory,
            "sound");

    Directory.CreateDirectory(
        trailerSoundDirectory);

    File.WriteAllText(
        Path.Combine(
            trailerSoundDirectory,
            "trailer.cfg"),
        Lines(
            "[loopsound]",
            "idle.wav",
            "44100",
            "engine_n",
            "600",
            "0.7",
            "[viewpoint]",
            "5"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            vehicleDirectory,
            "SyntheticTrailer.bus"),
        Lines(
            "[model]",
            @"model\model.cfg",
            "[sound]",
            @"sound\trailer.cfg",
            "[friendlyname]",
            "Synthetic Coachworks",
            "Camera Bus Trailer",
            "Test Skin",
            "[mass]",
            "6",
            "[momentofintertia]",
            "150",
            "40",
            "150",
            "[schwerpunkt]",
            "1.2",
            "[rollwiderstand]",
            "500",
            "[rot_pnt_long]",
            "-0.39",
            "[newachse]",
            "achse_long",
            "-1.19687",
            "achse_raddurchmesser",
            "1.02",
            "achse_feder",
            "280",
            "achse_maxforce",
            "116",
            "achse_daempfer",
            "20",
            "achse_antrieb",
            "1",
            "[coupling_front]",
            "0",
            "3.5",
            "0.5",
            "[coupling_front_character]",
            "52.5",
            "-20",
            "20",
            "1",
            "[couple_front_open_for_sound]"),
        Encoding.Unicode);

    var trailerBus =
        OmsiBusReader.ReadFile(
            root,
            Path.Combine(
                vehicleDirectory,
                "SyntheticTrailer.bus"));

    Require(
        trailerBus.FrontCoupling is
            { } trailerFront &&
        Math.Abs(
            trailerFront.Y -
            3.5) <
        0.0001 &&
        trailerBus.FrontCouplingCharacter is
            { } trailerCharacter &&
        Math.Abs(
            trailerCharacter.MaximumYawDegrees -
            52.5) <
        0.0001 &&
        Math.Abs(
            trailerCharacter.MinimumPitchDegrees +
            20.0) <
        0.0001 &&
        Math.Abs(
            trailerCharacter.MaximumPitchDegrees -
            20.0) <
        0.0001 &&
        trailerCharacter.Type ==
            1 &&
        trailerBus.FrontCouplingOpenForSound &&
        trailerBus.SoundConfigPath is
            { Length: > 0 } &&
        Path.GetFileName(
            trailerBus.SoundConfigPath) ==
            "trailer.cfg" &&
        Math.Abs(
            trailerBus.Physics.MassTonnes!.Value -
            6.0) <
        0.0001 &&
        Math.Abs(
            trailerBus.Physics.MomentOfInertiaZ!.Value -
            150.0) <
        0.0001 &&
        Math.Abs(
            trailerBus.Physics.RotationPointLongitudinalMeters!.Value +
            0.39) <
        0.0001 &&
        Math.Abs(
            trailerBus.Physics.AverageWheelDiameterMeters!.Value -
            1.02) <
        0.0001,
        "OMSI trailer coupling, sound and per-section physical data must be parsed from the coupled .bus file.");

    var articulatedAsset =
        OmsiArticulatedVehicleAssetLoader.Load(
            contentRoot,
            bus);

    Require(
        articulatedAsset.SectionCount ==
            2 &&
        articulatedAsset.Sections is
            { Count: 1 } &&
        articulatedAsset.Sections[0].Index ==
            1 &&
        articulatedAsset.Sections[0].ParentIndex ==
            0 &&
        Math.Abs(
            articulatedAsset.Sections[0].JointY +
            4.5) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].MaximumYawDegrees -
            52.5) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].MinimumPitchDegrees +
            20.0) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].MaximumPitchDegrees -
            20.0) <
        0.0001 &&
        articulatedAsset.Sections[0].CouplingType ==
            1 &&
        Math.Abs(
            articulatedAsset.Sections[0].OriginY +
            8.0) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].FollowerLengthMeters -
            3.89) <
        0.0001 &&
        articulatedAsset.Sections[0].OpenForSound &&
        articulatedAsset.Sections[0].SoundConfigPath is
            { Length: > 0 } &&
        Path.GetFileName(
            articulatedAsset.Sections[0].SoundConfigPath) ==
            "trailer.cfg" &&
        Math.Abs(
            articulatedAsset.Sections[0].MassTonnes!.Value -
            6.0) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].YawInertiaTonneSquareMeters!.Value -
            150.0) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Sections[0].AverageWheelDiameterMeters!.Value -
            1.02) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Bus.Physics.MassTonnes!.Value -
            16.9) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Bus.Physics.RollingResistanceNewtons!.Value -
            1500.0) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Bus.Physics.MomentOfInertiaZ!.Value -
            300.0) <
        0.0001 &&
        articulatedAsset.Meshes.Count ==
            vehicleAsset.Meshes.Count * 2 &&
        articulatedAsset.Meshes[
                vehicleAsset.Meshes.Count]
            .SectionIndex ==
            1 &&
        articulatedAsset.Meshes[
                vehicleAsset.Meshes.Count]
            .LightEffects is
            { Count: > 0 } &&
        vehicleAsset.Meshes[0]
            .LightEffects is
            { Count: > 0 } &&
        Math.Abs(
            articulatedAsset.Meshes[
                    vehicleAsset.Meshes.Count]
                .LightEffects![0]
                .PositionY -
            vehicleAsset.Meshes[0]
                .LightEffects![0]
                .PositionY) <
        0.0001 &&
        Math.Abs(
            articulatedAsset.Meshes[
                    vehicleAsset.Meshes.Count]
                .Transform.PositionY -
            vehicleAsset.Meshes[0]
                .Transform.PositionY +
            8.0) <
        0.0001,
        "OMSI articulated loader must follow [couple_back], preserve the joint metadata and place the trailer origin so coupling_back and coupling_front coincide.");

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
        scriptCatalog.Program.FrameAiBlocks.Count == 1 &&
        scriptCatalog.Program.Macros.ContainsKey(
            "HELPER") &&
        scriptCatalog.NumericVariables.Contains(
            "ENGINE_SPEED") &&
        scriptCatalog.Constants.ContainsKey(
            "ENGINE_IDLE") &&
        scriptCatalog.Curves.ContainsKey(
            "ENGINE_CURVE"),
        "Synthetic OMSI script entry points were not parsed.");

    var scriptRuntime =
        new OmsiScriptRuntime(
            scriptCatalog);

    Require(
        scriptRuntime.WritesLocalVariable(
            "engine_speed"),
        "OMSI script write analysis did not detect an S.L. target.");

    Require(
        scriptRuntime.WritesLocalVariable(
            "mesh_visible"),
        "OMSI script write analysis missed an init-block S.L. target.");

    Require(
        scriptRuntime.WritesLocalVariable(
            "ai_counter"),
        "OMSI script write analysis missed a frame_ai S.L. target.");

    Require(
        !scriptRuntime.WritesLocalVariable(
            "Throttle"),
        "OMSI script write analysis incorrectly marked a read-only host variable as script-authored.");

    Require(
        scriptRuntime.WritesStringLocalVariable(
            "IBIS_line"),
        "OMSI script write analysis did not detect an S.$. target.");

    Require(
        !scriptRuntime.WritesStringLocalVariable(
            "Matrix_SchildFrnt"),
        "OMSI script write analysis incorrectly marked an unwritten string variable as script-authored.");

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
        fileSoundTriggers.Count == 2 &&
        fileSoundTriggers[0].Trigger ==
            "ev_IBIS_Ansagen" &&
        fileSoundTriggers[0].File ==
            "announcement.wav" &&
        fileSoundTriggers[1].Trigger ==
            "ev_DefaultConfiguredSound" &&
        fileSoundTriggers[1].File ==
            string.Empty,
        "OMSI T.F file/default sound trigger execution failed.");
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

    scriptRuntime.ExecuteFrameAi();

    Require(
        Math.Abs(
            scriptRuntime.GetLocal(
                "ai_counter") -
            1.0) <
            0.0001,
        "OMSI {frame_ai} execution failed.");

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
        "OMSI variables and system variables must resolve case-insensitively.");

    Require(
        scriptRuntime.HasTrigger(
            "CoLlIsIoN"),
        "OMSI trigger lookup must be case-insensitive.");

    scriptRuntime.ExecuteTrigger(
        "CoLlIsIoN");

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
        bus.ReflectionCameras[0].VisibilityThreshold is null &&
        !bus.ReflectionCameras[0].ContinuousRendering,
        "Conditional OMSI reflection camera metadata is incorrect.");
    Require(
        bus.ReflectionCameras[1].RuntimeTextureName ==
            "reflexion1.bmp" &&
        Math.Abs(
            (bus.ReflectionCameras[1].VisibilityThreshold ?? 0.0) -
            0.15) < 0.0001 &&
        bus.ReflectionCameras[1].ContinuousRendering,
        "Continuous OMSI reflection camera metadata is incorrect.");
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

    var catalogTrafficVehiclePath =
        Path.Combine(
            vehicleDirectory,
            "traffic.bus");

    var catalogTaxiVehiclePath =
        Path.Combine(
            vehicleDirectory,
            "taxi.bus");

    File.WriteAllText(
        catalogTrafficVehiclePath,
        string.Empty);

    File.WriteAllText(
        catalogTaxiVehiclePath,
        string.Empty);

    File.WriteAllText(
        Path.Combine(
            mapDirectory,
            "unsched_vehgroups.txt"),
        Lines(
            "[group]",
            "<aigroup-name>",
            "<default density class>",
            "[group]",
            "NormalCars",
            "1",
            "[group]",
            "Taxi",
            "1"),
        Encoding.Unicode);

    File.WriteAllText(
        Path.Combine(
            mapDirectory,
            "ailists.cfg"),
        Lines(
            "[aigroup_2]",
            "NormalCars",
            @"Vehicles\Synthetic\traffic.bus 1",
            "[end]",
            "[aigroup_2]",
            "Taxi",
            @"Vehicles\Synthetic\taxi.bus 1",
            "[end]"),
        Encoding.Unicode);

    var map = maps[0];
    var world = WorldLoader.Load(contentRoot, map);

    Require(
        world.SignalRoutes is
            { Sections.Count: 6 } &&
        world.SignalRoutes.Sections[2].Name.Equals(
            "entry",
            StringComparison.OrdinalIgnoreCase) &&
        world.SignalRoutes.Sections[2].RouteIndex ==
            0,
        "World load did not retain the parsed OMSI signalroutes.cfg data.");

    Require(
        world.AiCatalog.UnscheduledVehicleGroups is
            { Count: 2 } &&
        world.AiCatalog.UnscheduledVehicleGroups[0].Index ==
            0 &&
        world.AiCatalog.UnscheduledVehicleGroups[0].Name ==
            "NormalCars" &&
        world.AiCatalog.UnscheduledVehicleGroups[1].Index ==
            1 &&
        world.AiCatalog.UnscheduledVehicleGroups[1].Name ==
            "Taxi",
        "unsched_vehgroups.txt order was not preserved as OMSI group indices.");

    Require(
        world.Tiles.Count == 1,
        $"Expected 1 active tile, found {world.Tiles.Count}.");
    Require(
        world.Objects.Count == 4,
        $"Expected 4 objects, found {world.Objects.Count}.");
    Require(
        world.Splines.Count == 2,
        $"Expected 2 splines, found {world.Splines.Count}.");
    Require(
        world.PlacementParseIssueCount == 0,
        "Synthetic placements should parse without issues.");

    Require(
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\masked.sco",
            out var maskedAsset) &&
        maskedAsset.IsRenderable &&
        maskedAsset.Meshes is
            [{ Materials.Count: 1 }] &&
        maskedAsset.Meshes[0].Materials[0].RequiresExternalTransMap &&
        maskedAsset.Meshes[0].Materials[0].AlphaMode ==
            1 &&
        maskedAsset.Meshes[0].SourceTransform is
            { } maskedSourceTransform &&
        Math.Abs(
            maskedSourceTransform.M41 -
            1.25f) <
            0.0001f &&
        Math.Abs(
            maskedSourceTransform.M42 -
            2.5f) <
            0.0001f &&
        Math.Abs(
            maskedSourceTransform.M43 -
            3.75f) <
            0.0001f &&
        !string.IsNullOrWhiteSpace(
            maskedAsset.Meshes[0].Materials[0].TransMapTexturePath) &&
        Path.GetFileName(
            maskedAsset.Meshes[0].Materials[0].TransMapTexturePath!) ==
            "mask.bmp",
        "Bare scenery [matl_transmap] did not resolve its mask and imply alpha cutout.");

    Require(
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\collision_meta.sco",
            out var collisionAsset) &&
        collisionAsset.CollisionMeshSource ==
            "collision_low.o3d" &&
        collisionAsset.CollisionBounds is
            {
                MinimumX: -1.5,
                MaximumX: 2.5,
                MinimumY: 1.0,
                MaximumY: 4.0,
                MinimumZ: -1.25,
                MaximumZ: 0.75
            } &&
        collisionAsset.CollisionGeometry is
            { Positions.Length: >= 9, Indices.Length: >= 3 },
        "WorldLoader did not preserve OMSI [collision_mesh] bounds and triangle geometry.");

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
        worldSpline.TerrainAlignMode ==
            4,
        "Spline [spline_terrain_align_2] mode was not preserved.");

    Require(
        worldSpline.NextId == 2002,
        "Spline next-link ID was not preserved.");

    var mirroredBankedSpline =
        new WorldSplinePlacement(
            new WorldTileCoordinate(
                0,
                0),
            9400,
            -1,
            -1,
            @"Splines\Synthetic\mirrored.sli",
            new WorldVector3(
                0.0,
                10.0,
                0.0),
            0.0,
            10.0,
            0.0,
            0.0,
            0.0,
            true,
            DeltaHeightMeters:
                2.0,
            CantStartPercent:
                10.0,
            CantEndPercent:
                10.0,
            SkewStart:
                0.5,
            SkewEnd:
                0.5,
            Mirror:
                true);

    var mirroredBankedAsset =
        new WorldSplineAsset(
            @"Splines\Synthetic\mirrored.sli",
            null,
            true,
            Array.Empty<WorldSplineSurface>(),
            [
                new WorldSplinePath(
                    0,
                    2.0,
                    0.25,
                    2.5,
                    0)
            ]);

    var mirroredBankedNetwork =
        WorldTrafficPathNetworkBuilder.Build(
            [mirroredBankedSpline],
            new Dictionary<string, WorldSplineAsset>(
                StringComparer.OrdinalIgnoreCase)
            {
                [
                    @"Splines\Synthetic\mirrored.sli"
                ] =
                    mirroredBankedAsset
            });

    var mirroredBankedPath =
        mirroredBankedNetwork
            .Segments
            .Single();

    var mirroredBankedStart =
        mirroredBankedPath.Points[0];

    var mirroredBankedEnd =
        mirroredBankedPath.Points[^1];

    Require(
        Math.Abs(
            mirroredBankedStart.X +
            2.0) <
            0.001 &&
        Math.Abs(
            mirroredBankedStart.Z +
            1.0) <
            0.001 &&
        Math.Abs(
            mirroredBankedStart.Y -
            10.05) <
            0.001,
        "Mirrored/skewed/canted spline traffic path start did not match the visual spline transform.");

    Require(
        Math.Abs(
            mirroredBankedEnd.X +
            2.0) <
            0.001 &&
        Math.Abs(
            mirroredBankedEnd.Z -
            9.0) <
            0.001 &&
        Math.Abs(
            mirroredBankedEnd.Y -
            12.05) <
            0.001,
        "spline_h traffic path did not preserve mirrored offset, cant and delta height.");

    var trafficPaths =
        world.TrafficPaths;

    Require(
        trafficPaths.Segments.Count == 5 &&
        trafficPaths.RoadVehicleSegmentCount == 3 &&
        trafficPaths.PedestrianSegmentCount == 2 &&
        trafficPaths.RailSegmentCount == 0 &&
        trafficPaths.AircraftSegmentCount == 0,
        "Synthetic spline/scenery [path] lanes were not expanded into the expected world traffic network.");

    Require(
        trafficPaths.ConnectedEndpointCount == 3 &&
        trafficPaths.TerminalEndpointCount == 4 &&
        trafficPaths.BoundaryEndpointCount == 0 &&
        trafficPaths.UnmatchedEndpointCount == 0,
        "Synthetic traffic path endpoint connectivity is incorrect.");

    var firstRoadPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.SplineId == 2001 &&
                segment.Type == 0);

    var secondRoadPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.SplineId == 2002 &&
                segment.Type == 0);

    Require(
        secondRoadPath.SpeedLimitKilometersPerHour.HasValue &&
        Math.Abs(
            secondRoadPath.SpeedLimitKilometersPerHour.Value -
            10.0) <
            0.0001,
        "Synthetic OMSI [rule] speedlimit was not attached to the matching path.");

    Require(
        secondRoadPath.TrafficDensityWeights is not null &&
        secondRoadPath.TrafficDensityWeights.TryGetValue(
            0,
            out var normalDensity) &&
        Math.Abs(
            normalDensity -
            0.5) <
            0.0001,
        "Synthetic OMSI [rule] trafficdensity was not attached to the matching group/path.");

    Require(
        secondRoadPath.BlockedUnscheduledGroupIndices is not null &&
        secondRoadPath.BlockedUnscheduledGroupIndices.Contains(
            1),
        "Synthetic OMSI [rule] no_cars was not attached to the matching group/path.");

    Require(
        secondRoadPath.TrafficPriority ==
            192,
        "Synthetic OMSI [rule] priority was not attached to the matching path.");

    Require(
        firstRoadPath.TrafficPriority ==
            128,
        "Unknown OMSI priority encoding did not fall back to normal priority.");

    Require(
        firstRoadPath.ForwardConnections.Count == 1 &&
        firstRoadPath.ForwardConnections[0] ==
            secondRoadPath.Index,
        "Forward road path did not connect across linked OMSI splines.");

    var navigationRoute =
        firstRoadPath.Points
            .Concat(
                secondRoadPath.Points.Skip(
                    1))
            .ToArray();

    var navigationAssist =
        new WorldNavigationAssist();

    navigationAssist.SetRoute(
        navigationRoute);

    var navigationStart =
        navigationRoute[0];

    var navigationHeading =
        navigationRoute.Length >
                1
            ? Math.Atan2(
                navigationRoute[1].X -
                    navigationStart.X,
                navigationRoute[1].Z -
                    navigationStart.Z) *
              180.0 /
              Math.PI
            : 0.0;

    var navigationState =
        navigationAssist.Build(
            navigationStart,
            navigationHeading,
            50.0,
            onFoot:
                false,
            [
                new WorldTrafficAgentState(
                    9001,
                    firstRoadPath.Index,
                    0.0,
                    0.0,
                    "Vehicles/Synthetic/car.bus",
                    new WorldVector3(
                        navigationStart.X +
                            2.0,
                        navigationStart.Y,
                        navigationStart.Z +
                            2.0),
                    0.0),
                new WorldTrafficAgentState(
                    9002,
                    firstRoadPath.Index,
                    1.0,
                    2.0,
                    "Vehicles/Synthetic/car.bus",
                    new WorldVector3(
                        navigationStart.X +
                            4.0,
                        navigationStart.Y,
                        navigationStart.Z +
                            4.0),
                    0.0),
                new WorldTrafficAgentState(
                    9003,
                    firstRoadPath.Index,
                    2.0,
                    3.0,
                    "Vehicles/Synthetic/car.bus",
                    new WorldVector3(
                        navigationStart.X +
                            6.0,
                        navigationStart.Y,
                        navigationStart.Z +
                            6.0),
                    0.0)
            ]);

    Require(
        navigationState.RouteLoaded &&
        !navigationState.OffRoute &&
        navigationState.SuggestedMapRadiusMeters ==
            1250.0 &&
        navigationState.CongestionLevel ==
            "heavy" &&
        navigationState.GuidanceWaypoints.Count >
            0 &&
        navigationState.GroundArrows.Count >
            0,
        "NavBR navigation core did not preserve adaptive zoom, congestion, route waypoints and ground guidance.");

    var navigationOffRoute =
        navigationAssist.Build(
            new WorldVector3(
                navigationStart.X +
                    1000.0,
                navigationStart.Y,
                navigationStart.Z +
                    1000.0),
            navigationHeading,
            25.0,
            onFoot:
                false);

    Require(
        navigationOffRoute.OffRoute &&
        navigationOffRoute.DistanceFromRouteMeters >
            45.0 &&
        navigationOffRoute.RejoinTarget.HasValue,
        "NavBR route projection did not detect off-route state or produce a rejoin target.");

    var syntheticLineTrack =
        new OmsiTimetableTrack(
            "SyntheticLineTrack",
            "synthetic.ttr",
            [
                new OmsiTimetableTrackEntry(
                    firstRoadPath.SplineId,
                    firstRoadPath.LocalPathIndex,
                    0,
                    25.0),
                new OmsiTimetableTrackEntry(
                    secondRoadPath.SplineId,
                    secondRoadPath.LocalPathIndex,
                    0,
                    25.0)
            ]);

    var resolvedLineRoute =
        WorldLineAiRouteResolver.Resolve(
            syntheticLineTrack,
            trafficPaths);

    var syntheticStationLinkTrip =
        new OmsiTimetableTrip(
            "Linha200",
            "Linha200.ttp",
            string.Empty,
            "Bairro",
            "200",
            [
                new OmsiTimetableStop(
                    42,
                    "Terminal Central",
                    0.0,
                    0),
                new OmsiTimetableStop(
                    43,
                    "Avenida Brasil",
                    75.5,
                    0)
            ]);

    var syntheticStationLinks =
        new[]
        {
            new OmsiTimetableStationLink(
                150.0,
                42,
                43,
                [
                    new OmsiTimetableTrackEntry(
                        firstRoadPath.SplineId,
                        firstRoadPath.LocalPathIndex,
                        0,
                        100.0),
                    new OmsiTimetableTrackEntry(
                        secondRoadPath.SplineId,
                        secondRoadPath.LocalPathIndex,
                        0,
                        50.0)
                ])
        };

    var resolvedStationLinkRoute =
        WorldLineAiRouteResolver.ResolveStationLinks(
            syntheticStationLinkTrip,
            syntheticStationLinks,
            trafficPaths);

    Require(
        resolvedStationLinkRoute.FullyResolved &&
        resolvedStationLinkRoute.SegmentIndices
            .SequenceEqual(
                new[]
                {
                    firstRoadPath.Index,
                    secondRoadPath.Index
                }),
        "LineAI did not resolve an ordinary OMSI bus TTP through StnLinks.cfg.");

    Require(
        resolvedLineRoute.FullyResolved &&
        resolvedLineRoute.SegmentIndices.Count ==
            2 &&
        resolvedLineRoute.SegmentIndices[0] ==
            firstRoadPath.Index &&
        resolvedLineRoute.SegmentIndices[1] ==
            secondRoadPath.Index,
        "LineAI did not resolve ordered OMSI TTR entries to the exact connected road segments.");

    var lineScheduleCatalog =
        new OmsiTimetableCatalog(
            [
                new OmsiTimetableTrip(
                    "Linha100",
                    "Linha100.ttp",
                    syntheticLineTrack.Name,
                    "Centro",
                    "100",
                    Array.Empty<OmsiTimetableStop>())
            ],
            new Dictionary<string, OmsiTimetableTrack>(
                StringComparer.OrdinalIgnoreCase)
            {
                [syntheticLineTrack.Name] =
                    syntheticLineTrack
            },
            [
                new OmsiTimetableLine(
                    "100",
                    "100.ttl",
                    true,
                    3,
                    [
                        new OmsiTimetableTour(
                            "1",
                            "Busses",
                            "1023",
                            [
                                new OmsiTimetableTourTrip(
                                    "Linha100",
                                    0,
                                    480.5)
                            ])
                    ])
            ]);

    var syntheticHof =
        OmsiHofCatalogReader.ParseText(
            Lines(
                "stringcount_terminus",
                "3",
                "[name]",
                "SyntheticDepot",
                "[addterminus]",
                "7",
                "Centro",
                "CENTRO",
                "",
                "CENTRO",
                "[addterminus_list]",
                "\t12\tBairro\tBAIRRO\t\tBAIRRO",
                "[end]"));

    Require(
        syntheticHof.Name ==
            "SyntheticDepot" &&
        syntheticHof.Termini.Count ==
            2 &&
        syntheticHof.FindTerminusIndex(
            "Centro") ==
            0 &&
        syntheticHof.FindTerminusIndex(
            "bairro") ==
            1 &&
        syntheticHof.Termini[1].Code ==
            12,
        "HOF parser did not preserve OMSI terminus order/identifiers for scheduled AI.");

    var lineAiVehicle =
        new OmsiAiVehicleDefinition(
            "Busses",
            @"Vehicles\Synthetic\traffic.bus",
            Path.Combine(
                contentRoot.RootPath,
                "Vehicles",
                "Synthetic",
                "traffic.bus"),
            1.0,
            "SyntheticDepot");

    var stationLinkScheduleCatalog =
        new OmsiTimetableCatalog(
            [syntheticStationLinkTrip],
            new Dictionary<string, OmsiTimetableTrack>(
                StringComparer.OrdinalIgnoreCase),
            [
                new OmsiTimetableLine(
                    "200",
                    "200.ttl",
                    true,
                    3,
                    [
                        new OmsiTimetableTour(
                            "1",
                            "Busses",
                            "1023",
                            [
                                new OmsiTimetableTourTrip(
                                    "Linha200",
                                    0,
                                    481.0)
                            ])
                    ])
            ],
            syntheticStationLinks);

    var stationLinkSchedule =
        WorldLineAiScheduleResolver.Resolve(
            stationLinkScheduleCatalog,
            trafficPaths,
            new OmsiMapAiCatalog(
                [lineAiVehicle],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()));

    Require(
        stationLinkSchedule.ReadyCount ==
            1 &&
        stationLinkSchedule.Trips[0].Status ==
            WorldLineAiScheduleStatus.Ready &&
        stationLinkSchedule.Trips[0].Route is
            { FullyResolved: true } &&
        stationLinkSchedule.Trips[0].Route!.SegmentIndices
            .SequenceEqual(
                new[]
                {
                    firstRoadPath.Index,
                    secondRoadPath.Index
                }),
        "LineAI schedule resolver did not accept a bus service whose TTP route comes from StnLinks.cfg.");

    var lineSchedule =
        WorldLineAiScheduleResolver.Resolve(
            lineScheduleCatalog,
            trafficPaths,
            new OmsiMapAiCatalog(
                [lineAiVehicle],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()));

    Require(
        lineSchedule.Trips.Count ==
            1 &&
        lineSchedule.ReadyCount ==
            1 &&
        lineSchedule.UnresolvedCount ==
            0 &&
        lineSchedule.Trips[0].Status ==
            WorldLineAiScheduleStatus.Ready &&
        lineSchedule.Trips[0].Route is
            { FullyResolved: true } &&
        lineSchedule.Trips[0].Route!.SegmentIndices
            .SequenceEqual(
                new[]
                {
                    firstRoadPath.Index,
                    secondRoadPath.Index
                }) &&
        lineSchedule.Trips[0].DepartureMinutes ==
            480.5 &&
        WorldLineAiScheduleResolver.SelectVehicle(
            lineSchedule.Trips[0]) ==
            lineAiVehicle,
        "LineAI did not connect TTL tour/departure to the real TTP/TTR route and AI vehicle group.");

    var priorityFilteredLineSimulation =
        new WorldLineAiSimulation(
            lineSchedule,
            trafficPaths,
            maximumActiveAgents:
                4,
            serviceStartMinutes:
                480.0,
            maximumLinePriority:
                2);

    Require(
        priorityFilteredLineSimulation.RequiredVehiclePaths.Count ==
            0,
        "LineAI ignored OMSI scheduled-traffic priority filtering.");

    var calendarFilteredLineSimulation =
        new WorldLineAiSimulation(
            lineSchedule,
            trafficPaths,
            maximumActiveAgents:
                4,
            serviceStartMinutes:
                480.0,
            maximumLinePriority:
                3,
            requiredDayBit:
                1 <<
                7,
            requiredSchoolBit:
                1 <<
                9);

    Require(
        calendarFilteredLineSimulation.RequiredVehiclePaths.Count ==
            1,
        "LineAI rejected a tour whose OMSI day/school mask is valid.");

    var lineSimulation =
        new WorldLineAiSimulation(
            lineSchedule,
            trafficPaths,
            maximumActiveAgents:
                4,
            serviceStartMinutes:
                480.0);

    Require(
        lineSimulation.RequiredVehiclePaths.Count ==
            1 &&
        lineSimulation.ActiveCount ==
            0,
        "LineAI simulation did not prepare the selected scheduled vehicle without spawning early.");

    for (var lineStep = 0;
         lineStep < 80;
         lineStep++)
    {
        lineSimulation.Step(
            0.25);
    }

    Require(
        lineSimulation.ActiveCount ==
            0 &&
        lineSimulation.Snapshot().Count ==
            0,
        "LineAI activated the TTL trip before its departure time.");

    for (var lineStep = 0;
         lineStep < 50;
         lineStep++)
    {
        lineSimulation.Step(
            0.25);
    }

    var departedLineAgents =
        lineSimulation.Snapshot();

    Require(
        lineSimulation.ActiveCount ==
            1 &&
        departedLineAgents.Count ==
            1 &&
        departedLineAgents[0].LineName ==
            "100" &&
        departedLineAgents[0].TripName ==
            "Linha100" &&
        departedLineAgents[0].SegmentIndex ==
            firstRoadPath.Index &&
        departedLineAgents[0].DepotHofName ==
            "SyntheticDepot",
        "LineAI did not activate the exact TTL service after its scheduled departure.");

    for (var lineStep = 0;
         lineStep < 52;
         lineStep++)
    {
        lineSimulation.Step(
            0.25);
    }

    var advancedLineAgents =
        lineSimulation.Snapshot();

    Require(
        advancedLineAgents.Count ==
            1 &&
        advancedLineAgents[0].RouteSegmentIndex >=
            1 &&
        advancedLineAgents[0].SegmentIndex ==
            secondRoadPath.Index,
        "LineAI left the resolved TTR sequence instead of advancing onto the next scheduled road segment.");

    var blockedLineSimulation =
        new WorldLineAiSimulation(
            lineSchedule,
            trafficPaths,
            maximumActiveAgents:
                1,
            serviceStartMinutes:
                480.5);

    var obstaclePoint =
        firstRoadPath.Points[
            Math.Min(
                1,
                firstRoadPath.Points.Count -
                    1)];

    var obstacleHeading =
        firstRoadPath.Points.Count >
                1
            ? Math.Atan2(
                firstRoadPath.Points[1].X -
                    firstRoadPath.Points[0].X,
                firstRoadPath.Points[1].Z -
                    firstRoadPath.Points[0].Z)
            : 0.0;

    blockedLineSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            obstaclePoint,
            obstacleHeading,
            0.0,
            1.0,
            1.0));

    for (var blockedStep = 0;
         blockedStep <
             12;
         blockedStep++)
    {
        blockedLineSimulation.Step(
            0.25);
    }

    var blockedLineAgents =
        blockedLineSimulation.Snapshot();

    Require(
        blockedLineAgents.Count ==
            1 &&
        blockedLineAgents[0].DistanceMeters <
            0.5 &&
        blockedLineAgents[0].SpeedMetersPerSecond <
            0.2 &&
        blockedLineAgents[0].AiBrakeLight &&
        blockedLineAgents[0].AtStation ==
            0,
        "LineAI did not hold safely for the player/external obstacle ahead or confused an obstacle hold with station boarding.");

    var missingGroupSchedule =
        WorldLineAiScheduleResolver.Resolve(
            lineScheduleCatalog,
            trafficPaths,
            OmsiMapAiCatalog.Empty);

    Require(
        missingGroupSchedule.Trips.Count ==
            1 &&
        missingGroupSchedule.Trips[0].Status ==
            WorldLineAiScheduleStatus.VehicleGroupMissing &&
        !missingGroupSchedule.Trips[0].Ready,
        "LineAI guessed a fallback vehicle when the TTL AI group was missing.");

    var tileAwareLineNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    90,
                    9900,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TileCoordinate:
                        new WorldTileCoordinate(
                            0,
                            0)),
                new WorldTrafficPathSegment(
                    91,
                    9900,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            20.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            20.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TileCoordinate:
                        new WorldTileCoordinate(
                            1,
                            0))
            ],
            2,
            0,
            0,
            0,
            0,
            0,
            2,
            0);

    var tileAwareLineRoute =
        WorldLineAiRouteResolver.Resolve(
            new OmsiTimetableTrack(
                "TileAware",
                "tile-aware.ttr",
                [
                    new OmsiTimetableTrackEntry(
                        9900,
                        0,
                        1,
                        10.0,
                        new OmsiTileCoordinate(
                            1,
                            0))
                ]),
            tileAwareLineNetwork);

    Require(
        tileAwareLineRoute.FullyResolved &&
        tileAwareLineRoute.SegmentIndices
            .SequenceEqual(
                new[]
                {
                    91
                }),
        "LineAI ignored the TTR global.cfg tile index when duplicate path IDs existed.");

    var ambiguousLineNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    100,
                    9900,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    101,
                    9900,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            20.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            20.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>())
            ],
            2,
            0,
            0,
            0,
            0,
            0,
            2,
            0);

    var ambiguousLineRoute =
        WorldLineAiRouteResolver.Resolve(
            new OmsiTimetableTrack(
                "Ambiguous",
                "ambiguous.ttr",
                [
                    new OmsiTimetableTrackEntry(
                        9900,
                        0,
                        0,
                        10.0)
                ]),
            ambiguousLineNetwork);

    Require(
        !ambiguousLineRoute.FullyResolved &&
        ambiguousLineRoute.UnresolvedEntryCount ==
            1 &&
        ambiguousLineRoute.AmbiguousEntryCount ==
            1 &&
        ambiguousLineRoute.SegmentIndices.Count ==
            0,
        "LineAI guessed an ambiguous OMSI timetable path instead of failing closed.");

    var signalControlledPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.TrafficSignal is not null);

    Require(
        signalControlledPath.TrafficSignal is
            { SignalIndex: 0, Jumps.Count: 1, Stops.Count: 1 } &&
        Math.Abs(
            signalControlledPath.TrafficSignal.ApproachDistanceMeters -
            12.0) <
            0.0001 &&
        signalControlledPath.TrafficSignal.Jumps![0].CheckTrafficLightIndex ==
            0 &&
        signalControlledPath.TrafficSignal.Stops![0].CheckTrafficLightIndex ==
            0,
        "Traffic path network did not retain OMSI signal-group approach/jump/stop metadata.");

    var firstPedestrianPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.SplineId == 2001 &&
                segment.Type == 1);

    var secondPedestrianPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.SplineId == 2002 &&
                segment.Type == 1);

    Require(
        firstPedestrianPath.ForwardConnections.Contains(
            secondPedestrianPath.Index) &&
        secondPedestrianPath.ReverseConnections.Contains(
            firstPedestrianPath.Index),
        "Bidirectional pedestrian paths did not connect in both travel directions.");

    var syntheticAiVehiclePath =
        Path.Combine(
            contentRoot.RootPath,
            "Vehicles",
            "Synthetic",
            "traffic.bus");

    Directory.CreateDirectory(
        Path.GetDirectoryName(
            syntheticAiVehiclePath)!);

    var syntheticAiCarPath =
        Path.Combine(
            contentRoot.RootPath,
            "Vehicles",
            "Synthetic",
            "traffic.ovh");

    File.WriteAllText(
        syntheticAiCarPath,
        string.Empty);

    var syntheticAiTrainPath =
        Path.Combine(
            contentRoot.RootPath,
            "Vehicles",
            "Synthetic",
            "A_train.zug");

    File.WriteAllText(
        syntheticAiTrainPath,
        string.Empty);

    var trafficSimulation =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\A_train.zug",
                        syntheticAiTrainPath,
                        10.0),
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var initialTraffic =
        trafficSimulation.Snapshot();

    Require(
        initialTraffic.Count ==
            1 &&
        initialTraffic[0].SegmentIndex ==
            firstRoadPath.Index &&
        initialTraffic[0].SpeedMetersPerSecond >
            0.0 &&
        initialTraffic[0].VehiclePath ==
            syntheticAiVehiclePath,
        "Traffic simulation did not spawn a road-compatible vehicle on the first resolved road path.");

    var zeroWeightVehiclePath =
        Path.Combine(
            contentRoot.RootPath,
            "Vehicles",
            "Synthetic",
            "disabled.bus");

    File.WriteAllText(
        zeroWeightVehiclePath,
        string.Empty);

    var zeroWeightSimulation =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\disabled.bus",
                        zeroWeightVehiclePath,
                        0.0),
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var zeroWeightAgent =
        zeroWeightSimulation
            .Snapshot()
            .Single();

    Require(
        zeroWeightAgent.VehiclePath ==
            syntheticAiVehiclePath,
        "Zero-weight AI entry must not be selected for unscheduled traffic.");

    var weightedVehicleSimulation =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\disabled.bus",
                        zeroWeightVehiclePath,
                        0.1),
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        10.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var weightedVehicleAgent =
        weightedVehicleSimulation
            .Snapshot()
            .Single();

    Require(
        weightedVehicleAgent.VehiclePath ==
            syntheticAiVehiclePath,
        "Weighted AI selection remained biased toward the first vehicle entry.");

    var longRoadNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8400,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            1000.0)
                    ],
                    [1],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8401,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            1000.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            2000.0)
                    ],
                    Array.Empty<int>(),
                    [0])
            ],
            2,
            0,
            0,
            0,
            0,
            0,
            2,
            0);

    var longRoadSimulation =
        new WorldTrafficSimulation(
            longRoadNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                5,
            spawnExclusionCenter:
                new WorldVector3(
                    5000.0,
                    0.0,
                    5000.0),
            spawnExclusionRadiusMeters:
                45.0,
            spawnIntervalSeconds:
                0.25);

    longRoadSimulation.Step(
        2.0);

    var longRoadAgents =
        longRoadSimulation
            .Snapshot()
            .ToArray();

    Require(
        longRoadAgents.Length ==
            5 &&
        longRoadAgents
            .Select(
                static agent =>
                    agent.DistanceMeters)
            .Distinct()
            .Count() ==
            5,
        "Runtime traffic remained capped by OMSI path count instead of available road length.");

    var obstacleSimulation =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var obstacleInitial =
        obstacleSimulation
            .Snapshot()
            .Single();

    var obstacleAhead =
        new WorldVector3(
            obstacleInitial.Position.X +
                Math.Sin(
                    obstacleInitial.HeadingRadians) *
                12.0,
            obstacleInitial.Position.Y,
            obstacleInitial.Position.Z +
                Math.Cos(
                    obstacleInitial.HeadingRadians) *
                12.0);

    obstacleSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            obstacleAhead,
            obstacleInitial.HeadingRadians,
            0.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.35));

    obstacleSimulation.Step(
        2.0);

    var obstacleStopped =
        obstacleSimulation
            .Snapshot()
            .Single();

    Require(
        obstacleStopped.TraveledDistanceMeters <
            0.1 &&
        obstacleStopped.SpeedMetersPerSecond <
            obstacleInitial.SpeedMetersPerSecond,
        "Traffic AI did not stop for the external player-vehicle obstacle.");

    var crossedBusSimulation =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var crossedBusInitial =
        crossedBusSimulation
            .Snapshot()
            .Single();

    var crossedBusForwardX =
        Math.Sin(
            crossedBusInitial.HeadingRadians);
    var crossedBusForwardZ =
        Math.Cos(
            crossedBusInitial.HeadingRadians);
    var crossedBusRightX =
        crossedBusForwardZ;
    var crossedBusRightZ =
        -crossedBusForwardX;

    var crossedBusCenter =
        new WorldVector3(
            crossedBusInitial.Position.X +
                crossedBusForwardX *
                    12.0 +
                crossedBusRightX *
                    5.0,
            crossedBusInitial.Position.Y,
            crossedBusInitial.Position.Z +
                crossedBusForwardZ *
                    12.0 +
                crossedBusRightZ *
                    5.0);

    crossedBusSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            crossedBusCenter,
            crossedBusInitial.HeadingRadians +
                Math.PI /
                    2.0,
            0.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.35));

    crossedBusSimulation.Step(
        2.0);

    var crossedBusStopped =
        crossedBusSimulation
            .Snapshot()
            .Single();

    Require(
        crossedBusStopped.TraveledDistanceMeters <
            0.1 &&
        crossedBusStopped.SpeedMetersPerSecond <
            crossedBusInitial.SpeedMetersPerSecond,
        "Traffic AI ignored the oriented footprint of a player bus crossing the lane.");

    var playerOccupiedJunctionNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    9300,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [1],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [3],
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9301),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    1,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -5.0,
                            0.0,
                            7.5),
                        new WorldVector3(
                            5.0,
                            0.0,
                            7.5)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9301),
                new WorldTrafficPathSegment(
                    3,
                    9302,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>())
            ],
            4,
            0,
            0,
            0,
            2,
            0,
            2,
            0);

    var playerOccupiedJunctionSimulation =
        new WorldTrafficSimulation(
            playerOccupiedJunctionNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    playerOccupiedJunctionSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            new WorldVector3(
                0.0,
                0.0,
                7.5),
            Math.PI /
                2.0,
            0.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.35));

    playerOccupiedJunctionSimulation.Step(
        2.0);

    var playerOccupiedJunctionAgent =
        playerOccupiedJunctionSimulation
            .Snapshot()
            .Single();

    Require(
        playerOccupiedJunctionAgent.SegmentIndex ==
            0 &&
        playerOccupiedJunctionAgent.Position.Z <
            5.0 &&
        playerOccupiedJunctionAgent.AiBrakeLight,
        "AI traffic entered an OMSI junction while the player bus occupied the conflicting crossing path.");

    var gradeSeparatedJunctionSimulation =
        new WorldTrafficSimulation(
            playerOccupiedJunctionNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    gradeSeparatedJunctionSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            new WorldVector3(
                0.0,
                10.0,
                7.5),
            Math.PI /
                2.0,
            0.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.35));

    gradeSeparatedJunctionSimulation.Step(
        2.0);

    var gradeSeparatedJunctionAgent =
        gradeSeparatedJunctionSimulation
            .Snapshot()
            .Single();

    Require(
        gradeSeparatedJunctionAgent.SegmentIndex !=
            0 ||
        gradeSeparatedJunctionAgent.Position.Z >=
            5.0,
        "Grade-separated player vehicle incorrectly blocked the junction below.");

    var playerBlockedExitSimulation =
        new WorldTrafficSimulation(
            playerOccupiedJunctionNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    playerBlockedExitSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            new WorldVector3(
                0.0,
                0.0,
                19.5),
            0.0,
            0.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.35));

    playerBlockedExitSimulation.Step(
        2.0);

    var playerBlockedExitAgent =
        playerBlockedExitSimulation
            .Snapshot()
            .Single();

    Require(
        playerBlockedExitAgent.SegmentIndex ==
            0 &&
        playerBlockedExitAgent.Position.Z <
            5.0 &&
        playerBlockedExitAgent.AiBrakeLight,
        "AI traffic entered the junction while the player bus blocked the exit.");

    var staggeredTraffic =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                2,
            spawnExclusionCenter:
                new WorldVector3(
                    10000.0,
                    0.0,
                    10000.0),
            spawnExclusionRadiusMeters:
                0.0);

    var initialStaggeredAgent =
        staggeredTraffic
            .Snapshot()
            .Single();

    Require(
        initialStaggeredAgent.SpeedMetersPerSecond ==
            0.0,
        "Runtime traffic did not spawn the initial AI from rest.");

    staggeredTraffic.Step(
        0.5);

    var acceleratingStaggeredAgent =
        staggeredTraffic
            .Snapshot()
            .Single();

    Require(
        acceleratingStaggeredAgent.SpeedMetersPerSecond >
            initialStaggeredAgent.SpeedMetersPerSecond,
        "Runtime traffic did not accelerate progressively after spawn.");

    staggeredTraffic.Step(
        1.6);

    Require(
        staggeredTraffic.Snapshot().Count >=
            2,
        "Runtime traffic did not activate the next AI after its stagger delay.");

    var realisticCarTraffic =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.ovh",
                        syntheticAiCarPath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1,
            spawnExclusionCenter:
                new WorldVector3(
                    10000.0,
                    0.0,
                    10000.0),
            spawnExclusionRadiusMeters:
                0.0);

    var realisticBusTraffic =
        new WorldTrafficSimulation(
            trafficPaths,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "LineBuses",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1,
            spawnExclusionCenter:
                new WorldVector3(
                    10000.0,
                    0.0,
                    10000.0),
            spawnExclusionRadiusMeters:
                0.0);

    realisticCarTraffic.Step(
        1.0);
    realisticBusTraffic.Step(
        1.0);

    var realisticCarAgent =
        realisticCarTraffic
            .Snapshot()
            .Single();
    var realisticBusAgent =
        realisticBusTraffic
            .Snapshot()
            .Single();

    Require(
        realisticCarAgent.SpeedMetersPerSecond >
            realisticBusAgent.SpeedMetersPerSecond +
                0.25,
        "Realistic AI profiles did not keep the line bus acceleration gentler than normal road traffic.");

    var crossPathRetryNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    9100,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            100.0)
                    ],
                    [1],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    9101,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            100.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            200.0)
                    ],
                    Array.Empty<int>(),
                    [0]),
                new WorldTrafficPathSegment(
                    2,
                    9200,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            500.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            500.0,
                            0.0,
                            100.0)
                    ],
                    [3],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    3,
                    9201,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            500.0,
                            0.0,
                            100.0),
                        new WorldVector3(
                            500.0,
                            0.0,
                            200.0)
                    ],
                    Array.Empty<int>(),
                    [2])
            ],
            4,
            0,
            0,
            0,
            2,
            0,
            2,
            0);

    var crossPathRetrySimulation =
        new WorldTrafficSimulation(
            crossPathRetryNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1,
            spawnExclusionCenter:
                new WorldVector3(
                    0.0,
                    0.0,
                    50.0),
            spawnExclusionRadiusMeters:
                100.0);

    var crossPathRetryAgent =
        crossPathRetrySimulation
            .Snapshot()
            .Single();

    Require(
        crossPathRetryAgent.SegmentIndex ==
            2,
        "Runtime initial AI placement did not retry a different allowed path after the first path was excluded.");

    trafficSimulation.Step(
        20.0);

    var movedTraffic =
        trafficSimulation.Snapshot();

    Require(
        movedTraffic.Count ==
            1 &&
        movedTraffic[0].SegmentIndex ==
            secondRoadPath.Index &&
        movedTraffic[0].Position.Z >
            initialTraffic[0].Position.Z &&
        movedTraffic[0].SpeedMetersPerSecond <=
            10.0 /
            3.6 +
            0.0001 &&
        movedTraffic[0].TraveledDistanceMeters >
            0.0 &&
        double.IsFinite(
            movedTraffic[0].HeadingRadians),
        "Traffic simulation did not advance through the connected road path graph while honoring speedlimit.");

    var sceneryRoadPath =
        trafficPaths.Segments.Single(
            static segment =>
                segment.SceneryObjectId ==
                    1001 &&
                segment.Type ==
                    0);

    Require(
        sceneryRoadPath.SplineId ==
            -1 &&
        sceneryRoadPath.Points.Count >=
            2 &&
        sceneryRoadPath.ForwardConnections.Count ==
            0,
        "Crossing/scenery [path] was not represented as a world traffic segment.");

    var bridgeSplineAsset =
        new WorldSplineAsset(
            @"Splines\Synthetic\bridge.sli",
            null,
            true,
            Array.Empty<WorldSplineSurface>(),
            [
                new WorldSplinePath(
                    0,
                    0.0,
                    0.0,
                    2.5,
                    0)
            ]);

    var bridgeSplines =
        new[]
        {
            new WorldSplinePlacement(
                new WorldTileCoordinate(
                    0,
                    0),
                3001,
                -1,
                -1,
                @"Splines\Synthetic\bridge.sli",
                new WorldVector3(
                    0.0,
                    0.0,
                    0.0),
                0.0,
                10.0,
                0.0,
                0.0,
                0.0,
                false,
                0),
            new WorldSplinePlacement(
                new WorldTileCoordinate(
                    0,
                    0),
                3002,
                -1,
                -1,
                @"Splines\Synthetic\bridge.sli",
                new WorldVector3(
                    0.0,
                    0.0,
                    15.0),
                0.0,
                10.0,
                0.0,
                0.0,
                0.0,
                false,
                0)
        };

    var bridgeObject =
        new WorldObjectPlacement(
            new WorldTileCoordinate(
                0,
                0),
            4001,
            @"Sceneryobjects\Synthetic\bridge.sco",
            new WorldVector3(
                0.0,
                0.0,
                10.0),
            0.0,
            0.0,
            0.0,
            Array.Empty<string>(),
            0);

    var bridgeSceneryAsset =
        new WorldSceneryAsset(
            @"Sceneryobjects\Synthetic\bridge.sco",
            null,
            true,
            true,
            false,
            null,
            Array.Empty<WorldSceneryMeshAsset>(),
            null,
            [
                new WorldSceneryPath(
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    5.0,
                    0.0,
                    0.0,
                    0,
                    2.5,
                    0,
                    Array.Empty<string>())
            ]);

    var bridgeNetwork =
        WorldTrafficPathNetworkBuilder.Build(
            bridgeSplines,
            new Dictionary<string, WorldSplineAsset>(
                StringComparer.OrdinalIgnoreCase)
            {
                [@"Splines\Synthetic\bridge.sli"] =
                    bridgeSplineAsset
            },
            [
                bridgeObject
            ],
            new Dictionary<string, WorldSceneryAsset>(
                StringComparer.OrdinalIgnoreCase)
            {
                [@"Sceneryobjects\Synthetic\bridge.sco"] =
                    bridgeSceneryAsset
            },
            Array.Empty<WorldTile>());

    var bridgeFirst =
        bridgeNetwork.Segments.Single(
            static segment =>
                segment.SplineId ==
                    3001);

    var bridgeCrossing =
        bridgeNetwork.Segments.Single(
            static segment =>
                segment.SceneryObjectId ==
                    4001);

    var bridgeSecond =
        bridgeNetwork.Segments.Single(
            static segment =>
                segment.SplineId ==
                    3002);

    Require(
        bridgeFirst.ForwardConnections.Contains(
            bridgeCrossing.Index) &&
        bridgeCrossing.ForwardConnections.Contains(
            bridgeSecond.Index),
        "Crossing/scenery traffic path did not bridge adjacent spline endpoints.");

    var bridgeSimulation =
        new WorldTrafficSimulation(
            bridgeNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    bridgeSimulation.Step(
        3.0);

    var bridgedTraffic =
        bridgeSimulation.Snapshot();

    Require(
        bridgedTraffic.Count ==
            1 &&
        bridgedTraffic[0].SegmentIndex ==
            bridgeSecond.Index,
        "Traffic simulation did not traverse spline -> crossing/scenery -> spline.");

    var reverseSplineAsset =
        new WorldSplineAsset(
            @"Splines\Synthetic\reverse.sli",
            null,
            true,
            Array.Empty<WorldSplineSurface>(),
            [
                new WorldSplinePath(
                    0,
                    0.0,
                    0.0,
                    2.5,
                    1)
            ]);

    var reverseNetwork =
        WorldTrafficPathNetworkBuilder.Build(
            [
                new WorldSplinePlacement(
                    new WorldTileCoordinate(
                        0,
                        0),
                    5002,
                    5001,
                    -1,
                    @"Splines\Synthetic\reverse.sli",
                    new WorldVector3(
                        0.0,
                        0.0,
                        10.0),
                    0.0,
                    10.0,
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0),
                new WorldSplinePlacement(
                    new WorldTileCoordinate(
                        0,
                        0),
                    5001,
                    -1,
                    5002,
                    @"Splines\Synthetic\reverse.sli",
                    new WorldVector3(
                        0.0,
                        0.0,
                        0.0),
                    0.0,
                    10.0,
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0)
            ],
            new Dictionary<string, WorldSplineAsset>(
                StringComparer.OrdinalIgnoreCase)
            {
                [@"Splines\Synthetic\reverse.sli"] =
                    reverseSplineAsset
            });

    var reverseStartSegment =
        reverseNetwork.Segments.Single(
            static segment =>
                segment.SplineId ==
                    5002);

    var reverseNextSegment =
        reverseNetwork.Segments.Single(
            static segment =>
                segment.SplineId ==
                    5001);

    Require(
        reverseStartSegment.ReverseConnections.Contains(
            reverseNextSegment.Index),
        "Reverse-only OMSI path did not resolve its previous-spline connection.");

    var reverseSimulation =
        new WorldTrafficSimulation(
            reverseNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                1);

    var reverseInitial =
        reverseSimulation.Snapshot();

    reverseSimulation.Step(
        2.0);

    var reverseMoved =
        reverseSimulation.Snapshot();

    Require(
        reverseInitial.Count ==
            1 &&
        reverseInitial[0].SegmentIndex ==
            reverseStartSegment.Index &&
        reverseMoved.Count ==
            1 &&
        reverseMoved[0].SegmentIndex ==
            reverseNextSegment.Index &&
        reverseMoved[0].Position.Z <
            reverseInitial[0].Position.Z &&
        Math.Abs(
            Math.Abs(
                reverseMoved[0].HeadingRadians) -
            Math.PI) <
            0.001,
        "Reverse-only OMSI traffic path did not move End -> Start through ReverseConnections.");

    var blockedSpawnNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8100,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    BlockedUnscheduledGroupIndices:
                        new HashSet<int>
                        {
                            0
                        }),
                new WorldTrafficPathSegment(
                    1,
                    8101,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>())
            ],
            2,
            0,
            0,
            0,
            0,
            0,
            2,
            0);

    var normalGroupCatalog =
        new OmsiMapAiCatalog(
            [
                new OmsiAiVehicleDefinition(
                    "NormalCars",
                    @"Vehicles\Synthetic\traffic.bus",
                    syntheticAiVehiclePath,
                    1.0)
            ],
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            [
                new OmsiUnscheduledVehicleGroup(
                    0,
                    "NormalCars",
                    1),
                new OmsiUnscheduledVehicleGroup(
                    1,
                    "Taxi",
                    1)
            ]);

    var blockedSpawnSimulation =
        new WorldTrafficSimulation(
            blockedSpawnNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    var blockedSpawnAgent =
        blockedSpawnSimulation
            .Snapshot()
            .Single();

    Require(
        blockedSpawnAgent.GroupIndex ==
            0 &&
        blockedSpawnAgent.SegmentIndex ==
            1,
        "no_cars did not prevent the matching unscheduled group from spawning on a blocked path.");

    var groupRoutingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8200,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [
                        1,
                        2
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8201,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            -5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    BlockedUnscheduledGroupIndices:
                        new HashSet<int>
                        {
                            0
                        }),
                new WorldTrafficPathSegment(
                    2,
                    8202,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    BlockedUnscheduledGroupIndices:
                        new HashSet<int>
                        {
                            1
                        })
            ],
            3,
            0,
            0,
            0,
            1,
            0,
            2,
            0);

    var normalRoutingSimulation =
        new WorldTrafficSimulation(
            groupRoutingNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    var normalApproachAgent =
        normalRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        normalApproachAgent.AiBlinkerRight &&
        !normalApproachAgent.AiBlinkerLeft,
        "NormalCars AI did not signal right before the +X branch.");

    normalRoutingSimulation.Step(
        2.0);

    var normalRoutedAgent =
        normalRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        normalRoutedAgent.GroupIndex ==
            0 &&
        normalRoutedAgent.SegmentIndex ==
            2 &&
        normalRoutedAgent.Position.X >
            0.0,
        "no_cars did not exclude a blocked route for the NormalCars group.");

    var taxiVehiclePath =
        Path.Combine(
            contentRoot.RootPath,
            "Vehicles",
            "Synthetic",
            "taxi.bus");

    var taxiGroupCatalog =
        new OmsiMapAiCatalog(
            [
                new OmsiAiVehicleDefinition(
                    "Taxi",
                    @"Vehicles\Synthetic\taxi.bus",
                    taxiVehiclePath,
                    1.0)
            ],
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            [
                new OmsiUnscheduledVehicleGroup(
                    0,
                    "NormalCars",
                    1),
                new OmsiUnscheduledVehicleGroup(
                    1,
                    "Taxi",
                    1)
            ]);

    var taxiRoutingSimulation =
        new WorldTrafficSimulation(
            groupRoutingNetwork,
            taxiGroupCatalog,
            maximumAgents:
                1);

    var taxiApproachAgent =
        taxiRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        taxiApproachAgent.AiBlinkerLeft &&
        !taxiApproachAgent.AiBlinkerRight,
        "Taxi AI did not signal left before the -X branch.");

    taxiRoutingSimulation.Step(
        2.0);

    var taxiRoutedAgent =
        taxiRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        taxiRoutedAgent.GroupIndex ==
            1 &&
        taxiRoutedAgent.SegmentIndex ==
            1 &&
        taxiRoutedAgent.Position.X <
            0.0,
        "no_cars did not exclude a blocked route for the Taxi group.");

    var multiPathTurnNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8240,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [1],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            8.0)
                    ],
                    [2],
                    [0],
                    SceneryObjectId:
                        8241),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    1,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            8.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            13.0)
                    ],
                    Array.Empty<int>(),
                    [1],
                    SceneryObjectId:
                        8241)
            ],
            3,
            0,
            0,
            0,
            2,
            0,
            1,
            0);

    var multiPathTurnSimulation =
        new WorldTrafficSimulation(
            multiPathTurnNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    var multiPathTurnAgent =
        multiPathTurnSimulation
            .Snapshot()
            .Single();

    Require(
        multiPathTurnAgent.AiBlinkerRight &&
        !multiPathTurnAgent.AiBlinkerLeft,
        "AI did not signal before a turn distributed across multiple junction paths.");

    var curvedSteeringNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8250,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            2.0),
                        new WorldVector3(
                            3.0,
                            0.0,
                            8.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>())
            ],
            1,
            0,
            0,
            0,
            0,
            0,
            1,
            0);

    var curvedSteeringSimulation =
        new WorldTrafficSimulation(
            curvedSteeringNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    var curvedSteeringAgent =
        curvedSteeringSimulation
            .Snapshot()
            .Single();

    Require(
        curvedSteeringAgent.PathCurvaturePerMeter >
            0.01 &&
        double.IsFinite(
            curvedSteeringAgent.PathCurvaturePerMeter),
        "AI path curvature was not derived from the upcoming right-hand bend.");

    curvedSteeringSimulation.Step(
        0.5);

    var curvedBrakingAgent =
        curvedSteeringSimulation
            .Snapshot()
            .Single();

    Require(
        curvedBrakingAgent.SpeedMetersPerSecond <
            curvedSteeringAgent.SpeedMetersPerSecond &&
        curvedBrakingAgent.AiBrakeLight,
        "AI traffic did not brake progressively for upcoming road curvature.");

    var preImpactSpeed =
        curvedBrakingAgent.SpeedMetersPerSecond;

    curvedSteeringSimulation.ApplyCollisionResponse(
        curvedBrakingAgent.AgentIndex,
        40.0);

    var impactedTrafficAgent =
        curvedSteeringSimulation
            .Snapshot()
            .Single();

    Require(
        impactedTrafficAgent.SpeedMetersPerSecond <
            preImpactSpeed &&
        impactedTrafficAgent.AiBrakeLight,
        "AI collision response did not reduce speed and engage braking.");

    curvedSteeringSimulation.Step(
        0.1);

    var heldImpactAgent =
        curvedSteeringSimulation
            .Snapshot()
            .Single();

    Require(
        heldImpactAgent.SpeedMetersPerSecond <=
            impactedTrafficAgent.SpeedMetersPerSecond +
                0.0001 &&
        heldImpactAgent.AiBrakeLight,
        "AI collision hold allowed immediate re-acceleration after impact.");

    var defaultDisabledCatalog =
        new OmsiMapAiCatalog(
            [
                new OmsiAiVehicleDefinition(
                    "Service",
                    @"Vehicles\Synthetic\traffic.bus",
                    syntheticAiVehiclePath,
                    1.0)
            ],
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            [
                new OmsiUnscheduledVehicleGroup(
                    0,
                    "NormalCars",
                    1),
                new OmsiUnscheduledVehicleGroup(
                    1,
                    "Taxi",
                    1),
                new OmsiUnscheduledVehicleGroup(
                    2,
                    "Service",
                    0)
            ]);

    var defaultDisabledSpawnNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8300,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8301,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            10.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [2] =
                                1.0
                        })
            ],
            2,
            0,
            0,
            0,
            0,
            0,
            2,
            0);

    var defaultDisabledSpawnSimulation =
        new WorldTrafficSimulation(
            defaultDisabledSpawnNetwork,
            defaultDisabledCatalog,
            maximumAgents:
                1);

    var defaultDisabledSpawnAgent =
        defaultDisabledSpawnSimulation
            .Snapshot()
            .Single();

    Require(
        defaultDisabledSpawnAgent.GroupIndex ==
            2 &&
        defaultDisabledSpawnAgent.SegmentIndex ==
            1,
        "Default-disabled OMSI unscheduled group spawned on a path without explicit trafficdensity.");

    var defaultDisabledRoutingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8400,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [
                        1,
                        2
                    ],
                    Array.Empty<int>(),
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [2] =
                                1.0
                        }),
                new WorldTrafficPathSegment(
                    1,
                    8401,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            -5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    2,
                    8402,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [2] =
                                0.5
                        })
            ],
            3,
            0,
            0,
            0,
            1,
            0,
            2,
            0);

    var defaultDisabledRoutingSimulation =
        new WorldTrafficSimulation(
            defaultDisabledRoutingNetwork,
            defaultDisabledCatalog,
            maximumAgents:
                1);

    defaultDisabledRoutingSimulation.Step(
        2.0);

    var defaultDisabledRoutedAgent =
        defaultDisabledRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        defaultDisabledRoutedAgent.GroupIndex ==
            2 &&
        defaultDisabledRoutedAgent.SegmentIndex ==
            2 &&
        defaultDisabledRoutedAgent.Position.X >
            0.0,
        "Default-disabled OMSI unscheduled group entered a path without explicit trafficdensity.");

    var crossingReservationNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8500,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        2
                    ],
                    Array.Empty<int>(),
                    TrafficPriority:
                        64),
                new WorldTrafficPathSegment(
                    1,
                    8501,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -5.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        3
                    ],
                    Array.Empty<int>(),
                    TrafficPriority:
                        192),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [
                        4
                    ],
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9001),
                new WorldTrafficPathSegment(
                    3,
                    -1,
                    1,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            5.0)
                    ],
                    [
                        5
                    ],
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9001),
                new WorldTrafficPathSegment(
                    4,
                    8504,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    5,
                    8505,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            5.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            15.0,
                            0.0,
                            5.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>())
            ],
            6,
            0,
            0,
            0,
            4,
            0,
            2,
            0);

    var crossingReservationSimulation =
        new WorldTrafficSimulation(
            crossingReservationNetwork,
            normalGroupCatalog,
            maximumAgents:
                2);

    crossingReservationSimulation.Step(
        1.0);

    var crossingReservationFirst =
        crossingReservationSimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        crossingReservationFirst.Length ==
            2 &&
        crossingReservationFirst[0].SegmentIndex ==
            0 &&
        crossingReservationFirst[0].SpeedMetersPerSecond ==
            0.0 &&
        crossingReservationFirst[1].SegmentIndex ==
            3,
        "OMSI priority did not let the higher-priority AI agent reserve the crossing first.");

    crossingReservationSimulation.Step(
        2.0);

    var crossingReservationReleased =
        crossingReservationSimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        crossingReservationReleased[0].SegmentIndex is
            2 or 4 &&
        crossingReservationReleased[1].SegmentIndex ==
            5,
        "Crossing reservation did not release the lower-priority AI only after the higher-priority agent left.");

    var stalledPrioritySimulation =
        new WorldTrafficSimulation(
            crossingReservationNetwork,
            normalGroupCatalog,
            maximumAgents:
                2);

    stalledPrioritySimulation.ApplyCollisionResponse(
        1,
        80.0);

    stalledPrioritySimulation.ApplyCollisionResponse(
        1,
        80.0);

    stalledPrioritySimulation.Step(
        0.75);

    var stalledPriorityAgents =
        stalledPrioritySimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        stalledPriorityAgents.Length ==
            2 &&
        stalledPriorityAgents[0].SegmentIndex ==
            2 &&
        stalledPriorityAgents[1].SegmentIndex ==
            1,
        "A stalled higher-priority AI approach incorrectly reserved the crossing.");

    var parallelCrossingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8600,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -10.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            -5.0,
                            0.0,
                            0.0)
                    ],
                    [
                        2
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8601,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -10.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            -5.0,
                            0.0,
                            5.0)
                    ],
                    [
                        3
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -5.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            0.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9002),
                new WorldTrafficPathSegment(
                    3,
                    -1,
                    1,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            -5.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            5.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9002)
            ],
            4,
            0,
            0,
            0,
            2,
            0,
            2,
            0);

    var parallelCrossingSimulation =
        new WorldTrafficSimulation(
            parallelCrossingNetwork,
            normalGroupCatalog,
            maximumAgents:
                2);

    parallelCrossingSimulation.Step(
        1.0);

    var parallelCrossingAgents =
        parallelCrossingSimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        parallelCrossingAgents.Length ==
            2 &&
        parallelCrossingAgents[0].SegmentIndex ==
            2 &&
        parallelCrossingAgents[1].SegmentIndex ==
            3 &&
        parallelCrossingAgents.All(
            static agent =>
                agent.SpeedMetersPerSecond >
                0.0),
        "Non-conflicting paths inside the same OMSI crossing were unnecessarily interlocked.");

    var rightBeforeLeftNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8700,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            -5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0)
                    ],
                    [
                        2
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8701,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            5.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0)
                    ],
                    [
                        3
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9003),
                new WorldTrafficPathSegment(
                    3,
                    -1,
                    1,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            -10.0,
                            0.0,
                            0.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        9003)
            ],
            4,
            0,
            0,
            0,
            2,
            0,
            2,
            0);

    var rightBeforeLeftSimulation =
        new WorldTrafficSimulation(
            rightBeforeLeftNetwork,
            normalGroupCatalog,
            maximumAgents:
                2);

    rightBeforeLeftSimulation.Step(
        1.0);

    var rightBeforeLeftAgents =
        rightBeforeLeftSimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        rightBeforeLeftAgents.Length ==
            2 &&
        rightBeforeLeftAgents[0].SegmentIndex ==
            0 &&
        rightBeforeLeftAgents[1].SegmentIndex ==
            3,
        "Equal-priority OMSI crossing traffic did not yield to the vehicle approaching from the right.");

    var stalledRightPrioritySimulation =
        new WorldTrafficSimulation(
            rightBeforeLeftNetwork,
            normalGroupCatalog,
            maximumAgents:
                2);

    stalledRightPrioritySimulation.ApplyCollisionResponse(
        1,
        80.0);

    stalledRightPrioritySimulation.ApplyCollisionResponse(
        1,
        80.0);

    stalledRightPrioritySimulation.Step(
        0.75);

    var stalledRightPriorityAgents =
        stalledRightPrioritySimulation
            .Snapshot()
            .OrderBy(
                static agent =>
                    agent.AgentIndex)
            .ToArray();

    Require(
        stalledRightPriorityAgents.Length ==
            2 &&
        stalledRightPriorityAgents[0].SegmentIndex ==
            2 &&
        stalledRightPriorityAgents[1].SegmentIndex ==
            1,
        "A stalled equal-priority approach from the right incorrectly reserved the crossing.");

    var signalProgram =
        new WorldTrafficSignalProgram(
            "Main",
            8.0,
            [
                new WorldTrafficSignalPhase(
                    0,
                    2.0),
                new WorldTrafficSignalPhase(
                    6,
                    6.0)
            ],
            12.0);

    var signalNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8300,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        1
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            15.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        8301,
                    TrafficSignal:
                        signalProgram)
            ],
            2,
            0,
            0,
            0,
            1,
            0,
            1,
            0);

    var signalSimulation =
        new WorldTrafficSimulation(
            signalNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    var initialSignalAgent =
        signalSimulation
            .Snapshot()
            .Single();

    signalSimulation.Step(
        0.5);

    var brakingSignalAgent =
        signalSimulation
            .Snapshot()
            .Single();

    Require(
        brakingSignalAgent.SegmentIndex ==
            0 &&
        brakingSignalAgent.Position.Z >
            initialSignalAgent.Position.Z &&
        brakingSignalAgent.Position.Z <
            5.0 &&
        brakingSignalAgent.SpeedMetersPerSecond >
            0.0 &&
        brakingSignalAgent.SpeedMetersPerSecond <
            initialSignalAgent.SpeedMetersPerSecond &&
        brakingSignalAgent.AiBrakeLight,
        "Traffic agent did not brake progressively before a red OMSI traffic-light path.");

    signalSimulation.Step(
        1.0);

    var redSignalAgent =
        signalSimulation
            .Snapshot()
            .Single();

    Require(
        redSignalAgent.SegmentIndex ==
            0 &&
        redSignalAgent.Position.Z <
            5.0 &&
        redSignalAgent.SpeedMetersPerSecond <
            0.5 &&
        redSignalAgent.AiBrakeLight &&
        redSignalAgent.TraveledDistanceMeters >=
            brakingSignalAgent.TraveledDistanceMeters,
        "Traffic agent did not stop smoothly before the red OMSI traffic-light path.");

    signalSimulation.Step(
        1.5);

    var greenSignalAgent =
        signalSimulation
            .Snapshot()
            .Single();

    Require(
        greenSignalAgent.SegmentIndex ==
            1 &&
        greenSignalAgent.Position.Z >
            redSignalAgent.Position.Z &&
        greenSignalAgent.TraveledDistanceMeters >
            redSignalAgent.TraveledDistanceMeters &&
        !greenSignalAgent.AiBrakeLight,
        "Traffic agent did not enter the OMSI traffic-light path during the green phase.");

    var requestJump =
        new WorldTrafficLightJump(
            1,
            2.0,
            JumpIfNoApproach:
                true,
            TargetTimeSeconds:
                0.0);

    var requestMainSignal =
        new WorldTrafficSignalProgram(
            "Main",
            8.0,
            [
                new WorldTrafficSignalPhase(
                    0,
                    2.0),
                new WorldTrafficSignalPhase(
                    6,
                    6.0)
            ],
            12.0,
            SignalIndex:
                0,
            Jumps:
                [
                    requestJump
                ]);

    var requestProbeSignal =
        new WorldTrafficSignalProgram(
            "BusLoop",
            8.0,
            [
                new WorldTrafficSignalPhase(
                    0,
                    8.0)
            ],
            12.0,
            SignalIndex:
                1,
            Jumps:
                [
                    requestJump
                ]);

    var requestJumpNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8400,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        1
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            15.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        8401,
                    TrafficSignal:
                        requestMainSignal),
                new WorldTrafficPathSegment(
                    2,
                    -1,
                    1,
                    1,
                    0,
                    2.0,
                    [
                        new WorldVector3(
                            5.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            10.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        8401,
                    TrafficSignal:
                        requestProbeSignal)
            ],
            3,
            1,
            0,
            0,
            1,
            0,
            1,
            0);

    var requestJumpSimulation =
        new WorldTrafficSimulation(
            requestJumpNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    requestJumpSimulation.Step(
        3.0);

    var requestJumpAgent =
        requestJumpSimulation
            .Snapshot()
            .Single();

    Require(
        requestJumpAgent.SegmentIndex ==
            0 &&
        requestJumpAgent.Position.Z <
            5.0 &&
        requestJumpAgent.AiBrakeLight,
        "OMSI traffic_light_jump did not keep the group before green when the monitored signal had no approach.");

    var requestJumpSignalState =
        requestJumpSimulation
            .SnapshotTrafficSignals()
            .Single(
                static state =>
                    state.SegmentIndex ==
                    1);

    Require(
        requestJumpSignalState.Phase ==
            0 &&
        requestJumpSignalState.PositionSeconds <
            2.0,
        "Dynamic signal snapshot did not expose the traffic_light_jump-adjusted group clock.");

    var requestStop =
        new WorldTrafficLightStop(
            0,
            1.0,
            StopIfNoApproach:
                false);

    var requestStopSignal =
        new WorldTrafficSignalProgram(
            "Main",
            8.0,
            [
                new WorldTrafficSignalPhase(
                    0,
                    2.0),
                new WorldTrafficSignalPhase(
                    6,
                    6.0)
            ],
            12.0,
            SignalIndex:
                0,
            Stops:
                [
                    requestStop
                ]);

    var requestStopNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8500,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        1
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            15.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    SceneryObjectId:
                        8501,
                    TrafficSignal:
                        requestStopSignal)
            ],
            2,
            0,
            0,
            0,
            1,
            0,
            1,
            0);

    var requestStopSimulation =
        new WorldTrafficSimulation(
            requestStopNetwork,
            normalGroupCatalog,
            maximumAgents:
                1);

    requestStopSimulation.Step(
        3.0);

    var requestStopAgent =
        requestStopSimulation
            .Snapshot()
            .Single();

    Require(
        requestStopAgent.SegmentIndex ==
            0 &&
        requestStopAgent.Position.Z <
            5.0 &&
        requestStopAgent.AiBrakeLight,
        "OMSI traffic_light_stop did not hold the group while the monitored signal had an active approach.");

    var requestStopSignalState =
        requestStopSimulation
            .SnapshotTrafficSignals()
            .Single();

    Require(
        requestStopSignalState.Phase ==
            0 &&
        Math.Abs(
            requestStopSignalState.PositionSeconds -
            1.0) <
            0.001 &&
        requestStopSignalState.Held,
        "Dynamic signal snapshot did not expose the traffic_light_stop-held group clock.");

    var playerRequestSimulation =
        new WorldTrafficSimulation(
            requestStopNetwork,
            normalGroupCatalog,
            maximumAgents:
                0);

    playerRequestSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            new WorldVector3(
                0.0,
                0.0,
                1.0),
            HeadingRadians:
                0.0,
            SpeedMetersPerSecond:
                5.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.3));

    playerRequestSimulation.Step(
        3.0);

    var playerRequestSignalState =
        playerRequestSimulation
            .SnapshotTrafficSignals()
            .Single();

    Require(
        playerRequestSignalState.Phase ==
            0 &&
        Math.Abs(
            playerRequestSignalState.PositionSeconds -
            1.0) <
            0.001 &&
        playerRequestSignalState.Held,
        "Player-only OMSI signal approach did not hold traffic_light_stop with AI traffic disabled.");

    var ambiguousPlayerRequestNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8600,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0)
                    ],
                    [
                        1,
                        2
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    -1,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            15.0)
                    ],
                    Array.Empty<int>(),
                    [
                        0
                    ],
                    SceneryObjectId:
                        8601,
                    TrafficSignal:
                        requestStopSignal),
                new WorldTrafficPathSegment(
                    2,
                    8602,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            5.0),
                        new WorldVector3(
                            8.0,
                            0.0,
                            12.0)
                    ],
                    Array.Empty<int>(),
                    [
                        0
                    ])
            ],
            3,
            0,
            0,
            0,
            1,
            0,
            2,
            0);

    var ambiguousPlayerRequestSimulation =
        new WorldTrafficSimulation(
            ambiguousPlayerRequestNetwork,
            normalGroupCatalog,
            maximumAgents:
                0);

    ambiguousPlayerRequestSimulation.SetExternalObstacle(
        new WorldTrafficObstacleState(
            new WorldVector3(
                0.0,
                0.0,
                1.0),
            HeadingRadians:
                0.0,
            SpeedMetersPerSecond:
                5.0,
            HalfLengthMeters:
                6.0,
            HalfWidthMeters:
                1.3));

    ambiguousPlayerRequestSimulation.Step(
        3.0);

    var ambiguousPlayerSignalState =
        ambiguousPlayerRequestSimulation
            .SnapshotTrafficSignals()
            .Single();

    Require(
        ambiguousPlayerSignalState.Phase ==
            6 &&
        ambiguousPlayerSignalState.PositionSeconds >
            2.5 &&
        !ambiguousPlayerSignalState.Held,
        "Player signal approach incorrectly guessed a route across an ambiguous branch.");

    var densityRoutingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    8000,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0)
                    ],
                    [
                        1,
                        2
                    ],
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    8001,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            -5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] =
                                0.0,
                            [1] =
                                4.0
                        }),
                new WorldTrafficPathSegment(
                    2,
                    8002,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            10.0),
                        new WorldVector3(
                            5.0,
                            0.0,
                            20.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>(),
                    TrafficDensityWeights:
                        new Dictionary<int, double>
                        {
                            [0] =
                                3.0,
                            [1] =
                                0.0
                        })
            ],
            3,
            0,
            0,
            0,
            1,
            0,
            2,
            0);

    var densityRoutingSimulation =
        new WorldTrafficSimulation(
            densityRoutingNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                [
                    new OmsiUnscheduledVehicleGroup(
                        0,
                        "NormalCars",
                        1),
                    new OmsiUnscheduledVehicleGroup(
                        1,
                        "Taxi",
                        1)
                ]),
            maximumAgents:
                1);

    densityRoutingSimulation.Step(
        2.0);

    var densityRoutedAgent =
        densityRoutingSimulation
            .Snapshot()
            .Single();

    Require(
        densityRoutedAgent.GroupIndex ==
            0 &&
        densityRoutedAgent.GroupName ==
            "NormalCars" &&
        densityRoutedAgent.SegmentIndex ==
            2 &&
        densityRoutedAgent.Position.X >
            0.0,
        "OMSI trafficdensity=0 path was not excluded for the agent's unscheduled vehicle group.");

    var followingNetwork =
        new WorldTrafficPathNetwork(
            [
                new WorldTrafficPathSegment(
                    0,
                    7002,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            40.0)
                    ],
                    Array.Empty<int>(),
                    Array.Empty<int>()),
                new WorldTrafficPathSegment(
                    1,
                    7001,
                    0,
                    0,
                    0,
                    2.5,
                    [
                        new WorldVector3(
                            0.0,
                            0.0,
                            0.0),
                        new WorldVector3(
                            0.0,
                            0.0,
                            20.0)
                    ],
                    [
                        0
                    ],
                    Array.Empty<int>())
            ],
            2,
            0,
            0,
            0,
            1,
            0,
            1,
            0);

    var followingSimulation =
        new WorldTrafficSimulation(
            followingNetwork,
            new OmsiMapAiCatalog(
                [
                    new OmsiAiVehicleDefinition(
                        "NormalCars",
                        @"Vehicles\Synthetic\traffic.bus",
                        syntheticAiVehiclePath,
                        1.0)
                ],
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>(),
                Array.Empty<OmsiAiFileReference>()),
            maximumAgents:
                2);

    var followingInitial =
        followingSimulation.Snapshot();

    followingSimulation.Step(
        8.0);

    var followingMoved =
        followingSimulation.Snapshot();

    var frontAgent =
        followingMoved.Single(
            static agent =>
                agent.AgentIndex ==
                    0);

    var rearAgent =
        followingMoved.Single(
            static agent =>
                agent.AgentIndex ==
                    1);

    var centerSeparation =
        frontAgent.Position.Z -
        rearAgent.Position.Z;

    Require(
        followingInitial.Count ==
            2 &&
        followingMoved.Count ==
            2 &&
        rearAgent.Position.Z <
            frontAgent.Position.Z &&
        centerSeparation >=
            5.9 &&
        rearAgent.SpeedMetersPerSecond <
            30.0 /
            3.6,
        "Traffic following control did not preserve separation or slow the faster rear agent.");

    var firstRoadStart =
        firstRoadPath.Points[0];

    Require(
        Math.Abs(
            firstRoadStart.X -
            3.9393398) <
            0.01 &&
        Math.Abs(
            firstRoadStart.Y -
            7.1) <
            0.01 &&
        Math.Abs(
            firstRoadStart.Z -
            7.0606602) <
            0.01,
        "Traffic path world transform does not match the spline frame/lateral offset.");

    Require(
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\object.sco",
            out var editorOnlyAsset) &&
        editorOnlyAsset.OnlyEditor &&
        !editorOnlyAsset.IsRenderable,
        "[onlyeditor] scenery must remain in the world but be hidden in game rendering.");

    var verifiedEditorOnlyAsset =
        editorOnlyAsset ??
        throw new InvalidOperationException(
            "Synthetic editor-only scenery asset was not loaded.");

    Require(
        verifiedEditorOnlyAsset.Paths.Count == 1,
        "Crossing/scenery [path] metadata was not preserved.");

    Require(
        verifiedEditorOnlyAsset.ScriptManifest is
            { RegisteredFileCount: 2, MissingFileCount: 0 } &&
        verifiedEditorOnlyAsset.Meshes is
            [{ VisibilityConditions.Count: 1 }] &&
        verifiedEditorOnlyAsset.Meshes[0].VisibilityConditions![0].VariableName ==
            "signal_lamp" &&
        Math.Abs(
            verifiedEditorOnlyAsset.Meshes[0].VisibilityConditions![0].Value -
            1.0) <
            0.0001,
        "World scenery asset did not retain OMSI signal script and [visible] directive.");

    var crossingPath =
        verifiedEditorOnlyAsset.Paths[0];

    Require(
        Math.Abs(crossingPath.X - 1.5) < 0.0001 &&
        Math.Abs(crossingPath.Z - 0.1) < 0.0001 &&
        Math.Abs(crossingPath.HeadingDegrees + 90.0) < 0.0001 &&
        Math.Abs(crossingPath.LengthMeters - 3.0) < 0.0001 &&
        crossingPath.Type == 0 &&
        Math.Abs(crossingPath.WidthMeters - 2.5) < 0.0001 &&
        crossingPath.Direction == 0 &&
        crossingPath.ExtraValues.Count == 1 &&
        crossingPath.ExtraValues[0] == "1" &&
        crossingPath.TrafficLightIndex ==
            0,
        "Crossing/scenery [path] field mapping is incorrect.");

    Require(
        Math.Abs(
            (verifiedEditorOnlyAsset.TrafficLightCycleSeconds ??
             0.0) -
            8.0) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLights is
            { Count: 1 } &&
        verifiedEditorOnlyAsset.TrafficLights[0].Name ==
            "Main" &&
        verifiedEditorOnlyAsset.TrafficLights[0].Phases.Count ==
            2 &&
        verifiedEditorOnlyAsset.TrafficLights[0].Phases[0].Phase ==
            0 &&
        Math.Abs(
            verifiedEditorOnlyAsset.TrafficLights[0].Phases[0].DurationSeconds -
            2.0) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLights[0].Phases[1].Phase ==
            6 &&
        Math.Abs(
            verifiedEditorOnlyAsset.TrafficLights[0].ApproachDistanceMeters -
            12.0) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLightJumps is
            { Count: 1 } &&
        verifiedEditorOnlyAsset.TrafficLightJumps[0].CheckTrafficLightIndex ==
            0 &&
        Math.Abs(
            verifiedEditorOnlyAsset.TrafficLightJumps[0].TriggerTimeSeconds -
            2.0) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLightJumps[0].JumpIfNoApproach &&
        Math.Abs(
            verifiedEditorOnlyAsset.TrafficLightJumps[0].TargetTimeSeconds) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLightStops is
            { Count: 1 } &&
        verifiedEditorOnlyAsset.TrafficLightStops[0].CheckTrafficLightIndex ==
            0 &&
        Math.Abs(
            verifiedEditorOnlyAsset.TrafficLightStops[0].TriggerTimeSeconds -
            1.0) <
            0.0001 &&
        verifiedEditorOnlyAsset.TrafficLightStops[0].StopIfNoApproach,
        "Crossing traffic-light cycle/phases/jumps/stops were not preserved.");

    Require(
        world.SceneryAssets.TryGetValue(
            @"Sceneryobjects\Synthetic\tree.sco",
            out var editorOnlyTreeAsset) &&
        editorOnlyTreeAsset.OnlyEditor &&
        editorOnlyTreeAsset.Tree is not null &&
        editorOnlyTreeAsset.IsRenderable,
        "[tree] scenery must remain runtime-renderable even when its helper mesh is [onlyeditor].");

    Require(
        world.Dependencies.RequiredCount == 5,
        "Expected five primary dependencies.");
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

    Require(
        OpenOmsiLanProtocol.NormalizeVehiclePath(
            @"Vehicles\Synthetic\Synthetic.bus") ==
        "Vehicles/Synthetic/Synthetic.bus" &&
        OpenOmsiLanProtocol.NormalizeVehiclePath(
            @"..\Synthetic.bus") is null &&
        OpenOmsiLanProtocol.NormalizeVehiclePath(
            @"C:\OMSI\Vehicles\Synthetic.bus") is null,
        "openOMSI LAN vehicle path validation is unsafe or incompatible.");

    var lanPose =
        new OpenOmsiLanPose
        {
            Id =
                2,
            Name =
                "Smoke Driver",
            VehiclePath =
                "Vehicles/Synthetic/Synthetic.bus",
            Paint =
                "Test",
            Line =
                "76",
            Destination =
                "Bahnhof",
            Tour =
                "76/1",
            DisplayTexts =
                ["76", "Bahnhof"],
            LengthMeters =
                18.0f,
            WidthMeters =
                2.5f,
            BoxOffsetMeters =
                -1.5f,
            SyncTableHash =
                0x1234ABCD,
            X =
                123.45,
            Y =
                -456.78,
            Z =
                12.34,
            HeadingDegrees =
                271.25f,
            PitchDegrees =
                1.25f,
            BankDegrees =
                -0.75f,
            SpeedKph =
                43.2f,
            SteeringDegrees =
                -12.4f,
            Flags =
                OpenOmsiLanProtocol.FlagVehicle |
                OpenOmsiLanProtocol.FlagEngine |
                OpenOmsiLanProtocol.FlagElectrics |
                OpenOmsiLanProtocol.FlagBrake,
            HeadLights =
                2,
            InteriorLights =
                1,
            Blinker =
                1,
            Rpm =
                1375.0f,
            Throttle =
                0.65f,
            Brake =
                0.2f,
            Passengers =
                37,
            Doors =
                [0.0f, 0.5f, 1.0f],
            Suspension =
                [0.01f, -0.015f, 0.02f, -0.025f],
            RearSections =
                [
                    new OpenOmsiLanPartPose(
                        120.0,
                        -462.0,
                        12.30,
                        269.5f)
                ],
            Lamps =
                [1.0f, 0.5f, 0.0f],
            Switches =
                [1.0f, -2.0f],
            Values =
                Enumerable
                    .Range(
                        0,
                        OpenOmsiLanProtocol.MaximumValues)
                    .Select(
                        static value =>
                            (float)value)
                    .ToList()
        };

    var lanInfo =
        OpenOmsiLanProtocol.EncodeInfo(
            lanPose);

    Require(
        OpenOmsiLanProtocol.TryDecodeInfo(
            lanInfo,
            out var decodedLanInfo) &&
        decodedLanInfo.Id ==
            lanPose.Id &&
        decodedLanInfo.VehiclePath ==
            lanPose.VehiclePath &&
        decodedLanInfo.Line ==
            lanPose.Line &&
        decodedLanInfo.Destination ==
            lanPose.Destination &&
        decodedLanInfo.Tour ==
            lanPose.Tour &&
        decodedLanInfo.DisplayTexts.SequenceEqual(
            lanPose.DisplayTexts),
        "openOMSI LAN INFO round-trip failed.");

    var lanState =
        OpenOmsiLanStateCodec.Encode(
            lanPose,
            65530);

    Require(
        lanState.Length <=
            OpenOmsiLanProtocol.MaximumStateBytes &&
        OpenOmsiLanStateCodec.TryDecode(
            lanState,
            out var decodedSequence,
            out var decodedLanState) &&
        decodedSequence ==
            65530 &&
        decodedLanState.Id ==
            lanPose.Id &&
        Math.Abs(
            decodedLanState.X -
            lanPose.X) <
            0.011 &&
        Math.Abs(
            decodedLanState.Y -
            lanPose.Y) <
            0.011 &&
        Math.Abs(
            decodedLanState.Z -
            lanPose.Z) <
            0.011 &&
        Math.Abs(
            decodedLanState.SpeedKph -
            lanPose.SpeedKph) <
            0.051 &&
        decodedLanState.Doors.Count ==
            lanPose.Doors.Count &&
        decodedLanState.RearSections.Count ==
            1 &&
        OpenOmsiLanProtocol.ProtocolVersion ==
            6 &&
        decodedLanState.Values.Count ==
            OpenOmsiLanProtocol.MaximumValues &&
        decodedLanState.Values[^1] ==
            OpenOmsiLanProtocol.MaximumValues -
                1 &&
        OpenOmsiLanProtocol.SequenceIsNewer(
            2,
            ushort.MaxValue),
        "openOMSI LAN binary STATE round-trip/sequence wrap failed.");

    var opsMessage =
        new OpenOmsiLanOperationalMessage(
            2,
            "Smoke Driver",
            "Empresa Teste",
            "0042",
            "FLEETLINK",
            "CHAT",
            "Central, iniciando viagem.",
            string.Empty,
            DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds());

    var encodedOps =
        OpenOmsiLanOperationalCodec.Encode(
            opsMessage);

    Require(
        OpenOmsiLanOperationalCodec.TryDecode(
            encodedOps,
            out var decodedOps) &&
        decodedOps.SenderId ==
            opsMessage.SenderId &&
        decodedOps.CompanyName ==
            opsMessage.CompanyName &&
        decodedOps.Module ==
            "FLEETLINK" &&
        decodedOps.Kind ==
            "CHAT" &&
        decodedOps.Text ==
            opsMessage.Text,
        "Runtime operational LAN extension round-trip failed.");

    var voicePcm =
        new byte[]
        {
            0x00,
            0x01,
            0xFE,
            0x7F,
            0x10,
            0x80,
            0x34,
            0x12
        };

    var encodedVoice =
        OpenOmsiLanVoiceCodec.Encode(
            2,
            ushort.MaxValue,
            voicePcm);

    Require(
        OpenOmsiLanVoiceCodec.TryDecode(
            encodedVoice,
            out var decodedVoice) &&
        decodedVoice.SenderId ==
            2 &&
        decodedVoice.Sequence ==
            ushort.MaxValue &&
        decodedVoice.Pcm16Mono8Khz
            .SequenceEqual(
                voicePcm),
        "CommsLink voice frame codec round-trip failed.");

    var malformedVoice =
        encodedVoice.ToArray();

    malformedVoice[10] =
        0xFF;
    malformedVoice[11] =
        0x7F;

    Require(
        !OpenOmsiLanVoiceCodec.TryDecode(
            malformedVoice,
            out _),
        "CommsLink voice frame codec accepted an invalid payload length.");

    var walkerPose =
        new OpenOmsiLanPose
        {
            Id =
                9,
            Name =
                "Walker",
            FigurePath =
                "Humans/Man01.hum",
            Flags =
                0,
            Walker =
                new OpenOmsiLanWalker(
                    123.45,
                    678.90,
                    12.34,
                    205.0f,
                    1.45f,
                    210.0f,
                    false,
                    null)
        };

    var expectedWalker =
        walkerPose.Walker!;

    var walkerState =
        OpenOmsiLanStateCodec.Encode(
            walkerPose,
            44);

    Require(
        OpenOmsiLanStateCodec.TryDecode(
            walkerState,
            out var decodedWalkerSequence,
            out var decodedWalkerPose) &&
        decodedWalkerSequence ==
            44 &&
        !decodedWalkerPose.HasVehicle &&
        decodedWalkerPose.Walker is
            { } decodedWalker &&
        Math.Abs(
            decodedWalker.X -
            expectedWalker.X) <
            0.011 &&
        Math.Abs(
            decodedWalker.Y -
            expectedWalker.Y) <
            0.011 &&
        Math.Abs(
            decodedWalker.Z -
            expectedWalker.Z) <
            0.011 &&
        Math.Abs(
            decodedWalker.SpeedMetersPerSecond -
            expectedWalker.SpeedMetersPerSecond) <
            0.02f &&
        Math.Abs(
            decodedWalker.CourseDegrees -
            expectedWalker.CourseDegrees) <
            0.2f,
        "openOMSI walker STATE round-trip failed.");

    var sessionCode =
        new OpenOmsiLanSessionCode(
            OpenOmsiLanProtocol.ProtocolVersion,
            [
                System.Net.IPAddress.Parse(
                    "192.168.10.25"),
                System.Net.IPAddress.Parse(
                    "10.20.30.40")
            ],
            27015,
            0x1234ABCDEF12UL);

    var sessionCodeText =
        sessionCode.Encode();

    var sessionCodeDecoded =
        OpenOmsiLanSessionCode.TryDecode(
            sessionCodeText,
            out var decodedSessionCode,
            out var sessionCodeError);

    var sessionCodeAddressesMatch =
        sessionCodeDecoded &&
        decodedSessionCode.Addresses
            .Select(
                static address =>
                    address.ToString())
            .SequenceEqual(
                sessionCode.Addresses
                    .Select(
                        static address =>
                            address.ToString()),
                StringComparer.Ordinal);

    Require(
        sessionCodeText.Equals(
            "OMSI-YVD3-K2EX-GEG9-EWBY-6RVB-77NV-CBSX-XS2A",
            StringComparison.Ordinal) &&
        sessionCodeDecoded &&
        decodedSessionCode.Protocol ==
            sessionCode.Protocol &&
        decodedSessionCode.Port ==
            sessionCode.Port &&
        decodedSessionCode.SessionId ==
            sessionCode.SessionId &&
        sessionCodeAddressesMatch,
        $"openOMSI LAN session-code round-trip failed: code={sessionCodeText}; decoded={sessionCodeDecoded}; error={sessionCodeError ?? "-"}; protocol={decodedSessionCode.Protocol}/{sessionCode.Protocol}; port={decodedSessionCode.Port}/{sessionCode.Port}; session={decodedSessionCode.SessionId:X12}/{sessionCode.SessionId:X12}; ips={string.Join(",", decodedSessionCode.Addresses)} / {string.Join(",", sessionCode.Addresses)}.");

    var peopleFrame =
        new OpenOmsiLanWorldPeopleFrame(
            88,
            654321,
            [
                new OpenOmsiLanWorldPersonState(
                    21,
                    OpenOmsiLanWorldPersonActivity.Walk,
                    false,
                    false,
                    0,
                    892250.25,
                    4196464.50,
                    33.10,
                    180.0f,
                    1.45f,
                    3_000_123_456,
                    4,
                    null),
                new OpenOmsiLanWorldPersonState(
                    22,
                    OpenOmsiLanWorldPersonActivity.Sit,
                    true,
                    true,
                    9,
                    0.35,
                    1.75,
                    1.10,
                    90.0f,
                    0.0f,
                    null,
                    null,
                    7)
            ]);

    var peoplePackets =
        OpenOmsiLanWorldPeopleCodec.Encode(
            peopleFrame);

    Require(
        peoplePackets.Count ==
            1 &&
        OpenOmsiLanWorldPeopleCodec.TryDecode(
            peoplePackets[0],
            out var decodedPeopleFrame) &&
        decodedPeopleFrame.Sequence ==
            peopleFrame.Sequence &&
        decodedPeopleFrame.People.Count ==
            2 &&
        decodedPeopleFrame.People[0].WaitingStopObjectId ==
            peopleFrame.People[0].WaitingStopObjectId &&
        decodedPeopleFrame.People[0].WaitingSpot ==
            peopleFrame.People[0].WaitingSpot &&
        decodedPeopleFrame.People[1].Aboard &&
        decodedPeopleFrame.People[1].PlayerBus &&
        decodedPeopleFrame.People[1].BusId ==
            peopleFrame.People[1].BusId &&
        decodedPeopleFrame.People[1].SeatIndex ==
            peopleFrame.People[1].SeatIndex,
        "openOMSI WORLD people encode/decode round-trip failed.");

    var personDescription =
        new OpenOmsiLanWorldPersonDescription(
            21,
            "Humans/Man01.hum");

    Require(
        OpenOmsiLanWorldPeopleCodec.TryDecodeDescription(
            OpenOmsiLanWorldPeopleCodec.EncodeDescription(
                personDescription),
            out var decodedPersonDescription) &&
        decodedPersonDescription ==
            personDescription,
        "openOMSI DESC p round-trip failed.");

    var wantText =
        OpenOmsiLanWorldControlCodec.EncodeWant(
            7,
            [
                new OpenOmsiLanWorldEntityRef(
                    false,
                    10),
                new OpenOmsiLanWorldEntityRef(
                    true,
                    21)
            ]);

    Require(
        OpenOmsiLanWorldControlCodec.TryDecodeWant(
            wantText,
            out var wantRequest) &&
        wantRequest.PlayerId ==
            7 &&
        wantRequest.Entities.SequenceEqual(
            [
                new OpenOmsiLanWorldEntityRef(
                    false,
                    10),
                new OpenOmsiLanWorldEntityRef(
                    true,
                    21)
            ]),
        "openOMSI WANT round-trip failed.");

    var claimText =
        OpenOmsiLanWorldControlCodec.EncodeClaim(
            7,
            [
                21u,
                22u
            ]);

    Require(
        OpenOmsiLanWorldControlCodec.TryDecodeClaim(
            claimText,
            out var claimRequest) &&
        claimRequest.PlayerId ==
            7 &&
        claimRequest.People.SequenceEqual(
            [
                21u,
                22u
            ]),
        "openOMSI CLAIM round-trip failed.");

    foreach (var granted in
             new[]
             {
                 true,
                 false
             })
    {
        var resultText =
            OpenOmsiLanWorldControlCodec.EncodeClaimResult(
                granted,
                [
                    21u,
                    22u
                ]);

        Require(
            OpenOmsiLanWorldControlCodec.TryDecodeClaimResult(
                resultText,
                out var claimResult) &&
            claimResult.Granted ==
                granted &&
            claimResult.People.SequenceEqual(
                [
                    21u,
                    22u
                ]),
            $"openOMSI {(granted ? "GRANT" : "DENY")} round-trip failed.");
    }

    var sharedWorldFrame =
        new OpenOmsiLanWorldFrame(
            77,
            123456,
            [
                new OpenOmsiLanWorldCarState(
                    10,
                    892248.18,
                    4196461.37,
                    33.21,
                    271.3f,
                    -1.2f,
                    0.4f,
                    13.85f,
                    -7.5f,
                    2,
                    true,
                    true,
                    1),
                new OpenOmsiLanWorldCarState(
                    11,
                    892260.25,
                    4196470.5,
                    33.4,
                    90.0f,
                    0.0f,
                    0.0f,
                    5.5f,
                    3.0f,
                    0,
                    false,
                    true,
                    0)
            ],
            [
                new OpenOmsiLanWorldLightState(
                    3_000_123_456,
                    63.45,
                    true)
            ],
            [
                new OpenOmsiLanWorldGoneEntity(
                    false,
                    91),
                new OpenOmsiLanWorldGoneEntity(
                    true,
                    92)
            ],
            new OpenOmsiLanWorldParkedState(
                true,
                [
                    101u,
                    102u
                ]));

    var worldPackets =
        OpenOmsiLanWorldCodec.Encode(
            sharedWorldFrame);

    var decodedWorldFrame =
        new OpenOmsiLanWorldFrame(
            0,
            0,
            Array.Empty<
                OpenOmsiLanWorldCarState>(),
            Array.Empty<
                OpenOmsiLanWorldLightState>());

    var worldDecoded =
        worldPackets.Count >
            0 &&
        OpenOmsiLanWorldCodec.TryDecode(
            worldPackets[0],
            out decodedWorldFrame);

    var decodedFirstCar =
        worldDecoded &&
        decodedWorldFrame.Cars.Count >
            0
            ? decodedWorldFrame.Cars[0]
            : null;

    var decodedFirstLight =
        worldDecoded &&
        decodedWorldFrame.Lights.Count >
            0
            ? decodedWorldFrame.Lights[0]
            : null;

    Require(
        worldPackets.Count ==
            1 &&
        worldPackets[0].Length <=
            OpenOmsiLanWorldCodec.MaximumDatagramBytes &&
        worldDecoded &&
        decodedWorldFrame.Sequence ==
            sharedWorldFrame.Sequence &&
        decodedWorldFrame.Cars.Count ==
            2 &&
        decodedWorldFrame.Lights.Count ==
            1 &&
        decodedFirstCar is not null &&
        Math.Abs(
            decodedFirstCar.X -
            sharedWorldFrame.Cars[0].X) <
            0.011 &&
        Math.Abs(
            decodedFirstCar.Y -
            sharedWorldFrame.Cars[0].Y) <
            0.011 &&
        decodedFirstCar.Brake &&
        decodedFirstCar.AtStation ==
            1 &&
        decodedFirstLight is not null &&
        decodedFirstLight.ObjectId ==
            sharedWorldFrame.Lights[0].ObjectId &&
        decodedFirstLight.Held &&
        decodedWorldFrame.Gone is
            { Count: 2 } &&
        !decodedWorldFrame.Gone[0].Person &&
        decodedWorldFrame.Gone[0].Id ==
            91 &&
        decodedWorldFrame.Gone[1].Person &&
        decodedWorldFrame.Gone[1].Id ==
            92 &&
        decodedWorldFrame.Parked is
            {
                Complete:
                    true
            } &&
        decodedWorldFrame.Parked.ParkingObjectIds.SequenceEqual(
            [
                101u,
                102u
            ]),
        $"openOMSI WORLD car/signal/gone/parked codec round-trip failed: packets={worldPackets.Count}; bytes={(worldPackets.Count > 0 ? worldPackets[0].Length : 0)}; decoded={worldDecoded}; seq={(worldDecoded ? decodedWorldFrame.Sequence : 0)}/{sharedWorldFrame.Sequence}; cars={(worldDecoded ? decodedWorldFrame.Cars.Count : 0)}; lights={(worldDecoded ? decodedWorldFrame.Lights.Count : 0)}; gone={(worldDecoded ? decodedWorldFrame.Gone?.Count ?? 0 : 0)}; parked={(worldDecoded ? decodedWorldFrame.Parked?.ParkingObjectIds.Count ?? 0 : 0)}; car0={(decodedFirstCar is null ? "-" : $"{decodedFirstCar.X:0.000},{decodedFirstCar.Y:0.000},{decodedFirstCar.Z:0.000},brake={decodedFirstCar.Brake},station={decodedFirstCar.AtStation}")}; expectedCar0={sharedWorldFrame.Cars[0].X:0.000},{sharedWorldFrame.Cars[0].Y:0.000},{sharedWorldFrame.Cars[0].Z:0.000}; light0={(decodedFirstLight is null ? "-" : $"{decodedFirstLight.ObjectId},held={decodedFirstLight.Held},t={decodedFirstLight.PositionSeconds:0.00}")}; expectedLight0={sharedWorldFrame.Lights[0].ObjectId},held={sharedWorldFrame.Lights[0].Held},t={sharedWorldFrame.Lights[0].PositionSeconds:0.00}.");

    var peopleWorldVector =
        Convert.FromHexString(
            "B4063700D204000064000000C80000000A000081030000CA00000A00000000758180A465091000003401004006F0000A80030000");

    Require(
        OpenOmsiLanWorldPeopleCodec.TryDecode(
            peopleWorldVector,
            out var decodedPeopleWorld) &&
        decodedPeopleWorld.Sequence ==
            55 &&
        decodedPeopleWorld.HostMilliseconds ==
            1234 &&
        decodedPeopleWorld.People.Count ==
            2 &&
        decodedPeopleWorld.People[0].Id ==
            7 &&
        decodedPeopleWorld.People[0].Activity ==
            OpenOmsiLanWorldPersonActivity.Walk &&
        !decodedPeopleWorld.People[0].Aboard &&
        Math.Abs(
            decodedPeopleWorld.People[0].X -
            100.25) <
            0.011 &&
        decodedPeopleWorld.People[0].WaitingStopObjectId ==
            3_000_123_456 &&
        decodedPeopleWorld.People[0].WaitingSpot ==
            4 &&
        decodedPeopleWorld.People[1].Id ==
            8 &&
        decodedPeopleWorld.People[1].Aboard &&
        decodedPeopleWorld.People[1].PlayerBus &&
        decodedPeopleWorld.People[1].BusId ==
            9 &&
        decodedPeopleWorld.People[1].Activity ==
            OpenOmsiLanWorldPersonActivity.Sit &&
        decodedPeopleWorld.People[1].SeatIndex ==
            3,
        "openOMSI WORLD people decode vector failed.");

    var peopleInteropFrame =
        new OpenOmsiLanWorldPeopleFrame(
            55,
            1234,
            [
                new OpenOmsiLanWorldPersonState(
                    7,
                    OpenOmsiLanWorldPersonActivity.Walk,
                    false,
                    false,
                    0,
                    100.25,
                    200.40,
                    10.0,
                    90.0f,
                    1.45f,
                    3_000_123_456,
                    4,
                    null),
                new OpenOmsiLanWorldPersonState(
                    8,
                    OpenOmsiLanWorldPersonActivity.Sit,
                    true,
                    true,
                    9,
                    0.50,
                    1.20,
                    0.40,
                    180.0f,
                    0.0f,
                    null,
                    null,
                    3)
            ]);

    var peopleInteropPackets =
        OpenOmsiLanWorldPeopleCodec.Encode(
            peopleInteropFrame);

    Require(
        peopleInteropPackets.Count ==
            1 &&
        peopleInteropPackets[0].SequenceEqual(
            peopleWorldVector),
        "Runtime WORLD people encoder no longer matches the openOMSI protocol-6 golden packet.");

    var passengerCabinSmokePath =
        Path.Combine(
            root,
            "passengercabin-smoke.cfg");

    File.WriteAllText(
        passengerCabinSmokePath,
        """
        [entry]
        0
        {withbutton}

        [entry]
        2
        {noticketsale}

        [exit]
        3

        [passpos]
        0.50
        2.00
        1.00
        0.45
        90
        seat_enabled
        seat_taken

        [drivpos]
        -0.80
        4.50
        1.10
        0.50
        0
        """,
        Encoding.UTF8);

    var passengerCabinSmoke =
        OmsiPassengerCabinReader.ReadFile(
            passengerCabinSmokePath);

    Require(
        passengerCabinSmoke.Entries.Count ==
            2 &&
        passengerCabinSmoke.Entries[0].PathPoint ==
            0 &&
        passengerCabinSmoke.Entries[0].WithButton &&
        passengerCabinSmoke.Entries[1].PathPoint ==
            2 &&
        passengerCabinSmoke.Entries[1].NoTicketSale &&
        passengerCabinSmoke.Exits.SequenceEqual(
            [
                3
            ]) &&
        passengerCabinSmoke.PassengerPositions.Count ==
            1 &&
        passengerCabinSmoke.PassengerPositions[0].FileIndex ==
            0 &&
        passengerCabinSmoke.PassengerPositions[0].SwitchVariable ==
            "seat_enabled" &&
        passengerCabinSmoke.PassengerPositions[0].TakenVariable ==
            "seat_taken" &&
        passengerCabinSmoke.DriverPositions.Count ==
            1 &&
        passengerCabinSmoke.DriverPositions[0].FileIndex ==
            1,
        "OMSI passengercabin.cfg compatibility smoke failed.");

    var passengerPathsSmokePath =
        Path.Combine(
            root,
            "paths-smoke.cfg");

    File.WriteAllText(
        passengerPathsSmokePath,
        """
        [pathpnt]
        0
        0
        0

        [pathpnt]
        0
        2
        0

        [next_stepsound]
        2

        [next_roomheight]
        2.4

        [pathlink]
        0
        1

        [pathlink_oneway]
        1
        0
        """,
        Encoding.UTF8);

    var passengerPathsSmoke =
        OmsiVehiclePathReader.ReadFile(
            passengerPathsSmokePath);

    Require(
        passengerPathsSmoke.Points.Count ==
            2 &&
        passengerPathsSmoke.Links.Count ==
            2 &&
        !passengerPathsSmoke.Links[0].OneWay &&
        passengerPathsSmoke.Links[0].StepSoundPack ==
            2 &&
        Math.Abs(
            passengerPathsSmoke.Links[0].RoomHeight -
            2.4) <
            0.001 &&
        passengerPathsSmoke.Links[1].OneWay,
        "OMSI paths.cfg passenger-network compatibility smoke failed.");

    var carParkScenerySmokePath =
        Path.Combine(
            root,
            "carpark-smoke.sco");

    File.WriteAllText(
        carParkScenerySmokePath,
        """
        [onlyeditor]

        [carpark_p]
        """,
        Encoding.UTF8);

    var carParkScenerySmoke =
        OmsiSceneryObjectReader.ReadFile(
            carParkScenerySmokePath);

    Require(
        carParkScenerySmoke.Exists &&
        carParkScenerySmoke.OnlyEditor &&
        carParkScenerySmoke.IsCarPark,
        "OMSI [carpark_p] scenery marker was not preserved for parked-car WORLD state.");

    Require(
        WorldParkedCarResolver.ResolveParkListIndex(
            Array.Empty<string>()) ==
            0 &&
        WorldParkedCarResolver.ResolveParkListIndex(
            [
                " 2 ",
                "ignored"
            ]) ==
            2 &&
        WorldParkedCarResolver.ResolveParkListIndex(
            [
                "Taxi"
            ]) ==
            0,
        "OMSI parked-car parklist caption selection diverged from openOMSI.");

    var parkedCarTypes =
        new[]
        {
            "Vehicles/Test/parked_a.sco",
            "Vehicles/Test/parked_b.sco",
            "Vehicles/Test/parked_c.sco"
        };

    Require(
        WorldParkedCarResolver.SelectParkedCar(
            1,
            parkedCarTypes) is
            null &&
        WorldParkedCarResolver.SelectParkedCar(
            2,
            parkedCarTypes) ==
            parkedCarTypes[0] &&
        WorldParkedCarResolver.SelectParkedCar(
            3,
            parkedCarTypes) ==
            parkedCarTypes[1] &&
        WorldParkedCarResolver.SelectParkedCar(
            5,
            parkedCarTypes) ==
            parkedCarTypes[2],
        "OMSI parked-car deterministic selection diverged from openOMSI.");

    var departedParkingSmoke =
        new HashSet<uint>
        {
            2,
            77
        };

    Require(
        WorldParkedCarResolver.IsDeparted(
            2,
            departedParkingSmoke) &&
        !WorldParkedCarResolver.IsDeparted(
            3,
            departedParkingSmoke) &&
        !WorldParkedCarResolver.IsDeparted(
            -1,
            departedParkingSmoke) &&
        !WorldParkedCarResolver.IsDeparted(
            (long)uint.MaxValue +
            1,
            departedParkingSmoke),
        "openOMSI parked-car departed-id filtering failed.");

    var worldPersonDescriptionVector =
        new OpenOmsiLanWorldPersonDescription(
            7,
            "Humans/Man01.hum");

    Require(
        OpenOmsiLanWorldPeopleCodec.TryDecodeDescription(
            OpenOmsiLanWorldPeopleCodec.EncodeDescription(
                worldPersonDescriptionVector),
            out var decodedWorldPersonDescriptionVector) &&
        decodedWorldPersonDescriptionVector ==
            worldPersonDescriptionVector,
        "openOMSI WORLD person DESC round-trip failed.");

    var worldDescription =
        new OpenOmsiLanWorldCarDescription(
            10,
            "Vehicles/Synthetic/Synthetic.bus",
            2,
            "76",
            "Bahnhof");

    Require(
        OpenOmsiLanWorldCodec.TryDecodeDescription(
            OpenOmsiLanWorldCodec.EncodeDescription(
                worldDescription),
            out var decodedWorldDescription) &&
        decodedWorldDescription ==
            worldDescription,
        "openOMSI WORLD DESC round-trip failed.");

    var lanWorld =
        new OpenOmsiLanWorld(
            "maps/SyntheticMap/global.cfg",
            "2026-09-30",
            12.0 * 3600.0,
            string.Empty,
            "autumn");

    using (var missingHostClient =
           OpenOmsiLanSession.Join(
               "127.0.0.1:65534",
               "Timeout Client",
               lanWorld))
    {
        var timeoutPose =
            lanPose.Clone();

        timeoutPose.Name =
            "Timeout Client";

        for (var timeoutStep = 0;
             timeoutStep <
                 21 &&
             missingHostClient.RejectionReason is null;
             timeoutStep++)
        {
            missingHostClient.Tick(
                0.5,
                timeoutPose);
        }

        Require(
            !missingHostClient.Connected &&
            missingHostClient.RejectionReason?.Contains(
                "10 seconds",
                StringComparison.Ordinal) ==
            true,
            "openOMSI LAN join timeout did not stop an unreachable host after 10 seconds.");
    }

    using var lanHost =
        OpenOmsiLanSession.Host(
            0,
            "Smoke Host",
            lanWorld,
            tryNextPorts:
                false);

    using var lanClient =
        OpenOmsiLanSession.Join(
            $"127.0.0.1:{lanHost.LocalPort}",
            "Smoke Client",
            lanWorld);

    var hostPose =
        lanPose.Clone();

    hostPose.Id =
        1;

    hostPose.Name =
        "Smoke Host";

    var clientPose =
        lanPose.Clone();

    clientPose.Id =
        0;

    clientPose.Name =
        "Smoke Client";

    clientPose.X +=
        12.0;

    var lanConnected =
        false;

    for (var lanStep = 0;
         lanStep <
             300;
         lanStep++)
    {
        lanClient.Tick(
            0.02,
            clientPose);

        lanHost.Tick(
            0.02,
            hostPose);

        lanClient.Tick(
            0.02,
            clientPose);

        if (lanClient.Connected &&
            lanHost.SnapshotPeers()
                .Any(
                    static peer =>
                        peer.HasInfo &&
                        peer.HasState) &&
            lanClient.SnapshotPeers()
                .Any(
                    static peer =>
                        peer.Id ==
                        1 &&
                        peer.HasInfo &&
                        peer.HasState))
        {
            lanConnected =
                true;

            break;
        }

        Thread.Sleep(
            2);
    }

    Require(
        lanConnected &&
        lanHost.SnapshotPeers().Count ==
            1 &&
        lanClient.SnapshotPeers()
            .Any(
                peer =>
                    peer.Id ==
                        1 &&
                    peer.Pose.VehiclePath ==
                        hostPose.VehiclePath),
        "openOMSI LAN protocol-6 host/client loopback handshake, INFO or STATE relay failed.");

    using var lanObserver =
        OpenOmsiLanSession.Join(
            $"127.0.0.1:{lanHost.LocalPort}",
            "Smoke Observer",
            lanWorld);

    var observerPose =
        lanPose.Clone();

    observerPose.Id =
        0;

    observerPose.Name =
        "Smoke Observer";

    observerPose.X +=
        24.0;

    var observerConnected =
        false;

    for (var observerStep = 0;
         observerStep <
             300;
         observerStep++)
    {
        lanObserver.Tick(
            0.02,
            observerPose);
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        lanObserver.Tick(
            0.02,
            observerPose);

        if (lanObserver.Connected &&
            lanHost.SnapshotPeers().Count ==
                2)
        {
            observerConnected =
                true;
            break;
        }

        Thread.Sleep(
            2);
    }

    Require(
        observerConnected,
        "openOMSI LAN second client did not join the loopback host.");

    OpenOmsiLanWorldPersonDescription?
        observerRelayedDescription =
            null;
    OpenOmsiLanWorldPeopleFrame?
        observerRelayedPeople =
            null;
    var sourceEchoedPeople =
        false;

    lanObserver.WorldPersonDescriptionReceived +=
        description =>
            observerRelayedDescription =
                description;

    lanObserver.WorldPeopleFrameReceived +=
        frame =>
            observerRelayedPeople =
                frame;

    lanClient.WorldPeopleFrameReceived +=
        _ =>
            sourceEchoedPeople =
                true;

    lanHost.WorldPersonDescriptionUpReceived +=
        (
            peerId,
            description
        ) =>
        {
            lanHost.RelayWorldPersonDescription(
                peerId,
                description with
                {
                    Id =
                        0x00C00021u
                });
        };

    lanHost.WorldPeopleFrameUpReceived +=
        (
            peerId,
            frame
        ) =>
        {
            lanHost.RelayWorldPeopleFrame(
                peerId,
                frame with
                {
                    People =
                    [
                        frame.People[0] with
                        {
                            Id =
                                0x00C00021u,
                            PlayerBus =
                                true,
                            BusId =
                                peerId,
                            WaitingStopObjectId =
                                null,
                            WaitingSpot =
                                null
                        }
                    ]
                });
        };

    lanClient.SendWorldPersonDescriptionUp(
        new OpenOmsiLanWorldPersonDescription(
            21,
            "Humans/Man01.hum"));

    lanClient.SendWorldPeopleFrameUp(
        new OpenOmsiLanWorldPeopleFrame(
            0,
            0,
            [
                new OpenOmsiLanWorldPersonState(
                    21,
                    OpenOmsiLanWorldPersonActivity.Sit,
                    true,
                    true,
                    lanClient.PlayerId,
                    0.35,
                    1.75,
                    1.10,
                    90.0f,
                    0.0f,
                    null,
                    null,
                    7)
            ]));

    for (var relayStep = 0;
         relayStep <
             120 &&
         (
             observerRelayedDescription is null ||
             observerRelayedPeople is null
         );
         relayStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        lanObserver.Tick(
            0.02,
            observerPose);

        Thread.Sleep(
            1);
    }

    Require(
        observerRelayedDescription?.Id ==
            0x00C00021u &&
        observerRelayedPeople?.People.Count ==
            1 &&
        observerRelayedPeople.People[0].Id ==
            0x00C00021u &&
        observerRelayedPeople.People[0].PlayerBus &&
        observerRelayedPeople.People[0].BusId ==
            lanClient.PlayerId &&
        !sourceEchoedPeople,
        "openOMSI WORLD people uplink relay did not reach only the other client.");

    OpenOmsiLanWorldClaimResult?
        deniedClaim =
            null;

    lanClient.WorldClaimResultReceived +=
        result =>
            deniedClaim =
                result;

    lanHost.WorldClaimRequested +=
        request =>
            lanHost.SendWorldClaimResult(
                request.PlayerId,
                Array.Empty<uint>(),
                request.People);

    lanClient.SendWorldClaim(
        [
            77u
        ]);

    for (var claimStep = 0;
         claimStep <
             120 &&
         deniedClaim is null;
         claimStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        lanObserver.Tick(
            0.02,
            observerPose);

        Thread.Sleep(
            1);
    }

    Require(
        deniedClaim is
            {
                Granted:
                    false
            } &&
        deniedClaim.People.SequenceEqual(
            [
                77u
            ]),
        "openOMSI WORLD passenger CLAIM/DENY loopback flow failed.");

    var synchronizedWorld =
        new OpenOmsiLanWorld(
            lanWorld.Map,
            "2026-10-01",
            13.0 *
                3600.0 +
            37.0,
            "weather/regen.cfg",
            "winter");

    lanHost.SetWorld(
        synchronizedWorld);

    var worldClockSynchronized =
        false;

    for (var clockStep = 0;
         clockStep <
             20;
         clockStep++)
    {
        lanHost.Tick(
            0.5,
            hostPose);
        lanClient.Tick(
            0.5,
            clientPose);

        if (lanClient.World ==
            synchronizedWorld)
        {
            worldClockSynchronized =
                true;

            break;
        }

        Thread.Sleep(
            1);
    }

    Require(
        worldClockSynchronized,
        "openOMSI LAN CLOCK did not keep the client date/time/weather/season synchronized with the host.");

    OpenOmsiLanOperationalMessage?
        hostReceivedOps =
            null;
    OpenOmsiLanOperationalMessage?
        clientReceivedOps =
            null;

    lanHost.OperationalMessageReceived +=
        message =>
            hostReceivedOps =
                message;
    lanClient.OperationalMessageReceived +=
        message =>
            clientReceivedOps =
                message;

    Require(
        lanClient.SendOperationalMessage(
            opsMessage),
        "Runtime operational LAN client send was rejected.");

    for (var opsStep = 0;
         opsStep <
             100 &&
         hostReceivedOps is null;
         opsStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        Thread.Sleep(
            1);
    }

    Require(
        hostReceivedOps is not null &&
        hostReceivedOps.SenderId ==
            lanClient.PlayerId &&
        hostReceivedOps.Module ==
            "FLEETLINK" &&
        hostReceivedOps.Text ==
            opsMessage.Text,
        "Runtime operational LAN client-to-host relay failed.");

    Require(
        lanHost.SendOperationalMessage(
            new OpenOmsiLanOperationalMessage(
                0,
                string.Empty,
                "Empresa Teste",
                "CCO",
                "CONTROLHUB",
                "DISPATCH",
                "Retorne à garagem após a viagem.",
                string.Empty,
                0)),
        "Runtime operational LAN host send was rejected.");

    for (var opsStep = 0;
         opsStep <
             100 &&
         clientReceivedOps is null;
         opsStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        Thread.Sleep(
            1);
    }

    Require(
        clientReceivedOps is not null &&
        clientReceivedOps.SenderId ==
            lanHost.PlayerId &&
        clientReceivedOps.Module ==
            "CONTROLHUB" &&
        clientReceivedOps.Kind ==
            "DISPATCH",
        "Runtime operational LAN host-to-client relay failed.");

    OpenOmsiLanVoiceFrame?
        hostReceivedVoice =
            null;
    OpenOmsiLanVoiceFrame?
        clientReceivedVoice =
            null;

    lanHost.VoiceFrameReceived +=
        frame =>
            hostReceivedVoice =
                frame;
    lanClient.VoiceFrameReceived +=
        frame =>
            clientReceivedVoice =
                frame;

    Require(
        lanClient.SendVoiceFrame(
            voicePcm),
        "CommsLink client voice frame send was rejected.");

    for (var voiceStep = 0;
         voiceStep <
             100 &&
         hostReceivedVoice is null;
         voiceStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        Thread.Sleep(
            1);
    }

    Require(
        hostReceivedVoice is not null &&
        hostReceivedVoice.SenderId ==
            lanClient.PlayerId &&
        hostReceivedVoice.Pcm16Mono8Khz
            .SequenceEqual(
                voicePcm),
        "CommsLink client-to-host voice relay failed.");

    var hostVoicePcm =
        voicePcm
            .Reverse()
            .ToArray();

    Require(
        lanHost.SendVoiceFrame(
            hostVoicePcm),
        "CommsLink host voice frame send was rejected.");

    for (var voiceStep = 0;
         voiceStep <
             100 &&
         clientReceivedVoice is null;
         voiceStep++)
    {
        lanHost.Tick(
            0.02,
            hostPose);
        lanClient.Tick(
            0.02,
            clientPose);
        Thread.Sleep(
            1);
    }

    Require(
        clientReceivedVoice is not null &&
        clientReceivedVoice.SenderId ==
            lanHost.PlayerId &&
        clientReceivedVoice.Pcm16Mono8Khz
            .SequenceEqual(
                hostVoicePcm),
        "CommsLink host-to-client voice relay failed.");

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

static void WriteSyntheticCollisionO3d(
    string path)
{
    using var stream =
        File.Create(path);

    using var writer =
        new BinaryWriter(stream);

    writer.Write((byte)0x84);
    writer.Write((byte)0x19);
    writer.Write((byte)3);

    writer.Write((byte)0x17);
    writer.Write((ushort)4);

    void Vertex(
        float x,
        float y,
        float z)
    {
        writer.Write(x);
        writer.Write(y);
        writer.Write(z);
        writer.Write(0.0f);
        writer.Write(1.0f);
        writer.Write(0.0f);
        writer.Write(0.0f);
        writer.Write(0.0f);
    }

    Vertex(-2.0f, 0.0f, -1.0f);
    Vertex(2.0f, 0.0f, -1.0f);
    Vertex(2.0f, 3.0f, 1.0f);
    Vertex(-2.0f, 3.0f, 1.0f);

    writer.Write((byte)0x49);
    writer.Write((ushort)2);

    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)2);
    writer.Write((ushort)0);

    writer.Write((ushort)0);
    writer.Write((ushort)2);
    writer.Write((ushort)3);
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

    foreach (var value in
             new float[]
             {
                 1, 0, 0, 0,
                 0, 1, 0, 0,
                 0, 0, 1, 0,
                 0.5f, 1.0f, -0.25f, 1
             })
    {
        writer.Write(
            value);
    }
}

static void WriteSyntheticO3d(
    string path,
    bool extendedHeader = false,
    uint protectionKey = uint.MaxValue,
    bool includeBones = false)
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

    if (includeBones)
    {
        writer.Write(
            (byte)0x54);
        writer.Write(
            (ushort)1);

        var boneName =
            Encoding.Latin1.GetBytes(
                "SyntheticBone");

        writer.Write(
            (byte)boneName.Length);
        writer.Write(
            boneName);

        writer.Write(
            (ushort)2);

        writer.Write(
            (ushort)0);
        writer.Write(
            0.5f);

        writer.Write(
            (ushort)1);
        writer.Write(
            0.75f);
    }

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
