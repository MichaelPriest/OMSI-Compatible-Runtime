using OmsiCompat.Core;
using OmsiCompat.Map;
using OmsiCompat.Vehicles;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
                BusDiscovery.Discover(
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
                repaintCtiRelativePath);

        Application.Run(context);
        return 0;
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
