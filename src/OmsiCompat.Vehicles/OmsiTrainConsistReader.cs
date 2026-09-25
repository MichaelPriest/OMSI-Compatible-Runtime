namespace OmsiCompat.Vehicles;

public sealed record OmsiTrainConsistVehicle(
    string DeclaredPath,
    string? ResolvedPath,
    bool Reverse)
{
    public bool Exists =>
        ResolvedPath is not null;
}

public sealed record OmsiTrainConsist(
    string SourcePath,
    IReadOnlyList<OmsiTrainConsistVehicle> Vehicles)
{
    public bool IsEmpty =>
        Vehicles.Count == 0;
}

public static class OmsiTrainConsistReader
{
    public static OmsiTrainConsist ReadFile(
        string contentRoot,
        string trainFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            trainFilePath);

        var fullRoot =
            EnsureTrailingSeparator(
                Path.GetFullPath(
                    contentRoot));

        var fullTrainPath =
            Path.GetFullPath(
                trainFilePath);

        if (!File.Exists(
                fullTrainPath))
        {
            throw new FileNotFoundException(
                "OMSI train consist file was not found.",
                fullTrainPath);
        }

        var dataLines =
            File.ReadAllLines(
                    fullTrainPath)
                .Select(
                    Clean)
                .Where(
                    static line =>
                        line.Length >
                        0)
                .ToArray();

        var vehicles =
            new List<OmsiTrainConsistVehicle>();

        for (var index = 0;
             index <
                 dataLines.Length;)
        {
            var declaredPath =
                NormalizePath(
                    dataLines[index]);

            if (!Path.GetExtension(
                    declaredPath)
                .Equals(
                    ".ovh",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Invalid OMSI .zug vehicle entry '{dataLines[index]}' at data line {index + 1}. Expected an .ovh path.");
            }

            if (index + 1 >=
                dataLines.Length)
            {
                throw new InvalidDataException(
                    $"Missing OMSI .zug orientation for '{dataLines[index]}'.");
            }

            var orientation =
                dataLines[index + 1];

            var reverse =
                orientation switch
                {
                    "0" => false,
                    "1" => true,
                    _ => throw new InvalidDataException(
                        $"Invalid OMSI .zug orientation '{orientation}' for '{dataLines[index]}'. Expected 0 or 1.")
                };

            vehicles.Add(
                new OmsiTrainConsistVehicle(
                    declaredPath,
                    ResolveRootRelativeFile(
                        fullRoot,
                        declaredPath),
                    reverse));

            index +=
                2;
        }

        return new OmsiTrainConsist(
            fullTrainPath,
            vehicles);
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
        string fullRoot,
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
