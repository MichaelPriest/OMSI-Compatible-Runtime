using System.Diagnostics;
using System.Numerics;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Plugins;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.Common;
using OMSICompatible.Renderer.D3D11;
using OMSICompatible.Renderer.D3D12;
using OMSICompatible.Multiplayer;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal sealed class RuntimeApplicationContext :
    ApplicationContext
{
    private readonly OmsiContentRoot _contentRoot;
    private readonly OmsiMapInfo _map;
    private readonly OmsiBusInfo? _bus;
    private readonly OmsiMapEntryPoint _entryPoint;
    private readonly bool _externalLoading;
    private readonly string? _repaintName;
    private readonly string? _repaintCtiRelativePath;
    private readonly string? _selectedHofPath;
    private readonly OmsiRuntimeOptions _options;
    private readonly LoadingForm _loading;
    private readonly SemaphoreSlim _streamingGate =
        new(1, 1);
    private readonly SemaphoreSlim _prefetchGate =
        new(1, 1);
    private readonly SemaphoreSlim _multiplayerAssetGate =
        new(1, 1);
    private (int X, int Y)? _pendingPrefetchCenter;
    private (int X, int Y)? _lastPrefetchedCenter;

    private D3D11RenderWindow? _runtimeWindow;
    private OmsiVehicleAsset? _vehicleAsset;
    private OmsiScriptRuntime? _playerScriptRuntime;
    private WorldTrafficSimulation? _trafficSimulation;
    private WorldLineAiSimulation? _lineAiSimulation;
    private WorldRailTrafficSimulation? _railTrafficSimulation;
    private readonly Lazy<OmsiTimetableCatalog> _timetableCatalog;
    private readonly Lazy<OmsiMapCalendar> _mapCalendar;
    private WorldLineAiSchedule _lineAiSchedule =
        WorldLineAiSchedule.Empty;
    private double _lineAiServiceMinutes;
    private readonly WorldNavigationAssist
        _navigationAssist =
            new();
    private string _navigationRouteKey =
        string.Empty;
    private RuntimeTrafficPathPointInfo[]
        _navigationRuntimeRoute =
            [];
    private double _navigationUpdateAccumulator;
    private WorldDefinition? _currentWorld;
    private OpenOmsiLanSession? _multiplayerSession;
    private readonly RuntimeCommsLinkVoiceService
        _commsLinkVoice =
            new();
    private double _multiplayerStatusAccumulator;
    private float _multiplayerVehicleLengthMeters;
    private float _multiplayerVehicleWidthMeters;
    private bool _multiplayerNearRequested;
    private DateTimeOffset? _commsLinkLastRemoteVoiceAt;
    private uint _commsLinkLastRemoteSpeakerId;
    private readonly HashSet<string>
        _pendingMultiplayerVehiclePaths =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, (
        OpenOmsiLanWorldCarState State,
        DateTimeOffset LastSeen)>
        _sharedWorldCars =
            [];
    private readonly Dictionary<uint, OpenOmsiLanWorldCarDescription>
        _sharedWorldDescriptions =
            [];
    private readonly Dictionary<uint, (
        OpenOmsiLanWorldPersonState State,
        DateTimeOffset LastSeen)>
        _sharedWorldPeople =
            [];
    private readonly Dictionary<uint, OpenOmsiLanWorldPersonDescription>
        _sharedWorldPersonDescriptions =
            [];
    private readonly Dictionary<uint, (
        OpenOmsiLanWorldPersonState State,
        string HumanPath)>
        _pendingWorldPassengerClaims =
            [];
    private readonly Dictionary<uint, DateTimeOffset>
        _worldPassengerClaimAttemptedAt =
            [];
    private readonly Dictionary<uint, RuntimeWorldPassengerSimulation>
        _ownedWorldPassengers =
            [];
    private double _worldPassengerUplinkAccumulator;
    private readonly RuntimeHostWorldPassengerAuthority
        _hostWorldPassengers =
            new();
    private double _hostWorldPassengerRefreshAccumulator =
        1.0;
    private double _hostWorldPassengerSendAccumulator;
    private readonly Dictionary<uint, WorldPeerVisibilityView>
        _hostWorldPassengerPeerViews =
            [];
    private readonly Dictionary<(bool Person, uint Id), DateTimeOffset>
        _sharedWorldWantRequestedAt =
            [];
    private readonly Dictionary<(uint PeerId, uint PersonId), uint>
        _relayedWorldPersonIds =
            [];
    private readonly HashSet<uint>
        _relayedWorldPersonIdsInUse =
            [];
    private readonly Dictionary<(uint ObserverPeerId, uint SourcePeerId), WorldPeerVisibilityView>
        _relayedWorldPassengerPeerViews =
            [];
    private uint _nextRelayedWorldPersonId =
        0x00C00000u;
    private readonly Dictionary<long, (
        OpenOmsiLanWorldLightState State,
        DateTimeOffset LastSeen)>
        _sharedWorldLights =
            [];
    private readonly HashSet<uint>
        _sharedWorldDepartedParkingObjectIds =
            [];
    private readonly object _sharedWorldParkingGate =
        new();
    private int _sharedWorldParkingRevision;
    private int _sharedWorldParkingAppliedRevision;
    private readonly List<RuntimeTrafficSignalStateInfo>
        _sharedWorldSignalStateBuffer =
            [];
    private double _sharedWorldSendAccumulator;
    private double _sharedWorldDescriptionAccumulator;
    private DateTimeOffset? _sharedWorldLastFrameAt;
    private int _sharedWorldLastAdvertisedCarCount;
    private readonly HashSet<uint>
        _sharedWorldPublishedCarIds =
            [];
    private readonly Dictionary<uint, double>
        _sharedWorldGoneCarSeconds =
            [];
    private readonly Dictionary<uint, WorldPeerVisibilityView>
        _sharedWorldCarPeerViews =
            [];

    private readonly Dictionary<uint, double>
        _multiplayerTravelMeters =
            [];
    private readonly Dictionary<uint, MultiplayerRemoteRenderState>
        _multiplayerRemoteStates =
            [];
    private readonly Dictionary<uint, OmsiScriptRuntime>
        _multiplayerScriptRuntimes =
            [];
    private readonly Dictionary<uint, string>
        _multiplayerScriptPaths =
            [];
    private readonly Dictionary<uint, bool>
        _multiplayerHornStates =
            [];
    private readonly HashSet<uint>
        _multiplayerActivePeerIds =
            [];
    private readonly Dictionary<string, OmsiTrainConsist>
        _railTrainConsists =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RailRuntimeConsist>
        _railRuntimeConsists =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OmsiVehicleAsset>
        _trafficVehicleAssets =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OmsiScriptCatalog>
        _trafficScriptCatalogs =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?>
        _trafficHofByVehiclePath =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OmsiHofCatalog?>
        _hofCatalogsByPath =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TrafficScriptRuntimeState>
        _trafficScriptRuntimes =
            [];
    private readonly List<WorldTrafficAgentState>
        _trafficAgentStateBuffer =
            [];
    private readonly List<WorldRailTrafficAgentState>
        _railTrafficAgentStateBuffer =
            [];
    private readonly List<RuntimeTrafficAgentInfo>
        _runtimeTrafficAgentBuffer =
            [];
    private readonly List<WorldTrafficSignalState>
        _worldTrafficSignalStateBuffer =
            [];
    private readonly List<RuntimeTrafficSignalStateInfo>
        _runtimeTrafficSignalStateBuffer =
            [];
    private readonly List<WorldRailSignalRouteState>
        _worldRailSignalRouteStateBuffer =
            [];
    private readonly List<RuntimeRailSignalRouteStateInfo>
        _runtimeRailSignalRouteStateBuffer =
            [];
    private readonly Dictionary<long, WorldRailSignalRouteState>
        _railSignalBestStateByObjectId =
            [];
    private readonly HashSet<int>
        _activeTrafficScriptAgentIds =
            [];
    private readonly List<int>
        _staleTrafficScriptAgentIds =
            [];
    private readonly List<(
        WorldTrafficAgentState Agent,
        TrafficScriptRuntimeState State,
        double DeltaSeconds)>
        _trafficScriptWork =
            [];
    private readonly Dictionary<long, OmsiScriptRuntime>
        _railSignalScriptRuntimes =
            [];
    private (int X, int Y)? _pendingStreamingCenter;
    private int _loadedCenterX;
    private int _loadedCenterY;
    private bool _loadEntireMap;
    private bool _closing;
    private readonly List<HostedPluginSession>
        _pluginSessions =
            [];

    private sealed class MultiplayerRemoteRenderState
    {
        public bool Initialized { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double HeadingDegrees { get; set; }
    }

    private sealed class WorldPeerVisibilityView
    {
        public HashSet<uint> VisibleIds { get; } =
            [];

        public Dictionary<uint, double> GoneSeconds { get; } =
            [];
    }

    private sealed class HostedPluginSession(
        OmsiPluginDefinition definition,
        OmsiPluginRemoteClient client)
    {
        public OmsiPluginDefinition Definition { get; } =
            definition;

        public OmsiPluginRemoteClient Client { get; } =
            client;

        public bool[] TriggerStates { get; } =
            new bool[
                definition.Triggers.Count];

        public double AccumulatedSeconds
        {
            get;
            set;
        }

        public bool Failed
        {
            get;
            set;
        }
    }

    private const int CompleteMapTileThreshold = 64;

    public RuntimeApplicationContext(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map,
        OmsiBusInfo? bus,
        OmsiMapEntryPoint entryPoint,
        bool externalLoading,
        string? repaintName = null,
        string? repaintCtiRelativePath = null,
        string? selectedHofPath = null)
    {
        _contentRoot = contentRoot;
        _map = map;
        _timetableCatalog =
            new Lazy<OmsiTimetableCatalog>(
                () =>
                    OmsiTimetableCatalogReader.Read(
                        _map),
                LazyThreadSafetyMode.ExecutionAndPublication);
        _mapCalendar =
            new Lazy<OmsiMapCalendar>(
                () =>
                    OmsiMapCalendarReader.Read(
                        _map),
                LazyThreadSafetyMode.ExecutionAndPublication);
        _bus = bus;
        _entryPoint = entryPoint;
        _externalLoading =
            externalLoading;
        _repaintName =
            string.IsNullOrWhiteSpace(
                repaintName)
                ? null
                : repaintName.Trim();
        _repaintCtiRelativePath =
            string.IsNullOrWhiteSpace(
                repaintCtiRelativePath)
                ? null
                : repaintCtiRelativePath.Trim();
        _selectedHofPath =
            !string.IsNullOrWhiteSpace(
                selectedHofPath) &&
            File.Exists(
                selectedHofPath)
                ? Path.GetFullPath(
                    selectedHofPath)
                : null;
        _options =
            OmsiRuntimeOptions.Load();
        _lineAiServiceMinutes =
            DateTime.Now.TimeOfDay.TotalMinutes;

        var d3d12 =
            D3D12BackendProbe.Probe(
                _options.RuntimePreferHardwareGpu);

        var requestedGraphicsBackend =
            string.IsNullOrWhiteSpace(
                _options.RuntimeGraphicsBackend)
                ? "Auto"
                : _options.RuntimeGraphicsBackend.Trim();

        Console.WriteLine(
            $"[graphics] requested={requestedGraphicsBackend}; d3d12Available={d3d12.Available}; adapter={d3d12.AdapterName}; featureLevel={d3d12.FeatureLevel}; software={d3d12.SoftwareAdapter}; error={d3d12.Error ?? "<none>"}");

        if (requestedGraphicsBackend.Equals(
                "D3D12",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                d3d12.Available
                    ? "[graphics] D3D12 device is available; renderer port is staged but D3D11 remains active until the D3D12 draw/resource path reaches feature parity."
                    : "[graphics] D3D12 was requested but is unavailable; using D3D11 fallback.");
        }

        var discoveredPlugins =
            OmsiPluginDiscovery.Discover(
                _contentRoot.RootPath);

        var pluginArchitectures =
            discoveredPlugins
                .Where(
                    static plugin =>
                        File.Exists(
                            plugin.DllPath))
                .Select(
                    static plugin =>
                        OmsiPluginBinaryInspector.Inspect(
                            plugin.DllPath))
                .ToArray();

        Console.WriteLine(
            $"[plugins] discovered={discoveredPlugins.Count:N0}; dllResolved={pluginArchitectures.Length:N0}; x86={pluginArchitectures.Count(static architecture => architecture == OmsiPluginBinaryArchitecture.X86):N0}; x64={pluginArchitectures.Count(static architecture => architecture == OmsiPluginBinaryArchitecture.X64):N0}; vars={discoveredPlugins.Sum(static plugin => plugin.Variables.Count):N0}; strings={discoveredPlugins.Sum(static plugin => plugin.StringVariables.Count):N0}; systemVars={discoveredPlugins.Sum(static plugin => plugin.SystemVariables.Count):N0}; triggers={discoveredPlugins.Sum(static plugin => plugin.Triggers.Count):N0}");

        PrepareCompatiblePluginSessions(
            discoveredPlugins);

        _loadedCenterX =
            entryPoint.Tile.X;
        _loadedCenterY =
            entryPoint.Tile.Y;

        _loading =
            new LoadingForm(
                map.FolderName,
                ResolveMapImage(
                    map.DirectoryPath));

        if (_externalLoading)
        {
            _loading.Opacity = 0.0;
            _loading.ShowInTaskbar = false;
            _loading.StartPosition =
                FormStartPosition.Manual;
            _loading.Location =
                new Point(
                    -32000,
                    -32000);
            _loading.Size =
                new Size(
                    1,
                    1);
        }

        MainForm = _loading;

        _loading.Shown +=
            OnLoadingShown;

        _loading.FormClosed +=
            (_, _) =>
            {
                if (_runtimeWindow is null)
                {
                    ExitThread();
                }
            };
    }

    private async void OnLoadingShown(
        object? sender,
        EventArgs e)
    {
        _loading.Shown -=
            OnLoadingShown;

        try
        {
            ReportProgress(
                new WorldLoadProgress(
                    3,
                    "Inicializando",
                    "Preparando runtime x64..."));

            var worldLoadPercent = 0;
            var vehicleLoadPercent =
                _bus is null
                    ? 100
                    : 0;

            void ReportCombinedLoadProgress(
                string stage,
                string detail)
            {
                var combined =
                    5 +
                    (int)Math.Round(
                        worldLoadPercent * 0.65 +
                        vehicleLoadPercent * 0.20);

                ReportProgress(
                    new WorldLoadProgress(
                        Math.Clamp(
                            combined,
                            5,
                            90),
                        stage,
                        detail));
            }

            var worldProgress =
                new Progress<WorldLoadProgress>(
                    item =>
                    {
                        worldLoadPercent =
                            Math.Clamp(
                                (int)Math.Round(
                                    item.Percent /
                                    90.0 *
                                    100.0),
                                0,
                                100);

                        ReportCombinedLoadProgress(
                            item.Stage,
                            item.Detail);
                    });

            var vehicleProgress =
                new Progress<OmsiVehicleLoadProgress>(
                    item =>
                    {
                        vehicleLoadPercent =
                            Math.Clamp(
                                item.Percent,
                                0,
                                100);

                        ReportCombinedLoadProgress(
                            "Carregando ônibus",
                            item.Detail);
                    });

            var discoveredTileCount =
                MapTileDiscovery.Discover(
                    _map).Count;

            var loadEntireMap =
                _options.LoadWholeMapAtStart ||
                (discoveredTileCount > 0 &&
                 discoveredTileCount <=
                     CompleteMapTileThreshold);

            _loadEntireMap =
                loadEntireMap;

            var streamingRadius =
                Math.Clamp(
                    _options.RuntimeStreamingRadius,
                    0,
                    8);

            var worldTask =
                Task.Run(
                    () =>
                        WorldLoader.Load(
                            _contentRoot,
                            _map,
                            worldProgress,
                            new WorldLoadOptions(
                                _entryPoint.Tile.X,
                                _entryPoint.Tile.Y,
                                ActiveTileRadius:
                                    streamingRadius,
                                LoadEntireMap:
                                    loadEntireMap)));

            var selectedBus =
                _bus;

            var selectedRepaint =
                selectedBus is null ||
                string.IsNullOrWhiteSpace(
                    _repaintName)
                    ? null
                    : OmsiVehicleRepaintCatalog
                        .Discover(
                            selectedBus)
                        .FirstOrDefault(
                            repaint =>
                                string.Equals(
                                    repaint.Name,
                                    _repaintName,
                                    StringComparison.OrdinalIgnoreCase) &&
                                (string.IsNullOrWhiteSpace(
                                     _repaintCtiRelativePath) ||
                                 string.Equals(
                                     repaint.RelativeCtiPath,
                                     _repaintCtiRelativePath,
                                     StringComparison.OrdinalIgnoreCase)));

            Task<OmsiVehicleAsset?> vehicleTask =
                selectedBus is null
                    ? Task.FromResult<OmsiVehicleAsset?>(
                        null)
                    : Task.Run(
                        () =>
                            (OmsiVehicleAsset?)
                            OmsiArticulatedVehicleAssetLoader.Load(
                                _contentRoot,
                                selectedBus,
                                vehicleProgress,
                                selectedRepaint));

            await Task.WhenAll(
                worldTask,
                vehicleTask);

            var world =
                await worldTask;

            var vehicle =
                await vehicleTask;

            _vehicleAsset =
                vehicle;
            _currentWorld =
                world;

            if (vehicle is not null &&
                _bus is not null)
            {
                var playerVehiclePath =
                    Path.GetFullPath(
                        _bus.FilePath);

                _trafficVehicleAssets[
                    playerVehiclePath] =
                    vehicle;

                if (_bus.ScriptManifest.RegisteredFileCount >
                    0)
                {
                    try
                    {
                        _trafficScriptCatalogs[
                            playerVehiclePath] =
                            OmsiScriptCatalogLoader.Load(
                                _contentRoot,
                                _bus.ScriptManifest);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(
                            $"[multiplayer] player bus AI script catalog unavailable: {exception.Message}");
                    }
                }
            }

            var simulationBuildStarted =
                Stopwatch.GetTimestamp();

            var initialSimulations =
                await Task.Run(
                    () =>
                    {
                        var traffic =
                            CreateTrafficSimulation(
                                world);
                        var line =
                            CreateLineAiSimulation(
                                world);
                        var rail =
                            CreateRailTrafficSimulation(
                                world);

                        return (
                            Traffic:
                                traffic,
                            Line:
                                line,
                            Rail:
                                rail);
                    });

            _trafficSimulation =
                initialSimulations.Traffic;
            _lineAiSimulation =
                initialSimulations.Line;
            _railTrafficSimulation =
                initialSimulations.Rail;

            Console.WriteLine(
                $"[startup-perf] simulationsMs={Stopwatch.GetElapsedTime(simulationBuildStarted).TotalMilliseconds:0.0}");

            _trafficScriptRuntimes.Clear();

            await EnsureTrafficVehicleAssetsAsync(
                _trafficSimulation,
                _lineAiSimulation);

            await EnsureRailTrafficAssetsAsync(
                _railTrafficSimulation);

            RebuildRailSignalScriptRuntimes(
                world);

            WriteTrafficDiagnostics(
                world,
                _trafficSimulation);
            WriteLineAiDiagnostics(
                _lineAiSchedule);

            WriteRailTrafficDiagnostics(
                world,
                _railTrafficSimulation);

            if (vehicle is not null)
            {
                WriteVehicleLoadDiagnostics(
                    vehicle,
                    _contentRoot);
            }

            WriteWorldLoadDiagnostics(
                world);

            var renderDetail =
                vehicle is null
                    ? $"{world.Tiles.Count:N0}/{world.TotalTileCount:N0} tiles ativos · modo sem ônibus"
                    : $"{world.Tiles.Count:N0}/{world.TotalTileCount:N0} tiles ativos · " +
                      $"{vehicle.RenderableMeshCount:N0} mesh(es) renderizáveis do ônibus · " +
                      $"{vehicle.ProtectedMeshCount:N0} criptografada(s) · " +
                      $"{vehicle.FailedMeshCount:N0} com falha...";

            ReportProgress(
                new WorldLoadProgress(
                    90,
                    "Preparando renderização",
                    renderDetail));

            var runtimeInfoStarted =
                Stopwatch.GetTimestamp();

            var runtimeInfo =
                await Task.Run(
                    () =>
                        BuildRuntimeInfo(
                            world,
                            vehicle,
                            _entryPoint,
                            _contentRoot.RootPath,
                            _trafficVehicleAssets,
                                    SnapshotSharedWorldDepartedParkingIds()));

            Console.WriteLine(
                $"[startup-perf] runtimeInfoMs={Stopwatch.GetElapsedTime(runtimeInfoStarted).TotalMilliseconds:0.0}");

            await ValidateD3D12SceneAsync(
                runtimeInfo);

            OmsiScriptRuntime? scriptRuntime =
                null;

            if (_bus is not null)
            {
                var scriptCatalog =
                    OmsiScriptCatalogLoader.Load(
                        _contentRoot,
                        _bus.ScriptManifest);

                scriptRuntime =
                    new OmsiScriptRuntime(
                        scriptCatalog);

                ApplyHofToScriptRuntime(
                    scriptRuntime,
                    _selectedHofPath ??
                    ResolveMapHofForBus(
                        _bus,
                        _map.FolderName));
            }

            _playerScriptRuntime =
                scriptRuntime;

            var sectionScriptRuntimes =
                new Dictionary<int, OmsiScriptRuntime>();

            foreach (var section in
                     vehicle?.Sections ??
                     Array.Empty<OmsiVehicleSectionAssetInfo>())
            {
                if (section.ScriptManifest is not
                    { } sectionManifest ||
                    sectionManifest.RegisteredFileCount <=
                    0)
                {
                    continue;
                }

                try
                {
                    var sectionCatalog =
                        OmsiScriptCatalogLoader.Load(
                            _contentRoot,
                            sectionManifest);

                    var sectionRuntime =
                        new OmsiScriptRuntime(
                            sectionCatalog);

                    ApplyHofToScriptRuntime(
                        sectionRuntime,
                        _selectedHofPath ??
                        (_bus is null
                            ? null
                            : ResolveMapHofForBus(
                                _bus,
                                _map.FolderName)));

                    sectionScriptRuntimes[
                        section.Index] =
                        sectionRuntime;
                }
                catch (Exception exception)
                {
                    Console.WriteLine(
                        $"[vehicle-script] section={section.Index} catalog unavailable: {exception.Message}");
                }
            }

            ReportProgress(
                new WorldLoadProgress(
                    96,
                    "Inicializando Direct3D 11",
                    "Criando dispositivo, shaders e recursos gráficos..."));

            _runtimeWindow =
                new D3D11RenderWindow(
                    runtimeInfo,
                    scriptRuntime,
                    _options.TargetFps,
                    _options.RuntimeVSync,
                    showFps:
                        _options.RuntimeShowFps,
                    msaaSamples:
                        _options.RuntimeMsaaSamples,
                    sharpenStrength:
                        _options.RuntimeSharpenStrength,
                    vehiclePreviewMode:
                        false,
                    initialVehicleVariables:
                        selectedRepaint?.SetVariables,
                    inputLanguage:
                        _options.Language,
                    gameControllerEnabled:
                        _options.GameControllerEnabled,
                    controllerDeadZone:
                        _options.ControllerDeadZone,
                    mouseSteeringSensitivity:
                        _options.MouseSteeringSensitivity,
                    throttlePedalResponse:
                        _options.ThrottlePedalResponse,
                    brakePedalResponse:
                        _options.BrakePedalResponse,
                    wheelRangeDegrees:
                        _options.WheelRangeDegrees,
                    wheelLockDegrees:
                        _options.WheelLockDegrees,
                    fieldOfViewDegrees:
                        _options.FieldOfViewDegrees,
                    sectionScriptRuntimes:
                        sectionScriptRuntimes,
                    masterVolumePercent:
                        _options.MasterVolumePercent,
                    automaticSteeringCenter:
                        _options.AutomaticSteeringCenter,
                    maximumSoundCount:
                        _options.MaximumSoundCount,
                    aiVehicleSoundsEnabled:
                        _options.AiVehicleSounds,
                    vehicleToVehicleCollisionsEnabled:
                        _options.VehicleToVehicleCollisions,
                    vehicleLandscapeCollisionsEnabled:
                        _options.VehicleLandscapeCollisions,
                    materialLightMapEnabled:
                        _options.MaterialLightMap,
                    materialReflectionMapEnabled:
                        _options.MaterialReflectionMap,
                    materialBumpMapEnabled:
                        _options.MaterialBumpMap,
                    materialNightMapEnabled:
                        _options.MaterialNightMap,
                    maximumObjectVisibilityMeters:
                        _options.MaximumObjectVisibilityMeters,
                    trafficStep:
                        StepTrafficSimulation,
                    trafficSignalStateProvider:
                        GetTrafficSignalStates,
                    trafficCollisionResponse:
                        ApplyTrafficCollisionResponse,
                    pluginStep:
                        StepHostedPlugins,
                    railSignalStateProvider:
                        GetRailSignalRouteStates,
                    terrainCollisionsEnabled:
                        _options.TerrainCollisions,
                    reflectionTextureSize:
                        _options.RealTimeReflectionTextureSize,
                    reflectionMode:
                        _options.RealTimeReflections);

            _runtimeWindow.DriveOpsMessageRequested +=
                OnDriveOpsMessageRequested;
            _runtimeWindow.CommsLinkTransmitRequested +=
                OnCommsLinkTransmitRequested;
            _runtimeWindow.KeyDown +=
                OnCommsLinkKeyDown;
            _runtimeWindow.KeyUp +=
                OnCommsLinkKeyUp;

            StartMultiplayerSession();

            if (_options.RuntimeBorderlessFullscreen)
            {
                _runtimeWindow.FormBorderStyle =
                    FormBorderStyle.None;
                _runtimeWindow.WindowState =
                    FormWindowState.Maximized;
            }

            _runtimeWindow.StreamingCenterChanged +=
                OnStreamingCenterChanged;

            _runtimeWindow.Shown +=
                (_, _) =>
                {
                    ReportProgress(
                        new WorldLoadProgress(
                            100,
                            "Pronto",
                            "Entrando no mundo..."));

                    Console.WriteLine(
                        "[runtime-ready]");

                    _loading.Hide();
                };

            _runtimeWindow.FormClosed +=
                (_, _) =>
                {
                    _closing = true;

                    _runtimeWindow.StreamingCenterChanged -=
                        OnStreamingCenterChanged;
                    _runtimeWindow.DriveOpsMessageRequested -=
                        OnDriveOpsMessageRequested;
                    _runtimeWindow.CommsLinkTransmitRequested -=
                        OnCommsLinkTransmitRequested;
                    _runtimeWindow.KeyDown -=
                        OnCommsLinkKeyDown;
                    _runtimeWindow.KeyUp -=
                        OnCommsLinkKeyUp;

                    _commsLinkVoice.Dispose();

                    _runtimeWindow.Dispose();
                    _runtimeWindow = null;

                    DisposeMultiplayerSession();
                    DisposePluginClients();

                    if (!_loading.IsDisposed)
                    {
                        _loading.Close();
                    }

                    ExitThread();
                };

            // Let the 96% loading-state paint before the heavier GPU
            // resource preparation runs on the UI thread.
            await Task.Yield();

            _runtimeWindow.PrepareForDisplay();

            ReportProgress(
                new WorldLoadProgress(
                    99,
                    "Finalizando",
                    "Renderização preparada · apresentando primeiro frame..."));

            MainForm =
                _runtimeWindow;

            _runtimeWindow.Show();
        }
        catch (Exception ex)
        {
            DisposeMultiplayerSession();
            _commsLinkVoice.Dispose();
            DisposePluginClients();

            Console.Error.WriteLine(ex);

            ReportProgress(
                new WorldLoadProgress(
                    100,
                    "Falha ao iniciar",
                    ex.Message));

            if (_externalLoading)
            {
                _loading.Close();
                ExitThread();
            }
            else
            {
                _loading.ShowFailure(
                    ex.Message);
            }
        }
    }

    private async Task ValidateD3D12SceneAsync(
        RuntimeWindowInfo runtimeInfo)
    {
        if (!string.Equals(
                _options.RuntimeGraphicsBackend,
                "D3D12",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var support =
            D3D12BackendProbe.Probe(
                _options.RuntimePreferHardwareGpu);

        if (!support.Available)
        {
            Console.WriteLine(
                $"[d3d12-scene] skipped; no compatible adapter. error={support.Error ?? "<none>"}");
            return;
        }

        try
        {
            var buildStarted =
                Stopwatch.GetTimestamp();

            var geometry =
                await Task.Run(
                    () =>
                    {
                        var terrain =
                            RuntimeTerrainGeometryBuilder.Build(
                                runtimeInfo.Tiles,
                                runtimeInfo.GroundTextures,
                                runtimeInfo.Splines);

                        var splines =
                            RuntimeSplineGeometryBuilder.Build(
                                runtimeInfo.Splines);

                        var objects =
                            RuntimeObjectGeometryBuilder.Build(
                                runtimeInfo.Tiles,
                                runtimeInfo.Objects,
                                runtimeInfo.SceneryAssets,
                                useNativeOmsiModelSpace:
                                    true,
                                isolatedObjectIds:
                                    runtimeInfo.DynamicSceneryObjectIds);

                        var vehicle =
                            RuntimeVehicleGeometry.Build(
                                runtimeInfo.Vehicle,
                                viewpointBit:
                                    1);

                        return (
                            Terrain: terrain,
                            Splines: splines,
                            Objects: objects,
                            Vehicle: vehicle
                        );
                    });

            var buildElapsed =
                Stopwatch.GetElapsedTime(
                    buildStarted);

            if (geometry.Terrain.Vertices.Length ==
                    0 &&
                geometry.Splines.Vertices.Length ==
                    0 &&
                geometry.Objects.Vertices.Length ==
                    0 &&
                geometry.Vehicle.Vertices.Length ==
                    0)
            {
                Console.WriteLine(
                    $"[d3d12-scene] skipped; no geometry; buildMs={buildElapsed.TotalMilliseconds:0.0}");
                return;
            }

            using var form =
                new Form
                {
                    Text =
                        "OMSI Compatible Runtime - D3D12 Scene Validation",
                    ClientSize =
                        new Size(
                            640,
                            360),
                    StartPosition =
                        FormStartPosition.Manual,
                    Location =
                        new Point(
                            -32000,
                            -32000),
                    ShowInTaskbar =
                        false,
                    FormBorderStyle =
                        FormBorderStyle.FixedToolWindow
                };

            form.CreateControl();

            using var graphics =
                D3D12PresentationContext.Create(
                    form.Handle,
                    form.ClientSize.Width,
                    form.ClientSize.Height,
                    _options.RuntimePreferHardwareGpu);

            var uploadStarted =
                Stopwatch.GetTimestamp();

            using var terrain =
                geometry.Terrain.Vertices.Length >
                        0
                    ? graphics.CreateTerrainResources(
                        geometry.Terrain)
                    : null;

            using var splines =
                geometry.Splines.Vertices.Length >
                        0
                    ? graphics.CreateObjectResources(
                        geometry.Splines.Vertices,
                        geometry.Splines.Batches
                            .Select(
                                static batch =>
                                    new RuntimeObjectDrawBatch(
                                        batch.StartVertex,
                                        batch.VertexCount,
                                        batch.TexturePath,
                                        batch.AlphaCutout,
                                        batch.AlphaBlend,
                                        batch.TransMapTexturePath,
                                        batch.NoZWrite,
                                        batch.NoZCheck))
                            .ToArray())
                    : null;

            using var objects =
                geometry.Objects.Vertices.Length >
                        0
                    ? graphics.CreateObjectResources(
                        geometry.Objects.Vertices,
                        geometry.Objects.Batches
                            .Select(
                                static batch =>
                                    new RuntimeObjectDrawBatch(
                                        batch.StartVertex,
                                        batch.VertexCount,
                                        batch.TexturePath,
                                        batch.AlphaCutout,
                                        batch.AlphaBlend,
                                        batch.TransMapTexturePath,
                                        batch.NoZWrite,
                                        batch.NoZCheck))
                            .ToArray())
                    : null;

            using var vehicle =
                geometry.Vehicle.Vertices.Length >
                        0
                    ? graphics.CreateObjectResources(
                        geometry.Vehicle.Vertices,
                        geometry.Vehicle.Batches
                            .Select(
                                static batch =>
                                    new RuntimeObjectDrawBatch(
                                        batch.StartVertex,
                                        batch.VertexCount,
                                        batch.TexturePath,
                                        batch.AlphaCutout,
                                        batch.AlphaBlend,
                                        batch.TransMapTexturePath,
                                        batch.NoZWrite,
                                        batch.NoZCheck))
                            .ToArray())
                    : null;

            var uploadElapsed =
                Stopwatch.GetElapsedTime(
                    uploadStarted);

            var span =
                MathF.Max(
                    geometry.Terrain.HorizontalSpan,
                    300.0f);

            var center =
                geometry.Terrain.Vertices.Length >
                        0
                    ? geometry.Terrain.Center
                    : new Vector3(
                        (float)(
                            runtimeInfo.Tiles.Average(
                                static tile =>
                                    tile.X) *
                            300.0 +
                            150.0),
                        0.0f,
                        (float)(
                            runtimeInfo.Tiles.Average(
                                static tile =>
                                    tile.Y) *
                            300.0 +
                            150.0));

            graphics.SetRenderOrigin(
                center);

            var relativeEye =
                new Vector3(
                    0.0f,
                    MathF.Max(
                        span *
                            0.45f,
                        100.0f),
                    -MathF.Max(
                        span *
                            0.65f,
                        150.0f));

            var view =
                Matrix4x4.CreateLookAt(
                    relativeEye,
                    Vector3.Zero,
                    Vector3.UnitY);

            var projection =
                Matrix4x4.CreatePerspectiveFieldOfView(
                    MathF.PI /
                        3.0f,
                    form.ClientSize.Width /
                        (float)form.ClientSize.Height,
                    1.0f,
                    MathF.Max(
                        span *
                            8.0f,
                        5000.0f));

            graphics.SetViewProjection(
                view *
                projection);

            var drawStarted =
                Stopwatch.GetTimestamp();

            var vehicleModel =
                runtimeInfo.Spawn is
                    { } spawn
                    ? Matrix4x4.CreateRotationY(
                          (float)(
                              spawn.HeadingDegrees *
                              Math.PI /
                              180.0)) *
                      Matrix4x4.CreateTranslation(
                          (float)spawn.X,
                          (float)spawn.Y,
                          (float)spawn.Z)
                    : Matrix4x4.Identity;

            graphics.DrawSceneAndPresent(
                terrain,
                splines,
                objects,
                vehicle,
                vehicleModel,
                0.04f,
                0.06f,
                0.09f,
                vsync:
                    false);

            var drawElapsed =
                Stopwatch.GetElapsedTime(
                    drawStarted);

            Console.WriteLine(
                $"[d3d12-scene] success; terrainVertices={geometry.Terrain.Vertices.Length:N0}; terrainBatches={geometry.Terrain.Batches.Count:N0}; splineVertices={geometry.Splines.Vertices.Length:N0}; objectVertices={geometry.Objects.Vertices.Length:N0}; vehicleVertices={geometry.Vehicle.Vertices.Length:N0}; buildMs={buildElapsed.TotalMilliseconds:0.0}; uploadMs={uploadElapsed.TotalMilliseconds:0.0}; drawMs={drawElapsed.TotalMilliseconds:0.0}; span={span:0.0}m");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[d3d12-scene] validation failed; continuing with D3D11 fallback: {exception.Message}");
        }
    }

    private static void WriteWorldLoadDiagnostics(
        WorldDefinition world)
    {
        try
        {
            var logPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "world-load.log");

            var placementCounts =
                world.Objects
                    .GroupBy(
                        static item =>
                            item.AssetPath,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static group =>
                            group.Key,
                        static group =>
                            group.Count(),
                        StringComparer.OrdinalIgnoreCase);

            var scenery =
                world.SceneryAssets
                    .OrderBy(
                        static pair =>
                            pair.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var renderable =
                scenery.Count(
                    static pair =>
                        pair.Value.IsRenderable);

            var protectedAssets =
                scenery.Count(
                    static pair =>
                        pair.Value.ProtectedMeshCount > 0);

            var missingAssets =
                scenery.Count(
                    static pair =>
                        !pair.Value.Exists);

            var editorOnlyAssets =
                scenery.Count(
                    static pair =>
                        pair.Value.OnlyEditor);

            var failedMeshGroups =
                scenery
                    .SelectMany(
                        static pair =>
                            pair.Value.Meshes)
                    .Where(
                        static mesh =>
                            !string.IsNullOrWhiteSpace(
                                mesh.ErrorCode))
                    .GroupBy(
                        static mesh =>
                            mesh.ErrorCode!,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(
                        static group =>
                            group.Count())
                    .Select(
                        static group =>
                            $"{group.Key}={group.Count()}")
                    .ToArray();

            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"world={world.Name}",
                    $"tilesActive={world.Tiles.Count}",
                    $"tilesTotal={world.TotalTileCount}",
                    $"objects={world.Objects.Count}",
                    $"splines={world.Splines.Count}",
                    $"sceneryAssetTypes={scenery.Length}",
                    $"sceneryRenderable={renderable}",
                    $"sceneryMissing={missingAssets}",
                    $"sceneryEncrypted={protectedAssets}",
                    $"sceneryOnlyEditor={editorOnlyAssets}",
                    $"placementParseIssues={world.PlacementParseIssueCount}",
                    $"terrainParseIssues={world.TerrainParseIssueCount}",
                    $"meshErrors={(failedMeshGroups.Length == 0 ? "<none>" : string.Join("; ", failedMeshGroups))}",
                    "",
                    "sceneryAssets:"
                };

            foreach (var pair in scenery)
            {
                var asset =
                    pair.Value;

                placementCounts.TryGetValue(
                    pair.Key,
                    out var placements);

                var errors =
                    asset.Meshes
                        .Where(
                            static mesh =>
                                !string.IsNullOrWhiteSpace(
                                    mesh.ErrorCode))
                        .GroupBy(
                            static mesh =>
                                mesh.ErrorCode!,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(
                            static group =>
                                $"{group.Key}:{group.Count()}")
                        .ToArray();

                lines.Add(
                    $"{pair.Key} | placements={placements} | exists={asset.Exists} | renderable={asset.IsRenderable} | editorOnly={asset.OnlyEditor} | meshes={asset.Meshes.Count} | renderableMeshes={asset.RenderableMeshCount} | encryptedMeshes={asset.ProtectedMeshCount} | tree={asset.Tree is not null} | resolved={asset.ResolvedPath ?? "<null>"} | errors={(errors.Length == 0 ? "<none>" : string.Join(",", errors))}");
            }

            File.WriteAllLines(
                logPath,
                lines);

            Console.WriteLine(
                $"[world-load] tiles={world.Tiles.Count}/{world.TotalTileCount}; objects={world.Objects.Count}; scenery={renderable}/{scenery.Length} renderable; missing={missingAssets}; encrypted={protectedAssets}; editorOnly={editorOnlyAssets}; meshErrors={(failedMeshGroups.Length == 0 ? "<none>" : string.Join("; ", failedMeshGroups))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[world-load] unable to write diagnostics: {ex.Message}");
        }
    }

    private static void WriteVehicleLoadDiagnostics(
        OmsiVehicleAsset vehicle,
        OmsiContentRoot contentRoot)
    {
        try
        {
            var logPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "vehicle-load.log");

            var errorGroups =
                vehicle.Meshes
                    .Where(
                        static mesh =>
                            !string.IsNullOrWhiteSpace(
                                mesh.ErrorCode))
                    .GroupBy(
                        static mesh =>
                            mesh.ErrorCode!,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(
                        static group =>
                            group.Count())
                    .Select(
                        static group =>
                            $"{group.Key}={group.Count()}")
                    .ToArray();

            var lodGroups =
                vehicle.Meshes
                    .GroupBy(
                        static mesh =>
                            mesh.LodThreshold)
                    .OrderByDescending(
                        static group =>
                            group.Key ??
                            double.PositiveInfinity)
                    .Select(
                        static group =>
                            $"{(group.Key.HasValue ? group.Key.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "<none>")}:{group.Count()}")
                    .ToArray();

            var failedExamples =
                vehicle.Meshes
                    .Where(
                        static mesh =>
                            !mesh.IsRenderable)
                    .Take(20)
                    .Select(
                        static mesh =>
                            $"{mesh.DeclaredPath} | resolved={mesh.ResolvedPath ?? "<null>"} | error={mesh.ErrorCode ?? "<none>"}")
                    .ToArray();

            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"bus={vehicle.Bus.DisplayName}",
                    $"busFile={vehicle.Bus.FilePath}",
                    $"modelCfg={vehicle.Bus.ModelConfigPath ?? "<null>"}",
                    $"modelCfgExists={vehicle.Bus.ModelConfigPath is not null && File.Exists(vehicle.Bus.ModelConfigPath)}",
                    $"sections={vehicle.SectionCount}",
                    $"meshTotal={vehicle.Meshes.Count}",
                    $"meshRenderable={vehicle.RenderableMeshCount}",
                    $"meshEncrypted={vehicle.ProtectedMeshCount}",
                    $"meshFailed={vehicle.FailedMeshCount}",
                    $"driverCameras={vehicle.Bus.DriverCameras.Count}",
                    $"passengerCameras={vehicle.Bus.PassengerCameras.Count}",
                    $"lodGroups={(lodGroups.Length == 0 ? "<none>" : string.Join(", ", lodGroups))}",
                    $"errors={(errorGroups.Length == 0 ? "<none>" : string.Join("; ", errorGroups))}",
                    "",
                    "failedMeshes:"
                };

            lines.AddRange(
                failedExamples);

            lines.Add("");
            lines.Add("sectionScripts:");

            foreach (var section in
                     vehicle.Sections ??
                     Array.Empty<OmsiVehicleSectionAssetInfo>())
            {
                var manifest =
                    section.ScriptManifest;

                if (manifest is null)
                {
                    lines.Add(
                        $"section={section.Index} | scripts=<none>");
                    continue;
                }

                lines.Add(
                    $"section={section.Index} | registered={manifest.RegisteredFileCount} | missing={manifest.MissingFileCount} | scripts={manifest.ScriptFiles.Count} | varlists={manifest.VariableLists.Count} | stringvarlists={manifest.StringVariableLists.Count} | constfiles={manifest.ConstantFiles.Count}");

                foreach (var script in
                         manifest.ScriptFiles)
                {
                    lines.Add(
                        $"  script={script.DeclaredPath} | resolved={script.ResolvedPath ?? "<missing>"}");
                }

                try
                {
                    var catalog =
                        OmsiScriptCatalogLoader.Load(
                            contentRoot,
                            manifest);

                    var keyVariables =
                        new[]
                        {
                            "M_Wheel",
                            "Brakeforce",
                            "Axle_Brakeforce_0_L",
                            "Axle_Brakeforce_0_R",
                            "Axle_Brakeforce_1_L",
                            "Axle_Brakeforce_1_R",
                            "articulation_0_alpha",
                            "articulation_0_beta",
                            "engine_on",
                            "elec_busbar_main",
                            "Snd_OutsideVol"
                        }
                        .Where(
                            catalog.NumericVariables.Contains)
                        .ToArray();

                    lines.Add(
                        $"  catalogVars={catalog.NumericVariables.Count} | stringVars={catalog.StringVariables.Count} | triggers={catalog.Program.Triggers.Count} | frameBlocks={catalog.Program.FrameBlocks.Count} | initBlocks={catalog.Program.InitBlocks.Count}");

                    lines.Add(
                        $"  keyVars={(keyVariables.Length == 0 ? "<none>" : string.Join(",", keyVariables))}");

                    if (catalog.Program.Triggers.Count > 0)
                    {
                        lines.Add(
                            $"  triggerSample={string.Join(",", catalog.Program.Triggers.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).Take(40))}");
                    }

                    foreach (var diagnostic in
                             catalog.Diagnostics.Take(40))
                    {
                        lines.Add(
                            $"  diagnostic={diagnostic}");
                    }
                }
                catch (Exception exception)
                {
                    lines.Add(
                        $"  catalogError={exception.GetType().Name}: {exception.Message}");
                }
            }

            lines.Add("");
            lines.Add("meshMaterials:");

            foreach (var mesh in
                     vehicle.Meshes)
            {
                var visibility =
                    mesh.VisibilityConditions.Count == 0
                        ? "<none>"
                        : string.Join(
                            ",",
                            mesh.VisibilityConditions.Select(
                                static condition =>
                                    $"{condition.VariableName}={condition.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}"));

                lines.Add(
                    $"mesh={mesh.DeclaredPath} | renderable={mesh.IsRenderable} | viewpoint={mesh.ViewpointFlag} | lod={(mesh.LodThreshold.HasValue ? mesh.LodThreshold.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "<none>")} | materials={mesh.Materials.Count} | visible={visibility} | animations={mesh.Animations.Count} | lights={mesh.LightEffects?.Count ?? 0}");

                for (var materialIndex = 0;
                     materialIndex < mesh.Materials.Count;
                     materialIndex++)
                {
                    var material =
                        mesh.Materials[materialIndex];

                    var changeSets =
                        material.MaterialChangeSets is
                            { Count: > 0 }
                            ? string.Join(
                                ";",
                                material.MaterialChangeSets.Select(
                                    static set =>
                                        $"{set.VariableName}[{string.Join(",", set.Items.Select(static item => item.ItemIndex))}]"))
                            : "<none>";

                    lines.Add(
                        $"  mat#{materialIndex} tex={Path.GetFileName(material.TexturePath) ?? "<none>"} | alpha={material.AlphaMode} | transmapDirective={material.HasTransMapDirective} | transmap={Path.GetFileName(material.TransMapTexturePath) ?? "<none>"} | noZwrite={material.NoZWrite} | noZcheck={material.NoZCheck} | alphaScale={material.AlphaScaleVariable ?? "<none>"} | lightmap={Path.GetFileName(material.LightMapTexturePath) ?? "<none>"} | matlChange={material.MaterialChangeVariable ?? "<none>"} | changeSets={changeSets}");
                }
            }

            File.WriteAllLines(
                logPath,
                lines);

            Console.WriteLine(
                $"[vehicle-load] {string.Join("; ", lines.Take(11))}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[vehicle-load] unable to write diagnostics: {ex.Message}");
        }
    }

    private void ReportProgress(
        WorldLoadProgress progress)
    {
        if (!_loading.IsDisposed)
        {
            _loading.UpdateProgress(
                progress);
        }

        var stage =
            SanitizeProgressField(
                progress.Stage);

        var detail =
            SanitizeProgressField(
                progress.Detail);

        Console.WriteLine(
            $"[runtime-progress]|{Math.Clamp(progress.Percent, 0, 100)}|{stage}|{detail}");
    }

    private static string SanitizeProgressField(
        string value) =>
        value
            .Replace(
                '|',
                '/')
            .Replace(
                '\r',
                ' ')
            .Replace(
                '\n',
                ' ');

    private async void OnStreamingCenterChanged(
        int tileX,
        int tileY)
    {
        if (_closing ||
            _loadEntireMap)
        {
            return;
        }

        // Renderer X mirrors OMSI's map X to keep the X/Z ground plane
        // right-handed after converting OMSI X/Y + Z-up into X/Z + Y-up.
        // Convert the renderer tile coordinate back to the source OMSI tile
        // before asking WorldLoader for the next streaming window.
        _pendingStreamingCenter =
            (SourceTileXFromRuntimeTileX(
                 tileX),
             tileY);

        if (!await _streamingGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (!_closing &&
                   _pendingStreamingCenter is
                       { } requested)
            {
                _pendingStreamingCenter =
                    null;

                if (requested.X ==
                        _loadedCenterX &&
                    requested.Y ==
                        _loadedCenterY)
                {
                    continue;
                }

                Console.WriteLine(
                    $"[streaming] Loading tile window centered at {requested.X},{requested.Y}...");

                var streamedWorld =
                    await Task.Run(
                        () =>
                            WorldLoader.Load(
                                _contentRoot,
                                _map,
                                progress: null,
                                new WorldLoadOptions(
                                    requested.X,
                                    requested.Y,
                                    ActiveTileRadius:
                                        Math.Clamp(
                                            _options.RuntimeStreamingRadius,
                                            0,
                                            8),
                                    LoadEntireMap:
                                        _options.LoadWholeMapAtStart)));

                if (_closing ||
                    _runtimeWindow is null ||
                    _runtimeWindow.IsDisposed)
                {
                    return;
                }

                _currentWorld =
                    streamedWorld;

                var simulationBuildStarted =
                    Stopwatch.GetTimestamp();

                var streamedSimulations =
                    await Task.Run(
                        () =>
                        {
                            var traffic =
                                CreateTrafficSimulation(
                                    streamedWorld);
                            var line =
                                CreateLineAiSimulation(
                                    streamedWorld);
                            var rail =
                                CreateRailTrafficSimulation(
                                    streamedWorld);

                            return (
                                Traffic:
                                    traffic,
                                Line:
                                    line,
                                Rail:
                                    rail);
                        });

                _trafficSimulation =
                    streamedSimulations.Traffic;
                _lineAiSimulation =
                    streamedSimulations.Line;
                _railTrafficSimulation =
                    streamedSimulations.Rail;

                Console.WriteLine(
                    $"[streaming-perf] simulationsMs={Stopwatch.GetElapsedTime(simulationBuildStarted).TotalMilliseconds:0.0}");

                _trafficScriptRuntimes.Clear();

                await EnsureTrafficVehicleAssetsAsync(
                    _trafficSimulation,
                    _lineAiSimulation);

                await EnsureRailTrafficAssetsAsync(
                    _railTrafficSimulation);

                RebuildRailSignalScriptRuntimes(
                    streamedWorld);

                WriteTrafficDiagnostics(
                    streamedWorld,
                    _trafficSimulation);
                WriteLineAiDiagnostics(
                    _lineAiSchedule);

                WriteRailTrafficDiagnostics(
                    streamedWorld,
                    _railTrafficSimulation);

                var runtimeInfoStarted =
                    Stopwatch.GetTimestamp();

                var runtimeInfo =
                    await Task.Run(
                        () =>
                            BuildRuntimeInfo(
                                streamedWorld,
                                _vehicleAsset,
                                _entryPoint,
                                _contentRoot.RootPath,
                                _trafficVehicleAssets,
                                    SnapshotSharedWorldDepartedParkingIds()));

                Console.WriteLine(
                    $"[streaming-perf] runtimeInfoMs={Stopwatch.GetElapsedTime(runtimeInfoStarted).TotalMilliseconds:0.0}");

                await _runtimeWindow.ApplyStreamedWorldAsync(
                    runtimeInfo);

                _loadedCenterX =
                    requested.X;

                _loadedCenterY =
                    requested.Y;

                Console.WriteLine(
                    $"[streaming] Active {streamedWorld.Tiles.Count:N0}/{streamedWorld.TotalTileCount:N0} tiles around {requested.X},{requested.Y}.");

                ScheduleDirectionalPrefetch();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[streaming] {ex}");
        }
        finally
        {
            _streamingGate.Release();
        }
    }

    private void ScheduleDirectionalPrefetch()
    {
        if (_closing ||
            _loadEntireMap ||
            _runtimeWindow is null ||
            _runtimeWindow.IsDisposed ||
            _runtimeWindow.PlayerTrafficObstacle is not
                { } obstacle ||
            Math.Abs(
                obstacle.SpeedMetersPerSecond) <
                2.0)
        {
            return;
        }

        var forwardRuntimeX =
            Math.Sin(
                obstacle.HeadingRadians);

        var forwardRuntimeY =
            Math.Cos(
                obstacle.HeadingRadians);

        if (obstacle.SpeedMetersPerSecond <
            0.0)
        {
            forwardRuntimeX =
                -forwardRuntimeX;

            forwardRuntimeY =
                -forwardRuntimeY;
        }

        var runtimeOffsetX =
            Math.Abs(
                forwardRuntimeX) >=
                    0.35
                ? Math.Sign(
                    forwardRuntimeX)
                : 0;

        var sourceOffsetY =
            Math.Abs(
                forwardRuntimeY) >=
                    0.35
                ? Math.Sign(
                    forwardRuntimeY)
                : 0;

        if (runtimeOffsetX ==
                0 &&
            sourceOffsetY ==
                0)
        {
            return;
        }

        // Runtime X is mirrored relative to OMSI source tile X.
        var target =
            (
                X:
                    _loadedCenterX -
                    runtimeOffsetX,
                Y:
                    _loadedCenterY +
                    sourceOffsetY);

        if (target.X ==
                _loadedCenterX &&
            target.Y ==
                _loadedCenterY ||
            _lastPrefetchedCenter ==
                target ||
            _pendingPrefetchCenter ==
                target)
        {
            return;
        }

        _pendingPrefetchCenter =
            target;

        _ =
            ProcessDirectionalPrefetchAsync();
    }

    private async Task ProcessDirectionalPrefetchAsync()
    {
        if (!await _prefetchGate.WaitAsync(
                0))
        {
            return;
        }

        try
        {
            while (!_closing &&
                   _pendingPrefetchCenter is
                       { } requested)
            {
                _pendingPrefetchCenter =
                    null;

                if (_lastPrefetchedCenter ==
                    requested)
                {
                    continue;
                }

                Console.WriteLine(
                    $"[streaming-prefetch] warming {requested.X},{requested.Y}...");

                try
                {
                    var prefetchResult =
                        await Task.Run(
                            () =>
                            {
                                var thread =
                                    Thread.CurrentThread;

                                var previousPriority =
                                    thread.Priority;

                                try
                                {
                                    thread.Priority =
                                        System.Threading.ThreadPriority
                                            .BelowNormal;

                                    var texturePaths =
                                        WorldLoader.WarmCache(
                                            _contentRoot,
                                            _map,
                                            new WorldLoadOptions(
                                                requested.X,
                                                requested.Y,
                                                ActiveTileRadius:
                                                    Math.Clamp(
                                                        _options.RuntimeStreamingRadius,
                                                        0,
                                                        4),
                                                LoadEntireMap:
                                                    false));

                                    var warmedTextureFiles =
                                        D3D11RenderWindow
                                            .WarmTextureFileCache(
                                                texturePaths);

                                    var warmedDecodedTextures =
                                        D3D11RenderWindow
                                            .WarmDecodedTextureCache(
                                                texturePaths);

                                    return (
                                        TexturePaths:
                                            texturePaths,
                                        WarmedTextureFiles:
                                            warmedTextureFiles,
                                        WarmedDecodedTextures:
                                            warmedDecodedTextures);
                                }
                                finally
                                {
                                    thread.Priority =
                                        previousPriority;
                                }
                            });

                    var texturePaths =
                        prefetchResult.TexturePaths;

                    var warmedTextureFiles =
                        prefetchResult.WarmedTextureFiles;

                    var warmedDecodedTextures =
                        prefetchResult.WarmedDecodedTextures;

                    _lastPrefetchedCenter =
                        requested;

                    var textureCacheDiagnostics =
                        D3D11RenderWindow
                            .GetTextureFileCacheDiagnostics();

                    Console.WriteLine(
                        $"[streaming-prefetch] ready {requested.X},{requested.Y}; textures={texturePaths.Count}; cachedFiles={warmedTextureFiles}; decoded={warmedDecodedTextures}; {textureCacheDiagnostics}.");
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"[streaming-prefetch] {requested.X},{requested.Y}: {exception.Message}");
                }
            }
        }
        finally
        {
            _prefetchGate.Release();
        }
    }

    private async Task EnsureTrafficVehicleAssetsAsync(
        WorldTrafficSimulation simulation,
        WorldLineAiSimulation? lineSimulation = null)
    {
        var requestedPaths =
            simulation
                .Snapshot()
                .Select(
                    static agent =>
                        agent.VehiclePath)
                .Concat(
                    lineSimulation?.RequiredVehiclePaths ??
                    Array.Empty<string>())
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    path =>
                        !_trafficVehicleAssets.ContainsKey(
                            path))
                .ToArray();

        if (requestedPaths.Length ==
            0)
        {
            return;
        }

        var assetLoadStarted =
            Stopwatch.GetTimestamp();

        var loaded =
            await Task.Run(
                () =>
                {
                    var result =
                        new List<(
                            string Path,
                            OmsiVehicleAsset Asset,
                            OmsiScriptCatalog? Catalog)>();

                    foreach (var path in
                             requestedPaths)
                    {
                        try
                        {
                            if (!File.Exists(
                                    path))
                            {
                                continue;
                            }

                            var vehicleInfo =
                                OmsiBusReader.ReadFile(
                                    _contentRoot.RootPath,
                                    path);

                            var asset =
                                OmsiArticulatedVehicleAssetLoader.Load(
                                    _contentRoot,
                                    vehicleInfo);

                            if (asset.RenderableMeshCount <=
                                0)
                            {
                                Console.WriteLine(
                                    $"[traffic-ai] Vehicle has no renderable meshes: {path}");
                                continue;
                            }

                            OmsiScriptCatalog? catalog =
                                null;

                            if (asset.Bus.ScriptManifest.RegisteredFileCount >
                                0)
                            {
                                try
                                {
                                    catalog =
                                        OmsiScriptCatalogLoader.Load(
                                            _contentRoot,
                                            asset.Bus.ScriptManifest);
                                }
                                catch (Exception exception)
                                {
                                    Console.WriteLine(
                                        $"[traffic-ai] Script catalog unavailable for {Path.GetFileName(path)}: {exception.Message}");
                                }
                            }

                            result.Add(
                                (
                                    path,
                                    asset,
                                    catalog));
                        }
                        catch (Exception exception)
                        {
                            Console.WriteLine(
                                $"[traffic-ai] Failed to load {path}: {exception.Message}");
                        }
                    }

                    return result;
                });

        foreach (var item in
                 loaded)
        {
            _trafficVehicleAssets[
                item.Path] =
                item.Asset;

            if (item.Catalog is not null)
            {
                _trafficScriptCatalogs[
                    item.Path] =
                    item.Catalog;
            }
        }

        Console.WriteLine(
            $"[traffic-ai] assetCatalogMs={Stopwatch.GetElapsedTime(assetLoadStarted).TotalMilliseconds:0.0}");

        Console.WriteLine(
            $"[traffic-ai] cached={_trafficVehicleAssets.Count}; requested={requestedPaths.Length}; loaded={loaded.Count}");
    }

    private async Task EnsureRailTrafficAssetsAsync(
        WorldRailTrafficSimulation simulation)
    {
        var trainPaths =
            simulation
                .Snapshot()
                .Select(
                    static agent =>
                        agent.TrainConsistPath)
                .Where(
                    static path =>
                        !string.IsNullOrWhiteSpace(
                            path))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var missingTrainPaths =
            trainPaths
                .Where(
                    path =>
                        !_railTrainConsists.ContainsKey(
                            path))
                .ToArray();

        if (missingTrainPaths.Length >
            0)
        {
            var consistLoadStarted =
                Stopwatch.GetTimestamp();

            var loadedConsists =
                await Task.Run(
                    () =>
                    {
                        var result =
                            new List<KeyValuePair<
                                string,
                                OmsiTrainConsist>>();

                        foreach (var trainPath in
                                 missingTrainPaths)
                        {
                            try
                            {
                                result.Add(
                                    new KeyValuePair<
                                        string,
                                        OmsiTrainConsist>(
                                        trainPath,
                                        OmsiTrainConsistReader
                                            .ReadFile(
                                                _contentRoot.RootPath,
                                                trainPath)));
                            }
                            catch (Exception exception)
                            {
                                Console.WriteLine(
                                    $"[rail-ai] Failed to parse {trainPath}: {exception.Message}");
                            }
                        }

                        return result;
                    });

            foreach (var pair in
                     loadedConsists)
            {
                _railTrainConsists[
                    pair.Key] =
                    pair.Value;
            }

            Console.WriteLine(
                $"[rail-ai] consistsMs={Stopwatch.GetElapsedTime(consistLoadStarted).TotalMilliseconds:0.0}; requested={missingTrainPaths.Length}; loaded={loadedConsists.Count}");
        }

        var requestedVehiclePaths =
            trainPaths
                .Where(
                    path =>
                        _railTrainConsists.ContainsKey(
                            path))
                .SelectMany(
                    path =>
                        _railTrainConsists[
                            path]
                            .Vehicles)
                .Where(
                    static vehicle =>
                        vehicle.Exists)
                .Select(
                    static vehicle =>
                        vehicle.ResolvedPath!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Where(
                    path =>
                        !_trafficVehicleAssets.ContainsKey(
                            path))
                .ToArray();

        if (requestedVehiclePaths.Length >
            0)
        {
            var railAssetLoadStarted =
                Stopwatch.GetTimestamp();

            var loaded =
                await Task.Run(
                    () =>
                    {
                        var result =
                            new List<(
                                string Path,
                                OmsiVehicleAsset Asset,
                                OmsiScriptCatalog? Catalog)>();

                        foreach (var vehiclePath in
                                 requestedVehiclePaths)
                        {
                            try
                            {
                                var vehicleInfo =
                                    OmsiBusReader.ReadFile(
                                        _contentRoot.RootPath,
                                        vehiclePath);

                                var asset =
                                    OmsiVehicleAssetLoader.Load(
                                        _contentRoot,
                                        vehicleInfo);

                                if (asset.RenderableMeshCount <=
                                    0)
                                {
                                    Console.WriteLine(
                                        $"[rail-ai] Vehicle has no renderable meshes: {vehiclePath}");
                                    continue;
                                }

                                OmsiScriptCatalog? catalog =
                                    null;

                                if (asset.Bus.ScriptManifest.RegisteredFileCount >
                                    0)
                                {
                                    try
                                    {
                                        catalog =
                                            OmsiScriptCatalogLoader.Load(
                                                _contentRoot,
                                                asset.Bus.ScriptManifest);
                                    }
                                    catch (Exception exception)
                                    {
                                        Console.WriteLine(
                                            $"[rail-ai] Script catalog unavailable for {Path.GetFileName(vehiclePath)}: {exception.Message}");
                                    }
                                }

                                result.Add(
                                    (
                                        vehiclePath,
                                        asset,
                                        catalog));
                            }
                            catch (Exception exception)
                            {
                                Console.WriteLine(
                                    $"[rail-ai] Failed to load {vehiclePath}: {exception.Message}");
                            }
                        }

                        return result;
                    });

            foreach (var item in
                     loaded)
            {
                _trafficVehicleAssets[
                    item.Path] =
                    item.Asset;

                if (item.Catalog is not null)
                {
                    _trafficScriptCatalogs[
                        item.Path] =
                        item.Catalog;
                }
            }

            Console.WriteLine(
                $"[rail-ai] vehicleAssetCatalogMs={Stopwatch.GetElapsedTime(railAssetLoadStarted).TotalMilliseconds:0.0}");
        }

        foreach (var trainPath in
                 trainPaths)
        {
            if (!_railTrainConsists.TryGetValue(
                    trainPath,
                    out var consist))
            {
                continue;
            }

            var runtimeConsist =
                BuildRailRuntimeConsist(
                    consist);

            if (runtimeConsist.Cars.Count >
                0)
            {
                _railRuntimeConsists[
                    trainPath] =
                    runtimeConsist;

                if (runtimeConsist.TailClearanceMeters is
                    { } tailClearanceMeters)
                {
                    simulation.SetConsistTrailingDistance(
                        trainPath,
                        tailClearanceMeters);
                }
            }
        }

        Console.WriteLine(
            $"[rail-ai] consists={_railRuntimeConsists.Count}; requested={trainPaths.Length}; vehicles={requestedVehiclePaths.Length}");
    }

    private void CacheTrafficScriptCatalog(
        string vehiclePath,
        OmsiVehicleAsset asset)
    {
        if (_trafficScriptCatalogs.ContainsKey(
                vehiclePath) ||
            asset.Bus.ScriptManifest.RegisteredFileCount <=
                0)
        {
            return;
        }

        try
        {
            _trafficScriptCatalogs[
                vehiclePath] =
                OmsiScriptCatalogLoader.Load(
                    _contentRoot,
                    asset.Bus.ScriptManifest);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[traffic-ai] Script catalog unavailable for {Path.GetFileName(vehiclePath)}: {exception.Message}");
        }
    }

    private RailRuntimeConsist BuildRailRuntimeConsist(
        OmsiTrainConsist consist)
    {
        var cars =
            new List<RailRuntimeCar>();
        OmsiVehicleAsset? previousAsset =
            null;
        var previousReverse =
            false;
        var trailingDistance =
            0.0;

        foreach (var vehicle in
                 consist.Vehicles)
        {
            if (!vehicle.Exists ||
                !_trafficVehicleAssets.TryGetValue(
                    vehicle.ResolvedPath!,
                    out var asset))
            {
                Console.WriteLine(
                    $"[rail-ai] Missing consist vehicle: {vehicle.DeclaredPath}");
                break;
            }

            if (previousAsset is not null)
            {
                var spacing =
                    ResolveRailCarSpacing(
                        previousAsset,
                        previousReverse,
                        asset,
                        vehicle.Reverse);

                if (!spacing.HasValue)
                {
                    Console.WriteLine(
                        $"[rail-ai] Missing/invalid coupling geometry between {Path.GetFileName(previousAsset.Bus.FilePath)} and {Path.GetFileName(asset.Bus.FilePath)}; remaining consist cars are not placed.");
                    break;
                }

                trailingDistance +=
                    spacing.Value;
            }

            cars.Add(
                new RailRuntimeCar(
                    vehicle.ResolvedPath!,
                    vehicle.Reverse,
                    trailingDistance));

            previousAsset =
                asset;
            previousReverse =
                vehicle.Reverse;
        }

        double? tailClearanceMeters =
            null;

        if (cars.Count >
                0 &&
            previousAsset is not null)
        {
            var rearOverhang =
                ResolveRailRearOverhang(
                    previousAsset,
                    previousReverse);

            if (rearOverhang.HasValue)
            {
                tailClearanceMeters =
                    trailingDistance +
                    rearOverhang.Value;
            }
        }

        return new RailRuntimeConsist(
            consist.SourcePath,
            cars,
            tailClearanceMeters);
    }

    private static double? ResolveRailRearOverhang(
        OmsiVehicleAsset asset,
        bool reverse)
    {
        var rearCouplingLongitudinal =
            reverse
                ? asset.Bus.FrontCoupling?.Y
                : asset.Bus.BackCoupling?.Y;

        if (!rearCouplingLongitudinal.HasValue ||
            !double.IsFinite(
                rearCouplingLongitudinal.Value))
        {
            return null;
        }

        var distance =
            Math.Abs(
                rearCouplingLongitudinal.Value);

        return distance >
               0.1
            ? distance
            : null;
    }

    private static double? ResolveRailCarSpacing(
        OmsiVehicleAsset previous,
        bool previousReverse,
        OmsiVehicleAsset current,
        bool currentReverse)
    {
        var previousRear =
            previousReverse
                ? previous.Bus.FrontCoupling is
                    { } previousFront
                    ? -previousFront.Y
                    : (double?)null
                : previous.Bus.BackCoupling?.Y;

        var currentFront =
            currentReverse
                ? current.Bus.BackCoupling is
                    { } currentBack
                    ? -currentBack.Y
                    : (double?)null
                : current.Bus.FrontCoupling?.Y;

        if (!previousRear.HasValue ||
            !currentFront.HasValue)
        {
            return null;
        }

        var spacing =
            Math.Abs(
                previousRear.Value -
                currentFront.Value);

        return double.IsFinite(
                   spacing) &&
               spacing >
                   0.5
            ? spacing
            : null;
    }

    private WorldTrafficSimulation
        CreateTrafficSimulation(
            WorldDefinition world)
    {
        // Honor the OMSI random-traffic ceiling directly. The simulation
        // now applies an additional active-road-length capacity and spacing
        // filter, so large maps can use the configured population without
        // flooding a small streamed window.
        var configuredMaximum =
            Math.Clamp(
                _options.MaximumUnscheduledTraffic,
                0,
                100);

        var configuredFactor =
            Math.Clamp(
                _options.RoadTrafficFactorPercent,
                0,
                100) /
            100.0;

        var maximumAgents =
            (int)Math.Round(
                configuredMaximum *
                configuredFactor);

        var spawnIntervalSeconds =
            configuredFactor <=
                    0.0
                ? 60.0
                : Math.Clamp(
                    0.75 +
                    (1.0 -
                     configuredFactor) *
                    3.25,
                    0.75,
                    4.0);

        return new WorldTrafficSimulation(
            world.TrafficPaths,
            world.AiCatalog,
            maximumAgents:
                maximumAgents,
            spawnExclusionCenter:
                new WorldVector3(
                    _entryPoint.WorldX,
                    _entryPoint.WorldY,
                    _entryPoint.WorldZ),
            spawnExclusionRadiusMeters:
                45.0,
            spawnIntervalSeconds:
                spawnIntervalSeconds);
    }

    private WorldLineAiSimulation
        CreateLineAiSimulation(
            WorldDefinition world)
    {
        _lineAiSchedule =
            WorldLineAiScheduleResolver.Resolve(
                _timetableCatalog.Value,
                world.TrafficPaths,
                world.AiCatalog);

        var serviceDate =
            DateOnly.FromDateTime(
                DateTime.Today);

        var dayBits =
            OmsiTimetableDayMask.Resolve(
                serviceDate,
                _mapCalendar.Value);

        return new WorldLineAiSimulation(
            _lineAiSchedule,
            world.TrafficPaths,
            maximumActiveAgents:
                Math.Clamp(
                    _options.MaximumScheduledTraffic,
                    0,
                    100),
            serviceStartMinutes:
                _lineAiServiceMinutes,
            serviceDaySeed:
                serviceDate.DayOfYear,
            maximumLinePriority:
                Math.Clamp(
                    _options.ScheduledTrafficPriority,
                    1,
                    4),
            requiredDayBit:
                dayBits.DayBit,
            requiredSchoolBit:
                dayBits.SchoolBit);
    }

    private static WorldRailTrafficSimulation
        CreateRailTrafficSimulation(
            WorldDefinition world) =>
        new(
            world.TrafficPaths,
            world.AiCatalog,
            maximumAgents:
                4,
            signalRoutes:
                world.SignalRoutes);

    private IReadOnlyList<RuntimeRailSignalRouteStateInfo>
        GetRailSignalRouteStates()
    {
        var simulation =
            _railTrafficSimulation;

        if (simulation is null)
        {
            return Array.Empty<
                RuntimeRailSignalRouteStateInfo>();
        }

        _worldRailSignalRouteStateBuffer.Clear();
        simulation.AppendSignalRouteSnapshotTo(
            _worldRailSignalRouteStateBuffer);

        _railSignalBestStateByObjectId.Clear();

        foreach (var state in
                 _worldRailSignalRouteStateBuffer)
        {
            if (state.Signal is null)
            {
                continue;
            }

            var objectId =
                state.Signal.ObjectId;

            if (!_railSignalBestStateByObjectId.TryGetValue(
                    objectId,
                    out var current) ||
                state.Reserved &&
                !current.Reserved ||
                state.Reserved ==
                    current.Reserved &&
                state.RouteIndex <
                    current.RouteIndex)
            {
                _railSignalBestStateByObjectId[
                    objectId] =
                    state;
            }
        }

        foreach (var pair in
                 _railSignalScriptRuntimes)
        {
            var runtime =
                pair.Value;

            var signalState =
                _railSignalBestStateByObjectId.TryGetValue(
                    pair.Key,
                    out var state) &&
                state.Reserved
                    ? state.Signal!.SignalState
                    : 0;

            runtime.SetLocal(
                "Signal",
                signalState);

            runtime.SetLocal(
                "NextSignal",
                signalState);

            runtime.ExecuteFrame();
        }

        _runtimeRailSignalRouteStateBuffer.Clear();

        if (_runtimeRailSignalRouteStateBuffer.Capacity <
            _worldRailSignalRouteStateBuffer.Count)
        {
            _runtimeRailSignalRouteStateBuffer.Capacity =
                _worldRailSignalRouteStateBuffer.Count;
        }

        foreach (var state in
                 _worldRailSignalRouteStateBuffer)
        {
            if (state.Signal is null)
            {
                continue;
            }

            _runtimeRailSignalRouteStateBuffer.Add(
                new RuntimeRailSignalRouteStateInfo(
                    state.RouteIndex,
                    state.Signal.ObjectId,
                    state.Signal.SignalState,
                    state.Reserved,
                    state.ReservedAgentIndex,
                    _railSignalScriptRuntimes.TryGetValue(
                        state.Signal.ObjectId,
                        out var runtime)
                        ? runtime
                        : null));
        }

        return _runtimeRailSignalRouteStateBuffer;
    }

    private void RebuildRailSignalScriptRuntimes(
        WorldDefinition world)
    {
        _railSignalScriptRuntimes.Clear();

        var signalObjectIds =
            WorldRailSignalRouteResolver
                .Resolve(
                    world.TrafficPaths,
                    world.SignalRoutes)
                .Select(
                    static route =>
                        route.Signal?.ObjectId)
                .Where(
                    static objectId =>
                        objectId.HasValue)
                .Select(
                    static objectId =>
                        objectId!.Value)
                .Distinct()
                .ToHashSet();

        if (signalObjectIds.Count ==
            0)
        {
            return;
        }

        foreach (var placement in
                 world.Objects.Where(
                     placement =>
                         signalObjectIds.Contains(
                             placement.Id)))
        {
            if (!world.SceneryAssets.TryGetValue(
                    placement.AssetPath,
                    out var asset) ||
                asset.ScriptManifest is not
                    { RegisteredFileCount: > 0 } manifest)
            {
                continue;
            }

            try
            {
                var vehicleManifest =
                    new OmsiVehicleScriptManifest(
                        manifest.ScriptFiles
                            .Select(
                                static file =>
                                    new OmsiVehicleFileReference(
                                        file.DeclaredPath,
                                        file.ResolvedPath))
                            .ToArray(),
                        manifest.VariableLists
                            .Select(
                                static file =>
                                    new OmsiVehicleFileReference(
                                        file.DeclaredPath,
                                        file.ResolvedPath))
                            .ToArray(),
                        manifest.StringVariableLists
                            .Select(
                                static file =>
                                    new OmsiVehicleFileReference(
                                        file.DeclaredPath,
                                        file.ResolvedPath))
                            .ToArray(),
                        manifest.ConstantFiles
                            .Select(
                                static file =>
                                    new OmsiVehicleFileReference(
                                        file.DeclaredPath,
                                        file.ResolvedPath))
                            .ToArray());

                var catalog =
                    OmsiScriptCatalogLoader.Load(
                        _contentRoot,
                        vehicleManifest);

                var runtime =
                    new OmsiScriptRuntime(
                        catalog);

                runtime.SetLocal(
                    "Signal",
                    0.0);

                runtime.SetLocal(
                    "NextSignal",
                    0.0);

                runtime.ExecuteInit();

                _railSignalScriptRuntimes[
                    placement.Id] =
                    runtime;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[rail-signal] Script runtime unavailable for object {placement.Id}: {exception.Message}");
            }
        }

        Console.WriteLine(
            $"[rail-signal] scriptRuntimes={_railSignalScriptRuntimes.Count}; referencedObjects={signalObjectIds.Count}");
    }

    private void ApplyTrafficCollisionResponse(
        int agentIndex,
        float relativeImpactSpeedKph)
    {
        _trafficSimulation?
            .ApplyCollisionResponse(
                agentIndex,
                relativeImpactSpeedKph);
    }

    private IReadOnlyList<RuntimeTrafficSignalStateInfo>
        GetTrafficSignalStates()
    {
        _runtimeTrafficSignalStateBuffer.Clear();

        if (_trafficSimulation is
            { } roadSimulation)
        {
            _worldTrafficSignalStateBuffer.Clear();
            roadSimulation.AppendTrafficSignalSnapshotTo(
                _worldTrafficSignalStateBuffer);

            if (_runtimeTrafficSignalStateBuffer.Capacity <
                _worldTrafficSignalStateBuffer.Count)
            {
                _runtimeTrafficSignalStateBuffer.Capacity =
                    _worldTrafficSignalStateBuffer.Count;
            }

            foreach (var state in
                     _worldTrafficSignalStateBuffer)
            {
                _runtimeTrafficSignalStateBuffer.Add(
                    new RuntimeTrafficSignalStateInfo(
                        state.SegmentIndex,
                        state.Phase,
                        state.PositionSeconds));
            }
        }

        var sharedFrameFresh =
            _multiplayerSession is
                {
                    Role:
                        OpenOmsiLanRole.Client,
                    Connected:
                        true
                } &&
            _sharedWorldLastFrameAt.HasValue &&
            DateTimeOffset.UtcNow -
                _sharedWorldLastFrameAt.Value <=
                TimeSpan.FromSeconds(
                    3.5);

        if (!sharedFrameFresh ||
            _currentWorld is not
                { } sharedWorld)
        {
            return _runtimeTrafficSignalStateBuffer;
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var stale in
                 _sharedWorldLights
                     .Where(
                         pair =>
                             now -
                                 pair.Value.LastSeen >
                             TimeSpan.FromSeconds(
                                 3.5))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _sharedWorldLights.Remove(
                stale);
        }

        var remoteBySegment =
            new Dictionary<int, RuntimeTrafficSignalStateInfo>();

        foreach (var segment in
                 sharedWorld.TrafficPaths.Segments)
        {
            if (!segment.SceneryObjectId.HasValue ||
                segment.TrafficSignal is null ||
                !_sharedWorldLights.TryGetValue(
                    segment.SceneryObjectId.Value,
                    out var remote))
            {
                continue;
            }

            var positionSeconds =
                remote.State.PositionSeconds;

            if (!remote.State.Held)
            {
                positionSeconds +=
                    Math.Max(
                        0.0,
                        (
                            now -
                            remote.LastSeen
                        ).TotalSeconds);
            }

            remoteBySegment[
                segment.Index] =
                new RuntimeTrafficSignalStateInfo(
                    segment.Index,
                    ResolveSharedSignalPhase(
                        segment.TrafficSignal,
                        positionSeconds),
                    positionSeconds);
        }

        if (remoteBySegment.Count ==
            0)
        {
            return _runtimeTrafficSignalStateBuffer;
        }

        _sharedWorldSignalStateBuffer.Clear();

        foreach (var local in
                 _runtimeTrafficSignalStateBuffer)
        {
            if (remoteBySegment.Remove(
                    local.SegmentIndex,
                    out var remote))
            {
                _sharedWorldSignalStateBuffer.Add(
                    remote);
            }
            else
            {
                _sharedWorldSignalStateBuffer.Add(
                    local);
            }
        }

        foreach (var remote in
                 remoteBySegment.Values
                     .OrderBy(
                         static state =>
                             state.SegmentIndex))
        {
            _sharedWorldSignalStateBuffer.Add(
                remote);
        }

        return _sharedWorldSignalStateBuffer;
    }

    private static int? ResolveSharedSignalPhase(
        WorldTrafficSignalProgram program,
        double positionSeconds)
    {
        if (program.Phases.Count ==
            0)
        {
            return null;
        }

        var cycle =
            program.CycleSeconds >
                    0.001
                ? program.CycleSeconds
                : program.Phases.Sum(
                    static phase =>
                        phase.DurationSeconds);

        if (cycle <=
            0.001)
        {
            return program.Phases[
                0].Phase;
        }

        var position =
            (
                positionSeconds %
                cycle +
                cycle
            ) %
            cycle;

        foreach (var phase in
                 program.Phases)
        {
            if (position <
                phase.DurationSeconds)
            {
                return phase.Phase;
            }

            position -=
                phase.DurationSeconds;
        }

        return program.Phases[
            ^1].Phase;
    }

    private IReadOnlyList<RuntimeTrafficAgentInfo>
        StepTrafficSimulation(
            double deltaSeconds)
    {
        _trafficAgentStateBuffer.Clear();

        var agents =
            _trafficAgentStateBuffer;

        var playerObstacle =
            _runtimeWindow?
                .PlayerTrafficObstacle;

        var usingSharedWorld =
            TryAppendSharedWorldAgents(
                agents);

        var worldPlayerObstacle =
            playerObstacle is null
                ? null
                : new WorldTrafficObstacleState(
                    new WorldVector3(
                        -playerObstacle.X,
                        playerObstacle.Y,
                        playerObstacle.Z),
                    -playerObstacle.HeadingRadians,
                    playerObstacle.SpeedMetersPerSecond,
                    playerObstacle.HalfLengthMeters,
                    playerObstacle.HalfWidthMeters);

        if (!usingSharedWorld)
        {
        if (_trafficSimulation is
            { } roadSimulation)
        {
            roadSimulation.SetExternalObstacle(
                worldPlayerObstacle);

            if (double.IsFinite(
                    deltaSeconds) &&
                deltaSeconds >
                    0.0)
            {
                roadSimulation.Step(
                    deltaSeconds);
            }

            roadSimulation.AppendSnapshotTo(
                agents);
        }

        if (_lineAiSimulation is
            { } lineSimulation)
        {
            lineSimulation.SetExternalObstacle(
                worldPlayerObstacle);

            if (double.IsFinite(
                    deltaSeconds) &&
                deltaSeconds >
                    0.0)
            {
                lineSimulation.Step(
                    deltaSeconds);
            }

            _lineAiServiceMinutes =
                lineSimulation.ServiceMinutes;

            foreach (var lineAgent in
                     lineSimulation.Snapshot())
            {
                agents.Add(
                    new WorldTrafficAgentState(
                        lineAgent.AgentIndex,
                        lineAgent.SegmentIndex,
                        lineAgent.DistanceMeters,
                        lineAgent.SpeedMetersPerSecond,
                        lineAgent.VehiclePath,
                        lineAgent.Position,
                        lineAgent.HeadingRadians,
                        null,
                        $"LineAI {lineAgent.LineName}",
                        lineAgent.AiBrakeLight,
                        false,
                        false,
                        lineAgent.TraveledDistanceMeters,
                        0.0,
                        lineAgent.LineName,
                        lineAgent.Destination,
                        lineAgent.TourNumber,
                        lineAgent.TripName,
                        lineAgent.DepotHofName,
                        AtStation:
                            lineAgent.AtStation));
            }
        }

        if (_railTrafficSimulation is
            { } railSimulation)
        {
            if (double.IsFinite(
                    deltaSeconds) &&
                deltaSeconds >
                    0.0)
            {
                railSimulation.Step(
                    deltaSeconds);
            }

            _railTrafficAgentStateBuffer.Clear();
            railSimulation.AppendSnapshotTo(
                _railTrafficAgentStateBuffer);

            foreach (var train in
                     _railTrafficAgentStateBuffer)
            {
                if (!_railRuntimeConsists.TryGetValue(
                        train.TrainConsistPath,
                        out var consist))
                {
                    continue;
                }

                for (var carIndex = 0;
                     carIndex <
                         consist.Cars.Count;
                     carIndex++)
                {
                    var car =
                        consist.Cars[
                            carIndex];

                    int segmentIndex;
                    double segmentDistanceMeters;
                    WorldVector3 position;
                    double headingRadians;

                    if (car.TrailingDistanceMeters <=
                            0.000001)
                    {
                        segmentIndex =
                            train.SegmentIndex;
                        segmentDistanceMeters =
                            train.DistanceMeters;
                        position =
                            train.Position;
                        headingRadians =
                            train.HeadingRadians;
                    }
                    else if (!railSimulation.TrySampleBehind(
                                 train.AgentIndex,
                                 car.TrailingDistanceMeters,
                                 out segmentIndex,
                                 out segmentDistanceMeters,
                                 out position,
                                 out headingRadians))
                    {
                        continue;
                    }

                    if (car.Reverse)
                    {
                        headingRadians =
                            Math.Atan2(
                                -Math.Sin(
                                    headingRadians),
                                -Math.Cos(
                                    headingRadians));
                    }

                    agents.Add(
                        new WorldTrafficAgentState(
                            2_000_000 +
                            train.AgentIndex *
                            1_000 +
                            carIndex,
                            segmentIndex,
                            segmentDistanceMeters,
                            train.SpeedMetersPerSecond,
                            car.VehiclePath,
                            position,
                            headingRadians,
                            train.GroupIndex,
                            train.GroupName,
                            false,
                            false,
                            false,
                            train.TraveledDistanceMeters,
                            0.0));
                }
            }
        }

        }

        PublishSharedWorld(
            deltaSeconds,
            agents);

        if (agents.Count ==
            0)
        {
            _trafficScriptRuntimes.Clear();
        }
        else
        {
            UpdateTrafficScriptRuntimes(
                agents,
                deltaSeconds);
        }

        _runtimeTrafficAgentBuffer.Clear();

        if (_runtimeTrafficAgentBuffer.Capacity <
            agents.Count)
        {
            _runtimeTrafficAgentBuffer.Capacity =
                agents.Count;
        }

        foreach (var agent in
                 agents)
        {
            _runtimeTrafficAgentBuffer.Add(
                new RuntimeTrafficAgentInfo(
                    agent.AgentIndex,
                    agent.SegmentIndex,
                    agent.DistanceMeters,
                    agent.SpeedMetersPerSecond,
                    agent.VehiclePath,
                    RuntimeWorldXFromSource(
                        agent.Position.X),
                    agent.Position.Y,
                    agent.Position.Z,
                    RuntimeHeadingRadiansFromSource(
                        agent.HeadingRadians),
                    agent.AiBrakeLight,
                    agent.AiBlinkerLeft,
                    agent.AiBlinkerRight,
                    agent.TraveledDistanceMeters,
                    -agent.PathCurvaturePerMeter,
                    ResolveTrafficScriptRuntime(
                        agent)));
        }

        AppendMultiplayerTrafficAgents(
            deltaSeconds);

        UpdateNavigationGuidance(
            deltaSeconds,
            agents);

        return _runtimeTrafficAgentBuffer;
    }

    private static OpenOmsiLanWorldCarState
        ProjectSharedWorldCarState(
            OpenOmsiLanWorldCarState state,
            DateTimeOffset lastSeen,
            DateTimeOffset now)
    {
        var ageSeconds =
            Math.Clamp(
                (
                    now -
                    lastSeen
                ).TotalSeconds,
                0.0,
                0.20);

        if (ageSeconds <=
                0.000001 ||
            Math.Abs(
                state.SpeedMetersPerSecond) <
                0.01)
        {
            return state;
        }

        var headingRadians =
            state.HeadingDegrees *
            Math.PI /
            180.0;

        var travelMeters =
            state.SpeedMetersPerSecond *
            ageSeconds;

        return state with
        {
            X =
                state.X +
                Math.Sin(
                    headingRadians) *
                travelMeters,
            Y =
                state.Y +
                Math.Cos(
                    headingRadians) *
                travelMeters
        };
    }

    private bool TryAppendSharedWorldAgents(
        ICollection<WorldTrafficAgentState> agents)
    {
        var session =
            _multiplayerSession;

        var lastFrame =
            _sharedWorldLastFrameAt;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Client ||
            !session.Connected ||
            !lastFrame.HasValue ||
            DateTimeOffset.UtcNow -
                lastFrame.Value >
                TimeSpan.FromSeconds(
                    3.5))
        {
            return false;
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var stale in
                 _sharedWorldCars
                     .Where(
                         pair =>
                             now -
                                 pair.Value.LastSeen >
                             TimeSpan.FromSeconds(
                                 3.5))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _sharedWorldCars.Remove(
                stale);
        }

        foreach (var pair in
                 _sharedWorldCars
                     .OrderBy(
                         static pair =>
                             pair.Key))
        {
            if (!_sharedWorldDescriptions.TryGetValue(
                    pair.Key,
                    out var description))
            {
                continue;
            }

            var relative =
                OpenOmsiLanProtocol.NormalizeVehiclePath(
                    description.VehiclePath);

            if (relative is null)
            {
                continue;
            }

            var vehiclePath =
                Path.GetFullPath(
                    Path.Combine(
                        _contentRoot.RootPath,
                        relative.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            if (!File.Exists(
                    vehiclePath))
            {
                continue;
            }

            var car =
                ProjectSharedWorldCarState(
                    pair.Value.State,
                    pair.Value.LastSeen,
                    now);

            agents.Add(
                new WorldTrafficAgentState(
                    (int)Math.Min(
                        car.Id,
                        int.MaxValue),
                    -1,
                    0.0,
                    car.SpeedMetersPerSecond,
                    vehiclePath,
                    new WorldVector3(
                        car.X,
                        car.Z,
                        car.Y),
                    car.HeadingDegrees *
                        Math.PI /
                        180.0,
                    null,
                    "SharedWorld",
                    car.Brake,
                    car.Blinker is
                        1 or 3,
                    car.Blinker is
                        2 or 3,
                    0.0,
                    0.0,
                    string.IsNullOrWhiteSpace(
                        description.Line)
                        ? null
                        : description.Line,
                    string.IsNullOrWhiteSpace(
                        description.Destination)
                        ? null
                        : description.Destination,
                    AtStation:
                        car.AtStation));
        }

        if (_sharedWorldLastAdvertisedCarCount >
                0 &&
            agents.Count ==
                0)
        {
            return false;
        }

        return true;
    }

    private void PublishSharedWorld(
        double deltaSeconds,
        IReadOnlyList<WorldTrafficAgentState> agents)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Host ||
            !session.Connected)
        {
            return;
        }

        const double carRadiusMeters =
            650.0;
        const double lightRadiusMeters =
            450.0;

        var carRadiusSquared =
            carRadiusMeters *
            carRadiusMeters;

        var lightRadiusSquared =
            lightRadiusMeters *
            lightRadiusMeters;

        var dt =
            double.IsFinite(
                deltaSeconds)
                ? Math.Max(
                    deltaSeconds,
                    0.0)
                : 0.0;

        _sharedWorldSendAccumulator +=
            dt;
        _sharedWorldDescriptionAccumulator +=
            dt;

        var refreshDescriptions =
            _sharedWorldDescriptionAccumulator >=
            2.0;

        if (refreshDescriptions)
        {
            _sharedWorldDescriptionAccumulator =
                Math.Clamp(
                    _sharedWorldDescriptionAccumulator -
                        2.0,
                    0.0,
                    2.0);
        }

        if (_sharedWorldSendAccumulator <
            0.1)
        {
            return;
        }

        var elapsed =
            _sharedWorldSendAccumulator;

        _sharedWorldSendAccumulator =
            Math.Clamp(
                _sharedWorldSendAccumulator -
                    0.1,
                0.0,
                0.1);

        var carDescriptions =
            new Dictionary<
                uint,
                OpenOmsiLanWorldCarDescription>();

        foreach (var agent in
                 agents)
        {
            if (agent.AgentIndex <
                    0 ||
                agent.AgentIndex >
                    (int)OpenOmsiLanWorldCodec.MaximumEntityId)
            {
                continue;
            }

            var relative =
                Path.GetRelativePath(
                        _contentRoot.RootPath,
                        agent.VehiclePath)
                    .Replace(
                        '\\',
                        '/');

            if (OpenOmsiLanProtocol.NormalizeVehiclePath(
                    relative) is null)
            {
                continue;
            }

            var id =
                (uint)agent.AgentIndex;

            carDescriptions[
                id] =
                new OpenOmsiLanWorldCarDescription(
                    id,
                    relative,
                    null,
                    agent.ScheduledLine ??
                        string.Empty,
                    agent.ScheduledDestination ??
                        string.Empty);
        }

        var cars =
            agents
                .Where(
                    static agent =>
                        agent.AgentIndex >=
                            0 &&
                        agent.AgentIndex <=
                            (int)OpenOmsiLanWorldCodec.MaximumEntityId)
                .Select(
                    static agent =>
                        new OpenOmsiLanWorldCarState(
                            (uint)agent.AgentIndex,
                            agent.Position.X,
                            agent.Position.Z,
                            agent.Position.Y,
                            (float)(
                                agent.HeadingRadians *
                                180.0 /
                                Math.PI),
                            0.0f,
                            0.0f,
                            (float)agent.SpeedMetersPerSecond,
                            0.0f,
                            agent.AiBlinkerLeft &&
                            agent.AiBlinkerRight
                                ? (byte)3
                                : agent.AiBlinkerLeft
                                    ? (byte)1
                                    : agent.AiBlinkerRight
                                        ? (byte)2
                                        : (byte)0,
                            agent.AiBrakeLight,
                            true,
                            agent.AtStation))
                .ToArray();

        _worldTrafficSignalStateBuffer.Clear();
        _trafficSimulation?
            .AppendTrafficSignalSnapshotTo(
                _worldTrafficSignalStateBuffer);

        var segmentByIndex =
            _currentWorld?
                .TrafficPaths
                .Segments
                .ToDictionary(
                    static segment =>
                        segment.Index);

        var lights =
            new List<(
                OpenOmsiLanWorldLightState State,
                double X,
                double Y)>();

        if (segmentByIndex is not null)
        {
            foreach (var signal in
                     _worldTrafficSignalStateBuffer)
            {
                if (!segmentByIndex.TryGetValue(
                        signal.SegmentIndex,
                        out var segment) ||
                    !segment.SceneryObjectId.HasValue ||
                    segment.SceneryObjectId.Value <
                        0 ||
                    segment.SceneryObjectId.Value >
                        uint.MaxValue ||
                    segment.Points.Count ==
                        0)
                {
                    continue;
                }

                lights.Add(
                    (
                        new OpenOmsiLanWorldLightState(
                            segment.SceneryObjectId.Value,
                            signal.PositionSeconds,
                            signal.Held),
                        segment.Start.X,
                        segment.Start.Z
                    ));
            }
        }

        var activePeers =
            session.SnapshotPeers()
                .Where(
                    static peer =>
                        peer.HasState &&
                        peer.Pose.HasVehicle)
                .ToArray();

        var activePeerIds =
            activePeers
                .Select(
                    static peer =>
                        peer.Id)
                .ToHashSet();

        foreach (var stalePeerId in
                 _sharedWorldCarPeerViews
                     .Keys
                     .Where(
                         id =>
                             !activePeerIds.Contains(
                                 id))
                     .ToArray())
        {
            _sharedWorldCarPeerViews.Remove(
                stalePeerId);
        }

        foreach (var peer in
                 activePeers)
        {
            if (!_sharedWorldCarPeerViews.TryGetValue(
                    peer.Id,
                    out var view))
            {
                view =
                    new WorldPeerVisibilityView();

                _sharedWorldCarPeerViews[
                    peer.Id] =
                    view;
            }

            var visibleCars =
                cars
                    .Where(
                        car =>
                        {
                            var dx =
                                car.X -
                                peer.Pose.X;

                            var dy =
                                car.Y -
                                peer.Pose.Y;

                            return dx *
                                       dx +
                                   dy *
                                       dy <=
                                   carRadiusSquared;
                        })
                    .OrderBy(
                        static car =>
                            car.Id)
                    .ToArray();

            var visibleIds =
                visibleCars
                    .Select(
                        static car =>
                            car.Id)
                    .ToHashSet();

            foreach (var previousId in
                     view.VisibleIds)
            {
                if (!visibleIds.Contains(
                        previousId))
                {
                    view.GoneSeconds[
                        previousId] =
                        1.0;
                }
            }

            foreach (var visibleId in
                     visibleIds)
            {
                view.GoneSeconds.Remove(
                    visibleId);
            }

            foreach (var car in
                     visibleCars)
            {
                if (!carDescriptions.TryGetValue(
                        car.Id,
                        out var description))
                {
                    continue;
                }

                if (refreshDescriptions ||
                    !view.VisibleIds.Contains(
                        car.Id))
                {
                    session.SendWorldCarDescriptionTo(
                        peer.Id,
                        description);
                }
            }

            view.VisibleIds.Clear();

            foreach (var visibleId in
                     visibleIds)
            {
                view.VisibleIds.Add(
                    visibleId);
            }

            var gone =
                view.GoneSeconds
                    .Keys
                    .OrderBy(
                        static id =>
                            id)
                    .Take(
                        63)
                    .Select(
                        static id =>
                            new OpenOmsiLanWorldGoneEntity(
                                false,
                                id))
                    .ToArray();

            var visibleLights =
                lights
                    .Where(
                        light =>
                        {
                            var dx =
                                light.X -
                                peer.Pose.X;

                            var dy =
                                light.Y -
                                peer.Pose.Y;

                            return dx *
                                       dx +
                                   dy *
                                       dy <=
                                   lightRadiusSquared;
                        })
                    .Select(
                        static light =>
                            light.State)
                    .Take(
                        63)
                    .ToArray();

            session.SendWorldFrameTo(
                peer.Id,
                new OpenOmsiLanWorldFrame(
                    0,
                    0,
                    visibleCars,
                    visibleLights,
                    Gone:
                        gone,
                    Parked:
                        new OpenOmsiLanWorldParkedState(
                            true,
                            Array.Empty<uint>())));

            var goneStep =
                Math.Clamp(
                    elapsed,
                    0.0,
                    0.25);

            foreach (var removed in
                     gone)
            {
                if (!view.GoneSeconds.TryGetValue(
                        removed.Id,
                        out var remaining))
                {
                    continue;
                }

                remaining -=
                    goneStep;

                if (remaining <=
                    0.000001)
                {
                    view.GoneSeconds.Remove(
                        removed.Id);
                }
                else
                {
                    view.GoneSeconds[
                        removed.Id] =
                        remaining;
                }
            }
        }
    }

    private void UpdateNavigationGuidance(
        double deltaSeconds,
        IReadOnlyList<WorldTrafficAgentState> traffic)
    {
        var window =
            _runtimeWindow;

        var world =
            _currentWorld;

        if (window is null ||
            world is null)
        {
            return;
        }

        _navigationUpdateAccumulator +=
            Math.Max(
                0.0,
                double.IsFinite(
                    deltaSeconds)
                    ? deltaSeconds
                    : 0.0);

        if (_navigationUpdateAccumulator <
            0.15)
        {
            return;
        }

        _navigationUpdateAccumulator =
            0.0;

        var obstacle =
            window.PlayerTrafficObstacle;
        var walker =
            window.LocalWalkerState;

        var line =
            window.CurrentOperationLine
                .Trim();
        var destination =
            window.CurrentOperationDestination
                .Trim();

        if ((obstacle is null &&
             !walker.Active) ||
            string.IsNullOrWhiteSpace(
                line))
        {
            if (_navigationRouteKey.Length >
                0)
            {
                _navigationRouteKey =
                    string.Empty;
                _navigationRuntimeRoute =
                    [];
                _navigationAssist.ClearRoute();
                window.SetNavigationGuidance(
                    [],
                    []);
                window.SetTeleMatrixState(
                    RuntimeTeleMatrixState.Unavailable);
            }

            return;
        }

        var candidates =
            _lineAiSchedule.Trips
                .Where(
                    trip =>
                        trip.Ready &&
                        trip.Route is not null &&
                        (
                            trip.LineName.Equals(
                                line,
                                StringComparison.OrdinalIgnoreCase) ||
                            trip.Trip?.Line.Equals(
                                line,
                                StringComparison.OrdinalIgnoreCase) ==
                            true
                        ))
                .ToArray();

        if (candidates.Length ==
            0)
        {
            window.SetTeleMatrixState(
                RuntimeTeleMatrixState.Unavailable);
            return;
        }

        var selected =
            !string.IsNullOrWhiteSpace(
                destination)
                ? candidates.FirstOrDefault(
                      trip =>
                          trip.Trip?.Destination.Equals(
                              destination,
                              StringComparison.OrdinalIgnoreCase) ==
                          true) ??
                  candidates.FirstOrDefault(
                      trip =>
                          trip.Trip?.Destination.Contains(
                              destination,
                              StringComparison.OrdinalIgnoreCase) ==
                          true)
                : null;

        selected ??=
            candidates
                .OrderBy(
                    trip =>
                        CircularMinuteDistance(
                            trip.DepartureMinutes,
                            _lineAiServiceMinutes))
                .ThenBy(
                    static trip =>
                        trip.TripName,
                    StringComparer.OrdinalIgnoreCase)
                .First();

        var routeKey =
            string.Join(
                "|",
                selected.LineName,
                selected.TripName,
                selected.Route!.TrackName,
                selected.Trip?.Destination ??
                    string.Empty);

        if (!_navigationRouteKey.Equals(
                routeKey,
                StringComparison.Ordinal))
        {
            var route =
                BuildNavigationRoute(
                    selected.Route,
                    world.TrafficPaths);

            if (route.Length <
                2)
            {
                return;
            }

            _navigationRouteKey =
                routeKey;

            _navigationAssist.SetRoute(
                route);

            _navigationRuntimeRoute =
                route
                    .Select(
                        static point =>
                            new RuntimeTrafficPathPointInfo(
                                RuntimeWorldXFromSource(
                                    point.X),
                                point.Y,
                                point.Z))
                    .ToArray();
        }

        var navigationPosition =
            walker.Active
                ? new WorldVector3(
                    -walker.Position.X,
                    walker.Position.Y,
                    walker.Position.Z)
                : new WorldVector3(
                    -obstacle!.X,
                    obstacle.Y,
                    obstacle.Z);

        var navigationHeadingDegrees =
            walker.Active
                ? -walker.HeadingDegrees
                : -obstacle!.HeadingRadians *
                  180.0 /
                  Math.PI;

        var navigationSpeedKph =
            walker.Active
                ? walker.SpeedMetersPerSecond *
                  3.6
                : Math.Abs(
                      obstacle!.SpeedMetersPerSecond) *
                  3.6;

        var navigation =
            _navigationAssist.Build(
                navigationPosition,
                navigationHeadingDegrees,
                navigationSpeedKph,
                onFoot:
                    walker.Active,
                traffic);

        var guidance =
            navigation.GroundArrows
                .Select(
                    static arrow =>
                        new RuntimeNavigationGuidancePointInfo(
                            RuntimeWorldXFromSource(
                                arrow.Position.X),
                            arrow.Position.Y,
                            arrow.Position.Z,
                            RuntimeHeadingDegreesFromSource(
                                arrow.HeadingDegrees),
                            arrow.PitchDegrees,
                            arrow.DistanceAheadMeters,
                            arrow.Kind))
                .ToArray();

        window.SetNavigationGuidance(
            _navigationRuntimeRoute,
            guidance);

        window.SetTeleMatrixState(
            BuildTeleMatrixState(
                selected,
                navigation));
    }

    private RuntimeTeleMatrixState BuildTeleMatrixState(
        WorldLineAiScheduledTrip trip,
        WorldNavigationAssistState navigation)
    {
        var timing =
            trip.Timing;

        if (timing is null ||
            timing.Stops.Count ==
                0)
        {
            return RuntimeTeleMatrixState.Unavailable;
        }

        var authoredDistance =
            timing.Stops
                .Select(
                    static stop =>
                        stop.RouteDistanceMeters)
                .DefaultIfEmpty()
                .Max();

        var scale =
            authoredDistance >
                    0.001 &&
                navigation.RouteLengthMeters >
                    0.001
                ? navigation.RouteLengthMeters /
                  authoredDistance
                : 1.0;

        var scaledStops =
            timing.Stops
                .Select(
                    (
                        stop,
                        index
                    ) =>
                        (
                            Stop: stop,
                            Index: index,
                            Distance:
                                stop.RouteDistanceMeters *
                                scale
                        ))
                .ToArray();

        var next =
            scaledStops
                .Where(
                    item =>
                        item.Stop.Stops &&
                        item.Distance >=
                            navigation.ProgressMeters -
                            3.0)
                .OrderBy(
                    static item =>
                        item.Distance)
                .FirstOrDefault();

        if (next.Stop is null)
        {
            next =
                scaledStops[
                    ^1];
        }

        var previous =
            scaledStops
                .Where(
                    item =>
                        item.Distance <=
                            navigation.ProgressMeters)
                .OrderByDescending(
                    static item =>
                        item.Distance)
                .FirstOrDefault();

        var expectedSeconds =
            next.Stop.ArrivalSeconds;

        if (previous.Stop is not null &&
            next.Distance >
                previous.Distance +
                    0.001)
        {
            var t =
                Math.Clamp(
                    (
                        navigation.ProgressMeters -
                        previous.Distance
                    ) /
                    (
                        next.Distance -
                        previous.Distance
                    ),
                    0.0,
                    1.0);

            expectedSeconds =
                previous.Stop.DepartureSeconds +
                (
                    next.Stop.ArrivalSeconds -
                    previous.Stop.DepartureSeconds
                ) *
                t;
        }

        var elapsedMinutes =
            _lineAiServiceMinutes -
            trip.DepartureMinutes;

        if (elapsedMinutes <
            -720.0)
        {
            elapsedMinutes +=
                1440.0;
        }
        else if (elapsedMinutes >
                 720.0)
        {
            elapsedMinutes -=
                1440.0;
        }

        var tripSeconds =
            Math.Max(
                elapsedMinutes *
                    60.0,
                0.0);

        var delaySeconds =
            (int)Math.Round(
                tripSeconds -
                expectedSeconds,
                MidpointRounding.AwayFromZero);

        return new RuntimeTeleMatrixState(
            true,
            trip.LineName,
            trip.Trip?.Destination ??
                string.Empty,
            string.IsNullOrWhiteSpace(
                next.Stop.StopName)
                ? $"Parada {next.Index + 1}"
                : next.Stop.StopName,
            next.Index +
                1,
            timing.Stops.Count,
            delaySeconds,
            Math.Max(
                next.Distance -
                    navigation.ProgressMeters,
                0.0));
    }

    private static WorldVector3[] BuildNavigationRoute(
        WorldLineAiRoute route,
        WorldTrafficPathNetwork network)
    {
        if (route.SegmentIndices.Count ==
            0)
        {
            return [];
        }

        var segments =
            network.Segments.ToDictionary(
                static segment =>
                    segment.Index);

        var output =
            new List<WorldVector3>();

        for (var routeIndex = 0;
             routeIndex <
                 route.SegmentIndices.Count;
             routeIndex++)
        {
            if (!segments.TryGetValue(
                    route.SegmentIndices[
                        routeIndex],
                    out var segment) ||
                segment.Points.Count <
                    2)
            {
                continue;
            }

            var forward =
                true;

            if (output.Count >
                0)
            {
                var previous =
                    output[^1];

                forward =
                    DistanceSquared(
                        previous,
                        segment.Points[0]) <=
                    DistanceSquared(
                        previous,
                        segment.Points[^1]);
            }
            else if (routeIndex +
                         1 <
                     route.SegmentIndices.Count &&
                     segments.TryGetValue(
                         route.SegmentIndices[
                             routeIndex +
                             1],
                         out var next) &&
                     next.Points.Count >
                         1)
            {
                var fromStart =
                    Math.Min(
                        DistanceSquared(
                            segment.Points[0],
                            next.Points[0]),
                        DistanceSquared(
                            segment.Points[0],
                            next.Points[^1]));
                var fromEnd =
                    Math.Min(
                        DistanceSquared(
                            segment.Points[^1],
                            next.Points[0]),
                        DistanceSquared(
                            segment.Points[^1],
                            next.Points[^1]));

                forward =
                    fromEnd <=
                    fromStart;
            }

            if (forward)
            {
                AppendNavigationPoints(
                    output,
                    segment.Points);
            }
            else
            {
                AppendNavigationPoints(
                    output,
                    segment.Points.Reverse());
            }
        }

        return output.ToArray();
    }

    private static void AppendNavigationPoints(
        ICollection<WorldVector3> output,
        IEnumerable<WorldVector3> points)
    {
        foreach (var point in
                 points)
        {
            if (output is
                    List<WorldVector3> list &&
                list.Count >
                    0 &&
                DistanceSquared(
                    list[^1],
                    point) <
                0.04)
            {
                continue;
            }

            output.Add(
                point);
        }
    }

    private static double DistanceSquared(
        WorldVector3 first,
        WorldVector3 second)
    {
        var dx =
            first.X -
            second.X;
        var dy =
            first.Y -
            second.Y;
        var dz =
            first.Z -
            second.Z;

        return dx *
                   dx +
               dy *
                   dy +
               dz *
                   dz;
    }

    private static double CircularMinuteDistance(
        double first,
        double second)
    {
        var delta =
            Math.Abs(
                first -
                second) %
            1440.0;

        return Math.Min(
            delta,
            1440.0 -
            delta);
    }

    private void StartMultiplayerSession()
    {
        DisposeMultiplayerSession();
        _multiplayerStatusAccumulator =
            1.0;
        _sharedWorldDescriptionAccumulator =
            2.0;
        _sharedWorldSendAccumulator =
            0.1;

        var mode =
            (_options.MultiplayerMode ??
             "off")
                .Trim()
                .ToLowerInvariant();

        if (mode is not ("host" or "join"))
        {
            _runtimeWindow?.SetDriveOpsNetworkState(
                "OFFLINE",
                false,
                0,
                string.Empty);
            return;
        }

        var now =
            DateTime.Now;

        var world =
            new OpenOmsiLanWorld(
                _map.FolderName,
                now.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                now.TimeOfDay.TotalSeconds,
                string.Empty,
                string.Empty);

        try
        {
            _multiplayerSession =
                mode == "host"
                    ? OpenOmsiLanSession.Host(
                        Math.Clamp(
                            _options.MultiplayerPort,
                            1,
                            ushort.MaxValue),
                        _options.MultiplayerPlayerName,
                        world,
                        tryNextPorts:
                            true)
                    : OpenOmsiLanSession.Join(
                        _options.MultiplayerTarget,
                        _options.MultiplayerPlayerName,
                        world);

            _multiplayerSession.OperationalMessageReceived +=
                OnMultiplayerOperationalMessage;
            _multiplayerSession.VoiceFrameReceived +=
                OnMultiplayerVoiceFrame;
            _multiplayerSession.WorldFrameReceived +=
                OnMultiplayerWorldFrame;
            _multiplayerSession.WorldCarDescriptionReceived +=
                OnMultiplayerWorldCarDescription;
            _multiplayerSession.WorldPeopleFrameReceived +=
                OnMultiplayerWorldPeopleFrame;
            _multiplayerSession.WorldPersonDescriptionReceived +=
                OnMultiplayerWorldPersonDescription;
            _multiplayerSession.WorldPeopleFrameUpReceived +=
                OnMultiplayerWorldPeopleFrameUp;
            _multiplayerSession.WorldPersonDescriptionUpReceived +=
                OnMultiplayerWorldPersonDescriptionUp;
            _multiplayerSession.WorldClaimRequested +=
                OnMultiplayerWorldClaimRequested;
            _multiplayerSession.WorldClaimResultReceived +=
                OnMultiplayerWorldClaimResult;

            NotifyCommsLinkVoiceState(
                "VOZ: PTT F10 · pronto");

            _runtimeWindow?.SetDriveOpsNetworkState(
                _multiplayerSession.Role.ToString().ToUpperInvariant(),
                _multiplayerSession.Connected,
                0,
                string.IsNullOrWhiteSpace(
                    _multiplayerSession.SessionCode)
                    ? _multiplayerSession.SessionHex
                    : _multiplayerSession.SessionCode);

            Console.WriteLine(
                mode == "host"
                    ? $"[multiplayer] host protocol={OpenOmsiLanProtocol.ProtocolVersion}; port={_multiplayerSession.LocalPort}; session={_multiplayerSession.SessionHex}; code={_multiplayerSession.SessionCode}"
                    : $"[multiplayer] join protocol={OpenOmsiLanProtocol.ProtocolVersion}; target={_options.MultiplayerTarget}; localPort={_multiplayerSession.LocalPort}");
        }
        catch (Exception exception)
        {
            _multiplayerSession?.Dispose();
            _multiplayerSession =
                null;

            _runtimeWindow?.SetDriveOpsNetworkState(
                "ERRO",
                false,
                0,
                string.Empty);

            Console.Error.WriteLine(
                $"[multiplayer] startup failed: {exception.Message}");
        }
    }

    private void OnDriveOpsMessageRequested(
        RuntimeDriveOpsMessageRequest request)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            !session.Connected)
        {
            _runtimeWindow?.ReceiveDriveOpsMessage(
                new RuntimeDriveOpsInboundMessage(
                    0,
                    "SISTEMA",
                    request.CompanyName,
                    string.Empty,
                    request.Module,
                    "ERROR",
                    "Sem sessão multiplayer ativa.",
                    request.Target,
                    DateTimeOffset.UtcNow
                        .ToUnixTimeMilliseconds()));
            return;
        }

        try
        {
            if (!session.SendOperationalMessage(
                    new OpenOmsiLanOperationalMessage(
                        0,
                        request.DriverName,
                        request.CompanyName,
                        request.EmployeeNumber,
                        request.Module,
                        request.Kind,
                        request.Text,
                        request.Target,
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds())))
            {
                _runtimeWindow?.ReceiveDriveOpsMessage(
                    new RuntimeDriveOpsInboundMessage(
                        0,
                        "SISTEMA",
                        request.CompanyName,
                        string.Empty,
                        request.Module,
                        "ERROR",
                        "Mensagem operacional não enviada.",
                        request.Target,
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds()));
            }
        }
        catch (Exception exception)
        {
            _runtimeWindow?.ReceiveDriveOpsMessage(
                new RuntimeDriveOpsInboundMessage(
                    0,
                    "SISTEMA",
                    request.CompanyName,
                    string.Empty,
                    request.Module,
                    "ERROR",
                    exception.Message,
                    request.Target,
                    DateTimeOffset.UtcNow
                        .ToUnixTimeMilliseconds()));
        }
    }

    private void OnMultiplayerOperationalMessage(
        OpenOmsiLanOperationalMessage message)
    {
        _runtimeWindow?.ReceiveDriveOpsMessage(
            new RuntimeDriveOpsInboundMessage(
                message.SenderId,
                message.SenderName,
                message.CompanyName,
                message.EmployeeNumber,
                message.Module,
                message.Kind,
                message.Text,
                message.Target,
                message.TimestampUnixMilliseconds));
    }

    private void OnMultiplayerWorldFrame(
        OpenOmsiLanWorldFrame frame)
    {
        if (_multiplayerSession?.Role !=
            OpenOmsiLanRole.Client)
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        _sharedWorldLastFrameAt =
            now;
        _sharedWorldLastAdvertisedCarCount =
            frame.Cars.Count;

        foreach (var car in
                 frame.Cars)
        {
            _sharedWorldCars[
                car.Id] =
                (
                    car,
                    now
                );
        }

        RequestMissingWorldDescriptions(
            frame.Cars
                .Select(
                    static car =>
                        new OpenOmsiLanWorldEntityRef(
                            false,
                            car.Id)));

        foreach (var light in
                 frame.Lights)
        {
            _sharedWorldLights[
                light.ObjectId] =
                (
                    light,
                    now
                );
        }

        foreach (var removed in
                 frame.Gone ??
                 Array.Empty<
                     OpenOmsiLanWorldGoneEntity>())
        {
            if (removed.Person)
            {
                _sharedWorldPeople.Remove(
                    removed.Id);

                // A GONE may overtake the GRANT for a pending hand-over.
                // Preserve its DESC until the authority reply is resolved.
                if (!_pendingWorldPassengerClaims.ContainsKey(
                        removed.Id))
                {
                    _sharedWorldPersonDescriptions.Remove(
                        removed.Id);
                }
            }
            else
            {
                _sharedWorldCars.Remove(
                    removed.Id);
            }
        }

        if (frame.Parked is
            { } parked)
        {
            var parkingChanged =
                false;

            lock (_sharedWorldParkingGate)
            {
                if (parked.Complete)
                {
                    var next =
                        parked.ParkingObjectIds
                            .ToHashSet();

                    if (!_sharedWorldDepartedParkingObjectIds.SetEquals(
                            next))
                    {
                        _sharedWorldDepartedParkingObjectIds.Clear();

                        foreach (var objectId in
                                 next)
                        {
                            _sharedWorldDepartedParkingObjectIds.Add(
                                objectId);
                        }

                        parkingChanged =
                            true;
                    }
                }
                else
                {
                    foreach (var objectId in
                             parked.ParkingObjectIds)
                    {
                        parkingChanged |=
                            _sharedWorldDepartedParkingObjectIds.Add(
                                objectId);
                    }
                }
            }

            if (parkingChanged)
            {
                QueueSharedWorldParkingRefresh();
            }
        }
    }

    private void OnMultiplayerWorldPeopleFrame(
        OpenOmsiLanWorldPeopleFrame frame)
    {
        if (_multiplayerSession?.Role !=
            OpenOmsiLanRole.Client)
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var person in
                 frame.People)
        {
            _sharedWorldPeople[
                person.Id] =
                (
                    person,
                    now
                );
        }

        foreach (var removed in
                 frame.Gone ??
                 Array.Empty<
                     OpenOmsiLanWorldGoneEntity>())
        {
            if (!removed.Person)
            {
                continue;
            }

            _sharedWorldPeople.Remove(
                removed.Id);

            // UDP can deliver GONE before GRANT. The host has removed
            // its copy precisely because ownership may be transferring.
            // Do not discard the pending passenger pose or description.
            if (!_pendingWorldPassengerClaims.ContainsKey(
                    removed.Id))
            {
                _sharedWorldPersonDescriptions.Remove(
                    removed.Id);
                _worldPassengerClaimAttemptedAt.Remove(
                    removed.Id);
                _sharedWorldWantRequestedAt.Remove(
                    (
                        true,
                        removed.Id
                    ));
            }
        }

        RequestMissingWorldDescriptions(
            frame.People
                .Select(
                    static person =>
                        new OpenOmsiLanWorldEntityRef(
                            true,
                            person.Id)));
    }

    private void OnMultiplayerWorldPersonDescription(
        OpenOmsiLanWorldPersonDescription description)
    {
        _sharedWorldPersonDescriptions[
            description.Id] =
            description;

        if (_pendingWorldPassengerClaims.TryGetValue(
                description.Id,
                out var pending))
        {
            _pendingWorldPassengerClaims[
                description.Id] =
                (
                    pending.State,
                    description.HumanPath
                );
        }

        // DESC may be delivered after GRANT on an unordered UDP link.
        if (_ownedWorldPassengers.TryGetValue(
                description.Id,
                out var owned))
        {
            owned.UpdateHumanPath(
                description.HumanPath);
        }

        _sharedWorldWantRequestedAt.Remove(
            (
                true,
                description.Id
            ));
    }

    private void RequestMissingWorldDescriptions(
        IEnumerable<OpenOmsiLanWorldEntityRef> entities)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Client ||
            !session.Connected)
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        var missing =
            entities
                .Distinct()
                .Where(
                    entity =>
                        entity.Person
                            ? !_sharedWorldPersonDescriptions.ContainsKey(
                                entity.Id)
                            : !_sharedWorldDescriptions.ContainsKey(
                                entity.Id))
                .Where(
                    entity =>
                    {
                        var key =
                            (
                                entity.Person,
                                entity.Id
                            );

                        return !_sharedWorldWantRequestedAt.TryGetValue(
                                   key,
                                   out var last) ||
                               now -
                                   last >=
                               TimeSpan.FromSeconds(
                                   2.0);
                    })
                .Take(
                    64)
                .ToArray();

        if (missing.Length ==
                0 ||
            !session.SendWorldWant(
                missing))
        {
            return;
        }

        foreach (var entity in
                 missing)
        {
            _sharedWorldWantRequestedAt[
                (
                    entity.Person,
                    entity.Id
                )] =
                now;
        }
    }

    private void OnMultiplayerWorldPersonDescriptionUp(
        uint peerId,
        OpenOmsiLanWorldPersonDescription description)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Host)
        {
            return;
        }

        var mappedId =
            ResolveRelayedWorldPersonId(
                peerId,
                description.Id);

        var mapped =
            description with
            {
                Id =
                    mappedId
            };

        _sharedWorldPersonDescriptions[
            mappedId] =
            mapped;

        session.RelayWorldPersonDescription(
            peerId,
            mapped);
    }

    private void OnMultiplayerWorldPeopleFrameUp(
        uint peerId,
        OpenOmsiLanWorldPeopleFrame frame)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Host)
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        var mappedGone =
            (frame.Gone ??
             Array.Empty<OpenOmsiLanWorldGoneEntity>())
                .Where(
                    static removed =>
                        removed.Person)
                .Select(
                    removed =>
                    {
                        var key =
                            (
                                PeerId:
                                    peerId,
                                PersonId:
                                    removed.Id
                            );

                        if (!_relayedWorldPersonIds.TryGetValue(
                                key,
                                out var mappedId))
                        {
                            return null;
                        }

                        _sharedWorldPeople.Remove(
                            mappedId);

                        return new OpenOmsiLanWorldGoneEntity(
                            true,
                            mappedId);
                    })
                .Where(
                    static removed =>
                        removed is not null)
                .Select(
                    static removed =>
                        removed!)
                .ToArray();

        var mappedPeople =
            frame.People
                .Select(
                    person =>
                    {
                        var mappedId =
                            ResolveRelayedWorldPersonId(
                                peerId,
                                person.Id);

                        var mapped =
                            person with
                            {
                                Id =
                                    mappedId,
                                PlayerBus =
                                    person.Aboard,
                                BusId =
                                    person.Aboard
                                        ? peerId
                                        : 0,
                                WaitingStopObjectId =
                                    null,
                                WaitingSpot =
                                    null
                            };

                        _sharedWorldPeople[
                            mappedId] =
                            (
                                mapped,
                                now
                            );

                        return mapped;
                    })
                .ToArray();

        const double personRadiusMeters =
            260.0;
        const double busRadiusMeters =
            650.0;

        var personRadiusSquared =
            personRadiusMeters *
            personRadiusMeters;

        var busRadiusSquared =
            busRadiusMeters *
            busRadiusMeters;

        var peers =
            session.SnapshotPeers();

        var sourcePeer =
            peers.FirstOrDefault(
                peer =>
                    peer.Id ==
                        peerId &&
                    peer.HasState &&
                    peer.Pose.HasVehicle);

        foreach (var observer in
                 peers.Where(
                     peer =>
                         peer.Id !=
                             peerId &&
                         peer.HasState &&
                         peer.Pose.HasVehicle))
        {
            var viewKey =
                (
                    ObserverPeerId:
                        observer.Id,
                    SourcePeerId:
                        peerId
                );

            if (!_relayedWorldPassengerPeerViews.TryGetValue(
                    viewKey,
                    out var view))
            {
                view =
                    new WorldPeerVisibilityView();

                _relayedWorldPassengerPeerViews[
                    viewKey] =
                    view;
            }

            var sourceBusNear =
                sourcePeer is not null &&
                (
                    sourcePeer.Pose.X -
                    observer.Pose.X
                ) *
                (
                    sourcePeer.Pose.X -
                    observer.Pose.X
                ) +
                (
                    sourcePeer.Pose.Y -
                    observer.Pose.Y
                ) *
                (
                    sourcePeer.Pose.Y -
                    observer.Pose.Y
                ) <=
                busRadiusSquared;

            var visiblePeople =
                mappedPeople
                    .Where(
                        person =>
                        {
                            if (person.Aboard)
                            {
                                return sourceBusNear;
                            }

                            var dx =
                                person.X -
                                observer.Pose.X;

                            var dy =
                                person.Y -
                                observer.Pose.Y;

                            return dx *
                                       dx +
                                   dy *
                                       dy <=
                                   personRadiusSquared;
                        })
                    .OrderBy(
                        static person =>
                            person.Id)
                    .ToArray();

            var visibleIds =
                visiblePeople
                    .Select(
                        static person =>
                            person.Id)
                    .ToHashSet();

            foreach (var previousId in
                     view.VisibleIds)
            {
                if (!visibleIds.Contains(
                        previousId))
                {
                    view.GoneSeconds[
                        previousId] =
                        1.0;
                }
            }

            foreach (var removed in
                     mappedGone)
            {
                if (view.VisibleIds.Contains(
                        removed.Id))
                {
                    view.GoneSeconds[
                        removed.Id] =
                        1.0;
                }
            }

            foreach (var visibleId in
                     visibleIds)
            {
                view.GoneSeconds.Remove(
                    visibleId);
            }

            view.VisibleIds.Clear();

            foreach (var visibleId in
                     visibleIds)
            {
                view.VisibleIds.Add(
                    visibleId);
            }

            var gone =
                view.GoneSeconds
                    .Keys
                    .OrderBy(
                        static id =>
                            id)
                    .Take(
                        63)
                    .Select(
                        static id =>
                            new OpenOmsiLanWorldGoneEntity(
                                true,
                                id))
                    .ToArray();

            if (visiblePeople.Length >
                    0 ||
                gone.Length >
                    0)
            {
                session.SendWorldPeopleFrameTo(
                    observer.Id,
                    new OpenOmsiLanWorldPeopleFrame(
                        frame.Sequence,
                        frame.HostMilliseconds,
                        visiblePeople,
                        gone));
            }

            foreach (var removed in
                     gone)
            {
                if (!view.GoneSeconds.TryGetValue(
                        removed.Id,
                        out var remaining))
                {
                    continue;
                }

                remaining -=
                    0.1;

                if (remaining <=
                    0.000001)
                {
                    view.GoneSeconds.Remove(
                        removed.Id);
                }
                else
                {
                    view.GoneSeconds[
                        removed.Id] =
                        remaining;
                }
            }
        }
    }

    private void OnMultiplayerWorldClaimRequested(
        OpenOmsiLanWorldClaimRequest request)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            session.Role !=
                OpenOmsiLanRole.Host ||
            request.People.Count ==
                0)
        {
            return;
        }

        var granted =
            _hostWorldPassengers.HandOver(
                request.People);

        var grantedSet =
            granted.ToHashSet();

        var denied =
            request.People
                .Where(
                    id =>
                        !grantedSet.Contains(
                            id))
                .Distinct()
                .ToArray();

        foreach (var id in
                 granted)
        {
            var humanPath =
                _hostWorldPassengers.HumanPath(
                    id);

            if (!string.IsNullOrWhiteSpace(
                    humanPath))
            {
                session.SendWorldPersonDescriptionTo(
                    request.PlayerId,
                    new OpenOmsiLanWorldPersonDescription(
                        id,
                        humanPath));
            }

            var key =
                (
                    PeerId:
                        request.PlayerId,
                    PersonId:
                        id
                );

            if (_relayedWorldPersonIds.TryGetValue(
                    key,
                    out var previousMappedId) &&
                previousMappedId !=
                    id)
            {
                _relayedWorldPersonIdsInUse.Remove(
                    previousMappedId);
            }

            _relayedWorldPersonIds[
                key] =
                id;

            _relayedWorldPersonIdsInUse.Add(
                id);
        }

        session.SendWorldClaimResult(
            request.PlayerId,
            granted,
            denied);

        if (granted.Count >
                0 ||
            denied.Length >
                0)
        {
            Console.WriteLine(
                $"[multiplayer-world] host claim player={request.PlayerId}; granted={string.Join(",", granted)}; denied={string.Join(",", denied)}");
        }
    }

    private void OnMultiplayerWorldClaimResult(
        OpenOmsiLanWorldClaimResult result)
    {
        var session =
            _multiplayerSession;

        if (session?.Role !=
            OpenOmsiLanRole.Client)
        {
            return;
        }

        foreach (var id in
                 result.People)
        {
            if (result.Granted)
            {
                OpenOmsiLanWorldPersonState?
                    state =
                        null;

                string humanPath =
                    string.Empty;

                if (_pendingWorldPassengerClaims.TryGetValue(
                        id,
                        out var pending))
                {
                    state =
                        pending.State;
                    humanPath =
                        pending.HumanPath;
                }
                else if (_sharedWorldPeople.TryGetValue(
                             id,
                             out var shared))
                {
                    state =
                        shared.State;

                    if (_sharedWorldPersonDescriptions.TryGetValue(
                            id,
                            out var description))
                    {
                        humanPath =
                            description.HumanPath;
                    }
                }

                if (string.IsNullOrWhiteSpace(
                        humanPath) &&
                    _sharedWorldPersonDescriptions.TryGetValue(
                        id,
                        out var latestDescription))
                {
                    humanPath =
                        latestDescription.HumanPath;
                }

                if (state is not null)
                {
                    _ownedWorldPassengers[
                        id] =
                        new RuntimeWorldPassengerSimulation(
                            state,
                            humanPath);

                    _sharedWorldPeople.Remove(
                        id);

                    if (!string.IsNullOrWhiteSpace(
                            humanPath))
                    {
                        session.SendWorldPersonDescriptionUp(
                            new OpenOmsiLanWorldPersonDescription(
                                id,
                                humanPath));
                    }
                }

                _worldPassengerClaimAttemptedAt.Remove(
                    id);
            }

            _pendingWorldPassengerClaims.Remove(
                id);
        }

        Console.WriteLine(
            $"[multiplayer-world] passenger claim {(result.Granted ? "granted" : "denied")}: {string.Join(",", result.People)}");
    }

    private uint ResolveRelayedWorldPersonId(
        uint peerId,
        uint personId)
    {
        var key =
            (
                PeerId:
                    peerId,
                PersonId:
                    personId
            );

        if (_relayedWorldPersonIds.TryGetValue(
                key,
                out var existing))
        {
            return existing;
        }

        const uint start =
            0x00C00000u;
        const uint end =
            0x00FFFFFFu;

        var candidate =
            Math.Clamp(
                _nextRelayedWorldPersonId,
                start,
                end);

        for (var attempts = 0;
             attempts <
                 0x00400000;
             attempts++)
        {
            if (!_relayedWorldPersonIdsInUse.Contains(
                    candidate))
            {
                _relayedWorldPersonIds[
                    key] =
                    candidate;
                _relayedWorldPersonIdsInUse.Add(
                    candidate);

                _nextRelayedWorldPersonId =
                    candidate >=
                            end
                        ? start
                        : candidate +
                          1u;

                return candidate;
            }

            candidate =
                candidate >=
                        end
                    ? start
                    : candidate +
                      1u;
        }

        throw new InvalidOperationException(
            "No relayed openOMSI WORLD person ids are available.");
    }

    private void OnMultiplayerWorldCarDescription(
        OpenOmsiLanWorldCarDescription description)
    {
        _sharedWorldDescriptions[
            description.Id] =
            description;

        _sharedWorldWantRequestedAt.Remove(
            (
                false,
                description.Id
            ));

        var relative =
            OpenOmsiLanProtocol.NormalizeVehiclePath(
                description.VehiclePath);

        if (relative is null)
        {
            return;
        }

        var fullPath =
            Path.GetFullPath(
                Path.Combine(
                    _contentRoot.RootPath,
                    relative.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        if (File.Exists(
                fullPath))
        {
            QueueMultiplayerVehicleAsset(
                fullPath);
        }
    }

    private void OnCommsLinkTransmitRequested(
        bool active)
    {
        SetCommsLinkTransmit(
            active);
    }

    private void OnCommsLinkKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode !=
                Keys.F10 ||
            e.Control ||
            e.Shift ||
            e.Alt)
        {
            return;
        }

        SetCommsLinkTransmit(
            true);

        e.SuppressKeyPress =
            true;
    }

    private void OnCommsLinkKeyUp(
        object? sender,
        KeyEventArgs e)
    {
        if (e.KeyCode !=
            Keys.F10)
        {
            return;
        }

        SetCommsLinkTransmit(
            false);

        e.SuppressKeyPress =
            true;
    }

    private void SetCommsLinkTransmit(
        bool active)
    {
        if (!active)
        {
            if (_commsLinkVoice.IsTransmitting)
            {
                _commsLinkVoice.StopTransmit();
            }

            NotifyCommsLinkVoiceState(
                "VOZ: PTT F10 · pronto");
            return;
        }

        var session =
            _multiplayerSession;

        if (session is null ||
            !session.Connected)
        {
            NotifyCommsLinkVoiceState(
                "VOZ: indisponível · entre em uma sessão multiplayer");
            return;
        }

        _commsLinkLastRemoteVoiceAt =
            null;
        _commsLinkLastRemoteSpeakerId =
            0;

        if (_commsLinkVoice.StartTransmit())
        {
            NotifyCommsLinkVoiceState(
                "VOZ: TRANSMITINDO · solte F10/PTT para encerrar");
        }
        else
        {
            NotifyCommsLinkVoiceState(
                $"VOZ: falha no microfone · {_commsLinkVoice.LastError ?? "dispositivo indisponível"}");
        }
    }

    private void OnMultiplayerVoiceFrame(
        OpenOmsiLanVoiceFrame frame)
    {
        var session =
            _multiplayerSession;

        if (session is null ||
            frame.SenderId ==
                session.PlayerId)
        {
            return;
        }

        _commsLinkVoice.Play(
            frame);

        _commsLinkLastRemoteVoiceAt =
            DateTimeOffset.UtcNow;
        _commsLinkLastRemoteSpeakerId =
            frame.SenderId;

        var speaker =
            session
                .SnapshotPeers()
                .FirstOrDefault(
                    peer =>
                        peer.Id ==
                        frame.SenderId)
                ?.Name;

        NotifyCommsLinkVoiceState(
            string.IsNullOrWhiteSpace(
                speaker)
                ? $"VOZ: recebendo #{frame.SenderId}"
                : $"VOZ: {speaker} falando");
    }

    private void NotifyCommsLinkVoiceState(
        string text)
    {
        _runtimeWindow?.ReceiveDriveOpsMessage(
            new RuntimeDriveOpsInboundMessage(
                0,
                "SISTEMA",
                string.Empty,
                string.Empty,
                "COMMSLINK",
                "VOICE_STATE",
                text,
                string.Empty,
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()));
    }

    private void DisposeMultiplayerSession()
    {
        var session =
            _multiplayerSession;

        _multiplayerSession =
            null;

        if (session is null)
        {
            return;
        }

        session.OperationalMessageReceived -=
            OnMultiplayerOperationalMessage;
        session.VoiceFrameReceived -=
            OnMultiplayerVoiceFrame;
        session.WorldFrameReceived -=
            OnMultiplayerWorldFrame;
        session.WorldCarDescriptionReceived -=
            OnMultiplayerWorldCarDescription;
        session.WorldPeopleFrameReceived -=
            OnMultiplayerWorldPeopleFrame;
        session.WorldPersonDescriptionReceived -=
            OnMultiplayerWorldPersonDescription;
        session.WorldPeopleFrameUpReceived -=
            OnMultiplayerWorldPeopleFrameUp;
        session.WorldPersonDescriptionUpReceived -=
            OnMultiplayerWorldPersonDescriptionUp;
        session.WorldClaimRequested -=
            OnMultiplayerWorldClaimRequested;
        session.WorldClaimResultReceived -=
            OnMultiplayerWorldClaimResult;

        _commsLinkVoice.StopTransmit();
        _commsLinkLastRemoteVoiceAt =
            null;
        _commsLinkLastRemoteSpeakerId =
            0;
        NotifyCommsLinkVoiceState(
            "VOZ: PTT F10 · aguardando sessão");

        try
        {
            session.Leave();
        }
        catch
        {
        }

        session.Dispose();

        _runtimeWindow?.SetDriveOpsNetworkState(
            "OFFLINE",
            false,
            0,
            string.Empty);
        _runtimeWindow?.SetRemoteWalkers(
            []);
        _multiplayerTravelMeters.Clear();
        _multiplayerRemoteStates.Clear();
        _multiplayerScriptRuntimes.Clear();
        _multiplayerScriptPaths.Clear();
        _multiplayerHornStates.Clear();
        _multiplayerActivePeerIds.Clear();
        _multiplayerVehicleLengthMeters =
            0.0f;
        _multiplayerVehicleWidthMeters =
            0.0f;
        _multiplayerNearRequested =
            false;
        _sharedWorldCars.Clear();
        _sharedWorldDescriptions.Clear();
        _sharedWorldPeople.Clear();
        _sharedWorldPersonDescriptions.Clear();
        _pendingWorldPassengerClaims.Clear();
        _worldPassengerClaimAttemptedAt.Clear();
        _ownedWorldPassengers.Clear();
        _worldPassengerUplinkAccumulator =
            0.0;
        _hostWorldPassengers.ResetSession();
        _hostWorldPassengerRefreshAccumulator =
            1.0;
        _hostWorldPassengerSendAccumulator =
            0.0;
        _hostWorldPassengerPeerViews.Clear();
        _sharedWorldWantRequestedAt.Clear();
        _relayedWorldPersonIds.Clear();
        _relayedWorldPersonIdsInUse.Clear();
        _relayedWorldPassengerPeerViews.Clear();
        _nextRelayedWorldPersonId =
            0x00C00000u;
        _sharedWorldLights.Clear();

        bool restoreParkedCars;

        lock (_sharedWorldParkingGate)
        {
            restoreParkedCars =
                _sharedWorldDepartedParkingObjectIds.Count >
                0;

            _sharedWorldDepartedParkingObjectIds.Clear();
        }

        if (restoreParkedCars)
        {
            QueueSharedWorldParkingRefresh();
        }

        _sharedWorldSignalStateBuffer.Clear();
        _sharedWorldSendAccumulator =
            0.0;
        _sharedWorldDescriptionAccumulator =
            0.0;
        _sharedWorldLastFrameAt =
            null;
        _sharedWorldLastAdvertisedCarCount =
            0;
        _sharedWorldCarPeerViews.Clear();
        _sharedWorldPublishedCarIds.Clear();
        _sharedWorldGoneCarSeconds.Clear();
    }

    private OpenOmsiLanPose CreateLocalMultiplayerPose()
    {
        var pose =
            new OpenOmsiLanPose
            {
                Id =
                    _multiplayerSession?.PlayerId ??
                    0,
                Name =
                    _options.MultiplayerPlayerName,
                VehiclePath =
                    _bus?.RelativePath
                        .Replace(
                            '\\',
                            '/') ??
                    string.Empty,
                Paint =
                    _repaintName ??
                    string.Empty,
                Line =
                    _runtimeWindow?.CurrentOperationLine ??
                    string.Empty,
                Destination =
                    _runtimeWindow?.CurrentOperationDestination ??
                    string.Empty,
                FigurePath =
                    _currentWorld?
                        .AiCatalog
                        .Drivers
                        .Concat(
                            _currentWorld.AiCatalog.Humans)
                        .FirstOrDefault(
                            static figure =>
                                figure.Exists)?
                        .DeclaredPath
                        .Replace(
                            '\\',
                            '/') ??
                    string.Empty,
                RadioKeyed =
                    _commsLinkVoice.IsTransmitting
            };

        pose.DisplayTexts =
            ResolveVehicleDisplayTextValues(
                _vehicleAsset,
                _playerScriptRuntime);

        pose.FreeTexturePaths =
            ResolveVehicleFreeTextureValues(
                _vehicleAsset,
                _playerScriptRuntime);

        if (_runtimeWindow is not
            { IsDisposed: false } window ||
            _bus is null)
        {
            return pose;
        }

        var walker =
            window.LocalWalkerState;

        var state =
            window.LocalVehicleState;

        // openOMSI's wire pose uses X/Y as the ground plane and Z as
        // elevation. The D3D11 runtime uses X/Z as ground and Y as up.
        pose.X =
            -state.Position.X;
        pose.Y =
            state.Position.Z;
        pose.Z =
            state.Position.Y;
        pose.HeadingDegrees =
            -state.HeadingRadians *
            180.0f /
            MathF.PI;
        pose.SpeedKph =
            state.SpeedMetersPerSecond *
            3.6f;
        pose.SteeringDegrees =
            state.SteeringAngleRadians *
            180.0f /
            MathF.PI;
        pose.Throttle =
            Math.Clamp(
                state.AcceleratorLevel,
                0.0f,
                1.0f);
        pose.Brake =
            Math.Clamp(
                state.BrakeLevel,
                0.0f,
                1.0f);

        pose.Rpm =
            (float)ReadFirstPlayerLocal(
                "engine_n",
                "n_engine",
                "engine_rpm");

        var flags =
            OpenOmsiLanProtocol.FlagVehicle;

        if (state.EngineRunning ||
            pose.Rpm >
                100.0f ||
            ReadFirstPlayerLocal(
                "engine_on") >
                0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagEngine;
        }

        if (state.ElectricalSystemEnabled ||
            ReadFirstPlayerLocal(
                "elec_busbar_main",
                "elec_busbar_avail") >
                0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagElectrics;
        }

        if (ReadFirstPlayerLocal(
                "cockpit_hupe",
                "cockpit_hupe_swheel",
                "horn",
                "cockpit_hupe_volume") >
            0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagHorn;
        }

        if (state.BrakeLevel >
                0.05f ||
            ReadFirstPlayerLocal(
                "lights_brems") >
                0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagBrake;
        }

        if (state.Gear <
                0 ||
            ReadFirstPlayerLocal(
                "lights_rueckfahr") >
                0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagReverse;
        }

        if (ReadFirstPlayerLocal(
                "lights_nebelschluss") >
            0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagFog;
        }

        if (ReadFirstPlayerLocal(
                "bremse_kneeling",
                "vdv_kneel",
                "ecas_kneel",
                "kneeling") >
            0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagKneeling;
        }

        if (ReadFirstPlayerLocal(
                "wiperrunning",
                "wiper_running") >
            0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagWipers;
        }

        if (state.StopBrakeEngaged ||
            state.ParkingBrakeEngaged ||
            ReadFirstPlayerLocal(
                "bremse_halte") >
                0.5)
        {
            flags |=
                OpenOmsiLanProtocol.FlagStopBrake;
        }

        pose.Flags =
            flags;

        var parkingLights =
            ReadFirstPlayerLocal(
                "lights_stand",
                "lights_standlicht",
                "lights_parking") >
            0.5;

        var selectedSpotlight =
            _playerScriptRuntime is
                { } playerScript &&
            playerScript.HasLocalVariable(
                "Spot_Select") &&
            playerScript.GetLocal(
                "Spot_Select") >=
                0.0;

        var dippedLights =
            ReadFirstPlayerLocal(
                "lights_abbl",
                "lights_abblend",
                "lights_lowbeam",
                "lights_main",
                "ai_light") >
                0.5 ||
            selectedSpotlight;

        var highLights =
            ReadFirstPlayerLocal(
                "lights_fern",
                "lights_fernlicht",
                "lights_highbeam") >
            0.5;

        pose.HeadLights =
            highLights
                ? (byte)3
                : dippedLights
                    ? (byte)2
                    : parkingLights
                        ? (byte)1
                        : (byte)0;

        pose.InteriorLights =
            ReadFirstPlayerLocal(
                "lights_innen",
                "lights_interior",
                "cockpit_light") >
            0.5
                ? (byte)1
                : (byte)0;

        var leftBlinker =
            ReadFirstPlayerLocal(
                "lights_blinker_l",
                "blinker_l",
                "indicator_left") >
            0.5;

        var rightBlinker =
            ReadFirstPlayerLocal(
                "lights_blinker_r",
                "blinker_r",
                "indicator_right") >
            0.5;

        pose.Blinker =
            leftBlinker &&
            rightBlinker
                ? (byte)3
                : leftBlinker
                    ? (byte)1
                    : rightBlinker
                        ? (byte)2
                        : (byte)0;

        pose.Doors.Clear();

        for (var doorIndex = 0;
             doorIndex <
                 OpenOmsiLanProtocol.MaximumDoors;
             doorIndex++)
        {
            pose.Doors.Add(
                (float)Math.Clamp(
                    ReadFirstPlayerLocal(
                        $"door_{doorIndex}",
                        $"door{doorIndex}",
                        $"door_{doorIndex}_pos",
                        $"door_pos_{doorIndex}"),
                    0.0,
                    1.0));
        }

        if (window.PlayerTrafficObstacle is
            { } obstacle)
        {
            _multiplayerVehicleLengthMeters =
                (float)(
                    obstacle.HalfLengthMeters *
                    2.0);
            _multiplayerVehicleWidthMeters =
                (float)(
                    obstacle.HalfWidthMeters *
                    2.0);
        }

        pose.LengthMeters =
            _multiplayerVehicleLengthMeters;
        pose.WidthMeters =
            _multiplayerVehicleWidthMeters;

        if (walker.Active)
        {
            OpenOmsiLanAboard?
                aboard =
                    null;

            if (window.LocalWalkerAboardState is
                { } aboardState)
            {
                ushort? seatIndex =
                    aboardState.SeatIndex is
                        >= 0 and <= 1023
                        ? (ushort)aboardState.SeatIndex
                        : null;

                aboard =
                    new OpenOmsiLanAboard(
                        _multiplayerSession?
                            .PlayerId ??
                        0,
                        -aboardState.LocalPosition.X,
                        aboardState.LocalPosition.Z,
                        aboardState.LocalPosition.Y,
                        seatIndex);
            }

            pose.Walker =
                new OpenOmsiLanWalker(
                    -walker.Position.X,
                    walker.Position.Z,
                    walker.Position.Y,
                    -walker.HeadingDegrees,
                    walker.SpeedMetersPerSecond,
                    -walker.CourseDegrees,
                    walker.Seated,
                    aboard);
        }

        return pose;
    }

    private double ReadFirstPlayerLocal(
        params string[] names)
    {
        var runtime =
            _playerScriptRuntime;

        if (runtime is null)
        {
            return 0.0;
        }

        foreach (var name in
                 names)
        {
            if (runtime.HasLocalVariable(
                    name))
            {
                var value =
                    runtime.GetLocal(
                        name);

                if (double.IsFinite(
                        value))
                {
                    return value;
                }
            }
        }

        return 0.0;
    }

    private void AppendSharedWorldPeople(
        ICollection<RuntimeRemoteWalkerInfo> target,
        IReadOnlyList<OpenOmsiLanPeerSnapshot> peers)
    {
        var now =
            DateTimeOffset.UtcNow;

        foreach (var stale in
                 _sharedWorldPeople
                     .Where(
                         pair =>
                             now -
                                 pair.Value.LastSeen >
                             TimeSpan.FromSeconds(
                                 3.5))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _sharedWorldPeople.Remove(
                stale);
        }

        foreach (var pair in
                 _sharedWorldPeople
                     .OrderBy(
                         static pair =>
                             pair.Key))
        {
            var person =
                pair.Value.State;

            double sourceX;
            double sourceY;
            double sourceZ;
            float sourceHeading;
            var seated =
                person.Activity ==
                    OpenOmsiLanWorldPersonActivity.Sit ||
                person.SeatIndex.HasValue;

            if (!person.Aboard)
            {
                sourceX =
                    person.X;
                sourceY =
                    person.Y;
                sourceZ =
                    person.Z;
                sourceHeading =
                    person.HeadingDegrees;
            }
            else
            {
                double busX;
                double busY;
                double busZ;
                float busHeading;

                if (person.PlayerBus)
                {
                    var peer =
                        peers.FirstOrDefault(
                            candidate =>
                                candidate.Id ==
                                    person.BusId &&
                                candidate.HasState &&
                                candidate.Pose.HasVehicle);

                    if (peer is null)
                    {
                        continue;
                    }

                    busX =
                        peer.Pose.X;
                    busY =
                        peer.Pose.Y;
                    busZ =
                        peer.Pose.Z;
                    busHeading =
                        peer.Pose.HeadingDegrees;
                }
                else
                {
                    if (!_sharedWorldCars.TryGetValue(
                            person.BusId,
                            out var car))
                    {
                        continue;
                    }

                    var projectedCar =
                        ProjectSharedWorldCarState(
                            car.State,
                            car.LastSeen,
                            now);

                    busX =
                        projectedCar.X;
                    busY =
                        projectedCar.Y;
                    busZ =
                        projectedCar.Z;
                    busHeading =
                        projectedCar.HeadingDegrees;
                }

                var headingRadians =
                    busHeading *
                    Math.PI /
                    180.0;

                var rightX =
                    Math.Cos(
                        headingRadians);
                var rightY =
                    -Math.Sin(
                        headingRadians);
                var forwardX =
                    Math.Sin(
                        headingRadians);
                var forwardY =
                    Math.Cos(
                        headingRadians);

                sourceX =
                    busX +
                    rightX *
                        person.X +
                    forwardX *
                        person.Y;
                sourceY =
                    busY +
                    rightY *
                        person.X +
                    forwardY *
                        person.Y;
                sourceZ =
                    busZ +
                    person.Z;
                sourceHeading =
                    busHeading +
                    person.HeadingDegrees;
            }

            _sharedWorldPersonDescriptions.TryGetValue(
                person.Id,
                out var description);

            target.Add(
                new RuntimeRemoteWalkerInfo(
                    0x80000000u |
                    person.Id,
                    $"P{person.Id}",
                    description?.HumanPath ??
                        string.Empty,
                    RuntimeWorldXFromSource(
                        sourceX),
                    sourceZ,
                    sourceY,
                    (float)RuntimeHeadingDegreesFromSource(
                        sourceHeading),
                    person.SpeedMetersPerSecond,
                    (float)RuntimeHeadingDegreesFromSource(
                        sourceHeading),
                    seated));
        }
    }

    private void TryClaimSharedWorldPassengers(
        OpenOmsiLanSession session,
        OpenOmsiLanPose localPose)
    {
        if (session.Role !=
                OpenOmsiLanRole.Client ||
            !session.Connected)
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        foreach (var stale in
                 _worldPassengerClaimAttemptedAt
                     .Where(
                         pair =>
                             now -
                                 pair.Value >
                             TimeSpan.FromSeconds(
                                 10))
                     .Select(
                         static pair =>
                             pair.Key)
                     .ToArray())
        {
            _worldPassengerClaimAttemptedAt.Remove(
                stale);
            _pendingWorldPassengerClaims.Remove(
                stale);

            if (!_sharedWorldPeople.ContainsKey(
                    stale) &&
                !_ownedWorldPassengers.ContainsKey(
                    stale))
            {
                _sharedWorldPersonDescriptions.Remove(
                    stale);
                _sharedWorldWantRequestedAt.Remove(
                    (
                        true,
                        stale
                    ));
            }
        }

        // Expire unanswered hand-overs even when the doors have closed.
        if (_bus is null ||
            _vehicleAsset?.PassengerCabin?.Entries.Count <=
                0 ||
            Math.Abs(
                localPose.SpeedKph) >
                1.5 ||
            !IsPassengerEntryOpen(
                localPose))
        {
            return;
        }

        var candidates =
            _sharedWorldPeople
                .Where(
                    pair =>
                        !pair.Value.State.Aboard &&
                        pair.Value.State.WaitingStopObjectId.HasValue &&
                        !_ownedWorldPassengers.ContainsKey(
                            pair.Key) &&
                        !_worldPassengerClaimAttemptedAt.ContainsKey(
                            pair.Key))
                .Select(
                    pair =>
                    {
                        var dx =
                            pair.Value.State.X -
                            localPose.X;

                        var dy =
                            pair.Value.State.Y -
                            localPose.Y;

                        return new
                        {
                            Id =
                                pair.Key,
                            pair.Value.State,
                            Stop =
                                pair.Value.State.WaitingStopObjectId!.Value,
                            DistanceSquared =
                                dx *
                                dx +
                                dy *
                                dy
                        };
                    })
                .Where(
                    static item =>
                        item.DistanceSquared <=
                        12.0 *
                        12.0)
                .OrderBy(
                    static item =>
                        item.DistanceSquared)
                .ToArray();

        if (candidates.Length ==
            0)
        {
            return;
        }

        var stop =
            candidates[0]
                .Stop;

        var selected =
            candidates
                .Where(
                    item =>
                        item.Stop ==
                        stop)
                .Take(
                    32)
                .ToArray();

        var ids =
            selected
                .Select(
                    static item =>
                        item.Id)
                .ToArray();

        foreach (var item in
                 selected)
        {
            var humanPath =
                _sharedWorldPersonDescriptions.TryGetValue(
                    item.Id,
                    out var description)
                    ? description.HumanPath
                    : string.Empty;

            _pendingWorldPassengerClaims[
                item.Id] =
                (
                    item.State,
                    humanPath
                );

            _worldPassengerClaimAttemptedAt[
                item.Id] =
                now;
        }

        if (!session.SendWorldClaim(
                ids))
        {
            foreach (var id in
                     ids)
            {
                _pendingWorldPassengerClaims.Remove(
                    id);
                _worldPassengerClaimAttemptedAt.Remove(
                    id);
            }

            return;
        }

        Console.WriteLine(
            $"[multiplayer-world] passenger claim requested: stop={stop}; people={string.Join(",", ids)}");
    }

    private HashSet<int> GetOpenPassengerEntries(
        OpenOmsiLanPose pose)
    {
        var entries =
            new HashSet<int>();

        var cabin =
            _vehicleAsset?
                .PassengerCabin;

        var entryCount =
            cabin?
                .Entries.Count ??
            0;

        if (entryCount <=
            0)
        {
            return entries;
        }

        var runtime =
            _playerScriptRuntime;

        var hasPassengerDoorVariable =
            false;

        if (runtime is not null)
        {
            for (var index = 0;
                 index <
                     Math.Min(
                         16,
                         entryCount);
                 index++)
            {
                var name =
                    $"PAX_Entry{index}_Open";

                if (!runtime.HasLocalVariable(
                        name))
                {
                    continue;
                }

                hasPassengerDoorVariable =
                    true;

                if (runtime.GetLocal(
                        name) >
                    0.5)
                {
                    entries.Add(
                        index);
                }
            }
        }

        if (hasPassengerDoorVariable)
        {
            return entries;
        }

        if (!pose.Doors.Any(
                static value =>
                    value >
                    0.75f))
        {
            return entries;
        }

        for (var index = 0;
             index <
                 entryCount;
             index++)
        {
            entries.Add(
                index);
        }

        return entries;
    }

    private bool IsPassengerEntryOpen(
        OpenOmsiLanPose pose) =>
        GetOpenPassengerEntries(
            pose).Count >
        0;

    private HashSet<int> GetOpenPassengerExits(
        OpenOmsiLanPose pose)
    {
        var exits =
            new HashSet<int>();

        var cabin =
            _vehicleAsset?
                .PassengerCabin;

        var exitCount =
            cabin?
                .Exits.Count ??
            0;

        if (exitCount <=
            0)
        {
            return exits;
        }

        var runtime =
            _playerScriptRuntime;

        var hasPassengerExitVariable =
            false;

        if (runtime is not null)
        {
            for (var index = 0;
                 index <
                     Math.Min(
                         16,
                         exitCount);
                 index++)
            {
                var name =
                    $"PAX_Exit{index}_Open";

                if (!runtime.HasLocalVariable(
                        name))
                {
                    continue;
                }

                hasPassengerExitVariable =
                    true;

                if (runtime.GetLocal(
                        name) >
                    0.5)
                {
                    exits.Add(
                        index);
                }
            }
        }

        if (hasPassengerExitVariable)
        {
            return exits;
        }

        if (!pose.Doors.Any(
                static value =>
                    value >
                    0.75f))
        {
            return exits;
        }

        for (var index = 0;
             index <
                 exitCount;
             index++)
        {
            exits.Add(
                index);
        }

        return exits;
    }

    private void UpdateOwnedWorldPassengers(
        OpenOmsiLanSession session,
        OpenOmsiLanPose localPose,
        double deltaSeconds,
        ICollection<RuntimeRemoteWalkerInfo> target)
    {
        if (session.Role !=
                OpenOmsiLanRole.Client ||
            _ownedWorldPassengers.Count ==
                0)
        {
            return;
        }

        var reserved =
            _ownedWorldPassengers
                .Values
                .Select(
                    static passenger =>
                        passenger.ReservedPlaceIndex)
                .Where(
                    static index =>
                        index.HasValue)
                .Select(
                    static index =>
                        index!.Value)
                .ToHashSet();

        if (_vehicleAsset?.PassengerCabin is
                { } passengerCabin &&
            _playerScriptRuntime is
                { } scriptRuntime)
        {
            foreach (var place in
                     passengerCabin.PassengerPositions)
            {
                if (string.IsNullOrWhiteSpace(
                        place.SwitchVariable) ||
                    !scriptRuntime.HasLocalVariable(
                        place.SwitchVariable))
                {
                    continue;
                }

                if (scriptRuntime.GetLocal(
                        place.SwitchVariable) <=
                    0.5)
                {
                    reserved.Add(
                        place.FileIndex);
                }
            }
        }

        var openEntries =
            GetOpenPassengerEntries(
                localPose);

        var openExits =
            GetOpenPassengerExits(
                localPose);

        var doorsOpen =
            openEntries.Count >
                0 ||
            openExits.Count >
                0;

        foreach (var passenger in
                 _ownedWorldPassengers
                     .Values
                     .OrderBy(
                         static passenger =>
                             passenger.State.Id))
        {
            passenger.Update(
                deltaSeconds,
                _vehicleAsset,
                localPose,
                session.PlayerId,
                doorsOpen,
                reserved,
                openEntries,
                openExits);

            var world =
                passenger.WorldPose(
                    localPose);

            target.Add(
                new RuntimeRemoteWalkerInfo(
                    passenger.State.Id,
                    $"PAX {passenger.State.Id}",
                    passenger.HumanPath,
                    RuntimeWorldXFromSource(
                        world.X),
                    world.Z,
                    world.Y,
                    (float)RuntimeHeadingDegreesFromSource(
                        world.Heading),
                    passenger.State.SpeedMetersPerSecond,
                    (float)RuntimeHeadingDegreesFromSource(
                        world.Heading),
                    passenger.State.Activity ==
                        OpenOmsiLanWorldPersonActivity.Sit ||
                    passenger.State.SeatIndex.HasValue));
        }

        _worldPassengerUplinkAccumulator +=
            Math.Max(
                0.0,
                deltaSeconds);

        if (_worldPassengerUplinkAccumulator <
            0.1)
        {
            return;
        }

        _worldPassengerUplinkAccumulator =
            Math.Clamp(
                _worldPassengerUplinkAccumulator -
                    0.1,
                0.0,
                0.1);

        var completed =
            _ownedWorldPassengers
                .Values
                .Where(
                    static passenger =>
                        passenger.Completed)
                .Select(
                    static passenger =>
                        passenger.State.Id)
                .ToArray();

        session.SendWorldPeopleFrameUp(
            new OpenOmsiLanWorldPeopleFrame(
                0,
                unchecked(
                    (uint)Environment.TickCount64),
                _ownedWorldPassengers
                    .Values
                    .Where(
                        static passenger =>
                            !passenger.Completed)
                    .Select(
                        static passenger =>
                            passenger.State)
                    .ToArray(),
                completed
                    .Select(
                        static id =>
                            new OpenOmsiLanWorldGoneEntity(
                                true,
                                id))
                    .ToArray()));

        foreach (var id in
                 completed)
        {
            _ownedWorldPassengers.Remove(
                id);
        }
    }

    private void UpdateHostWorldPassengers(
        OpenOmsiLanSession session,
        IReadOnlyList<OpenOmsiLanPeerSnapshot> peers,
        OpenOmsiLanPose localPose,
        double deltaSeconds,
        ICollection<RuntimeRemoteWalkerInfo> target)
    {
        if (session.Role !=
            OpenOmsiLanRole.Host)
        {
            return;
        }

        const double personRadiusMeters =
            260.0;

        var radiusSquared =
            personRadiusMeters *
            personRadiusMeters;

        _hostWorldPassengerRefreshAccumulator +=
            Math.Max(
                0.0,
                deltaSeconds);

        if (_hostWorldPassengerRefreshAccumulator >=
            1.0)
        {
            _hostWorldPassengerRefreshAccumulator =
                Math.Clamp(
                    _hostWorldPassengerRefreshAccumulator -
                        1.0,
                    0.0,
                    1.0);

            try
            {
                _hostWorldPassengers.Refresh(
                    _currentWorld,
                    _timetableCatalog.Value);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"[multiplayer-world] host passenger refresh failed: {exception.Message}");
            }

            // Descriptions are now sent only when a passenger enters a
            // peer's 260 m WORLD view. The session caches each description
            // so a later WANT can be answered even if the first DESC packet
            // was lost.
            _ =
                _hostWorldPassengers.TakePendingDescriptions();
        }

        // The host's own 3D scene needs only people around the active
        // vehicle/walker. Other peers still receive their independent
        // 260 m WORLD views below, without using this local cull.
        var localCenterX =
            localPose.Walker?.X ??
            localPose.X;
        var localCenterY =
            localPose.Walker?.Y ??
            localPose.Y;

        foreach (var person in
                 _hostWorldPassengers.People.Where(
                     person =>
                     {
                         var dx =
                             person.X -
                             localCenterX;
                         var dy =
                             person.Y -
                             localCenterY;

                         return person.Aboard ||
                                dx * dx +
                                dy * dy <=
                                radiusSquared;
                     }))
        {
            var humanPath =
                _hostWorldPassengers.HumanPath(
                    person.Id) ??
                string.Empty;

            target.Add(
                new RuntimeRemoteWalkerInfo(
                    0x40000000u |
                    person.Id,
                    $"PAX {person.Id}",
                    humanPath,
                    RuntimeWorldXFromSource(
                        person.X),
                    person.Z,
                    person.Y,
                    (float)RuntimeHeadingDegreesFromSource(
                        person.HeadingDegrees),
                    person.SpeedMetersPerSecond,
                    (float)RuntimeHeadingDegreesFromSource(
                        person.HeadingDegrees),
                    person.Activity ==
                        OpenOmsiLanWorldPersonActivity.Sit));
        }

        _hostWorldPassengerSendAccumulator +=
            Math.Max(
                0.0,
                deltaSeconds);

        if (_hostWorldPassengerSendAccumulator <
            0.1)
        {
            return;
        }

        var elapsed =
            _hostWorldPassengerSendAccumulator;

        _hostWorldPassengerSendAccumulator =
            Math.Clamp(
                _hostWorldPassengerSendAccumulator -
                    0.1,
                0.0,
                0.1);

        // CreateFrame also ages the authority's global gone markers. Per-peer
        // visibility below maintains its own gone queue so people leaving a
        // client's radius disappear immediately, as in openOMSI.
        var authorityFrame =
            _hostWorldPassengers.CreateFrame(
                elapsed);

        var activePeers =
            peers
                .Where(
                    static peer =>
                        peer.HasState &&
                        peer.Pose.HasVehicle)
                .ToArray();

        var activePeerIds =
            activePeers
                .Select(
                    static peer =>
                        peer.Id)
                .ToHashSet();

        foreach (var stalePeerId in
                 _hostWorldPassengerPeerViews
                     .Keys
                     .Where(
                         id =>
                             !activePeerIds.Contains(
                                 id))
                     .ToArray())
        {
            _hostWorldPassengerPeerViews.Remove(
                stalePeerId);
        }

        foreach (var peer in
                 activePeers)
        {
            if (!_hostWorldPassengerPeerViews.TryGetValue(
                    peer.Id,
                    out var view))
            {
                view =
                    new WorldPeerVisibilityView();

                _hostWorldPassengerPeerViews[
                    peer.Id] =
                    view;
            }

            var visiblePeople =
                authorityFrame.People
                    .Where(
                        person =>
                        {
                            if (person.Aboard)
                            {
                                return true;
                            }

                            var dx =
                                person.X -
                                peer.Pose.X;

                            var dy =
                                person.Y -
                                peer.Pose.Y;

                            return dx *
                                       dx +
                                   dy *
                                       dy <=
                                   radiusSquared;
                        })
                    .OrderBy(
                        static person =>
                            person.Id)
                    .ToArray();

            var visibleIds =
                visiblePeople
                    .Select(
                        static person =>
                            person.Id)
                    .ToHashSet();

            foreach (var newlyVisible in
                     visiblePeople.Where(
                         person =>
                             !view.VisibleIds.Contains(
                                 person.Id)))
            {
                var humanPath =
                    _hostWorldPassengers.HumanPath(
                        newlyVisible.Id);

                if (string.IsNullOrWhiteSpace(
                        humanPath))
                {
                    continue;
                }

                session.SendWorldPersonDescriptionTo(
                    peer.Id,
                    new OpenOmsiLanWorldPersonDescription(
                        newlyVisible.Id,
                        humanPath));
            }

            foreach (var previousId in
                     view.VisibleIds)
            {
                if (!visibleIds.Contains(
                        previousId))
                {
                    view.GoneSeconds[
                        previousId] =
                        1.0;
                }
            }

            foreach (var visibleId in
                     visibleIds)
            {
                view.GoneSeconds.Remove(
                    visibleId);
            }

            view.VisibleIds.Clear();

            foreach (var visibleId in
                     visibleIds)
            {
                view.VisibleIds.Add(
                    visibleId);
            }

            var gone =
                view.GoneSeconds
                    .Keys
                    .OrderBy(
                        static id =>
                            id)
                    .Take(
                        63)
                    .Select(
                        static id =>
                            new OpenOmsiLanWorldGoneEntity(
                                true,
                                id))
                    .ToArray();

            if (visiblePeople.Length >
                    0 ||
                gone.Length >
                    0)
            {
                session.SendWorldPeopleFrameTo(
                    peer.Id,
                    new OpenOmsiLanWorldPeopleFrame(
                        0,
                        0,
                        visiblePeople,
                        gone));
            }

            var goneStep =
                Math.Clamp(
                    elapsed,
                    0.0,
                    0.25);

            foreach (var removed in
                     gone)
            {
                if (!view.GoneSeconds.TryGetValue(
                        removed.Id,
                        out var remaining))
                {
                    continue;
                }

                remaining -=
                    goneStep;

                if (remaining <=
                    0.000001)
                {
                    view.GoneSeconds.Remove(
                        removed.Id);
                }
                else
                {
                    view.GoneSeconds[
                        removed.Id] =
                        remaining;
                }
            }
        }
    }

    private void AppendMultiplayerTrafficAgents(
        double deltaSeconds)
    {
        var session =
            _multiplayerSession;

        if (session is null)
        {
            return;
        }

        var localPose =
            CreateLocalMultiplayerPose();

        if (session.Role ==
            OpenOmsiLanRole.Host)
        {
            session.SetLocalNearFootprints(
                _trafficAgentStateBuffer
                    .Select(
                        static agent =>
                            new OpenOmsiLanFootprint(
                                agent.Position.X,
                                agent.Position.Z,
                                agent.Position.Y,
                                (float)(
                                    agent.HeadingRadians *
                                    180.0 /
                                    Math.PI),
                                (float)Math.Max(
                                    agent.HalfLengthMeters *
                                    2.0,
                                    1.0),
                                (float)Math.Max(
                                    agent.HalfWidthMeters *
                                    2.0,
                                    1.0)))
                    .ToArray());
        }

        try
        {
            session.Tick(
                deltaSeconds,
                localPose);

            if (session.Role ==
                    OpenOmsiLanRole.Client &&
                session.Connected &&
                !_multiplayerNearRequested &&
                localPose.HasVehicle)
            {
                var headingRadians =
                    localPose.HeadingDegrees *
                    Math.PI /
                    180.0;

                var footprint =
                    new OpenOmsiLanFootprint(
                        localPose.X +
                            Math.Sin(
                                headingRadians) *
                            localPose.BoxOffsetMeters,
                        localPose.Y +
                            Math.Cos(
                                headingRadians) *
                            localPose.BoxOffsetMeters,
                        localPose.Z,
                        localPose.HeadingDegrees,
                        Math.Max(
                            localPose.LengthMeters,
                            1.0f),
                        Math.Max(
                            localPose.WidthMeters,
                            1.0f));

                if (session.RequestNear(
                        footprint))
                {
                    _multiplayerNearRequested =
                        true;
                }
            }

            while (_commsLinkVoice.TryDequeueOutgoing(
                       out var voicePcm))
            {
                if (!session.SendVoiceFrame(
                        voicePcm))
                {
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[multiplayer] tick failed: {exception.Message}");
            return;
        }

        var peers =
            session.SnapshotPeers();

        var remoteWalkers =
            new List<RuntimeRemoteWalkerInfo>();

        foreach (var peer in
                 peers)
        {
            var walker =
                peer.Pose.Walker;

            if (!peer.HasState ||
                walker is null)
            {
                continue;
            }

            var walkerSourceX =
                walker.X;
            var walkerSourceY =
                walker.Y;
            var walkerSourceZ =
                walker.Z;

            if (walker.Aboard is
                { } aboard)
            {
                OpenOmsiLanPose?
                    ownerPose =
                        null;

                if (aboard.OwnerId ==
                        session.PlayerId &&
                    localPose.HasVehicle)
                {
                    ownerPose =
                        localPose;
                }
                else
                {
                    ownerPose =
                        peers
                            .FirstOrDefault(
                                candidate =>
                                    candidate.Id ==
                                        aboard.OwnerId &&
                                    candidate.HasState &&
                                    candidate.Pose.HasVehicle)?
                            .Pose;
                }

                if (ownerPose is null)
                {
                    // openOMSI also suppresses a seated avatar when the
                    // player bus named by Aboard is not known locally.
                    if (walker.Seated)
                    {
                        continue;
                    }
                }
                else
                {
                    var busHeadingRadians =
                        ownerPose.HeadingDegrees *
                        Math.PI /
                        180.0;

                    var rightX =
                        Math.Cos(
                            busHeadingRadians);

                    var rightY =
                        -Math.Sin(
                            busHeadingRadians);

                    var forwardX =
                        Math.Sin(
                            busHeadingRadians);

                    var forwardY =
                        Math.Cos(
                            busHeadingRadians);

                    walkerSourceX =
                        ownerPose.X +
                        rightX *
                            aboard.LocalX +
                        forwardX *
                            aboard.LocalY;

                    walkerSourceY =
                        ownerPose.Y +
                        rightY *
                            aboard.LocalX +
                        forwardY *
                            aboard.LocalY;

                    walkerSourceZ =
                        ownerPose.Z +
                        aboard.LocalZ;
                }
            }

            remoteWalkers.Add(
                new RuntimeRemoteWalkerInfo(
                    peer.Id,
                    peer.Name,
                    peer.Pose.FigurePath,
                    RuntimeWorldXFromSource(
                        walkerSourceX),
                    walkerSourceZ,
                    walkerSourceY,
                    (float)RuntimeHeadingDegreesFromSource(
                        walker.HeadingDegrees),
                    walker.SpeedMetersPerSecond,
                    (float)RuntimeHeadingDegreesFromSource(
                        walker.CourseDegrees),
                    walker.Seated));
        }

        UpdateHostWorldPassengers(
            session,
            peers,
            localPose,
            deltaSeconds,
            remoteWalkers);

        TryClaimSharedWorldPassengers(
            session,
            localPose);

        AppendSharedWorldPeople(
            remoteWalkers,
            peers);

        UpdateOwnedWorldPassengers(
            session,
            localPose,
            deltaSeconds,
            remoteWalkers);

        _runtimeWindow?.SetRemoteWalkers(
            remoteWalkers);

        if (!_commsLinkVoice.IsTransmitting &&
            _commsLinkLastRemoteVoiceAt.HasValue &&
            DateTimeOffset.UtcNow -
                _commsLinkLastRemoteVoiceAt.Value >=
                TimeSpan.FromMilliseconds(
                    650))
        {
            _commsLinkLastRemoteVoiceAt =
                null;
            _commsLinkLastRemoteSpeakerId =
                0;

            NotifyCommsLinkVoiceState(
                "VOZ: PTT F10 · pronto");
        }

        _multiplayerStatusAccumulator +=
            Math.Max(
                0.0,
                deltaSeconds);

        if (_multiplayerStatusAccumulator >=
            1.0)
        {
            _multiplayerStatusAccumulator =
                0.0;

            var rejection =
                (session.RejectionReason ??
                 string.Empty)
                    .Replace(
                        '|',
                        '/')
                    .Replace(
                        '\r',
                        ' ')
                    .Replace(
                        '\n',
                        ' ');

            Console.WriteLine(
                $"[multiplayer-status]|{session.Role}|{(session.Connected ? 1 : 0)}|{peers.Count}|{session.SessionHex}|{session.LocalPort}|{rejection}");

            _runtimeWindow?.SetDriveOpsNetworkState(
                session.Role.ToString().ToUpperInvariant(),
                session.Connected,
                peers.Count,
                string.IsNullOrWhiteSpace(
                    session.SessionCode)
                    ? session.SessionHex
                    : session.SessionCode);
        }

        _multiplayerActivePeerIds.Clear();

        foreach (var peer in
                 peers)
        {
            _multiplayerActivePeerIds.Add(
                peer.Id);
            if (!peer.HasState ||
                !peer.Pose.HasVehicle)
            {
                continue;
            }

            var vehiclePath =
                ResolveMultiplayerVehiclePath(
                    peer.Pose.VehiclePath);

            if (vehiclePath is null)
            {
                continue;
            }

            if (!_trafficVehicleAssets.ContainsKey(
                    vehiclePath))
            {
                QueueMultiplayerVehicleAsset(
                    vehiclePath);
                continue;
            }

            var speedMetersPerSecond =
                peer.Pose.SpeedKph /
                3.6;

            if (!_multiplayerRemoteStates.TryGetValue(
                    peer.Id,
                    out var smoothed))
            {
                smoothed =
                    new MultiplayerRemoteRenderState();

                _multiplayerRemoteStates[
                    peer.Id] =
                    smoothed;
            }

            var ageSeconds =
                Math.Clamp(
                    (
                        DateTimeOffset.UtcNow -
                        peer.LastSeen
                    ).TotalSeconds +
                    0.04,
                    0.0,
                    0.4);

            var networkHeadingRadians =
                peer.Pose.HeadingDegrees *
                Math.PI /
                180.0;

            // Same fallback used by openOMSI for peers without enough sample
            // history: carry the latest state forward briefly by its speed,
            // then glide at roughly the network's 20 Hz cadence.
            var targetX =
                peer.Pose.X +
                Math.Sin(
                    networkHeadingRadians) *
                speedMetersPerSecond *
                ageSeconds;

            var targetY =
                peer.Pose.Y +
                Math.Cos(
                    networkHeadingRadians) *
                speedMetersPerSecond *
                ageSeconds;

            var targetZ =
                peer.Pose.Z;

            var smoothing =
                Math.Clamp(
                    Math.Max(
                        0.0,
                        deltaSeconds) *
                    20.0,
                    0.0,
                    1.0);

            var deltaX =
                targetX -
                smoothed.X;
            var deltaY =
                targetY -
                smoothed.Y;
            var deltaZ =
                targetZ -
                smoothed.Z;

            var distanceSquared =
                deltaX *
                    deltaX +
                deltaY *
                    deltaY +
                deltaZ *
                    deltaZ;

            if (!smoothed.Initialized ||
                distanceSquared >
                    25.0 *
                    25.0)
            {
                smoothed.X =
                    targetX;
                smoothed.Y =
                    targetY;
                smoothed.Z =
                    targetZ;
                smoothed.HeadingDegrees =
                    peer.Pose.HeadingDegrees;
                smoothed.Initialized =
                    true;
            }
            else
            {
                smoothed.X +=
                    deltaX *
                    smoothing;
                smoothed.Y +=
                    deltaY *
                    smoothing;
                smoothed.Z +=
                    deltaZ *
                    smoothing;

                var headingDelta =
                    (
                        peer.Pose.HeadingDegrees -
                        smoothed.HeadingDegrees +
                        540.0
                    ) %
                    360.0 -
                    180.0;

                smoothed.HeadingDegrees =
                    (
                        smoothed.HeadingDegrees +
                        headingDelta *
                        smoothing +
                        360.0
                    ) %
                    360.0;
            }

            _multiplayerTravelMeters.TryGetValue(
                peer.Id,
                out var traveled);

            traveled +=
                Math.Abs(
                    speedMetersPerSecond) *
                Math.Max(
                    0.0,
                    deltaSeconds);

            _multiplayerTravelMeters[
                peer.Id] =
                traveled;

            var blinker =
                peer.Pose.Blinker;

            var remoteScriptRuntime =
                ResolveMultiplayerScriptRuntime(
                    peer,
                    vehiclePath,
                    deltaSeconds);

            _runtimeTrafficAgentBuffer.Add(
                new RuntimeTrafficAgentInfo(
                    unchecked(
                        (int)(
                            0x60000000u |
                            (peer.Id &
                             0x1FFFFFFFu))),
                    -1,
                    0.0,
                    speedMetersPerSecond,
                    vehiclePath,
                    RuntimeWorldXFromSource(
                        smoothed.X),
                    smoothed.Z,
                    smoothed.Y,
                    RuntimeHeadingRadiansFromSource(
                        smoothed.HeadingDegrees *
                        Math.PI /
                        180.0),
                    peer.Pose.Brake >
                        0.05f ||
                    (peer.Pose.Flags &
                     OpenOmsiLanProtocol.FlagBrake) !=
                        0,
                    blinker is 1 or 3,
                    blinker is 2 or 3,
                    traveled,
                    0.0,
                    ScriptRuntime:
                        remoteScriptRuntime));
        }

        if (_multiplayerRemoteStates.Count >
            _multiplayerActivePeerIds.Count)
        {
            foreach (var staleId in
                     _multiplayerRemoteStates.Keys
                         .Where(
                             id =>
                                 !_multiplayerActivePeerIds.Contains(
                                     id))
                         .ToArray())
            {
                _multiplayerRemoteStates.Remove(
                    staleId);
                _multiplayerTravelMeters.Remove(
                    staleId);
                _multiplayerScriptRuntimes.Remove(
                    staleId);
                _multiplayerScriptPaths.Remove(
                    staleId);
                _multiplayerHornStates.Remove(
                    staleId);
            }
        }

        if (session.Role ==
            OpenOmsiLanRole.Host)
        {
            CleanupRelayedWorldPeople();
        }
    }

    private void CleanupRelayedWorldPeople()
    {
        foreach (var viewKey in
                 _relayedWorldPassengerPeerViews
                     .Keys
                     .Where(
                         key =>
                             !_multiplayerActivePeerIds.Contains(
                                 key.ObserverPeerId) ||
                             !_multiplayerActivePeerIds.Contains(
                                 key.SourcePeerId))
                     .ToArray())
        {
            _relayedWorldPassengerPeerViews.Remove(
                viewKey);
        }

        foreach (var key in
                 _relayedWorldPersonIds.Keys
                     .Where(
                         key =>
                             !_multiplayerActivePeerIds.Contains(
                                 key.PeerId))
                     .ToArray())
        {
            if (!_relayedWorldPersonIds.Remove(
                    key,
                    out var mappedId))
            {
                continue;
            }

            _relayedWorldPersonIdsInUse.Remove(
                mappedId);
            _sharedWorldPeople.Remove(
                mappedId);
            _sharedWorldPersonDescriptions.Remove(
                mappedId);
        }
    }

    private OmsiScriptRuntime? ResolveMultiplayerScriptRuntime(
        OpenOmsiLanPeerSnapshot peer,
        string vehiclePath,
        double deltaSeconds)
    {
        if (!_trafficScriptCatalogs.TryGetValue(
                vehiclePath,
                out var catalog))
        {
            return null;
        }

        if (!_multiplayerScriptRuntimes.TryGetValue(
                peer.Id,
                out var runtime) ||
            !_multiplayerScriptPaths.TryGetValue(
                peer.Id,
                out var previousPath) ||
            !previousPath.Equals(
                vehiclePath,
                StringComparison.OrdinalIgnoreCase))
        {
            runtime =
                new OmsiScriptRuntime(
                    catalog);

            if (_trafficVehicleAssets.TryGetValue(
                    vehiclePath,
                    out var asset))
            {
                ApplyHofToScriptRuntime(
                    runtime,
                    ResolveMapHofForBus(
                        asset.Bus,
                        _map.FolderName),
                    writeDiagnostic:
                        false);
            }

            runtime.ExecuteInit();

            _multiplayerScriptRuntimes[
                peer.Id] =
                runtime;
            _multiplayerScriptPaths[
                peer.Id] =
                vehiclePath;
            _multiplayerHornStates.Remove(
                peer.Id);
        }

        SeedMultiplayerScriptRuntime(
            runtime,
            peer.Pose,
            deltaSeconds);

        ApplyMultiplayerHornState(
            peer.Id,
            runtime,
            peer.Pose.Flags);

        runtime.ExecuteFrameAi();

        // Pin the network-driven values again after frame_ai so a bus whose
        // AI script computes defaults cannot erase the remote driver's state.
        SeedMultiplayerScriptRuntime(
            runtime,
            peer.Pose,
            0.0);

        if (_trafficVehicleAssets.TryGetValue(
                vehiclePath,
                out var remoteAsset))
        {
            ApplyVehicleDisplayTextValues(
                remoteAsset,
                runtime,
                peer.Pose.DisplayTexts);

            ApplyVehicleFreeTextureValues(
                remoteAsset,
                runtime,
                peer.Pose.FreeTexturePaths);
        }

        return runtime;
    }

    private void ApplyMultiplayerHornState(
        uint peerId,
        OmsiScriptRuntime runtime,
        uint flags)
    {
        var horn =
            (flags &
             OpenOmsiLanProtocol.FlagHorn) !=
            0;

        if (_multiplayerHornStates.TryGetValue(
                peerId,
                out var previous) &&
            previous ==
                horn)
        {
            return;
        }

        _multiplayerHornStates[
            peerId] =
            horn;

        var trigger =
            horn
                ? "horn"
                : "horn_off";

        if (runtime.HasTrigger(
                trigger))
        {
            runtime.ExecuteTrigger(
                trigger);
        }
    }

    private static List<string> ResolveVehicleDisplayTextValues(
        OmsiVehicleAsset? asset,
        OmsiScriptRuntime? runtime)
    {
        if (asset is null ||
            runtime is null)
        {
            return [];
        }

        return GetVehicleDisplayTextVariableNames(
                asset)
            .Select(
                name =>
                    runtime.HasStringLocalVariable(
                        name)
                        ? runtime.GetStringLocal(
                            name)
                        : string.Empty)
            .ToList();
    }

    private static void ApplyVehicleDisplayTextValues(
        OmsiVehicleAsset asset,
        OmsiScriptRuntime runtime,
        IReadOnlyList<string> values)
    {
        if (values.Count ==
            0)
        {
            return;
        }

        var names =
            GetVehicleDisplayTextVariableNames(
                asset);

        for (var index = 0;
             index <
                 Math.Min(
                     names.Count,
                     values.Count);
             index++)
        {
            runtime.SetStringLocal(
                names[index],
                values[index]);
        }
    }

    private static List<string> GetVehicleDisplayTextVariableNames(
        OmsiVehicleAsset asset)
    {
        var names =
            new List<string>();

        foreach (var textTexture in
                 asset.TextTextures
                     .OrderBy(
                         static texture =>
                             texture.Index))
        {
            var name =
                textTexture.StringVariable
                    .Trim();

            if (name.Length ==
                    0 ||
                names.Any(
                    existing =>
                        existing.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            names.Add(
                name);

            if (names.Count >=
                OpenOmsiLanProtocol.MaximumDisplayTexts)
            {
                break;
            }
        }

        return names;
    }

    private static List<string> ResolveVehicleFreeTextureValues(
        OmsiVehicleAsset? asset,
        OmsiScriptRuntime? runtime)
    {
        if (asset is null ||
            runtime is null)
        {
            return [];
        }

        return GetVehicleFreeTextureVariableNames(
                asset)
            .Select(
                name =>
                    runtime.HasStringLocalVariable(
                        name)
                        ? runtime.GetStringLocal(
                            name)
                        : string.Empty)
            .ToList();
    }

    private static void ApplyVehicleFreeTextureValues(
        OmsiVehicleAsset asset,
        OmsiScriptRuntime runtime,
        IReadOnlyList<string> values)
    {
        if (values.Count ==
            0)
        {
            return;
        }

        var names =
            GetVehicleFreeTextureVariableNames(
                asset);

        for (var index = 0;
             index <
                 Math.Min(
                     names.Count,
                     values.Count);
             index++)
        {
            runtime.SetStringLocal(
                names[index],
                values[index]);
        }
    }

    private static List<string> GetVehicleFreeTextureVariableNames(
        OmsiVehicleAsset asset)
    {
        var names =
            new List<string>();

        foreach (var material in
                 asset.Meshes.SelectMany(
                     static mesh =>
                         mesh.Materials))
        {
            foreach (var freeTexture in
                     material.FreeTextures)
            {
                if (!string.IsNullOrWhiteSpace(
                        freeTexture.VariableName))
                {
                    names.Add(
                        freeTexture.VariableName.Trim());
                }
            }

            foreach (var item in
                     material.MaterialChangeSets?
                         .SelectMany(
                             static set =>
                                 set.Items) ??
                     Array.Empty<
                         OmsiVehicleMaterialChangeItem>())
            {
                foreach (var freeTexture in
                         item.FreeTextures)
                {
                    if (!string.IsNullOrWhiteSpace(
                            freeTexture.VariableName))
                    {
                        names.Add(
                            freeTexture.VariableName.Trim());
                    }
                }
            }
        }

        return names
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                static name =>
                    name,
                StringComparer.OrdinalIgnoreCase)
            .Take(
                OpenOmsiLanProtocol.MaximumFreeTextures)
            .ToList();
    }

    private static void SeedMultiplayerScriptRuntime(
        OmsiScriptRuntime runtime,
        OpenOmsiLanPose pose,
        double deltaSeconds)
    {
        var speed =
            pose.SpeedKph;

        runtime.SetLocal(
            "Velocity",
            speed);
        runtime.SetLocal(
            "Velocity_Ground",
            speed);
        runtime.SetSystem(
            "Timegap",
            Math.Max(
                0.0,
                deltaSeconds));

        var engine =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagEngine) !=
            0;

        var electrics =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagElectrics) !=
            0;

        var engineValue =
            engine
                ? 1.0
                : 0.0;

        runtime.SetLocal(
            "engine_on",
            engineValue);
        runtime.SetLocal(
            "engine_injection_on",
            engineValue);
        runtime.SetLocal(
            "engine_n",
            pose.Rpm);
        runtime.SetLocal(
            "n_engine",
            pose.Rpm);

        runtime.SetLocal(
            "throttle",
            pose.Throttle);
        runtime.SetLocal(
            "gas",
            pose.Throttle);
        runtime.SetLocal(
            "brake",
            pose.Brake);
        runtime.SetLocal(
            "bremse",
            pose.Brake);

        var brakeLamp =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagBrake) !=
                0 ||
            pose.Brake >
                0.1f
                ? 1.0
                : 0.0;

        var reverse =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagReverse) !=
                0
                ? 1.0
                : 0.0;

        var fog =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagFog) !=
                0
                ? 1.0
                : 0.0;

        var kneeling =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagKneeling) !=
                0
                ? 1.0
                : 0.0;

        var wipers =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagWipers) !=
                0
                ? 1.0
                : 0.0;

        var stopBrake =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagStopBrake) !=
                0
                ? 1.0
                : 0.0;

        var horn =
            (pose.Flags &
             OpenOmsiLanProtocol.FlagHorn) !=
                0
                ? 1.0
                : 0.0;

        runtime.SetLocal(
            "lights_brems",
            brakeLamp);
        runtime.SetLocal(
            "lights_rueckfahr",
            reverse);
        runtime.SetLocal(
            "lights_nebelschluss",
            fog);
        runtime.SetLocal(
            "bremse_kneeling",
            kneeling);
        runtime.SetLocal(
            "vdv_kneel",
            kneeling);
        runtime.SetLocal(
            "ecas_kneel",
            kneeling);
        runtime.SetLocal(
            "kneeling",
            kneeling);
        runtime.SetLocal(
            "wiperrunning",
            wipers);
        runtime.SetLocal(
            "wiper_running",
            wipers);
        runtime.SetLocal(
            "bremse_halte",
            stopBrake);
        runtime.SetLocal(
            "bremse_halte_sw",
            stopBrake);
        runtime.SetLocal(
            "bus_stop_brake",
            stopBrake);
        runtime.SetLocal(
            "cockpit_hupe",
            horn);
        runtime.SetLocal(
            "cockpit_hupe_swheel",
            horn);
        runtime.SetLocal(
            "horn",
            horn);

        runtime.SetLocal(
            "ai_engine",
            engine
                ? 1.0
                : electrics
                    ? 0.0
                    : -1.0);

        var aiLight =
            pose.HeadLights switch
            {
                1 => 0.5,
                2 => 1.0,
                >= 3 => 2.0,
                _ => 0.0
            };

        runtime.SetLocal(
            "ai_light",
            aiLight);
        runtime.SetLocal(
            "ai_interiorlight",
            pose.InteriorLights >
                0
                ? 1.0
                : 0.0);

        var left =
            pose.Blinker is
                1 or 3
                ? 1.0
                : 0.0;

        var right =
            pose.Blinker is
                2 or 3
                ? 1.0
                : 0.0;

        runtime.SetLocal(
            "ai_blinker_l",
            left);
        runtime.SetLocal(
            "ai_blinker_r",
            right);
        runtime.SetLocal(
            "lights_blinker_l",
            left);
        runtime.SetLocal(
            "lights_blinker_r",
            right);

        for (var doorIndex = 0;
             doorIndex <
                 Math.Min(
                     pose.Doors.Count,
                     OpenOmsiLanProtocol.MaximumDoors);
             doorIndex++)
        {
            var door =
                Math.Clamp(
                    pose.Doors[
                        doorIndex],
                    0.0f,
                    1.0f);

            runtime.SetLocal(
                $"door_{doorIndex}",
                door);
            runtime.SetLocal(
                $"door{doorIndex}",
                door);
            runtime.SetLocal(
                $"door_{doorIndex}_pos",
                door);
            runtime.SetLocal(
                $"door_pos_{doorIndex}",
                door);
        }
    }

    private string? ResolveMultiplayerVehiclePath(
        string networkPath)
    {
        var normalized =
            OpenOmsiLanProtocol.NormalizeVehiclePath(
                networkPath);

        if (normalized is null)
        {
            return null;
        }

        var candidate =
            Path.GetFullPath(
                Path.Combine(
                    _contentRoot.RootPath,
                    normalized.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        var vehiclesRoot =
            Path.GetFullPath(
                Path.Combine(
                    _contentRoot.RootPath,
                    "Vehicles")) +
            Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(
                vehiclesRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(
                candidate))
        {
            return null;
        }

        return candidate;
    }

    private void QueueSharedWorldParkingRefresh()
    {
        Interlocked.Increment(
            ref _sharedWorldParkingRevision);

        _ =
            RefreshSharedWorldParkingAsync();
    }

    private IReadOnlySet<uint>? SnapshotSharedWorldDepartedParkingIds()
    {
        if (_multiplayerSession?.Role !=
                OpenOmsiLanRole.Client ||
            !_sharedWorldLastFrameAt.HasValue ||
            DateTimeOffset.UtcNow -
                _sharedWorldLastFrameAt.Value >
                TimeSpan.FromSeconds(
                    3.5))
        {
            return null;
        }

        lock (_sharedWorldParkingGate)
        {
            return _sharedWorldDepartedParkingObjectIds.Count ==
                    0
                ? null
                : _sharedWorldDepartedParkingObjectIds
                    .ToHashSet();
        }
    }

    private async Task RefreshSharedWorldParkingAsync()
    {
        await _multiplayerAssetGate.WaitAsync();

        try
        {
            if (_closing)
            {
                return;
            }

            var parkingRevision =
                Volatile.Read(
                    ref _sharedWorldParkingRevision);

            if (Volatile.Read(
                    ref _sharedWorldParkingAppliedRevision) ==
                parkingRevision)
            {
                return;
            }

            if (_currentWorld is
                    { } currentWorld &&
                _runtimeWindow is
                    { IsDisposed: false } window)
            {
                var departedParkingObjectIds =
                    SnapshotSharedWorldDepartedParkingIds();

                var runtimeInfo =
                    await Task.Run(
                        () =>
                            BuildRuntimeInfo(
                                currentWorld,
                                _vehicleAsset,
                                _entryPoint,
                                _contentRoot.RootPath,
                                _trafficVehicleAssets,
                                departedParkingObjectIds));

                await window.ApplyStreamedWorldAsync(
                    runtimeInfo);
            }

            Volatile.Write(
                ref _sharedWorldParkingAppliedRevision,
                parkingRevision);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[multiplayer-world] parked-car refresh failed: {exception.Message}");
        }
        finally
        {
            _multiplayerAssetGate.Release();

            if (!_closing &&
                Volatile.Read(
                    ref _sharedWorldParkingAppliedRevision) !=
                Volatile.Read(
                    ref _sharedWorldParkingRevision))
            {
                _ =
                    RefreshSharedWorldParkingAsync();
            }
        }
    }

    private void QueueMultiplayerVehicleAsset(
        string vehiclePath)
    {
        if (_trafficVehicleAssets.ContainsKey(
                vehiclePath) ||
            !_pendingMultiplayerVehiclePaths.Add(
                vehiclePath))
        {
            return;
        }

        _ =
            ProcessMultiplayerVehicleAssetsAsync();
    }

    private async Task ProcessMultiplayerVehicleAssetsAsync()
    {
        if (!await _multiplayerAssetGate.WaitAsync(
                0))
        {
            return;
        }

        try
        {
            while (!_closing &&
                   _pendingMultiplayerVehiclePaths.Count >
                       0)
            {
                var requested =
                    _pendingMultiplayerVehiclePaths
                        .ToArray();

                _pendingMultiplayerVehiclePaths.Clear();

                var loaded =
                    await Task.Run(
                        () =>
                        {
                            var result =
                                new List<(
                                    string Path,
                                    OmsiVehicleAsset Asset,
                                    OmsiScriptCatalog? Catalog)>();

                            foreach (var path in
                                     requested)
                            {
                                try
                                {
                                    var bus =
                                        OmsiBusReader.ReadFile(
                                            _contentRoot.RootPath,
                                            path);

                                    var asset =
                                        OmsiArticulatedVehicleAssetLoader.Load(
                                            _contentRoot,
                                            bus);

                                    if (asset.RenderableMeshCount >
                                        0)
                                    {
                                        OmsiScriptCatalog? catalog =
                                            null;

                                        if (asset.Bus.ScriptManifest.RegisteredFileCount >
                                            0)
                                        {
                                            try
                                            {
                                                catalog =
                                                    OmsiScriptCatalogLoader.Load(
                                                        _contentRoot,
                                                        asset.Bus.ScriptManifest);
                                            }
                                            catch (Exception exception)
                                            {
                                                Console.Error.WriteLine(
                                                    $"[multiplayer] remote script catalog unavailable for {Path.GetFileName(path)}: {exception.Message}");
                                            }
                                        }

                                        result.Add(
                                            (
                                                path,
                                                asset,
                                                catalog));
                                    }
                                }
                                catch (Exception exception)
                                {
                                    Console.Error.WriteLine(
                                        $"[multiplayer] remote vehicle load failed for {path}: {exception.Message}");
                                }
                            }

                            return result;
                        });

                foreach (var item in
                         loaded)
                {
                    _trafficVehicleAssets[
                        item.Path] =
                        item.Asset;

                    if (item.Catalog is not
                        null)
                    {
                        _trafficScriptCatalogs[
                            item.Path] =
                            item.Catalog;
                    }

                    Console.WriteLine(
                        $"[multiplayer] remote vehicle cached: {Path.GetFileName(item.Path)}");
                }

                if (loaded.Count >
                        0 &&
                    _currentWorld is
                        { } currentWorld &&
                    _runtimeWindow is
                        { IsDisposed: false } window)
                {
                    var runtimeInfo =
                        await Task.Run(
                            () =>
                                BuildRuntimeInfo(
                                    currentWorld,
                                    _vehicleAsset,
                                    _entryPoint,
                                    _contentRoot.RootPath,
                                    _trafficVehicleAssets,
                                    SnapshotSharedWorldDepartedParkingIds()));

                    await window.ApplyStreamedWorldAsync(
                        runtimeInfo);
                }
            }
        }
        finally
        {
            _multiplayerAssetGate.Release();
        }
    }

    private void UpdateTrafficScriptRuntimes(
        IReadOnlyList<WorldTrafficAgentState> agents,
        double deltaSeconds)
    {
        _activeTrafficScriptAgentIds.Clear();

        foreach (var agent in
                 agents)
        {
            _activeTrafficScriptAgentIds.Add(
                agent.AgentIndex);
        }

        _staleTrafficScriptAgentIds.Clear();

        foreach (var agentId in
                 _trafficScriptRuntimes.Keys)
        {
            if (!_activeTrafficScriptAgentIds.Contains(
                    agentId))
            {
                _staleTrafficScriptAgentIds.Add(
                    agentId);
            }
        }

        foreach (var staleAgentId in
                 _staleTrafficScriptAgentIds)
        {
            _trafficScriptRuntimes.Remove(
                staleAgentId);
        }

        var validDeltaSeconds =
            double.IsFinite(
                deltaSeconds) &&
            deltaSeconds >
                0.0
                ? deltaSeconds
                : 0.0;

        var playerObstacle =
            _runtimeWindow?
                .PlayerTrafficObstacle;

        var playerWorldX =
            playerObstacle is null
                ? double.NaN
                : -playerObstacle.X;

        var playerWorldZ =
            playerObstacle is null
                ? double.NaN
                : playerObstacle.Z;

        _trafficScriptWork.Clear();

        if (_trafficScriptWork.Capacity <
            agents.Count)
        {
            _trafficScriptWork.Capacity =
                agents.Count;
        }

        // Runtime creation, HOF assignment and dictionary mutation stay on
        // this thread. Once every agent owns its runtime, frame_ai execution
        // is independent and can safely fan out across worker threads.
        foreach (var agent in
                 agents)
        {
            if (!_trafficScriptCatalogs.TryGetValue(
                    agent.VehiclePath,
                    out var catalog))
            {
                continue;
            }

            if (!_trafficScriptRuntimes.TryGetValue(
                    agent.AgentIndex,
                    out var state) ||
                !state.VehiclePath.Equals(
                    agent.VehiclePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                var runtime =
                    new OmsiScriptRuntime(
                        catalog);

                double? wheelDiameterMeters =
                    null;
                string? trafficHofPath =
                    null;

                if (_trafficVehicleAssets.TryGetValue(
                        agent.VehiclePath,
                        out var trafficAsset))
                {
                    wheelDiameterMeters =
                        trafficAsset
                            .Bus
                            .Physics
                            .AverageWheelDiameterMeters;

                    var hofCacheKey =
                        BuildTrafficHofCacheKey(
                            agent.VehiclePath,
                            agent.DepotHofName);

                    if (!_trafficHofByVehiclePath.TryGetValue(
                            hofCacheKey,
                            out trafficHofPath))
                    {
                        trafficHofPath =
                            ResolveMapHofForBus(
                                trafficAsset.Bus,
                                _map.FolderName,
                                agent.DepotHofName);

                        _trafficHofByVehiclePath[
                            hofCacheKey] =
                            trafficHofPath;
                    }

                    ApplyHofToScriptRuntime(
                        runtime,
                        trafficHofPath,
                        writeDiagnostic:
                            false);
                }

                SeedTrafficScriptHostVariables(
                    runtime,
                    agent,
                    0.0,
                    wheelDiameterMeters);

                runtime.ExecuteInit();

                ApplyScheduledAiTarget(
                    runtime,
                    agent,
                    trafficHofPath);

                state =
                    new TrafficScriptRuntimeState(
                        agent.VehiclePath,
                        runtime,
                        wheelDiameterMeters);

                _trafficScriptRuntimes[
                    agent.AgentIndex] =
                    state;
            }

            state.AccumulatedFrameAiSeconds +=
                validDeltaSeconds;

            var targetIntervalSeconds =
                0.0;

            if (double.IsFinite(
                    playerWorldX) &&
                double.IsFinite(
                    playerWorldZ))
            {
                var deltaX =
                    agent.Position.X -
                    playerWorldX;

                var deltaZ =
                    agent.Position.Z -
                    playerWorldZ;

                var distanceSquared =
                    deltaX *
                        deltaX +
                    deltaZ *
                        deltaZ;

                if (distanceSquared >=
                    650.0 *
                    650.0)
                {
                    targetIntervalSeconds =
                        0.100;
                }
                else if (distanceSquared >=
                         300.0 *
                         300.0)
                {
                    targetIntervalSeconds =
                        0.050;
                }
            }

            if (validDeltaSeconds <=
                0.0)
            {
                SeedTrafficScriptHostVariables(
                    state.Runtime,
                    agent,
                    0.0,
                    state.WheelDiameterMeters);

                continue;
            }

            if (targetIntervalSeconds >
                    0.0 &&
                state.AccumulatedFrameAiSeconds +
                    0.000001 <
                    targetIntervalSeconds)
            {
                continue;
            }

            var executionDeltaSeconds =
                state.AccumulatedFrameAiSeconds;

            state.AccumulatedFrameAiSeconds =
                0.0;

            _trafficScriptWork.Add(
                (
                    agent,
                    state,
                    executionDeltaSeconds));
        }

        if (_trafficScriptWork.Count ==
            0)
        {
            return;
        }

        if (_trafficScriptWork.Count <
            4)
        {
            foreach (var item in
                     _trafficScriptWork)
            {
                SeedTrafficScriptHostVariables(
                    item.State.Runtime,
                    item.Agent,
                    item.DeltaSeconds,
                    item.State.WheelDiameterMeters);

                item.State.Runtime.ExecuteFrameAi();
            }

            return;
        }

        Parallel.ForEach(
            _trafficScriptWork,
            item =>
            {
                SeedTrafficScriptHostVariables(
                    item.State.Runtime,
                    item.Agent,
                    item.DeltaSeconds,
                    item.State.WheelDiameterMeters);

                item.State.Runtime.ExecuteFrameAi();
            });
    }

    private static void SeedTrafficScriptHostVariables(
        OmsiScriptRuntime runtime,
        WorldTrafficAgentState agent,
        double deltaSeconds,
        double? wheelDiameterMeters)
    {
        var speedKilometersPerHour =
            agent.SpeedMetersPerSecond *
            3.6;

        runtime.SetLocal(
            "Velocity",
            speedKilometersPerHour);

        runtime.SetLocal(
            "Velocity_Ground",
            speedKilometersPerHour);

        runtime.SetSystem(
            "Timegap",
            deltaSeconds);

        if (!wheelDiameterMeters.HasValue ||
            !double.IsFinite(
                wheelDiameterMeters.Value) ||
            wheelDiameterMeters.Value <=
                0.0)
        {
            return;
        }

        var circumference =
            Math.PI *
            wheelDiameterMeters.Value;

        if (circumference <=
            0.000001)
        {
            return;
        }

        var wheelRevolutionsPerMinute =
            agent.SpeedMetersPerSecond /
            circumference *
            60.0;

        runtime.SetLocal(
            "n_Wheel",
            wheelRevolutionsPerMinute);

        for (var axle = 0;
             axle < 8;
             axle++)
        {
            runtime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_L",
                wheelRevolutionsPerMinute);

            runtime.SetLocal(
                $"Wheel_RotationSpeed_{axle}_R",
                wheelRevolutionsPerMinute);
        }
    }

    private static string BuildTrafficHofCacheKey(
        string vehiclePath,
        string? depotHofName) =>
        vehiclePath +
        "|" +
        (depotHofName ??
         string.Empty);

    private static string? ResolveMapHofForBus(
        OmsiBusInfo bus,
        string mapFolderName,
        string? preferredHofName = null)
    {
        try
        {
            if (!Directory.Exists(
                    bus.DirectoryPath))
            {
                return null;
            }

            var hofFiles =
                Directory
                    .EnumerateFiles(
                        bus.DirectoryPath,
                        "*.hof",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        static path =>
                            Path.GetFileName(
                                path),
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            if (hofFiles.Length ==
                0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(
                    preferredHofName))
            {
                var preferred =
                    preferredHofName.Trim();

                var byFileName =
                    hofFiles.FirstOrDefault(
                        path =>
                            Path.GetFileNameWithoutExtension(
                                    path)
                                .Equals(
                                    preferred,
                                    StringComparison.OrdinalIgnoreCase));

                if (byFileName is not
                    null)
                {
                    return byFileName;
                }

                foreach (var path in
                         hofFiles)
                {
                    try
                    {
                        var catalog =
                            OmsiHofCatalogReader.ReadFile(
                                path);

                        if (catalog.Name.Equals(
                                preferred,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return path;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            var normalizedMap =
                mapFolderName
                    .Replace(
                        "_",
                        " ",
                        StringComparison.Ordinal)
                    .Trim();

            return hofFiles.FirstOrDefault(
                       path =>
                           Path.GetFileNameWithoutExtension(
                                   path)
                               .Contains(
                                   mapFolderName,
                                   StringComparison.OrdinalIgnoreCase))
                   ?? hofFiles.FirstOrDefault(
                       path =>
                           Path.GetFileNameWithoutExtension(
                                   path)
                               .Contains(
                                   normalizedMap,
                                   StringComparison.OrdinalIgnoreCase))
                   ?? hofFiles[0];
        }
        catch
        {
            return null;
        }
    }

    private void ApplyScheduledAiTarget(
        OmsiScriptRuntime runtime,
        WorldTrafficAgentState agent,
        string? hofPath)
    {
        if (string.IsNullOrWhiteSpace(
                agent.ScheduledLine) ||
            string.IsNullOrWhiteSpace(
                agent.ScheduledDestination))
        {
            return;
        }

        var line =
            agent.ScheduledLine.Trim();
        var destination =
            agent.ScheduledDestination.Trim();

        runtime.SetStringLocal(
            "SetLineTo",
            line);

        OmsiHofCatalog? hof =
            null;

        if (!string.IsNullOrWhiteSpace(
                hofPath))
        {
            if (!_hofCatalogsByPath.TryGetValue(
                    hofPath,
                    out hof))
            {
                try
                {
                    hof =
                        OmsiHofCatalogReader.ReadFile(
                            hofPath);
                }
                catch
                {
                    hof =
                        null;
                }

                _hofCatalogsByPath[
                    hofPath] =
                    hof;
            }
        }

        var terminusIndex =
            hof?.FindTerminusIndex(
                destination);

        if (!terminusIndex.HasValue)
        {
            Console.WriteLine(
                $"[line-ai] destination not found in HOF: line={line}; destination={destination}; hof={hof?.Name ?? "<none>"}");
            return;
        }

        var terminus =
            hof!.Termini[
                terminusIndex.Value];

        runtime.SetLocal(
            "AI_target_index",
            terminusIndex.Value);

        if (runtime.HasTrigger(
                "ai_scheduled_settarget"))
        {
            runtime.ExecuteTrigger(
                "ai_scheduled_settarget");

            Console.WriteLine(
                $"[line-ai] script target line={line}; destination={destination}; index={terminusIndex.Value}; hof={hof.Name}");
            return;
        }

        // Compatibility fallback for buses without the OMSI AI target trigger.
        runtime.SetLocal(
            "IBIS_TerminusIndex",
            terminusIndex.Value);
        runtime.SetLocal(
            "IBIS_TerminusCode",
            terminus.Code);
        runtime.SetStringLocal(
            "IBIS_terminus_name",
            terminus.Strings.FirstOrDefault(
                static value =>
                    !string.IsNullOrWhiteSpace(
                        value)) ??
            terminus.Identifier);
        runtime.SetStringLocal(
            "IBIS_terminus_texture",
            terminus.Identifier);

        var lineDigits =
            new string(
                line
                    .TakeWhile(
                        static character =>
                            char.IsDigit(
                                character))
                    .ToArray());

        if (double.TryParse(
                lineDigits,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var lineNumber))
        {
            runtime.SetLocal(
                "IBIS_LinieKurs",
                lineNumber);
        }

        Console.WriteLine(
            $"[line-ai] fallback target line={line}; destination={destination}; index={terminusIndex.Value}; hof={hof.Name}");
    }

    private static void ApplyHofToScriptRuntime(
        OmsiScriptRuntime runtime,
        string? hofPath,
        bool writeDiagnostic = true)
    {
        if (string.IsNullOrWhiteSpace(
                hofPath))
        {
            return;
        }

        var hofName =
            Path.GetFileNameWithoutExtension(
                hofPath);

        string[] pathVariables =
        [
            "HOF",
            "Hof",
            "hof",
            "HOF_File",
            "HOF_Filename",
            "HofDatei",
            "Hofdatei",
            "IBIS_HOF"
        ];

        foreach (var variable in
                 pathVariables)
        {
            runtime.SetStringLocal(
                variable,
                hofPath);
        }

        string[] nameVariables =
        [
            "HOF_Name",
            "HOFName",
            "HofName",
            "Hof_Name",
            "IBIS_HOF_Name"
        ];

        foreach (var variable in
                 nameVariables)
        {
            runtime.SetStringLocal(
                variable,
                hofName);
        }

        if (writeDiagnostic)
        {
            Console.WriteLine(
                $"[hof] assigned={hofName}; file={hofPath}");
        }
    }

    private OmsiScriptRuntime? ResolveTrafficScriptRuntime(
        WorldTrafficAgentState agent)
    {
        if (!_trafficScriptRuntimes.TryGetValue(
                agent.AgentIndex,
                out var state) ||
            !state.VehiclePath.Equals(
                agent.VehiclePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return state.Runtime;
    }

    private static void WriteTrafficDiagnostics(
        WorldDefinition world,
        WorldTrafficSimulation simulation)
    {
        var agents =
            simulation.Snapshot();

        Console.WriteLine(
            $"[traffic] agents={agents.Count}; " +
            $"paths={world.TrafficPaths.Segments.Count}; " +
            $"road={world.TrafficPaths.RoadVehicleSegmentCount}; " +
            $"connected={world.TrafficPaths.ConnectedEndpointCount}; " +
            $"terminal={world.TrafficPaths.TerminalEndpointCount}; " +
            $"boundary={world.TrafficPaths.BoundaryEndpointCount}; " +
            $"unmatched={world.TrafficPaths.UnmatchedEndpointCount}");
    }

    private void WriteLineAiDiagnostics(
        WorldLineAiSchedule schedule)
    {
        var statusCounts =
            schedule.Trips
                .GroupBy(
                    static trip =>
                        trip.Status)
                .OrderBy(
                    static group =>
                        group.Key)
                .Select(
                    static group =>
                        $"{group.Key}={group.Count()}");

        Console.WriteLine(
            $"[line-ai] scheduled={schedule.Trips.Count}; ready={schedule.ReadyCount}; unresolved={schedule.UnresolvedCount}; active={_lineAiSimulation?.ActiveCount ?? 0}; serviceMin={_lineAiServiceMinutes:0.00}; {string.Join("; ", statusCounts)}");
    }

    private static void WriteRailTrafficDiagnostics(
        WorldDefinition world,
        WorldRailTrafficSimulation simulation)
    {
        var agents =
            simulation.Snapshot();

        Console.WriteLine(
            $"[rail-ai] agents={agents.Count}; " +
            $"railPaths={world.TrafficPaths.RailSegmentCount}; " +
            $"consists={agents.Select(static agent => agent.TrainConsistPath).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");
    }

    private sealed record RailRuntimeCar(
        string VehiclePath,
        bool Reverse,
        double TrailingDistanceMeters);

    private sealed record RailRuntimeConsist(
        string TrainConsistPath,
        IReadOnlyList<RailRuntimeCar> Cars,
        double? TailClearanceMeters);

    private sealed class TrafficScriptRuntimeState(
        string vehiclePath,
        OmsiScriptRuntime runtime,
        double? wheelDiameterMeters)
    {
        public string VehiclePath { get; } =
            vehiclePath;

        public OmsiScriptRuntime Runtime { get; } =
            runtime;

        public double? WheelDiameterMeters { get; } =
            wheelDiameterMeters;

        public double AccumulatedFrameAiSeconds
        {
            get;
            set;
        }
    }

    private const double RuntimeTileSizeMeters =
        300.0;

    private static int RuntimeTileXFromSourceTileX(
        int sourceTileX) =>
        -sourceTileX -
        1;

    private static int SourceTileXFromRuntimeTileX(
        int runtimeTileX) =>
        -runtimeTileX -
        1;

    private static double RuntimeLocalXFromSourceLocalX(
        double sourceLocalX) =>
        RuntimeTileSizeMeters -
        sourceLocalX;

    private static double RuntimeWorldXFromSource(
        double sourceWorldX) =>
        -sourceWorldX;

    private static double RuntimeHeadingDegreesFromSource(
        double sourceHeadingDegrees) =>
        -sourceHeadingDegrees;

    private static double RuntimeHeadingRadiansFromSource(
        double sourceHeadingRadians) =>
        -sourceHeadingRadians;

    private static IReadOnlyList<float> MirrorTerrainHeightsX(
        WorldTerrainData terrain)
    {
        var sampleCount =
            terrain.CellCount +
            1;

        if (sampleCount <=
                0 ||
            terrain.Heights.Count !=
                sampleCount *
                sampleCount)
        {
            return terrain.Heights;
        }

        var mirrored =
            new float[
                terrain.Heights.Count];

        for (var row = 0;
             row <
                 sampleCount;
             row++)
        {
            for (var column = 0;
                 column <
                     sampleCount;
                 column++)
            {
                mirrored[
                    row *
                        sampleCount +
                    column] =
                    terrain.Heights[
                        row *
                            sampleCount +
                        (sampleCount -
                         1 -
                         column)];
            }
        }

        return mirrored;
    }

    private static RuntimeWindowInfo BuildRuntimeInfo(
        WorldDefinition world,
        OmsiVehicleAsset? vehicle,
        OmsiMapEntryPoint entryPoint,
        string contentRoot,
        IReadOnlyDictionary<string, OmsiVehicleAsset> trafficVehicleAssets,
        IReadOnlySet<uint>? departedParkingObjectIds = null)
    {
        var runtimeTiles =
            world.Tiles
                .Select(
                    static tile =>
                        new RuntimeTileInfo(
                            RuntimeTileXFromSourceTileX(
                                tile.Coordinate.X),
                            tile.Coordinate.Y,
                            tile.Objects.Count,
                            tile.Splines.Count,
                            tile.Terrain is null
                                ? null
                                : new RuntimeTerrainInfo(
                                    tile.Terrain.CellCount,
                                    MirrorTerrainHeightsX(
                                        tile.Terrain),
                                    tile.Terrain.MinimumHeight,
                                    tile.Terrain.MaximumHeight),
                            tile.Resources.LightmapPath,
                            tile.Resources.TerrainMasks
                                .Select(
                                    static mask =>
                                        new RuntimeTerrainMaskInfo(
                                            mask.LayerIndex,
                                            mask.Path))
                                .ToArray()))
                .ToArray();

        var runtimeSplines =
            world.Splines
                .Select(
                    spline =>
                    {
                        world.SplineAssets.TryGetValue(
                            spline.AssetPath,
                            out var asset);

                        var surfaces =
                            asset?.Surfaces
                                .Select(
                                    static surface =>
                                        new RuntimeSplineSurfaceInfo(
                                            new RuntimeSplineProfilePointInfo(
                                                -surface.From.X,
                                                surface.From.Z,
                                                surface.From.TextureX,
                                                surface.From.TextureScale),
                                            new RuntimeSplineProfilePointInfo(
                                                -surface.To.X,
                                                surface.To.Z,
                                                surface.To.TextureX,
                                                surface.To.TextureScale),
                                            surface.TexturePath,
                                            surface.AlphaMode))
                                .ToArray()
                            ?? Array.Empty<
                                RuntimeSplineSurfaceInfo>();

                        var paths =
                            asset?.Paths
                                .Select(
                                    static path =>
                                        new RuntimeSplinePathInfo(
                                            path.Type,
                                            -path.X,
                                            path.Z,
                                            path.Width,
                                            path.Direction))
                                .ToArray()
                            ?? Array.Empty<
                                RuntimeSplinePathInfo>();

                        return new RuntimeSplineInfo(
                            spline.Id,
                            spline.PreviousId,
                            spline.NextId,
                            RuntimeTileXFromSourceTileX(
                                spline.Tile.X),
                            spline.Tile.Y,
                            RuntimeLocalXFromSourceLocalX(
                                spline.Position.X),
                            spline.Position.Y,
                            spline.Position.Z,
                            RuntimeHeadingDegreesFromSource(
                                spline.HeadingDegrees),
                            spline.LengthMeters,
                            -spline.RadiusMeters,
                            spline.GradientStartPercent,
                            spline.GradientEndPercent,
                            spline.UsesHeightProfile,
                            spline.DeltaHeightMeters,
                            spline.CantStartPercent,
                            spline.CantEndPercent,
                            spline.SkewStart,
                            spline.SkewEnd,
                            spline.Mirror,
                            surfaces,
                            paths,
                            spline.TerrainAlignMode);
                    })
                .ToArray();

        var runtimeObjects =
            world.Objects
                .Select(
                    static item =>
                        new RuntimeObjectInfo(
                            RuntimeTileXFromSourceTileX(
                                item.Tile.X),
                            item.Tile.Y,
                            item.AssetPath,
                            RuntimeLocalXFromSourceLocalX(
                                item.Position.X),
                            item.Position.Y,
                            item.Position.Z,
                            RuntimeHeadingDegreesFromSource(
                                item.HeadingDegrees),
                            item.PitchDegrees,
                            -item.BankDegrees,
                            item.ExtraValues,
                            item.Id))
                .Concat(
                    (world.ParkedCars ??
                     Array.Empty<WorldParkedCarPlacement>())
                        .Where(
                            item =>
                                !WorldParkedCarResolver.IsDeparted(
                                    item.ParkingObjectId,
                                    departedParkingObjectIds))
                        .Select(
                            static item =>
                                new RuntimeObjectInfo(
                                    RuntimeTileXFromSourceTileX(
                                        item.Tile.X),
                                    item.Tile.Y,
                                    item.AssetPath,
                                    RuntimeLocalXFromSourceLocalX(
                                        item.Position.X),
                                    item.Position.Y,
                                    item.Position.Z,
                                    RuntimeHeadingDegreesFromSource(
                                        item.HeadingDegrees),
                                    item.PitchDegrees,
                                    -item.BankDegrees,
                                    Array.Empty<string>(),
                                    item.ParkingObjectId)))
                .ToArray();

        var runtimeSceneryAssets =
            world.SceneryAssets
                .ToDictionary(
                    static pair => pair.Key,
                    static pair =>
                        new RuntimeSceneryAssetInfo(
                            pair.Value.UsesAbsoluteHeight,
                            pair.Value.OnlyEditor,
                            pair.Value.RenderType,
                            pair.Value.Meshes
                                .Select(
                                    static mesh =>
                                        new RuntimeObjectMeshInfo(
                                            mesh.DeclaredPath,
                                            mesh.ResolvedPath,
                                            mesh.ErrorCode,
                                            new RuntimeObjectMeshTransformInfo(
                                                mesh.Transform.PositionX,
                                                mesh.Transform.PositionY,
                                                mesh.Transform.PositionZ,
                                                mesh.Transform.RotationX,
                                                mesh.Transform.RotationY,
                                                mesh.Transform.RotationZ,
                                                mesh.Transform.ScaleX,
                                                mesh.Transform.ScaleY,
                                                mesh.Transform.ScaleZ),
                                            mesh.Positions,
                                            mesh.Normals,
                                            mesh.Uvs,
                                            mesh.Indices,
                                            mesh.TriangleMaterialIndices,
                                            mesh.Materials
                                                .Select(
                                                    static material =>
                                                        new RuntimeO3dMaterialInfo(
                                                            material.DiffuseR,
                                                            material.DiffuseG,
                                                            material.DiffuseB,
                                                            material.DiffuseA,
                                                            material.TexturePath,
                                                            material.AlphaMode,
                                                            material.TransMapTexturePath,
                                                            material.NoZWrite,
                                                            material.NoZCheck,
                                                            RequiresExternalTransMap:
                                                                material.RequiresExternalTransMap))
                                                .ToArray(),
                                            0,
                                            null,
                                            mesh.VisibilityConditions?
                                                .Select(
                                                    static condition =>
                                                        new RuntimeVehicleVisibilityConditionInfo(
                                                            condition.VariableName,
                                                            condition.Value))
                                                .ToArray(),
                                            mesh.Animations?
                                                .Select(
                                                    static animation =>
                                                        new RuntimeVehicleAnimationInfo(
                                                            animation.Kind ==
                                                                OmsiVehicleAnimationKind.Translation
                                                                    ? RuntimeVehicleAnimationKind.Translation
                                                                    : RuntimeVehicleAnimationKind.Rotation,
                                                            animation.VariableName,
                                                            animation.Delta,
                                                            animation.OriginFromMesh,
                                                            animation.OriginX,
                                                            animation.OriginY,
                                                            animation.OriginZ,
                                                            animation.OriginRotationX,
                                                            animation.OriginRotationY,
                                                            animation.OriginRotationZ,
                                                            animation.Offset,
                                                            animation.MaxSpeed,
                                                            animation.Delay))
                                                .ToArray(),
                                            mesh.SourceTransform,
                                            mesh.LightEffects?
                                                .Select(
                                                    static light =>
                                                        new RuntimeVehicleLightEffectInfo(
                                                            light.PositionX,
                                                            light.PositionY,
                                                            light.PositionZ,
                                                            light.DirectionX,
                                                            light.DirectionY,
                                                            light.DirectionZ,
                                                            light.UpX,
                                                            light.UpY,
                                                            light.UpZ,
                                                            light.Omni,
                                                            light.Rotating,
                                                            light.Red,
                                                            light.Green,
                                                            light.Blue,
                                                            light.SizeMeters,
                                                            light.InnerConeAngleDegrees,
                                                            light.OuterConeAngleDegrees,
                                                            light.BrightnessVariable,
                                                            light.BrightnessFactor,
                                                            light.CameraOffsetMeters,
                                                            light.Parameters,
                                                            light.ConeEffect,
                                                            light.TimeConstantSeconds,
                                                            light.BitmapSource,
                                                            light.Enhanced))
                                                .ToArray(),
                                            mesh.MeshIdentifier,
                                            mesh.AnimationParent,
                                            0,
                                            mesh.ModelOrdinal))
                                .ToArray(),
                            pair.Value.Tree is null
                                ? null
                                : new RuntimeTreeInfo(
                                    pair.Value.Tree.TextureName,
                                    pair.Value.Tree.TexturePath,
                                    pair.Value.Tree.MinimumHeight,
                                    pair.Value.Tree.MaximumHeight,
                                    pair.Value.Tree.MinimumAspect,
                                    pair.Value.Tree.MaximumAspect),
                            pair.Value.NoCollision,
                            pair.Value.Fixed,
                            pair.Value.Surface,
                            pair.Value.CollisionMeshSource,
                            pair.Value.BoundingBox is null
                                ? null
                                : new RuntimeSceneryBoundingBoxInfo(
                                    pair.Value.BoundingBox.LengthX,
                                    pair.Value.BoundingBox.WidthY,
                                    pair.Value.BoundingBox.HeightZ,
                                    pair.Value.BoundingBox.CenterX,
                                    pair.Value.BoundingBox.CenterY,
                                    pair.Value.BoundingBox.CenterZ),
                            pair.Value.CollisionBounds is null
                                ? null
                                : new RuntimeSceneryCollisionBoundsInfo(
                                    pair.Value.CollisionBounds.MinimumX,
                                    pair.Value.CollisionBounds.MaximumX,
                                    pair.Value.CollisionBounds.MinimumY,
                                    pair.Value.CollisionBounds.MaximumY,
                                    pair.Value.CollisionBounds.MinimumZ,
                                    pair.Value.CollisionBounds.MaximumZ),
                            pair.Value.CollisionGeometry is null
                                ? null
                                : new RuntimeSceneryCollisionGeometryInfo(
                                    pair.Value.CollisionGeometry.Positions,
                                    pair.Value.CollisionGeometry.Indices)),
                    StringComparer.OrdinalIgnoreCase);

        var runtimeGroundTextures =
            world.GroundTextures
                .Select(
                    static layer =>
                        new RuntimeGroundTextureInfo(
                            layer.LayerIndex,
                            layer.MainTexturePath,
                            layer.DetailTexturePath,
                            layer.MainTextureRepeating,
                            layer.DetailTextureRepeating))
                .ToArray();

        var runtimeVehicle =
            vehicle is null
                ? null
                : RuntimeVehicleInfoFactory.FromAsset(
                    vehicle);

        var runtimeSpawn =
            new RuntimeSpawnInfo(
                entryPoint.Name,
                RuntimeWorldXFromSource(
                    entryPoint.WorldX),
                entryPoint.WorldY,
                entryPoint.WorldZ,
                RuntimeHeadingDegreesFromSource(
                    entryPoint.HeadingDegrees));

        var runtimeTrafficPaths =
            new RuntimeTrafficPathNetworkInfo(
                world.TrafficPaths.Segments
                    .Select(
                        static segment =>
                            new RuntimeTrafficPathSegmentInfo(
                                segment.Index,
                                segment.SplineId,
                                segment.LocalPathIndex,
                                segment.Type,
                                segment.Direction,
                                segment.WidthMeters,
                                segment.Points
                                    .Select(
                                        static point =>
                                            new RuntimeTrafficPathPointInfo(
                                                RuntimeWorldXFromSource(
                                                    point.X),
                                                point.Y,
                                                point.Z))
                                    .ToArray(),
                                segment.ForwardConnections,
                                segment.ReverseConnections,
                                segment.SpeedLimitKilometersPerHour,
                                segment.TrafficPriority,
                                segment.TrafficSignal is null
                                    ? null
                                    : new RuntimeTrafficSignalProgramInfo(
                                        segment.TrafficSignal.CycleSeconds,
                                        segment.TrafficSignal.ApproachDistanceMeters,
                                        segment.TrafficSignal.Phases
                                            .Select(
                                                static phase =>
                                                    new RuntimeTrafficSignalPhaseInfo(
                                                        phase.Phase,
                                                        phase.DurationSeconds))
                                            .ToArray()),
                                segment.SceneryObjectId))
                    .ToArray(),
                world.TrafficPaths.RoadVehicleSegmentCount,
                world.TrafficPaths.PedestrianSegmentCount,
                world.TrafficPaths.RailSegmentCount,
                world.TrafficPaths.AircraftSegmentCount,
                world.TrafficPaths.ConnectedEndpointCount,
                world.TrafficPaths.BoundaryEndpointCount,
                world.TrafficPaths.TerminalEndpointCount,
                world.TrafficPaths.UnmatchedEndpointCount);

        var runtimeTrafficVehicleAssets =
            trafficVehicleAssets
                .ToDictionary(
                    static pair =>
                        pair.Key,
                    static pair =>
                        RuntimeVehicleInfoFactory.FromAsset(
                            pair.Value),
                    StringComparer.OrdinalIgnoreCase);

        var runtimeAiCatalog =
            new RuntimeAiCatalogInfo(
                world.AiCatalog.MovingVehicles
                    .Select(
                        static item =>
                            new RuntimeAiVehicleDefinitionInfo(
                                item.GroupName,
                                item.DeclaredPath,
                                item.ResolvedPath,
                                item.Weight))
                    .ToArray(),
                world.AiCatalog.Humans
                    .Select(
                        static item =>
                            new RuntimeAiFileReferenceInfo(
                                item.DeclaredPath,
                                item.ResolvedPath))
                    .ToArray(),
                world.AiCatalog.Drivers
                    .Select(
                        static item =>
                            new RuntimeAiFileReferenceInfo(
                                item.DeclaredPath,
                                item.ResolvedPath))
                    .ToArray(),
                world.AiCatalog.ParkedVehicles
                    .Select(
                        static item =>
                            new RuntimeAiFileReferenceInfo(
                                item.DeclaredPath,
                                item.ResolvedPath))
                    .ToArray());

        var dynamicSceneryObjectIds =
            WorldRailSignalRouteResolver
                .Resolve(
                    world.TrafficPaths,
                    world.SignalRoutes)
                .Select(
                    static route =>
                        route.Signal?.ObjectId)
                .Where(
                    static objectId =>
                        objectId.HasValue &&
                        objectId.Value >=
                            0)
                .Select(
                    static objectId =>
                        objectId!.Value)
                .ToHashSet();

        return new RuntimeWindowInfo(
            world.Name,
            world.Tiles.Count,
            world.TotalTileCount,
            world.ActiveTileRadius,
            world.Objects.Count,
            world.Splines.Count,
            contentRoot,
            runtimeTiles,
            runtimeSplines,
            runtimeObjects,
            runtimeSceneryAssets,
            runtimeGroundTextures,
            runtimeTrafficPaths,
            runtimeAiCatalog,
            runtimeVehicle,
            runtimeSpawn,
            runtimeTrafficVehicleAssets,
            dynamicSceneryObjectIds);
    }

    private static RuntimeVehiclePhysicsInfo ConvertVehiclePhysics(
        OmsiVehiclePhysics physics) =>
        new RuntimeVehiclePhysicsInfo(
            physics.WheelBaseMeters,
            physics.MaximumSteeringAngleDegrees,
            physics.MassTonnes,
            physics.CenterOfGravityHeightMeters,
            physics.RollingResistanceNewtons,
            physics.TrackWidthMeters,
            physics.AverageWheelDiameterMeters,
            AverageAxleValue(
                physics.Axles,
                static axle =>
                    axle.SpringRateKilonewtonsPerMeter),
            AverageAxleValue(
                physics.Axles,
                static axle =>
                    axle.DamperRateKilonewtonSecondsPerMeter),
            physics.MomentOfInertiaZ,
            physics.RotationPointLongitudinalMeters,
            physics.InverseMinimumTurnRadius,
            physics.Axles.Count > 0
                ? physics.Axles.Max(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                : null,
            physics.Axles.Count > 0
                ? physics.Axles.Min(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                : null,
            AxleValueByPosition(
                physics.Axles,
                front: true,
                static axle =>
                    axle.SpringRateKilonewtonsPerMeter),
            AxleValueByPosition(
                physics.Axles,
                front: false,
                static axle =>
                    axle.SpringRateKilonewtonsPerMeter),
            AxleValueByPosition(
                physics.Axles,
                front: true,
                static axle =>
                    axle.DamperRateKilonewtonSecondsPerMeter),
            AxleValueByPosition(
                physics.Axles,
                front: false,
                static axle =>
                    axle.DamperRateKilonewtonSecondsPerMeter),
            physics.MomentOfInertiaX,
            physics.MomentOfInertiaY,
            physics.MomentOfInertiaZ,
            physics.Axles
                .Select(
                    static axle =>
                        new RuntimeVehicleAxleInfo(
                            axle.LongitudinalPositionMeters,
                            axle.WheelDiameterMeters,
                            axle.DriveFactor,
                            axle.MaximumWidthMeters,
                            axle.MinimumWidthMeters,
                            axle.SpringRateKilonewtonsPerMeter,
                            axle.MaximumForceKilonewtons,
                            axle.DamperRateKilonewtonSecondsPerMeter))
                .ToArray());

    private static double? AxleValueByPosition(
        IReadOnlyList<OmsiVehicleAxle> axles,
        bool front,
        Func<OmsiVehicleAxle, double?> selector)
    {
        var axle =
            axles
                .Where(
                    static item =>
                        double.IsFinite(
                            item.LongitudinalPositionMeters))
                .OrderBy(
                    item =>
                        front
                            ? -item.LongitudinalPositionMeters
                            : item.LongitudinalPositionMeters)
                .FirstOrDefault();

        return axle is null
            ? null
            : selector(
                axle);
    }

    private static double? AverageAxleValue(
        IReadOnlyList<OmsiVehicleAxle> axles,
        Func<OmsiVehicleAxle, double?> selector)
    {
        var values =
            axles
                .Select(
                    selector)
                .Where(
                    static value =>
                        value.HasValue &&
                        double.IsFinite(
                            value.Value))
                .Select(
                    static value =>
                        value!.Value)
                .ToArray();

        return values.Length == 0
            ? null
            : values.Average();
    }

    private void DisposePluginClients()
    {
        foreach (var session in
                 _pluginSessions)
        {
            try
            {
                session.Client.Dispose();
            }
            catch
            {
            }
        }

        _pluginSessions.Clear();
    }

    private void PrepareCompatiblePluginSessions(
        IReadOnlyList<OmsiPluginDefinition> plugins)
    {
        var hostPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "PluginHost-x86",
                "OMSICompatible.PluginHost.x86.exe");

        if (!File.Exists(
                hostPath))
        {
            Console.WriteLine(
                $"[plugins] x86 host unavailable at {hostPath}; plugin execution stays disabled.");
            return;
        }

        foreach (var plugin in
                 plugins)
        {
            if (!File.Exists(
                    plugin.DllPath) ||
                OmsiPluginBinaryInspector.Inspect(
                    plugin.DllPath) !=
                    OmsiPluginBinaryArchitecture.X86)
            {
                continue;
            }

            try
            {
                var client =
                    OmsiPluginRemoteClient.Start(
                        hostPath,
                        plugin.DllPath);

                _pluginSessions.Add(
                    new HostedPluginSession(
                        plugin,
                        client));

                Console.WriteLine(
                    $"[plugins] host-ready opl={Path.GetFileName(plugin.OplPath)}; dll={Path.GetFileName(plugin.DllPath)}; capabilities={client.Capabilities}");
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"[plugins] host failed for {Path.GetFileName(plugin.DllPath)}: {exception.Message}");
            }
        }

        Console.WriteLine(
            $"[plugins] hosted={_pluginSessions.Count:N0}; variable bridge enabled for known OMSI runtime state.");
    }

    private void StepHostedPlugins(
        double deltaSeconds,
        OmsiScriptRuntime runtime,
        Action<string> dispatchTrigger)
    {
        foreach (var session in
                 _pluginSessions)
        {
            if (session.Failed)
            {
                continue;
            }

            session.AccumulatedSeconds +=
                Math.Max(
                    deltaSeconds,
                    0.0);

            var declaredAccessCount =
                session.Definition.Variables.Count +
                session.Definition.StringVariables.Count +
                session.Definition.SystemVariables.Count +
                session.Definition.Triggers.Count;

            var intervalSeconds =
                declaredAccessCount >=
                    256
                    ? 1.0 / 30.0
                    : 1.0 / 60.0;

            if (session.AccumulatedSeconds <
                intervalSeconds)
            {
                continue;
            }

            session.AccumulatedSeconds =
                0.0;

            var numericNames =
                session.Definition.Variables
                    .Select(
                        (name, index) =>
                            (
                                Name: name,
                                Index: index
                            ))
                    .Where(
                        item =>
                            item.Index <=
                                ushort.MaxValue &&
                            runtime.HasLocalVariable(
                                item.Name))
                    .ToArray();

            var systemNames =
                session.Definition.SystemVariables
                    .Select(
                        (name, index) =>
                            (
                                Name: name,
                                Index: index
                            ))
                    .Where(
                        item =>
                            item.Index <=
                                ushort.MaxValue &&
                            runtime.HasSystemVariable(
                                item.Name))
                    .ToArray();

            var stringNames =
                session.Definition.StringVariables
                    .Select(
                        (name, index) =>
                            (
                                Name: name,
                                Index: index
                            ))
                    .Where(
                        item =>
                            item.Index <=
                                ushort.MaxValue &&
                            runtime.HasStringLocalVariable(
                                item.Name))
                    .ToArray();

            var triggerIndices =
                session.Definition.Triggers
                    .Select(
                        (name, index) =>
                            (
                                Name: name,
                                Index: index
                            ))
                    .Where(
                        static item =>
                            item.Index <=
                                ushort.MaxValue)
                    .ToArray();

            var frame =
                new OmsiPluginFrame(
                    systemNames
                        .Select(
                            item =>
                                (
                                    (ushort)item.Index,
                                    (float)runtime.GetSystem(
                                        item.Name)
                                ))
                        .ToArray(),
                    numericNames
                        .Select(
                            item =>
                                (
                                    (ushort)item.Index,
                                    (float)runtime.GetLocal(
                                        item.Name)
                                ))
                        .ToArray(),
                    stringNames
                        .Select(
                            item =>
                                (
                                    (ushort)item.Index,
                                    runtime.GetStringLocal(
                                        item.Name)
                                ))
                        .ToArray(),
                    triggerIndices
                        .Select(
                            static item =>
                                (ushort)item.Index)
                        .ToArray());

            try
            {
                var reply =
                    session.Client.Frame(
                        frame);

                for (var index = 0;
                     index <
                         numericNames.Length &&
                     index <
                         reply.Variables.Count;
                     index++)
                {
                    if (reply.Variables[index] is
                        { } value)
                    {
                        runtime.SetLocal(
                            numericNames[index].Name,
                            value);
                    }
                }

                for (var index = 0;
                     index <
                         systemNames.Length &&
                     index <
                         reply.SystemVariables.Count;
                     index++)
                {
                    if (reply.SystemVariables[index] is
                        { } value)
                    {
                        runtime.SetSystem(
                            systemNames[index].Name,
                            value);
                    }
                }

                for (var index = 0;
                     index <
                         stringNames.Length &&
                     index <
                         reply.StringVariables.Count;
                     index++)
                {
                    if (reply.StringVariables[index] is
                        { } value)
                    {
                        runtime.SetStringLocal(
                            stringNames[index].Name,
                            value);
                    }
                }

                for (var index = 0;
                     index <
                         triggerIndices.Length &&
                     index <
                         reply.TriggersActive.Count;
                     index++)
                {
                    var triggerIndex =
                        triggerIndices[index].Index;

                    var active =
                        reply.TriggersActive[
                            index];

                    if (session.TriggerStates[
                            triggerIndex] ==
                        active)
                    {
                        continue;
                    }

                    session.TriggerStates[
                        triggerIndex] =
                        active;

                    dispatchTrigger(
                        active
                            ? triggerIndices[index].Name
                            : triggerIndices[index].Name +
                              "_off");
                }
            }
            catch (Exception exception)
            {
                session.Failed =
                    true;

                Console.WriteLine(
                    $"[plugins] disabled {Path.GetFileName(session.Definition.DllPath)} after frame failure: {exception.Message}");
            }
        }
    }

    private static string? ResolveMapImage(
        string mapDirectory)
    {
        string[] names =
        [
            "picture.jpg",
            "picture.jpeg",
            "picture.png",
            "picture.bmp",
            "preview.jpg",
            "preview.png"
        ];

        foreach (var name in names)
        {
            var path =
                Path.Combine(
                    mapDirectory,
                    name);

            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
