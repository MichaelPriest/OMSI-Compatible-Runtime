using System.Diagnostics;
using System.Numerics;
using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Plugins;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.D3D11;
using OMSICompatible.Renderer.D3D12;
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
    private (int X, int Y)? _pendingPrefetchCenter;
    private (int X, int Y)? _lastPrefetchedCenter;

    private D3D11RenderWindow? _runtimeWindow;
    private OmsiVehicleAsset? _vehicleAsset;
    private WorldTrafficSimulation? _trafficSimulation;
    private WorldRailTrafficSimulation? _railTrafficSimulation;
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
    private readonly Dictionary<int, TrafficScriptRuntimeState>
        _trafficScriptRuntimes =
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

                    _runtimeWindow.Dispose();
                    _runtimeWindow = null;

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

                        return (
                            Terrain: terrain,
                            Splines: splines,
                            Objects: objects
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
                        geometry.Splines.Vertices)
                    : null;

            using var objects =
                geometry.Objects.Vertices.Length >
                        0
                    ? graphics.CreateObjectResources(
                        geometry.Objects.Vertices)
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
                        runtimeInfo.Tiles.Average(
                            static tile =>
                                tile.X) *
                            300.0f +
                        150.0f,
                        0.0f,
                        runtimeInfo.Tiles.Average(
                            static tile =>
                                tile.Y) *
                            300.0f +
                        150.0f);

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

            graphics.DrawSceneAndPresent(
                terrain,
                splines,
                objects,
                0.04f,
                0.06f,
                0.09f,
                vsync:
                    false);

            var drawElapsed =
                Stopwatch.GetElapsedTime(
                    drawStarted);

            Console.WriteLine(
                $"[d3d12-scene] success; terrainVertices={geometry.Terrain.Vertices.Length:N0}; terrainBatches={geometry.Terrain.Batches.Count:N0}; splineVertices={geometry.Splines.Vertices.Length:N0}; objectVertices={geometry.Objects.Vertices.Length:N0}; buildMs={buildElapsed.TotalMilliseconds:0.0}; uploadMs={uploadElapsed.TotalMilliseconds:0.0}; drawMs={drawElapsed.TotalMilliseconds:0.0}; span={span:0.0}m");
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

        var states =
            simulation
                .SignalRouteSnapshot()
                .Where(
                    static state =>
                        state.Signal is not null)
                .ToArray();

        var stateByObjectId =
            states
                .GroupBy(
                    static state =>
                        state.Signal!.ObjectId)
                .ToDictionary(
                    static group =>
                        group.Key,
                    static group =>
                        group
                            .OrderByDescending(
                                static state =>
                                    state.Reserved)
                            .ThenBy(
                                static state =>
                                    state.RouteIndex)
                            .First());

        foreach (var pair in
                 _railSignalScriptRuntimes)
        {
            var runtime =
                pair.Value;

            var signalState =
                stateByObjectId.TryGetValue(
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

        return states
            .Select(
                state =>
                    new RuntimeRailSignalRouteStateInfo(
                        state.RouteIndex,
                        state.Signal!.ObjectId,
                        state.Signal.SignalState,
                        state.Reserved,
                        state.ReservedAgentIndex,
                        _railSignalScriptRuntimes.TryGetValue(
                            state.Signal.ObjectId,
                            out var runtime)
                            ? runtime
                            : null))
            .ToArray();
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

        return roadSimulation
            .SnapshotTrafficSignals()
            .Select(
                static state =>
                    new RuntimeTrafficSignalStateInfo(
                        state.SegmentIndex,
                        state.Phase,
                        state.PositionSeconds))
            .ToArray();
    }

    private IReadOnlyList<RuntimeTrafficAgentInfo>
        StepTrafficSimulation(
            double deltaSeconds)
    {
        var agents =
            new List<WorldTrafficAgentState>();

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

            agents.AddRange(
                roadSimulation.Snapshot());
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

            foreach (var train in
                     railSimulation.Snapshot())
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
            return Array.Empty<
                RuntimeTrafficAgentInfo>();
        }

        UpdateTrafficScriptRuntimes(
            agents,
            deltaSeconds);

        return agents
            .Select(
                agent =>
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
                            agent)))
            .ToArray();
    }

    private void UpdateTrafficScriptRuntimes(
        IReadOnlyList<WorldTrafficAgentState> agents,
        double deltaSeconds)
    {
        var activeAgentIds =
            agents
                .Select(
                    static agent =>
                        agent.AgentIndex)
                .ToHashSet();

        foreach (var staleAgentId in
                 _trafficScriptRuntimes
                     .Keys
                     .Where(
                         id =>
                             !activeAgentIds.Contains(
                                 id))
                     .ToArray())
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

                SeedTrafficScriptHostVariables(
                    runtime,
                    agent,
                    0.0);

                if (_trafficVehicleAssets.TryGetValue(
                        agent.VehiclePath,
                        out var trafficAsset))
                {
                    ApplyHofToScriptRuntime(
                        runtime,
                        ResolveMapHofForBus(
                            trafficAsset.Bus,
                            _map.FolderName));
                }

                runtime.ExecuteInit();

                state =
                    new TrafficScriptRuntimeState(
                        agent.VehiclePath,
                        runtime);

                _trafficScriptRuntimes[
                    agent.AgentIndex] =
                    state;
            }

            SeedTrafficScriptHostVariables(
                state.Runtime,
                agent,
                validDeltaSeconds);

            if (validDeltaSeconds >
                0.0)
            {
                state.Runtime.ExecuteFrameAi();
            }
        }
    }

    private void SeedTrafficScriptHostVariables(
        OmsiScriptRuntime runtime,
        WorldTrafficAgentState agent,
        double deltaSeconds)
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

        if (!_trafficVehicleAssets.TryGetValue(
                agent.VehiclePath,
                out var asset))
        {
            return;
        }

        var wheelDiameter =
            asset
                .Bus
                .Physics
                .AverageWheelDiameterMeters;

        if (!wheelDiameter.HasValue ||
            !double.IsFinite(
                wheelDiameter.Value) ||
            wheelDiameter.Value <=
                0.0)
        {
            return;
        }

        var circumference =
            Math.PI *
            wheelDiameter.Value;

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
        string? hofPath)
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

        Console.WriteLine(
            $"[hof] assigned={hofName}; file={hofPath}");
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

    private sealed record TrafficScriptRuntimeState(
        string VehiclePath,
        OmsiScriptRuntime Runtime);

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
