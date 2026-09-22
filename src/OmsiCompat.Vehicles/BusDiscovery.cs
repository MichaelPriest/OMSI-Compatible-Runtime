using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

public static class BusDiscovery
{
    public static IReadOnlyList<OmsiBusInfo> Discover(
        OmsiContentRoot contentRoot)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);

        var root = Path.Combine(contentRoot.RootPath, "Vehicles");
        if (!Directory.Exists(root))
        {
            return Array.Empty<OmsiBusInfo>();
        }

        var buses = new List<OmsiBusInfo>();

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*.bus",
                     SearchOption.AllDirectories))
        {
            try
            {
                buses.Add(OmsiBusReader.ReadFile(contentRoot.RootPath, path));
            }
            catch
            {
                // One malformed add-on must not hide the remaining fleet.
            }
        }

        return buses
            .OrderBy(static bus => bus.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static bus => bus.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
