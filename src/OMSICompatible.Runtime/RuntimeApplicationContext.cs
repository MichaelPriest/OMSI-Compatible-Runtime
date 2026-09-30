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
    private WorldRailTrafficSimulation? _railTrafficSimulation;
    private WorldDefinition? _currentWorld;
    private OpenOmsiLanSession? _multiplayerSession;
    private readonly RuntimeCommsLinkVoiceService
        _commsLinkVoice =
            new();
    private double _multiplayerStatusAccumulator;
    private readonly HashSet<string>
        _pendingMultiplayerVehiclePaths =
            new(
                StringComparer.OrdinalIgnoreCase);
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
                        (
                            Traffic:
                                CreateTrafficSimulation(
                                    world),
                            Rail:
                                CreateRailTrafficSimulation(
                                    world)));

            _trafficSimulation =
                initialSimulations.Traffic;

            _railTrafficSimulation =
                initialSimulations.Rail;

            Console.WriteLine(
                $"[startup-perf] simulationsMs={Stopwatch.GetElapsedTime(simulationBuildStarted).TotalMilliseconds:0.0}");

            _trafficScriptRuntimes.Clear();

            await EnsureTrafficVehicleAssetsAsync(
                _trafficSimulation);

            await EnsureRailTrafficAssetsAsync(
                _railTrafficSimulation);

            RebuildRailSignalScriptRuntimes(
                world);

            WriteTrafficDiagnostics(
                world,
                _trafficSimulation);

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
                            _trafficVehicleAssets));

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
                            (
                                Traffic:
                                    CreateTrafficSimulation(
                                        streamedWorld),
                                Rail:
                                    CreateRailTrafficSimulation(
                                        streamedWorld)));

                _trafficSimulation =
                    streamedSimulations.Traffic;

                _railTrafficSimulation =
                    streamedSimulations.Rail;

                Console.WriteLine(
                    $"[streaming-perf] simulationsMs={Stopwatch.GetElapsedTime(simulationBuildStarted).TotalMilliseconds:0.0}");

                _trafficScriptRuntimes.Clear();

                await EnsureTrafficVehicleAssetsAsync(
                    _trafficSimulation);

                await EnsureRailTrafficAssetsAsync(
                    _railTrafficSimulation);

                RebuildRailSignalScriptRuntimes(
                    streamedWorld);

                WriteTrafficDiagnostics(
                    streamedWorld,
                    _trafficSimulation);

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
                                _trafficVehicleAssets));

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
        WorldTrafficSimulation simulation)
    {
        var requestedPaths =
            simulation
                .Snapshot()
                .Select(
                    static agent =>
                        agent.VehiclePath)
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
        if (_trafficSimulation is not
                { } roadSimulation)
        {
            return Array.Empty<
                RuntimeTrafficSignalStateInfo>();
        }

        _worldTrafficSignalStateBuffer.Clear();
        roadSimulation.AppendTrafficSignalSnapshotTo(
            _worldTrafficSignalStateBuffer);

        _runtimeTrafficSignalStateBuffer.Clear();

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

        return _runtimeTrafficSignalStateBuffer;
    }

    private IReadOnlyList<RuntimeTrafficAgentInfo>
        StepTrafficSimulation(
            double deltaSeconds)
    {
        _trafficAgentStateBuffer.Clear();

        var agents =
            _trafficAgentStateBuffer;

        if (_trafficSimulation is
            { } roadSimulation)
        {
            var playerObstacle =
                _runtimeWindow?
                    .PlayerTrafficObstacle;

            roadSimulation.SetExternalObstacle(
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
                        playerObstacle.HalfWidthMeters));

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

        return _runtimeTrafficAgentBuffer;
    }

    private void StartMultiplayerSession()
    {
        DisposeMultiplayerSession();
        _multiplayerStatusAccumulator =
            1.0;

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

            NotifyCommsLinkVoiceState(
                "VOZ: PTT F10 · pronto");

            _runtimeWindow?.SetDriveOpsNetworkState(
                _multiplayerSession.Role.ToString().ToUpperInvariant(),
                _multiplayerSession.Connected,
                0,
                _multiplayerSession.SessionHex);

            Console.WriteLine(
                mode == "host"
                    ? $"[multiplayer] host protocol={OpenOmsiLanProtocol.ProtocolVersion}; port={_multiplayerSession.LocalPort}; session={_multiplayerSession.SessionHex}"
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

        var session =
            _multiplayerSession;

        if (session is null ||
            !session.Connected)
        {
            NotifyCommsLinkVoiceState(
                "VOZ: indisponível · entre em uma sessão multiplayer");
            e.SuppressKeyPress =
                true;
            return;
        }

        if (_commsLinkVoice.StartTransmit())
        {
            NotifyCommsLinkVoiceState(
                "VOZ: TRANSMITINDO · solte F10 para encerrar");
        }
        else
        {
            NotifyCommsLinkVoiceState(
                $"VOZ: falha no microfone · {_commsLinkVoice.LastError ?? "dispositivo indisponível"}");
        }

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

        if (_commsLinkVoice.IsTransmitting)
        {
            _commsLinkVoice.StopTransmit();
            NotifyCommsLinkVoiceState(
                "VOZ: PTT F10 · pronto");
        }

        e.SuppressKeyPress =
            true;
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

        _commsLinkVoice.StopTransmit();
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
        _multiplayerTravelMeters.Clear();
        _multiplayerRemoteStates.Clear();
        _multiplayerScriptRuntimes.Clear();
        _multiplayerScriptPaths.Clear();
        _multiplayerActivePeerIds.Clear();
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
                    string.Empty
            };

        if (_runtimeWindow is not
            { IsDisposed: false } window ||
            _bus is null)
        {
            return pose;
        }

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

        var flags =
            OpenOmsiLanProtocol.FlagVehicle;

        if (state.EngineRunning)
        {
            flags |=
                OpenOmsiLanProtocol.FlagEngine;
        }

        if (state.ElectricalSystemEnabled)
        {
            flags |=
                OpenOmsiLanProtocol.FlagElectrics;
        }

        if (state.BrakeLevel >
            0.05f)
        {
            flags |=
                OpenOmsiLanProtocol.FlagBrake;
        }

        if (state.Gear <
            0)
        {
            flags |=
                OpenOmsiLanProtocol.FlagReverse;
        }

        if (state.StopBrakeEngaged ||
            state.ParkingBrakeEngaged)
        {
            flags |=
                OpenOmsiLanProtocol.FlagStopBrake;
        }

        pose.Flags =
            flags;

        pose.Rpm =
            (float)ReadFirstPlayerLocal(
                "engine_n",
                "n_engine",
                "engine_rpm");

        var parkingLights =
            ReadFirstPlayerLocal(
                "lights_stand",
                "lights_standlicht",
                "lights_parking") >
            0.5;

        var dippedLights =
            ReadFirstPlayerLocal(
                "lights_abbl",
                "lights_abblend",
                "lights_lowbeam",
                "ai_light") >
            0.5;

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
            pose.LengthMeters =
                (float)(
                    obstacle.HalfLengthMeters *
                    2.0);
            pose.WidthMeters =
                (float)(
                    obstacle.HalfWidthMeters *
                    2.0);
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

    private void AppendMultiplayerTrafficAgents(
        double deltaSeconds)
    {
        var session =
            _multiplayerSession;

        if (session is null)
        {
            return;
        }

        try
        {
            session.Tick(
                deltaSeconds,
                CreateLocalMultiplayerPose());

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
                session.SessionHex);
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
            }
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
        }

        SeedMultiplayerScriptRuntime(
            runtime,
            peer.Pose,
            deltaSeconds);

        runtime.ExecuteFrameAi();

        // Pin the network-driven values again after frame_ai so a bus whose
        // AI script computes defaults cannot erase the remote driver's state.
        SeedMultiplayerScriptRuntime(
            runtime,
            peer.Pose,
            0.0);

        return runtime;
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
                                    _trafficVehicleAssets));

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

                if (_trafficVehicleAssets.TryGetValue(
                        agent.VehiclePath,
                        out var trafficAsset))
                {
                    wheelDiameterMeters =
                        trafficAsset
                            .Bus
                            .Physics
                            .AverageWheelDiameterMeters;

                    if (!_trafficHofByVehiclePath.TryGetValue(
                            agent.VehiclePath,
                            out var trafficHofPath))
                    {
                        trafficHofPath =
                            ResolveMapHofForBus(
                                trafficAsset.Bus,
                                _map.FolderName);

                        _trafficHofByVehiclePath[
                            agent.VehiclePath] =
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

    private static string? ResolveMapHofForBus(
        OmsiBusInfo bus,
        string mapFolderName)
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
        IReadOnlyDictionary<string, OmsiVehicleAsset> trafficVehicleAssets)
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
