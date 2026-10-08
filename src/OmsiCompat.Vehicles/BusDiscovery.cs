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
        if (string.IsNullOrWhiteSpace(bus.ModelConfigPath) ||
            bus.DriverCameras.Count == 0)
        {
            return false;
        }

        // Many AI-specific .bus variants copy a driver's cockpit camera
        // from their parent .bus. A camera alone therefore does not make
        // an AI-only file a playable variant. Keep these files discoverable
        // through Discover() for traffic and schedule simulation.
        var name =
            Path.GetFileNameWithoutExtension(bus.FilePath);

        if (name.StartsWith("AI_", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AI-", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_AI", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-AI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("_AI_", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-AI-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var vehicleRelativePath =
            bus.RelativePath.Replace('\\', '/');
        var parts =
            vehicleRelativePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        return !parts.Any(
            static part =>
                part.Equals("AI", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("AI_Vehicles", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("AI-Fahrzeuge", StringComparison.OrdinalIgnoreCase));
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
