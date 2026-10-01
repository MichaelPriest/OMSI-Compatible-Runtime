namespace OmsiCompat.Plugins;

public sealed record OmsiPluginDefinition(
    string OplPath,
    string DllPath,
    IReadOnlyList<string> Variables,
    IReadOnlyList<string> StringVariables,
    IReadOnlyList<string> SystemVariables,
    IReadOnlyList<string> Triggers);

public static class OmsiPluginDiscovery
{
    public static IReadOnlyList<OmsiPluginDefinition> Discover(
        string omsiRoot)
    {
        if (string.IsNullOrWhiteSpace(
                omsiRoot))
        {
            return Array.Empty<OmsiPluginDefinition>();
        }

        var pluginsDirectory =
            Path.Combine(
                omsiRoot,
                "plugins");

        if (!Directory.Exists(
                pluginsDirectory))
        {
            return Array.Empty<OmsiPluginDefinition>();
        }

        return Directory
            .EnumerateFiles(
                pluginsDirectory,
                "*.opl",
                SearchOption.AllDirectories)
            .OrderBy(
                static path =>
                    path,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                path =>
                    TryRead(
                        path,
                        pluginsDirectory))
            .Where(
                static item =>
                    item is not null)
            .Select(
                static item =>
                    item!)
            .ToArray();
    }

    public static OmsiPluginDefinition? TryRead(
        string oplPath,
        string pluginsDirectory)
    {
        try
        {
            if (!File.Exists(
                    oplPath))
            {
                return null;
            }

            var lines =
                File.ReadAllLines(
                    oplPath);

            string? dll =
                null;

            var variables =
                new List<string>();
            var stringVariables =
                new List<string>();
            var systemVariables =
                new List<string>();
            var triggers =
                new List<string>();

            for (var index = 0;
                 index <
                     lines.Length;
                 index++)
            {
                var line =
                    lines[index]
                        .Trim();

                if (line.Length ==
                    0)
                {
                    continue;
                }

                if (line.Equals(
                        "[dll]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (++index <
                        lines.Length)
                    {
                        dll =
                            lines[index]
                                .Trim();
                    }

                    continue;
                }

                List<string>? destination =
                    line.ToLowerInvariant() switch
                    {
                        "[varlist]" =>
                            variables,
                        "[stringvarlist]" =>
                            stringVariables,
                        "[systemvarlist]" =>
                            systemVariables,
                        "[triggers]" =>
                            triggers,
                        _ =>
                            null
                    };

                if (destination is null ||
                    ++index >=
                        lines.Length)
                {
                    continue;
                }

                if (!int.TryParse(
                        lines[index]
                            .Trim(),
                        out var count) ||
                    count <=
                        0)
                {
                    continue;
                }

                for (var item = 0;
                     item <
                         count &&
                     index +
                         1 <
                         lines.Length;
                     item++)
                {
                    var value =
                        lines[++index]
                            .Trim();

                    if (value.Length >
                        0)
                    {
                        destination.Add(
                            value);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(
                    dll))
            {
                return null;
            }

            var resolvedDll =
                ResolveCaseInsensitivePath(
                    pluginsDirectory,
                    dll);

            return new OmsiPluginDefinition(
                oplPath,
                resolvedDll ??
                    Path.Combine(
                        pluginsDirectory,
                        dll.Replace(
                            '\\',
                            Path.DirectorySeparatorChar)
                           .Replace(
                            '/',
                            Path.DirectorySeparatorChar)),
                variables,
                stringVariables,
                systemVariables,
                triggers);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveCaseInsensitivePath(
        string baseDirectory,
        string relativePath)
    {
        var current =
            baseDirectory;

        foreach (var part in
                 relativePath
                     .Split(
                         ['\\', '/'],
                         StringSplitOptions.RemoveEmptyEntries))
        {
            var direct =
                Path.Combine(
                    current,
                    part);

            if (File.Exists(
                    direct) ||
                Directory.Exists(
                    direct))
            {
                current =
                    direct;
                continue;
            }

            if (!Directory.Exists(
                    current))
            {
                return null;
            }

            var match =
                Directory
                    .EnumerateFileSystemEntries(
                        current)
                    .FirstOrDefault(
                        candidate =>
                            string.Equals(
                                Path.GetFileName(
                                    candidate),
                                part,
                                StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return null;
            }

            current =
                match;
        }

        return current;
    }
}
