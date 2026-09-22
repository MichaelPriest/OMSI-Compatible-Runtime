using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public static class OmsiBusReader
{
    public static OmsiBusInfo ReadFile(
        string omsiRoot,
        string busFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(omsiRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(busFilePath);

        var fullPath = Path.GetFullPath(busFilePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("Bus file has no parent directory.");

        var document = OmsiSectionDocument.ParseFile(fullPath);
        var friendly = Values(document, "friendlyname").Take(3).ToArray();

        var displayName = friendly.Length switch
        {
            >= 2 => $"{friendly[0]} — {friendly[1]}",
            1 => friendly[0],
            _ => Path.GetFileNameWithoutExtension(fullPath)
        };

        var root = Path.GetFullPath(omsiRoot);
        var relative = Path.GetRelativePath(root, fullPath);

        return new OmsiBusInfo(
            displayName,
            fullPath,
            relative,
            directory,
            friendly,
            ResolveRelative(directory, First(document, "model")),
            ResolveRelative(directory, First(document, "passengercabin")),
            ResolveRelative(directory, First(document, "paths")),
            ResolveRelative(directory, First(document, "sound")));
    }

    private static string? First(
        OmsiSectionDocument document,
        string name) =>
        Values(document, name).FirstOrDefault();

    private static IEnumerable<string> Values(
        OmsiSectionDocument document,
        string name)
    {
        var section = document.Sections.FirstOrDefault(
            item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        return section is null
            ? Array.Empty<string>()
            : section.Lines
                .Select(static line => line.Value.Trim().Trim('"'))
                .Where(static value =>
                    value.Length > 0 &&
                    !value.StartsWith('#') &&
                    !value.StartsWith("//", StringComparison.Ordinal));
    }

    private static string? ResolveRelative(
        string baseDirectory,
        string? declaredPath)
    {
        if (string.IsNullOrWhiteSpace(declaredPath))
        {
            return null;
        }

        var normalized = declaredPath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        if (Path.IsPathRooted(normalized))
        {
            return null;
        }

        try
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}
