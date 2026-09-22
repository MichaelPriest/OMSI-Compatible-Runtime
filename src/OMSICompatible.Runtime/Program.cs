using OmsiCompat.Core;
using OmsiCompat.Map;

return RuntimeApp.Run(args);

internal static class RuntimeApp
{
    public static int Run(string[] args)
    {
        Console.WriteLine("OMSI Compatible Runtime x64");
        Console.WriteLine("Pre-alpha World Runtime bootstrap");
        Console.WriteLine();

        var contentPath = GetOption(args, "--content");
        var mapName = GetOption(args, "--map");

        if (!OmsiContentRoot.TryCreate(contentPath, out var contentRoot, out var contentError) || contentRoot is null)
        {
            Console.Error.WriteLine(contentError);
            Console.Error.WriteLine("Usage: OMSICompatible.Runtime --content <path> [--map <folder>]");
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

        var summary = GlobalConfigProbe.ReadSummary(selectedMap);

        Console.WriteLine();
        Console.WriteLine($"Selected map: {selectedMap.FolderName}");
        Console.WriteLine($"global.cfg: {summary.Path}");
        Console.WriteLine($"Size: {summary.Bytes:N0} bytes");
        Console.WriteLine($"Lines: {summary.LineCount:N0}");
        Console.WriteLine($"Section markers: {summary.SectionMarkerCount:N0}");
        Console.WriteLine();
        Console.WriteLine("World renderer is not enabled in this bootstrap yet.");

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
}
