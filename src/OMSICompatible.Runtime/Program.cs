using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using OMSICompatible.Renderer.D3D12;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (HasFlag(
                args,
                "--d3d12-smoke"))
        {
            return RunD3D12Smoke();
        }

        var contentPath =
            GetOption(
                args,
                "--content");

        var mapName =
            GetOption(
                args,
                "--map");

        var busRelativePath =
            GetOption(
                args,
                "--bus");

        var spawnName =
            GetOption(
                args,
                "--spawn");

        var repaintName =
            GetOption(
                args,
                "--repaint");

        var repaintCtiRelativePath =
            GetOption(
                args,
                "--repaint-cti");

        var hofPath =
            GetOption(
                args,
                "--hof");

        var noBus =
            HasFlag(
                args,
                "--no-bus");

        var externalLoading =
            HasFlag(
                args,
                "--external-loading");

        var headless =
            HasFlag(
                args,
                "--headless");

        var vehiclePreview =
            HasFlag(
                args,
                "--vehicle-preview");

        if (!OmsiContentRoot.TryCreate(
                contentPath,
                out var contentRoot,
                out var contentError) ||
            contentRoot is null)
        {
            Console.Error.WriteLine(
                contentError);
            return 2;
        }

        if (vehiclePreview)
        {
            OmsiBusInfo? previewBus =
                null;

            if (!string.IsNullOrWhiteSpace(
                    busRelativePath))
            {
                var normalizedBusPath =
                    busRelativePath
                        .Replace(
                            '\\',
                            Path.DirectorySeparatorChar)
                        .Replace(
                            '/',
                            Path.DirectorySeparatorChar)
                        .TrimStart(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar);

                var resolvedBusPath =
                    Path.Combine(
                        contentRoot.RootPath,
                        normalizedBusPath);

                if (File.Exists(
                        resolvedBusPath))
                {
                    try
                    {
                        var directBus =
                            OmsiBusReader.ReadFile(
                                contentRoot.RootPath,
                                resolvedBusPath);

                        if (BusDiscovery.IsPlayerSelectable(
                                directBus))
                        {
                            previewBus =
                                directBus;
                        }
                    }
                    catch
                    {
                        // Fall back to catalog discovery below.
                    }
                }
            }

            previewBus ??=
                BusDiscovery
                    .DiscoverPlayerSelectable(
                        contentRoot)
                    .FirstOrDefault(
                        bus =>
                            string.Equals(
                                bus.RelativePath,
                                busRelativePath,
                                StringComparison.OrdinalIgnoreCase));

            if (previewBus is null)
            {
                Console.Error.WriteLine(
                    "No OMSI .bus vehicle was found for preview.");
                return 4;
            }

            ApplicationConfiguration.Initialize();

            using var previewContext =
                new VehiclePreviewApplicationContext(
                    contentRoot,
                    previewBus,
                    repaintName,
                    repaintCtiRelativePath);

            Application.Run(
                previewContext);

            return 0;
        }

        var maps =
            MapDiscovery.Discover(
                contentRoot);

        if (string.IsNullOrWhiteSpace(
                mapName))
        {
            foreach (var map in maps)
            {
                Console.WriteLine(
                    map.FolderName);
            }

            return 0;
        }

        var selectedMap =
            maps.FirstOrDefault(
                map =>
                    string.Equals(
                        map.FolderName,
                        mapName,
                        StringComparison.OrdinalIgnoreCase));

        if (selectedMap is null)
        {
            Console.Error.WriteLine(
                $"Map not found: {mapName}");
            return 3;
        }

        if (headless)
        {
            var world =
                WorldLoader.Load(
                    contentRoot,
                    selectedMap);

            Console.WriteLine(
                $"Map={world.Name}; " +
                $"Tiles={world.Tiles.Count}; " +
                $"Objects={world.Objects.Count}; " +
                $"Splines={world.Splines.Count}");

            return 0;
        }

        OmsiBusInfo? selectedBus = null;

        if (!noBus)
        {
            var buses =
                BusDiscovery.DiscoverPlayerSelectable(
                    contentRoot);

            selectedBus =
                buses.FirstOrDefault(
                    bus =>
                        string.Equals(
                            bus.RelativePath,
                            busRelativePath,
                            StringComparison.OrdinalIgnoreCase))
                ?? buses.FirstOrDefault();

            if (selectedBus is null)
            {
                Console.Error.WriteLine(
                    "No OMSI .bus vehicle was found.");
                return 4;
            }
        }

        var entryPointGroups =
            MapEntryPointDiscovery.Discover(
                selectedMap);

        var selectedEntryPointGroup =
            entryPointGroups.FirstOrDefault(
                point =>
                    string.Equals(
                        point.Name,
                        spawnName,
                        StringComparison.OrdinalIgnoreCase))
            ?? entryPointGroups.FirstOrDefault();

        var selectedEntryPoint =
            selectedEntryPointGroup
                ?.Alternatives
                .FirstOrDefault();

        if (selectedEntryPoint is null)
        {
            Console.Error.WriteLine(
                "No OMSI bus entry point was found in the selected map.");
            return 5;
        }

        ApplicationConfiguration.Initialize();

        using var context =
            new RuntimeApplicationContext(
                contentRoot,
                selectedMap,
                selectedBus,
                selectedEntryPoint,
                externalLoading,
                repaintName,
                repaintCtiRelativePath,
                hofPath);

        Application.Run(context);
        return 0;
    }

    private static int RunD3D12Smoke()
    {
        try
        {
            ApplicationConfiguration.Initialize();

            using var form =
                new Form
                {
                    Text =
                        "OMSI Compatible Runtime - D3D12 Smoke",
                    ClientSize =
                        new Size(
                            320,
                            180),
                    StartPosition =
                        FormStartPosition.Manual,
                    Location =
                        new Point(
                            -32000,
                            -32000),
                    ShowInTaskbar =
                        false
                };

            form.CreateControl();

            using var graphics =
                D3D12PresentationContext.Create(
                    form.Handle,
                    form.ClientSize.Width,
                    form.ClientSize.Height);

            graphics.ClearAndPresent(
                0.04f,
                0.08f,
                0.12f,
                vsync:
                    false);

            graphics.ClearAndPresent(
                0.08f,
                0.12f,
                0.16f,
                vsync:
                    false);

            Console.WriteLine(
                "[d3d12-smoke] success");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[d3d12-smoke] failed: {exception}");

            return 10;
        }
    }

    private static string? GetOption(
        IReadOnlyList<string> args,
        string name)
    {
        for (var index = 0;
             index < args.Count - 1;
             index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(
        IEnumerable<string> args,
        string name)
    {
        return args.Any(
            value =>
                string.Equals(
                    value,
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }
}
