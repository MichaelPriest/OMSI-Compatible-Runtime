using System.Globalization;
using OmsiCompat.Map;

namespace OmsiCompat.Vehicles;

public sealed record OmsiDriverPosition(
    double X,
    double Y,
    double Z,
    double SeatHeight,
    double RotationDegrees);

public static class OmsiDriverPositionReader
{
    public static OmsiDriverPosition? ReadFile(string? passengerCabinPath)
    {
        if (string.IsNullOrWhiteSpace(passengerCabinPath) ||
            !File.Exists(passengerCabinPath))
        {
            return null;
        }

        var document = OmsiSectionDocument.ParseFile(passengerCabinPath);
        var section = document.Sections.FirstOrDefault(
            item => item.Name.Equals("drivpos", StringComparison.OrdinalIgnoreCase));

        if (section is null)
        {
            return null;
        }

        var values = section.Lines
            .Select(static line => line.Value.Trim())
            .Where(static value => value.Length > 0 && !value.StartsWith('#'))
            .Take(5)
            .ToArray();

        if (values.Length < 5 ||
            !Try(values[0], out var x) ||
            !Try(values[1], out var y) ||
            !Try(values[2], out var z) ||
            !Try(values[3], out var seat) ||
            !Try(values[4], out var rotation))
        {
            return null;
        }

        return new OmsiDriverPosition(x, y, z, seat, rotation);
    }

    private static bool Try(string value, out double result) =>
        double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        double.IsFinite(result);
}
