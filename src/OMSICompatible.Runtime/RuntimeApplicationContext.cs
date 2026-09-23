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
    private readonly OmsiBusInfo _bus;
    private readonly OmsiMapEntryPoint _entryPoint;
    private readonly bool _externalLoading;
    private readonly LoadingForm _loading;
    private readonly SemaphoreSlim _streamingGate =
        new(1, 1);

    private D3D11RenderWindow? _runtimeWindow;
    private OmsiVehicleAsset? _vehicleAsset;
    private (int X, int Y)? _pendingStreamingCenter;
    private int _loadedCenterX;
    private int _loadedCenterY;
    private bool _closing;

    private const int CompleteMapTileThreshold = 64;
    private const int StreamingTileRadius = 2;

    public RuntimeApplicationContext(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map,
        OmsiBusInfo bus,
        OmsiMapEntryPoint entryPoint,
        bool externalLoading)
    {
        _contentRoot = contentRoot;
        _map = map;
        _bus = bus;
        _entryPoint = entryPoint;
        _externalLoading =
            externalLoading;
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
            var vehicleLoadPercent = 0;

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
                discoveredTileCount > 0 &&
                discoveredTileCount <=
                    CompleteMapTileThreshold;

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
                                    StreamingTileRadius,
                                LoadEntireMap:
                                    loadEntireMap)));

            var vehicleTask =
                Task.Run(
                    () =>
                        OmsiVehicleAssetLoader.Load(
                            _contentRoot,
                            _bus,
                            vehicleProgress));

            await Task.WhenAll(
                worldTask,
                vehicleTask);

            var world =
                await worldTask;

            var vehicle =
                await vehicleTask;

            _vehicleAsset =
                vehicle;

            WriteVehicleLoadDiagnostics(
                vehicle);

            ReportProgress(
                new WorldLoadProgress(
                    90,
                    "Preparando renderização",
                    $"{world.Tiles.Count:N0}/{world.TotalTileCount:N0} tiles ativos · " +
                    $"{vehicle.RenderableMeshCount:N0} mesh(es) renderizáveis do ônibus · " +
                    $"{vehicle.ProtectedMeshCount:N0} protegida(s) · " +
                    $"{vehicle.FailedMeshCount:N0} com falha..."));

            var runtimeInfo =
                BuildRuntimeInfo(
                    world,
                    vehicle,
                    _entryPoint,
                    _contentRoot.RootPath);

            var scriptCatalog =
                OmsiScriptCatalogLoader.Load(
                    _contentRoot,
                    _bus.ScriptManifest);

            var scriptRuntime =
                new OmsiScriptRuntime(
                    scriptCatalog);

            ReportProgress(
                new WorldLoadProgress(
                    96,
                    "Inicializando Direct3D 11",
                    "Criando dispositivo, shaders e recursos gráficos..."));

            _runtimeWindow =
                new D3D11RenderWindow(
                    runtimeInfo,
                    scriptRuntime);

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

    private static void WriteVehicleLoadDiagnostics(
        OmsiVehicleAsset vehicle)
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
                    $"meshTotal={vehicle.Meshes.Count}",
                    $"meshRenderable={vehicle.RenderableMeshCount}",
                    $"meshProtected={vehicle.ProtectedMeshCount}",
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
            _vehicleAsset is null)
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
                                        StreamingTileRadius,
                                    LoadEntireMap: false)));

                if (_closing ||
                    _runtimeWindow is null ||
                    _runtimeWindow.IsDisposed ||
                    _vehicleAsset is null)
                {
                    return;
                }

                var runtimeInfo =
                    BuildRuntimeInfo(
                        streamedWorld,
                        _vehicleAsset,
                        _entryPoint,
                        _contentRoot.RootPath);

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

    private static RuntimeWindowInfo BuildRuntimeInfo(
        WorldDefinition world,
        OmsiVehicleAsset vehicle,
        OmsiMapEntryPoint entryPoint,
        string contentRoot)
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

                        return new RuntimeSplineInfo(
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
                            surfaces);
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
            new RuntimeVehicleInfo(
                vehicle.Bus.DisplayName,
                vehicle.Bus.RelativePath,
                vehicle.Meshes
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
                                                material.AlphaScaleVariable,
                                                material.LightMapTexturePath,
                                                material.LightMapVariable,
                                                material.MaterialChangeTexturePath,
                                                material.MaterialChangeVariable,
                                                material.BaseAllColor is null
                                                    ? null
                                                    : new RuntimeVehicleMaterialColorInfo(
                                                        material.BaseAllColor.DiffuseR,
                                                        material.BaseAllColor.DiffuseG,
                                                        material.BaseAllColor.DiffuseB,
                                                        material.BaseAllColor.DiffuseA,
                                                        material.BaseAllColor.AmbientR,
                                                        material.BaseAllColor.AmbientG,
                                                        material.BaseAllColor.AmbientB,
                                                        material.BaseAllColor.SpecularR,
                                                        material.BaseAllColor.SpecularG,
                                                        material.BaseAllColor.SpecularB,
                                                        material.BaseAllColor.EmissiveR,
                                                        material.BaseAllColor.EmissiveG,
                                                        material.BaseAllColor.EmissiveB,
                                                        material.BaseAllColor.Power),
                                                material.MaterialChangeAllColor is null
                                                    ? null
                                                    : new RuntimeVehicleMaterialColorInfo(
                                                        material.MaterialChangeAllColor.DiffuseR,
                                                        material.MaterialChangeAllColor.DiffuseG,
                                                        material.MaterialChangeAllColor.DiffuseB,
                                                        material.MaterialChangeAllColor.DiffuseA,
                                                        material.MaterialChangeAllColor.AmbientR,
                                                        material.MaterialChangeAllColor.AmbientG,
                                                        material.MaterialChangeAllColor.AmbientB,
                                                        material.MaterialChangeAllColor.SpecularR,
                                                        material.MaterialChangeAllColor.SpecularG,
                                                        material.MaterialChangeAllColor.SpecularB,
                                                        material.MaterialChangeAllColor.EmissiveR,
                                                        material.MaterialChangeAllColor.EmissiveG,
                                                        material.MaterialChangeAllColor.EmissiveB,
                                                        material.MaterialChangeAllColor.Power),
                                                material.EnvMapTexturePath,
                                                material.EnvMapStrength,
                                                material.EnvMapMaskTexturePath,
                                                material.BumpMapTexturePath,
                                                material.BumpMapStrength,
                                                material.FreeTextures
                                                    .Select(
                                                        static freeTexture =>
                                                            new RuntimeVehicleFreeTextureInfo(
                                                                freeTexture.SourceTextureName,
                                                                freeTexture.VariableName))
                                                    .ToArray(),
                                                material.TextTextureIndex,
                                                material.MaterialChangeSets?
                                                    .Select(
                                                        static changeSet =>
                                                            new RuntimeVehicleMaterialChangeSetInfo(
                                                                changeSet.VariableName,
                                                                changeSet.GroupIndex,
                                                                changeSet.Items
                                                                    .Select(
                                                                        static item =>
                                                                            new RuntimeVehicleMaterialChangeItemInfo(
                                                                                item.ItemIndex,
                                                                                item.AlphaMode,
                                                                                item.TransMapTexturePath,
                                                                                item.HasTransMapDirective,
                                                                                item.NoZWrite,
                                                                                item.NoZCheck,
                                                                                item.AlphaScaleVariable,
                                                                                item.LightMapTexturePath,
                                                                                item.LightMapVariable,
                                                                                item.MaterialChangeTexturePath,
                                                                                item.AllColor is null
                                                                                    ? null
                                                                                    : new RuntimeVehicleMaterialColorInfo(
                                                                                        item.AllColor.DiffuseR,
                                                                                        item.AllColor.DiffuseG,
                                                                                        item.AllColor.DiffuseB,
                                                                                        item.AllColor.DiffuseA,
                                                                                        item.AllColor.AmbientR,
                                                                                        item.AllColor.AmbientG,
                                                                                        item.AllColor.AmbientB,
                                                                                        item.AllColor.SpecularR,
                                                                                        item.AllColor.SpecularG,
                                                                                        item.AllColor.SpecularB,
                                                                                        item.AllColor.EmissiveR,
                                                                                        item.AllColor.EmissiveG,
                                                                                        item.AllColor.EmissiveB,
                                                                                        item.AllColor.Power),
                                                                                item.EnvMapTexturePath,
                                                                                item.EnvMapStrength,
                                                                                item.EnvMapMaskTexturePath,
                                                                                item.BumpMapTexturePath,
                                                                                item.BumpMapStrength,
                                                                                item.FreeTextures
                                                                                    .Select(
                                                                                        static freeTexture =>
                                                                                            new RuntimeVehicleFreeTextureInfo(
                                                                                                freeTexture.SourceTextureName,
                                                                                                freeTexture.VariableName))
                                                                                    .ToArray(),
                                                                                item.TextTextureIndex))
                                                                    .ToArray()))
                                                    .ToArray()))
                                    .ToArray(),
                                mesh.ViewpointFlag,
                                mesh.LodThreshold,
                                mesh.VisibilityConditions
                                    .Select(
                                        static condition =>
                                            new RuntimeVehicleVisibilityConditionInfo(
                                                condition.VariableName,
                                                condition.Value))
                                    .ToArray(),
                                mesh.Animations
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
                                mesh.SourceTransform))
                    .ToArray(),
                vehicle.Bus.DriverCameras
                    .Select(
                        static camera =>
                            new RuntimeDriverCameraInfo(
                                -camera.X,
                                camera.Z,
                                camera.Y,
                                camera.EyeDistance,
                                camera.FieldOfViewDegrees,
                                camera.HeadingDegrees,
                                camera.PitchDegrees))
                    .ToArray(),
                vehicle.Bus.PassengerCameras
                    .Select(
                        static camera =>
                            new RuntimePassengerCameraInfo(
                                -camera.X,
                                camera.Z,
                                camera.Y,
                                camera.EyeDistance,
                                camera.FieldOfViewDegrees,
                                camera.HeadingDegrees,
                                camera.PitchDegrees))
                    .ToArray(),
                vehicle.Bus.StandardDriverCameraIndex,
                vehicle.Bus.ScheduleDriverCameraIndex,
                vehicle.Bus.TicketSellingDriverCameraIndex,
                vehicle.Bus.OutsideCameraCenter is null
                    ? null
                    : new RuntimeOutsideCameraCenterInfo(
                        -vehicle.Bus.OutsideCameraCenter.X,
                        vehicle.Bus.OutsideCameraCenter.Z,
                        vehicle.Bus.OutsideCameraCenter.Y),
                vehicle.Bus.ReflectionCameras
                    .Select(
                        static camera =>
                            new RuntimeReflectionCameraInfo(
                                camera.Index,
                                -camera.X,
                                camera.Z,
                                camera.Y,
                                camera.EyeDistance,
                                camera.FieldOfViewDegrees,
                                camera.HeadingDegrees,
                                camera.PitchDegrees,
                                camera.MaximumRenderDistanceMeters,
                                camera.RuntimeTextureName,
                                camera.RuntimeTextureKey))
                    .ToArray(),
                new RuntimeVehiclePhysicsInfo(
                    vehicle.Bus.Physics.WheelBaseMeters,
                    vehicle.Bus.Physics.MaximumSteeringAngleDegrees),
                vehicle.DriverPosition is null
                    ? null
                    : new RuntimeDriverPositionInfo(
                        -vehicle.DriverPosition.X,
                        vehicle.DriverPosition.Z,
                        vehicle.DriverPosition.Y,
                        vehicle.DriverPosition.SeatHeight,
                        vehicle.DriverPosition.RotationDegrees),
                vehicle.ProtectedMeshCount,
                vehicle.TextTextures
                    .Select(
                        static texture =>
                            new RuntimeVehicleTextTextureInfo(
                                texture.Index,
                                texture.StringVariable,
                                texture.FontName,
                                texture.Width,
                                texture.Height,
                                texture.FullColor,
                                texture.Red,
                                texture.Green,
                                texture.Blue,
                                texture.Alignment,
                                texture.GridAligned))
                    .ToArray());

        var runtimeSpawn =
            new RuntimeSpawnInfo(
                entryPoint.Name,
                entryPoint.WorldX,
                entryPoint.WorldY,
                entryPoint.WorldZ,
                -entryPoint.HeadingDegrees);

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
            runtimeVehicle,
            runtimeSpawn);
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
