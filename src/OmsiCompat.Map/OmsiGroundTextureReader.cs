using System.Globalization;

namespace OmsiCompat.Map;

public static class OmsiGroundTextureReader
{
    public static IReadOnlyList<OmsiGroundTexture> ReadFile(
        string globalConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            globalConfigPath);

        var document =
            OmsiSectionDocument.ParseFile(
                globalConfigPath);

        var textures =
            new List<OmsiGroundTexture>();

        foreach (var section in
                 document.Sections.Where(
                     static item =>
                         item.Name.Equals(
                             "groundtex",
                             StringComparison.OrdinalIgnoreCase)))
        {
            var values =
                section.Lines
                    .Select(
                        static line =>
                            line.Value.Trim())
                    .Where(
                        static value =>
                            value.Length > 0 &&
                            !value.StartsWith('#'))
                    .Take(5)
                    .ToArray();

            if (values.Length < 5 ||
                string.IsNullOrWhiteSpace(values[0]) ||
                string.IsNullOrWhiteSpace(values[1]) ||
                !int.TryParse(
                    values[2],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var resolutionCode) ||
                !double.TryParse(
                    values[3],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var mainRepeating) ||
                !double.IsFinite(mainRepeating) ||
                mainRepeating <= 0 ||
                !double.TryParse(
                    values[4],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var detailRepeating) ||
                !double.IsFinite(detailRepeating) ||
                detailRepeating <= 0)
            {
                continue;
            }

            textures.Add(
                new OmsiGroundTexture(
                    values[0],
                    values[1],
                    resolutionCode,
                    mainRepeating,
                    detailRepeating));
        }

        return textures;
    }
}
