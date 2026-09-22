using OmsiCompat.Core;
using OmsiCompat.Map;
using OMSICompatible.Renderer.D3D11;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal sealed class RuntimeApplicationContext :
    ApplicationContext
{
    private readonly OmsiContentRoot _contentRoot;
    private readonly OmsiMapInfo _map;
    private readonly LoadingForm _loading;

    private D3D11RenderWindow? _runtimeWindow;

    public RuntimeApplicationContext(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map)
    {
        _contentRoot = contentRoot;
        _map = map;

        _loading =
            new LoadingForm(
                map.FolderName,
                ResolveMapImage(
                    map.DirectoryPath));

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
            _loading.SetStage(
                3,
                "Inicializando",
                "Preparando runtime x64...");

            var progress =
                new Progress<WorldLoadProgress>(
                    update =>
                        _loading.UpdateProgress(
                            update));

            var world =
                await Task.Run(
                    () =>
                        WorldLoader.Load(
                            _contentRoot,
                            _map,
                            progress));

            _loading.SetStage(
                90,
                "Preparando renderização",
                "Gerando terreno, vias e buffers D3D11...");

            var runtimeInfo =
                BuildRuntimeInfo(
                    world,
                    _contentRoot.RootPath);

            _loading.SetStage(
                96,
                "Inicializando Direct3D 11",
                "Criando dispositivo, shaders e recursos gráficos...");

            _runtimeWindow =
                new D3D11RenderWindow(
                    runtimeInfo);

            _runtimeWindow.Shown +=
                (_, _) =>
                {
                    _loading.SetStage(
                        100,
                        "Pronto",
                        "Entrando no mundo...");

                    _loading.Hide();
                };

            _runtimeWindow.FormClosed +=
                (_, _) =>
                {
                    _runtimeWindow.Dispose();
                    _runtimeWindow = null;

                    if (!_loading.IsDisposed)
                    {
                        _loading.Close();
                    }

                    ExitThread();
                };

            MainForm =
                _runtimeWindow;

            _runtimeWindow.Show();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);

            _loading.ShowFailure(
                ex.Message);
        }
    }

    private static RuntimeWindowInfo BuildRuntimeInfo(
        WorldDefinition world,
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
                                    tile.Terrain.MaximumHeight)))
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
                                                surface.From.Z),
                                            new RuntimeSplineProfilePointInfo(
                                                surface.To.X,
                                                surface.To.Z)))
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

        return new RuntimeWindowInfo(
            world.Name,
            world.Tiles.Count,
            world.Objects.Count,
            world.Splines.Count,
            contentRoot,
            runtimeTiles,
            runtimeSplines);
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
