using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Splines;

public static class OmsiSplineDefinitionReader
{
    public static OmsiSplineDefinition ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return OmsiSplineDefinition.Missing;
        }

        var document = OmsiSectionDocument.ParseFile(path);
        var textures = document.Sections
            .Where(static section =>
                section.Name.Equals("texture", StringComparison.OrdinalIgnoreCase))
            .Select(static section => Data(section).FirstOrDefault()?.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

        var alphaModes = new int[textures.Length];
        var currentTexture = -1;

        foreach (var section in document.Sections)
        {
            if (section.Name.Equals("texture", StringComparison.OrdinalIgnoreCase))
            {
                currentTexture++;
                continue;
            }

            if (currentTexture < 0 ||
                currentTexture >= alphaModes.Length ||
                !section.Name.Equals("matl_alpha", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = Data(section).FirstOrDefault()?.Value;
            if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var alphaMode))
            {
                alphaModes[currentTexture] = Math.Clamp(alphaMode, 0, 2);
            }
        }

        var surfaces = new List<OmsiSplineSurface>();

        for (var index = 0; index < document.Sections.Count; index++)
        {
            var section = document.Sections[index];
            if (!section.Name.Equals("profile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var textureValue = Data(section).FirstOrDefault()?.Value;
            if (!int.TryParse(
                    textureValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var textureIndex) ||
                textureIndex < 0)
            {
                continue;
            }

            var points = new List<OmsiSplineProfilePoint>();

            for (var nextIndex = index + 1;
                 nextIndex < document.Sections.Count;
                 nextIndex++)
            {
                var next = document.Sections[nextIndex];

                if (next.Name.Equals("profile", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (!next.Name.Equals("profilepnt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var point = TryReadPoint(next);
                if (point is not null)
                {
                    points.Add(point);
                }
            }

            for (var pointIndex = 0; pointIndex + 1 < points.Count; pointIndex++)
            {
                surfaces.Add(new OmsiSplineSurface(
                    textureIndex,
                    textureIndex < textures.Length ? textures[textureIndex] : null,
                    textureIndex < alphaModes.Length ? alphaModes[textureIndex] : 0,
                    points[pointIndex],
                    points[pointIndex + 1]));
            }
        }

        var paths = new List<OmsiSplinePathDefinition>();
        foreach (var section in document.Sections.Where(static section =>
                     section.Name.Equals("path", StringComparison.OrdinalIgnoreCase)))
        {
            var values = Data(section).Take(5).ToArray();
            if (values.Length < 5 ||
                !int.TryParse(values[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type) ||
                type is < 0 or > 3 ||
                !TryDouble(values[1].Value, out var x) ||
                !TryDouble(values[2].Value, out var z) ||
                !TryDouble(values[3].Value, out var width) ||
                width < 0 ||
                !int.TryParse(values[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direction) ||
                direction is < 0 or > 2)
            {
                continue;
            }

            paths.Add(new OmsiSplinePathDefinition(type, x, z, width, direction));
        }

        return new OmsiSplineDefinition(
            true,
            textures,
            surfaces.ToArray(),
            paths.ToArray());
    }

    private static OmsiSplineProfilePoint? TryReadPoint(OmsiSection section)
    {
        var values = Data(section).Take(4).ToArray();
        if (values.Length < 4 ||
            !TryDouble(values[0].Value, out var x) ||
            !TryDouble(values[1].Value, out var z) ||
            !TryDouble(values[2].Value, out var textureX) ||
            !TryDouble(values[3].Value, out var textureScale))
        {
            return null;
        }

        return new OmsiSplineProfilePoint(x, z, textureX, textureScale);
    }

    private static IReadOnlyList<OmsiSectionLine> Data(OmsiSection section)
    {
        return section.Lines
            .Where(static line =>
            {
                var value = line.Value.Trim();
                return value.Length > 0 && !value.StartsWith('#');
            })
            .ToArray();
    }

    private static bool TryDouble(string value, out double result)
    {
        return double.TryParse(
                   value.Trim(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out result) &&
               double.IsFinite(result);
    }
}
