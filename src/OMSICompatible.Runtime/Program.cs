using OmsiCompat.Core;
using OmsiCompat.Map;
using OMSICompatible.Renderer.D3D11;
using OMSICompatible.World;

namespace OMSICompatible.Runtime;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("OMSI Compatible Runtime x64");
        Console.WriteLine("Pre-alpha World Runtime");
        Console.WriteLine();

        var contentPath = GetOption(args, "--content");
        var mapName = GetOption(args, "--map");
        var headless = HasFlag(args, "--headless");

        if (!OmsiContentRoot.TryCreate(contentPath, out var contentRoot, out var contentError) || contentRoot is null)
        {
            Console.Error.WriteLine(contentError);
            Console.Error.WriteLine("Usage: OMSICompatible.Runtime --content <path> [--map <folder>] [--headless]");
            return 2;
        }

        var maps = MapDiscovery.Discover(contentRoot);

        Console.WriteLine($"Content: {contentRoot.RootPath}");
        Console.WriteLine($"Maps discovered: {maps.Count}");

        if (string.IsNullOrWhiteSpace(mapName))
        {
            foreach (var map in maps)
            {
                Console.WriteLine($"  - {map.FolderName}");
            }

            return 0;
        }

        var selectedMap = maps.FirstOrDefault(
            map => string.Equals(map.FolderName, mapName, StringComparison.OrdinalIgnoreCase));

        if (selectedMap is null)
        {
            Console.Error.WriteLine($"Map not found: {mapName}");
            return 3;
        }

        var globalSummary = GlobalConfigProbe.ReadSummary(selectedMap);
        var world = WorldLoader.Load(contentRoot, selectedMap);

        Console.WriteLine();
        Console.WriteLine($"Selected map: {world.Name}");
        Console.WriteLine($"global.cfg: {globalSummary.Path}");
        Console.WriteLine($"Tiles: {world.Tiles.Count:N0}");
        Console.WriteLine($"Objects: {world.Objects.Count:N0}");
        Console.WriteLine($"Splines: {world.Splines.Count:N0}");
        Console.WriteLine($"Placement parse issues: {world.PlacementParseIssueCount:N0}");
        Console.WriteLine($"Primary dependencies: {world.Dependencies.RequiredCount:N0}");
        Console.WriteLine($"Missing primary dependencies: {world.Dependencies.MissingCount:N0}");

        if (world.Bounds is not null)
        {
            Console.WriteLine(
                $"Tile bounds: {world.Bounds.MinimumX},{world.Bounds.MinimumY} -> " +
                $"{world.Bounds.MaximumX},{world.Bounds.MaximumY} " +
                $"({world.Bounds.WidthInTiles}x{world.Bounds.HeightInTiles})");
        }

        if (world.Dependencies.MissingCount > 0)
        {
            Console.WriteLine("Missing primary dependencies:");
            foreach (var dependency in world.Dependencies.Missing.Take(20))
            {
                Console.WriteLine($"  [{dependency.Kind}] {dependency.SourcePath}");
            }

            if (world.Dependencies.MissingCount > 20)
            {
                Console.WriteLine(
                    $"  ... and {world.Dependencies.MissingCount - 20:N0} more");
            }
        }

        if (headless)
        {
            Console.WriteLine();
            Console.WriteLine("Headless world probe complete.");
            return 0;
        }

        ApplicationConfiguration.Initialize();

        using var window = new D3D11RenderWindow(
            new RuntimeWindowInfo(
                world.Name,
                world.Tiles.Count,
                world.Objects.Count,
                world.Splines.Count,
                contentRoot.RootPath));

        Application.Run(window);
        return 0;
    }

    private static string? GetOption(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(IEnumerable<string> args, string name)
    {
        return args.Any(argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    }
}
