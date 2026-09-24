using System.Globalization;
using System.Text.RegularExpressions;
using OmsiCompat.Core;

namespace OmsiCompat.Map;

public sealed record OmsiAiFileReference(
    string DeclaredPath,
    string? ResolvedPath)
{
    public bool Exists =>
        ResolvedPath is not null;
}

public sealed record OmsiAiVehicleDefinition(
    string GroupName,
    string DeclaredPath,
    string? ResolvedPath,
    double Weight)
{
    public bool Exists =>
        ResolvedPath is not null;
}

public sealed record OmsiMapAiCatalog(
    IReadOnlyList<OmsiAiVehicleDefinition> MovingVehicles,
    IReadOnlyList<OmsiAiFileReference> Humans,
    IReadOnlyList<OmsiAiFileReference> Drivers,
    IReadOnlyList<OmsiAiFileReference> ParkedVehicles)
{
    public static OmsiMapAiCatalog Empty { get; } =
        new(
            Array.Empty<OmsiAiVehicleDefinition>(),
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>(),
            Array.Empty<OmsiAiFileReference>());
}

public static partial class OmsiMapAiCatalogReader
{
    [GeneratedRegex(
        @"^(?<path>.+?.(?:bus|ovh|zug))(?:s+(?<weight>[-+]?[0-9]+(?:[.,][0-9]+)?))?s*$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant)]
    private static partial Regex VehicleLineRegex();

    [GeneratedRegex(
        @"^(?<path>.+?.hum)s*$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant)]
    private static partial Regex HumanLineRegex();

    [GeneratedRegex(
        @"^(?<path>.+?.sco)(?:s+.*)?$",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant)]
    private static partial Regex SceneryLineRegex();

    public static OmsiMapAiCatalog Read(
        OmsiContentRoot contentRoot,
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(
            contentRoot);
        ArgumentNullException.ThrowIfNull(
            map);

        return new OmsiMapAiCatalog(
            ReadAiVehicles(
                contentRoot.RootPath,
                Path.Combine(
                    map.DirectoryPath,
                    "ailists.cfg")),
            ReadSimpleList(
                contentRoot.RootPath,
                Path.Combine(
                    map.DirectoryPath,
                    "humans.txt"),
                HumanLineRegex()),
            ReadSimpleList(
                contentRoot.RootPath,
                Path.Combine(
                    map.DirectoryPath,
                    "drivers.txt"),
                HumanLineRegex()),
            ReadSimpleList(
                contentRoot.RootPath,
                Path.Combine(
                    map.DirectoryPath,
                    "parklist_p.txt"),
                SceneryLineRegex()));
    }

    private static IReadOnlyList<OmsiAiVehicleDefinition>
        ReadAiVehicles(
            string root,
            string path)
    {
        if (!File.Exists(
                path))
        {
            return Array.Empty<
                OmsiAiVehicleDefinition>();
        }

        string[] lines;

        try
        {
            lines =
                File.ReadAllLines(
                    path);
        }
        catch
        {
            return Array.Empty<
                OmsiAiVehicleDefinition>();
        }

        var result =
            new List<
                OmsiAiVehicleDefinition>();

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var group =
            "Legacy";

        for (var index = 0;
             index <
                 lines.Length;
             index++)
        {
            var line =
                Clean(
                    lines[index]);

            if (line.Equals(
                    "[aigroup_2]",
                    StringComparison.OrdinalIgnoreCase) ||
                line.Equals(
                    "[aigroup_depot_typgroup_2]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var groupName =
                    NextNonEmptyDataLine(
                        lines,
                        index + 1);

                if (!string.IsNullOrWhiteSpace(
                        groupName))
                {
                    group =
                        groupName;
                }

                continue;
            }

            var match =
                VehicleLineRegex()
                    .Match(
                        line);

            if (!match.Success)
            {
                continue;
            }

            var declared =
                NormalizePath(
                    match.Groups["path"]
                        .Value);

            if (declared.Length ==
                    0 ||
                !seen.Add(
                    group +
                    "|" +
                    declared))
            {
                continue;
            }

            var weight =
                1.0;

            if (match.Groups["weight"]
                    .Success &&
                double.TryParse(
                    match.Groups["weight"]
                        .Value
                        .Replace(
                            ',',
                            '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed) &&
                double.IsFinite(
                    parsed) &&
                parsed >
                    0.0)
            {
                weight =
                    parsed;
            }

            result.Add(
                new OmsiAiVehicleDefinition(
                    group,
                    declared,
                    ResolveRootRelativeFile(
                        root,
                        declared),
                    weight));
        }

        return result;
    }

    private static IReadOnlyList<OmsiAiFileReference>
        ReadSimpleList(
            string root,
            string path,
            Regex matcher)
    {
        if (!File.Exists(
                path))
        {
            return Array.Empty<
                OmsiAiFileReference>();
        }

        string[] lines;

        try
        {
            lines =
                File.ReadAllLines(
                    path);
        }
        catch
        {
            return Array.Empty<
                OmsiAiFileReference>();
        }

        var result =
            new List<
                OmsiAiFileReference>();

        foreach (var raw in
                 lines)
        {
            var line =
                Clean(
                    raw);

            var match =
                matcher.Match(
                    line);

            if (!match.Success)
            {
                continue;
            }

            var declared =
                NormalizePath(
                    match.Groups["path"]
                        .Value);

            if (declared.Length ==
                0)
            {
                continue;
            }

            result.Add(
                new OmsiAiFileReference(
                    declared,
                    ResolveRootRelativeFile(
                        root,
                        declared)));
        }

        return result;
    }

    private static string? NextNonEmptyDataLine(
        IReadOnlyList<string> lines,
        int start)
    {
        for (var index =
                 start;
             index <
                 lines.Count;
             index++)
        {
            var value =
                Clean(
                    lines[index]);

            if (value.Length ==
                0)
            {
                continue;
            }

            if (value.StartsWith(
                    '[') &&
                value.EndsWith(
                    ']'))
            {
                return null;
            }

            return value;
        }

        return null;
    }

    private static string Clean(
        string value)
    {
        var trimmed =
            value
                .Trim()
                .Trim('"');

        if (trimmed.Length ==
                0 ||
            trimmed.StartsWith(
                '#') ||
            trimmed.StartsWith(
                "//",
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                "----------------",
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return trimmed;
    }

    private static string NormalizePath(
        string value) =>
        value
            .Trim()
            .Trim('"')
            .Replace(
                '/',
                Path.DirectorySeparatorChar)
            .Replace(
                '\\',
                Path.DirectorySeparatorChar);

    private static string? ResolveRootRelativeFile(
        string root,
        string declaredPath)
    {
        if (string.IsNullOrWhiteSpace(
                declaredPath) ||
            Path.IsPathRooted(
                declaredPath))
        {
            return null;
        }

        try
        {
            var fullRoot =
                EnsureTrailingSeparator(
                    Path.GetFullPath(
                        root));

            var fullPath =
                Path.GetFullPath(
                    Path.Combine(
                        fullRoot,
                        declaredPath));

            return fullPath.StartsWith(
                       fullRoot,
                       StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(
                       fullPath)
                ? fullPath
                : null;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    private static string EnsureTrailingSeparator(
        string path) =>
        path.EndsWith(
            Path.DirectorySeparatorChar) ||
        path.EndsWith(
            Path.AltDirectorySeparatorChar)
            ? path
            : path +
              Path.DirectorySeparatorChar;
}
