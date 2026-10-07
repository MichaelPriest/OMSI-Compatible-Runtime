using System.Globalization;
using OmsiCompat.Core;

namespace OMSICompatible.World;

public sealed record WorldParkedCarPlacement(
    long ParkingObjectId,
    WorldTileCoordinate Tile,
    string AssetPath,
    WorldVector3 Position,
    double HeadingDegrees,
    double PitchDegrees,
    double BankDegrees,
    int ParkListIndex);

public static class WorldParkedCarResolver
{
    private const ulong ParkingHashMultiplier =
        0x9E3779B97F4A7C15UL;

    public static int ResolveParkListIndex(
        IReadOnlyList<string> captions)
    {
        if (captions.Count ==
                0 ||
            !int.TryParse(
                captions[0]
                    .Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var index) ||
            index <
                0)
        {
            return 0;
        }

        return index;
    }

    public static string? SelectParkedCar(
        long parkingObjectId,
        IReadOnlyList<string> carTypes)
    {
        if (carTypes.Count ==
            0)
        {
            return null;
        }

        var hash =
            unchecked(
                (ulong)parkingObjectId *
                ParkingHashMultiplier) >>
            33;

        // openOMSI/OMSI behavior: roughly one quarter of authored
        // parking spaces stays empty.
        if (hash %
            4UL ==
            0UL)
        {
            return null;
        }

        var index =
            (int)(
                (
                    hash /
                    4UL
                ) %
                (ulong)carTypes.Count);

        return carTypes[index];
    }

    public static bool IsDeparted(
        long parkingObjectId,
        IReadOnlySet<uint>? departedParkingObjectIds)
    {
        if (departedParkingObjectIds is null ||
            parkingObjectId <
                0 ||
            parkingObjectId >
                uint.MaxValue)
        {
            return false;
        }

        return departedParkingObjectIds.Contains(
            (uint)parkingObjectId);
    }

    public static IReadOnlyList<WorldParkedCarPlacement> Build(
        string mapDirectory,
        IReadOnlyList<WorldObjectPlacement> objects,
        IReadOnlyDictionary<string, WorldSceneryAsset> sceneryAssets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            mapDirectory);
        ArgumentNullException.ThrowIfNull(
            objects);
        ArgumentNullException.ThrowIfNull(
            sceneryAssets);

        var lists =
            new Dictionary<int, IReadOnlyList<string>>();

        var result =
            new List<WorldParkedCarPlacement>();

        foreach (var placement in
                 objects)
        {
            if (!sceneryAssets.TryGetValue(
                    placement.AssetPath,
                    out var asset) ||
                !asset.IsCarPark)
            {
                continue;
            }

            var listIndex =
                ResolveParkListIndex(
                    placement.ExtraValues);

            if (!lists.TryGetValue(
                    listIndex,
                    out var carTypes))
            {
                carTypes =
                    ReadParkList(
                        mapDirectory,
                        listIndex);

                lists[
                    listIndex] =
                    carTypes;
            }

            var selected =
                SelectParkedCar(
                    placement.Id,
                    carTypes);

            if (string.IsNullOrWhiteSpace(
                    selected))
            {
                continue;
            }

            result.Add(
                new WorldParkedCarPlacement(
                    placement.Id,
                    placement.Tile,
                    selected,
                    placement.Position,
                    placement.HeadingDegrees,
                    placement.PitchDegrees,
                    placement.BankDegrees,
                    listIndex));
        }

        return result;
    }

    public static IReadOnlyList<string> ReadParkList(
        string mapDirectory,
        int index)
    {
        if (string.IsNullOrWhiteSpace(
                mapDirectory) ||
            index <
                0)
        {
            return Array.Empty<string>();
        }

        var fileName =
            index ==
                0
                ? "parklist_p.txt"
                : $"parklist_p_{index}.txt";

        var path =
            Path.Combine(
                mapDirectory,
                fileName);

        if (!File.Exists(
                path))
        {
            return Array.Empty<string>();
        }

        try
        {
            return OmsiText.ReadAllLines(
                    path)
                .Select(
                    static line =>
                        line
                            .Trim()
                            .Trim('"'))
                .Where(
                    static line =>
                        !string.IsNullOrWhiteSpace(
                            line) &&
                        line.EndsWith(
                            ".sco",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }
}
