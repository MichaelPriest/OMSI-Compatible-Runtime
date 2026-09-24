using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public sealed record OmsiVehicleRepaint(
    string Name,
    string CtiPath,
    string RelativeCtiPath,
    IReadOnlyDictionary<string, string> TextureOverrides,
    IReadOnlyDictionary<string, double> SetVariables)
{
    public bool TryResolveTexture(
        string? declaredTexture,
        string? resolvedTexture,
        out string replacement)
    {
        replacement = string.Empty;

        foreach (var key in CandidateKeys(
                     declaredTexture,
                     resolvedTexture))
        {
            if (TextureOverrides.TryGetValue(
                    key,
                    out var value) &&
                File.Exists(value))
            {
                replacement = value;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateKeys(
        string? declaredTexture,
        string? resolvedTexture)
    {
        if (!string.IsNullOrWhiteSpace(
                declaredTexture))
        {
            yield return NormalizeKey(
                declaredTexture);

            var fileName =
                Path.GetFileName(
                    declaredTexture);

            if (!string.IsNullOrWhiteSpace(
                    fileName))
            {
                yield return NormalizeKey(
                    fileName);
            }
        }

        if (!string.IsNullOrWhiteSpace(
                resolvedTexture))
        {
            yield return NormalizeKey(
                resolvedTexture);

            var fileName =
                Path.GetFileName(
                    resolvedTexture);

            if (!string.IsNullOrWhiteSpace(
                    fileName))
            {
                yield return NormalizeKey(
                    fileName);
            }
        }
    }

    internal static string NormalizeKey(
        string value) =>
        value
            .Replace(
                '/',
                '\\')
            .Trim()
            .Trim('"')
            .ToLowerInvariant();
}

public static class OmsiVehicleRepaintCatalog
{
    public static IReadOnlyList<OmsiVehicleRepaint> Discover(
        OmsiBusInfo bus)
    {
        ArgumentNullException.ThrowIfNull(
            bus);

        if (string.IsNullOrWhiteSpace(
                bus.ModelConfigPath) ||
            !File.Exists(
                bus.ModelConfigPath) ||
            !Directory.Exists(
                bus.DirectoryPath))
        {
            return Array.Empty<
                OmsiVehicleRepaint>();
        }

        var textureSlots =
            ReadTextureSlots(
                bus.ModelConfigPath);

        if (textureSlots.Count == 0)
        {
            return Array.Empty<
                OmsiVehicleRepaint>();
        }

        var result =
            new List<OmsiVehicleRepaint>();

        IEnumerable<string> ctiFiles;

        try
        {
            ctiFiles =
                Directory
                    .EnumerateFiles(
                        bus.DirectoryPath,
                        "*.cti",
                        SearchOption.AllDirectories)
                    .Take(4096)
                    .ToArray();
        }
        catch
        {
            return Array.Empty<
                OmsiVehicleRepaint>();
        }

        foreach (var ctiPath in
                 ctiFiles)
        {
            try
            {
                ReadCti(
                    bus,
                    ctiPath,
                    textureSlots,
                    result);
            }
            catch
            {
                // Broken third-party repaint files must not prevent the
                // vehicle itself from being selectable.
            }
        }

        return result
            .GroupBy(
                static repaint =>
                    repaint.RelativeCtiPath +
                    "\n" +
                    repaint.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                static group =>
                    group.First())
            .OrderBy(
                static repaint =>
                    repaint.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                static repaint =>
                    repaint.RelativeCtiPath,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, string>
        ReadTextureSlots(
            string modelConfigPath)
    {
        var document =
            OmsiSectionDocument.ParseFile(
                modelConfigPath);

        var result =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var section in
                 document.Sections.Where(
                     static section =>
                         section.Name.Equals(
                             "CTCTexture",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var values =
                Values(section)
                    .ToArray();

            if (values.Length < 2)
            {
                continue;
            }

            var variable =
                values[0];

            var sourceTexture =
                values[1];

            if (string.IsNullOrWhiteSpace(
                    variable) ||
                string.IsNullOrWhiteSpace(
                    sourceTexture))
            {
                continue;
            }

            result[variable] =
                sourceTexture;
        }

        return result;
    }

    private static void ReadCti(
        OmsiBusInfo bus,
        string ctiPath,
        IReadOnlyDictionary<string, string> textureSlots,
        ICollection<OmsiVehicleRepaint> destination)
    {
        var document =
            OmsiSectionDocument.ParseFile(
                ctiPath);

        var builders =
            new Dictionary<string, RepaintBuilder>(
                StringComparer.OrdinalIgnoreCase);

        string? currentRepaintName =
            null;

        RepaintBuilder GetBuilder(
            string repaintName)
        {
            var normalized =
                string.IsNullOrWhiteSpace(
                    repaintName)
                    ? Path.GetFileNameWithoutExtension(
                        ctiPath)
                    : repaintName.Trim();

            if (!builders.TryGetValue(
                    normalized,
                    out var builder))
            {
                builder =
                    new RepaintBuilder(
                        bus,
                        ctiPath,
                        normalized);

                builders[
                    normalized] =
                    builder;
            }

            return builder;
        }

        foreach (var section in
                 document.Sections)
        {
            if (section.Name.Equals(
                    "item",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section)
                        .ToArray();

                if (values.Length < 3)
                {
                    continue;
                }

                currentRepaintName =
                    values[0];

                var builder =
                    GetBuilder(
                        currentRepaintName);

                for (var index = 1;
                     index + 1 < values.Length;
                     index += 2)
                {
                    ApplyTexturePair(
                        builder,
                        textureSlots,
                        values[index],
                        values[index + 1]);
                }

                continue;
            }

            if (section.Name.Equals(
                    "setvar",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section)
                        .ToArray();

                if (values.Length >= 2 &&
                    !string.IsNullOrWhiteSpace(
                        currentRepaintName) &&
                    double.TryParse(
                        values[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed) &&
                    double.IsFinite(parsed))
                {
                    GetBuilder(
                            currentRepaintName)
                        .SetVariables[
                            values[0]] =
                        parsed;
                }

                continue;
            }

            if (section.Name.Equals(
                    "texture",
                    StringComparison.OrdinalIgnoreCase) ||
                section.Name.Equals(
                    "ctctexture",
                    StringComparison.OrdinalIgnoreCase))
            {
                var values =
                    Values(section)
                        .ToArray();

                if (values.Length >= 2 &&
                    !string.IsNullOrWhiteSpace(
                        currentRepaintName))
                {
                    ApplyTexturePair(
                        GetBuilder(
                            currentRepaintName),
                        textureSlots,
                        values[0],
                        values[1]);
                }
            }
        }

        foreach (var builder in
                 builders.Values)
        {
            var repaint =
                builder.Build();

            if (repaint is not null)
            {
                destination.Add(
                    repaint);
            }
        }
    }

    private static void ApplyTexturePair(
        RepaintBuilder builder,
        IReadOnlyDictionary<string, string> textureSlots,
        string variable,
        string replacement)
    {
        if (!textureSlots.TryGetValue(
                variable,
                out var sourceTexture))
        {
            return;
        }

        var resolved =
            ResolveRepaintTexture(
                builder.Bus,
                builder.CtiPath,
                replacement);

        if (resolved is null)
        {
            return;
        }

        builder.TextureOverrides[
            OmsiVehicleRepaint.NormalizeKey(
                sourceTexture)] =
            resolved;

        var sourceFileName =
            Path.GetFileName(
                sourceTexture);

        if (!string.IsNullOrWhiteSpace(
                sourceFileName))
        {
            builder.TextureOverrides[
                OmsiVehicleRepaint.NormalizeKey(
                    sourceFileName)] =
                resolved;
        }
    }

    private static string? ResolveRepaintTexture(
        OmsiBusInfo bus,
        string ctiPath,
        string declaredPath)
    {
        var normalized =
            declaredPath
                .Trim()
                .Trim('"')
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar)
                .Replace(
                    '\\',
                    Path.DirectorySeparatorChar);

        if (normalized.Length == 0 ||
            Path.IsPathRooted(
                normalized))
        {
            return null;
        }

        var ctiDirectory =
            Path.GetDirectoryName(
                ctiPath) ??
            bus.DirectoryPath;

        string[] candidates =
        [
            Path.Combine(
                ctiDirectory,
                normalized),
            Path.Combine(
                bus.DirectoryPath,
                normalized),
            Path.Combine(
                bus.DirectoryPath,
                "Texture",
                normalized),
            Path.Combine(
                bus.DirectoryPath,
                "texture",
                normalized)
        ];

        foreach (var candidate in
                 candidates)
        {
            try
            {
                var fullPath =
                    Path.GetFullPath(
                        candidate);

                if (File.Exists(
                        fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> Values(
        OmsiSection section) =>
        section.Lines
            .Select(
                static line =>
                    line.Value
                        .Trim()
                        .Trim('"'))
            .Where(
                static value =>
                    value.Length > 0 &&
                    !value.StartsWith(
                        '#') &&
                    !value.StartsWith(
                        "//",
                        StringComparison.Ordinal));

    private sealed class RepaintBuilder
    {
        public RepaintBuilder(
            OmsiBusInfo bus,
            string ctiPath,
            string name)
        {
            Bus =
                bus;

            CtiPath =
                ctiPath;

            Name =
                string.IsNullOrWhiteSpace(
                    name)
                    ? Path.GetFileNameWithoutExtension(
                        ctiPath)
                    : name.Trim();
        }

        public OmsiBusInfo Bus { get; }

        public string CtiPath { get; }

        public string Name { get; }

        public Dictionary<string, string>
            TextureOverrides { get; } =
                new(
                    StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double>
            SetVariables { get; } =
                new(
                    StringComparer.OrdinalIgnoreCase);

        public OmsiVehicleRepaint? Build()
        {
            if (TextureOverrides.Count == 0 &&
                SetVariables.Count == 0)
            {
                return null;
            }

            return new OmsiVehicleRepaint(
                Name,
                CtiPath,
                Path.GetRelativePath(
                    Bus.DirectoryPath,
                    CtiPath),
                new Dictionary<string, string>(
                    TextureOverrides,
                    StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, double>(
                    SetVariables,
                    StringComparer.OrdinalIgnoreCase));
        }
    }
}
