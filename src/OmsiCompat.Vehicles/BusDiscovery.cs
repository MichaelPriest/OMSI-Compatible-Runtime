using OmsiCompat.Core;

namespace OmsiCompat.Vehicles;

public static class BusDiscovery
{
    public static IReadOnlyList<OmsiBusInfo> DiscoverPlayerSelectable(
        OmsiContentRoot contentRoot) =>
        Discover(
            contentRoot)
            .Where(
                IsPlayerSelectable)
            .ToArray();

    public static bool IsPlayerSelectable(
        OmsiBusInfo bus)
    {
        ArgumentNullException.ThrowIfNull(
            bus);

        // AI-only .bus variants often contain a renderable model so they
        // belong in ailists.cfg, but they do not define a driver viewpoint.
        // Keep them available to the traffic loader while excluding them
        // from Carroceria / Modelo / Skin in the player launcher.
        return !string.IsNullOrWhiteSpace(
                   bus.ModelConfigPath) &&
               bus.DriverCameras.Count >
                   0;
    }

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
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!string.Equals(
                    Path.GetExtension(path),
                    ".bus",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                buses.Add(
                    OmsiBusReader.ReadFile(
                        contentRoot.RootPath,
                        path));
            }
            catch
            {
                // One malformed .bus must not hide the remaining fleet.
            }
        }

        return buses
            .OrderBy(static bus => bus.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static bus => bus.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
