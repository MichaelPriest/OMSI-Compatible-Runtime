using OmsiCompat.Core;
using OmsiCompat.Map;

Console.WriteLine("OMSI Compatible Launcher x64");
Console.WriteLine("Pre-alpha bootstrap");
Console.WriteLine();

var explicitContent = GetOption(args, "--content");
var contentRoot = ContentLocator.FindFirstValid(explicitContent, out var checkedPaths);

if (contentRoot is null)
{
    Console.Error.WriteLine("No OMSI-compatible content directory was found.");
    Console.Error.WriteLine("Pass one explicitly with: --content <path>");
    Console.Error.WriteLine();

    if (checkedPaths.Count > 0)
    {
        Console.Error.WriteLine("Checked:");
        foreach (var path in checkedPaths)
        {
            Console.Error.WriteLine($"  - {path}");
        }
    }

    return 2;
}

Console.WriteLine($"Content root: {contentRoot.RootPath}");
Console.WriteLine();

var maps = MapDiscovery.Discover(contentRoot);
Console.WriteLine($"Maps: {maps.Count}");

foreach (var map in maps)
{
    Console.WriteLine($"  - {map.FolderName}");
}

Console.WriteLine();
Console.WriteLine("Next milestone: graphical launcher + D3D11 World Runtime.");
return 0;

static string? GetOption(IReadOnlyList<string> args, string name)
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
