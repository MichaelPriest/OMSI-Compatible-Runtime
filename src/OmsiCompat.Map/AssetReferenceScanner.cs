namespace OmsiCompat.Map;

public sealed record OmsiAssetReference(
    string RawPath,
    string Extension,
    string SectionName,
    int LineNumber);

public static class AssetReferenceScanner
{
    private static readonly string[] KnownExtensions =
    [
        ".sco",
        ".sli",
        ".o3d",
        ".dds",
        ".bmp",
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".wav"
    ];

    public static IReadOnlyList<OmsiAssetReference> Scan(OmsiMapTileInfo tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var document = OmsiSectionDocument.ParseFile(tile.FilePath);
        var references = new List<OmsiAssetReference>();

        foreach (var section in document.Sections)
        {
            foreach (var line in section.Lines)
            {
                var candidate = ExtractAssetPath(line.Value);
                if (candidate is null)
                {
                    continue;
                }

                references.Add(new OmsiAssetReference(
                    candidate.Value.Path,
                    candidate.Value.Extension,
                    section.Name,
                    line.LineNumber));
            }
        }

        return references;
    }

    private static (string Path, string Extension)? ExtractAssetPath(string rawLine)
    {
        var value = rawLine.Trim();
        if (value.Length == 0 ||
            value.StartsWith(';') ||
            value.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var extension in KnownExtensions)
        {
            var index = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var end = index + extension.Length;
            var path = value[..end].Trim().Trim('"');

            if (path.Length == 0)
            {
                return null;
            }

            return (path.Replace('/', '\\'), extension);
        }

        return null;
    }
}
