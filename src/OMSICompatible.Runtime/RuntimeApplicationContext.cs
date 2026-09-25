using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.D3D11;
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
    private readonly OmsiRuntimeOptions _options;
    private readonly LoadingForm _loading;
    private readonly SemaphoreSlim _streamingGate =
        new(1, 1);

    private D3D11RenderWindow? _runtimeWindow;
    private OmsiVehicleAsset? _vehicleAsset;
    private WorldTrafficSimulation? _trafficSimulation;
    private readonly Dictionary<string, OmsiVehicleAsset>
        _trafficVehicleAssets =
            new(
                StringComparer.OrdinalIgnoreCase);
    private (int X, int Y)? _pendingStreamingCenter;
    private int _loadedCenterX;
    private int _loadedCenterY;
    private bool _loadEntireMap;
    private bool _closing;

    private const int CompleteMapTileThreshold = 64;

    public RuntimeApplicationContext(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map,
        OmsiBusInfo? bus,
        OmsiMapEntryPoint entryPoint,
        bool externalLoading,
        string? repaintName = null,
        string? repaintCtiRelativePath = null)
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
        _options =
            OmsiRuntimeOptions.Load();
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

            _trafficSimulation =
                CreateTrafficSimulation(
                    world);

            await EnsureTrafficVehicleAssetsAsync(
                _trafficSimulation);

            WriteTrafficDiagnostics(
                world,
                _trafficSimulation);

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

            var runtimeInfo =
                BuildRuntimeInfo(
                    world,
                    vehicle,
                    _entryPoint,
                    _contentRoot.RootPath,
                    _trafficVehicleAssets);

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

                    sectionScriptRuntimes[
                        section.Index] =
                        new OmsiScriptRuntime(
                            sectionCatalog);
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
                    materialLightMapEnabled:
                        _options.MaterialLightMap,
                    materialReflectionMapEnabled:
                        _options.MaterialReflectionMap,
                    materialBumpMapEnabled:
                        _options.MaterialBumpMap,
                    materialNightMapEnabled:
                        _options.MaterialNightMap,
                    trafficStep:
                        StepTrafficSimulation);

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

        _pendingStreamingCenter =
            (tileX, tileY);

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

                _trafficSimulation =
                    CreateTrafficSimulation(
                        streamedWorld);

                await EnsureTrafficVehicleAssetsAsync(
                    _trafficSimulation);

                WriteTrafficDiagnostics(
                    streamedWorld,
                    _trafficSimulation);

                var runtimeInfo =
                    BuildRuntimeInfo(
                        streamedWorld,
                        _vehicleAsset,
                        _entryPoint,
                        _contentRoot.RootPath,
                        _trafficVehicleAssets);

                _runtimeWindow.ApplyStreamedWorld(
                    runtimeInfo);

                _loadedCenterX =
                    requested.X;

                _loadedCenterY =
                    requested.Y;

                Console.WriteLine(
                    $"[streaming] Active {streamedWorld.Tiles.Count:N0}/{streamedWorld.TotalTileCount:N0} tiles around {requested.X},{requested.Y}.");
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

        var loaded =
            await Task.Run(
                () =>
                {
                    var result =
                        new List<KeyValuePair<
                            string,
                            OmsiVehicleAsset>>();

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

                            result.Add(
                                new KeyValuePair<
                                    string,
                                    OmsiVehicleAsset>(
                                    path,
                                    asset));
                        }
                        catch (Exception exception)
                        {
                            Console.WriteLine(
                                $"[traffic-ai] Failed to load {path}: {exception.Message}");
                        }
                    }

                    return result;
                });

        foreach (var pair in
                 loaded)
        {
            _trafficVehicleAssets[
                pair.Key] =
                pair.Value;
        }

        Console.WriteLine(
            $"[traffic-ai] cached={_trafficVehicleAssets.Count}; requested={requestedPaths.Length}; loaded={loaded.Count}");
    }

    private static WorldTrafficSimulation
        CreateTrafficSimulation(
            WorldDefinition world) =>
        new(
            world.TrafficPaths,
            world.AiCatalog,
            maximumAgents:
                12);

    private IReadOnlyList<RuntimeTrafficAgentInfo>
        StepTrafficSimulation(
            double deltaSeconds)
    {
        var simulation =
            _trafficSimulation;

        if (simulation is null)
        {
            return Array.Empty<
                RuntimeTrafficAgentInfo>();
        }

        if (double.IsFinite(
                deltaSeconds) &&
            deltaSeconds >
                0.0)
        {
            simulation.Step(
                deltaSeconds);
        }

        return simulation
            .Snapshot()
            .Select(
                static agent =>
                    new RuntimeTrafficAgentInfo(
                        agent.AgentIndex,
                        agent.SegmentIndex,
                        agent.DistanceMeters,
                        agent.SpeedMetersPerSecond,
                        agent.VehiclePath,
                        agent.Position.X,
                        agent.Position.Y,
                        agent.Position.Z,
                        agent.HeadingRadians,
                        agent.AiBrakeLight,
                        agent.AiBlinkerLeft,
                        agent.AiBlinkerRight,
                        agent.TraveledDistanceMeters,
                        agent.PathCurvaturePerMeter))
            .ToArray();
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
                            tile.Coordinate.X,
                            tile.Coordinate.Y,
                            tile.Objects.Count,
                            tile.Splines.Count,
                            tile.Terrain is null
                                ? null
                                : new RuntimeTerrainInfo(
                                    tile.Terrain.CellCount,
                                    tile.Terrain.Heights,
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
                                                surface.From.X,
                                                surface.From.Z,
                                                surface.From.TextureX,
                                                surface.From.TextureScale),
                                            new RuntimeSplineProfilePointInfo(
                                                surface.To.X,
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
                                            path.X,
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
                            spline.Tile.X,
                            spline.Tile.Y,
                            spline.Position.X,
                            spline.Position.Y,
                            spline.Position.Z,
                            spline.HeadingDegrees,
                            spline.LengthMeters,
                            spline.RadiusMeters,
                            spline.GradientStartPercent,
                            spline.GradientEndPercent,
                            surfaces,
                            paths);
                    })
                .ToArray();

        var runtimeObjects =
            world.Objects
                .Select(
                    static item =>
                        new RuntimeObjectInfo(
                            item.Tile.X,
                            item.Tile.Y,
                            item.AssetPath,
                            item.Position.X,
                            item.Position.Y,
                            item.Position.Z,
                            item.HeadingDegrees,
                            item.PitchDegrees,
                            item.BankDegrees,
                            item.ExtraValues))
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
                                                            material.NoZCheck))
                                                .ToArray()))
                                .ToArray(),
                            pair.Value.Tree is null
                                ? null
                                : new RuntimeTreeInfo(
                                    pair.Value.Tree.TextureName,
                                    pair.Value.Tree.TexturePath,
                                    pair.Value.Tree.MinimumHeight,
                                    pair.Value.Tree.MaximumHeight,
                                    pair.Value.Tree.MinimumAspect,
                                    pair.Value.Tree.MaximumAspect)),
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
                entryPoint.WorldX,
                entryPoint.WorldY,
                entryPoint.WorldZ,
                -entryPoint.HeadingDegrees);

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
                                                point.X,
                                                point.Y,
                                                point.Z))
                                    .ToArray(),
                                segment.ForwardConnections,
                                segment.ReverseConnections))
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
            runtimeTrafficVehicleAssets);
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
