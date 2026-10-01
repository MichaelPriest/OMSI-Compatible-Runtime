using System.Globalization;

namespace OmsiCompat.Map;

public sealed record OmsiTimetableStop(
    int? StopId,
    string Name,
    double? IntervalSeconds,
    int? TileIndex);

public sealed record OmsiTimetableTrackEntry(
    long ObjectId,
    int PathId,
    int TileIndex,
    double PathLengthMeters);

public sealed record OmsiTimetableTrack(
    string Name,
    string FilePath,
    IReadOnlyList<OmsiTimetableTrackEntry> Entries);

public sealed record OmsiTimetableTrip(
    string Name,
    string FilePath,
    string TrackName,
    string Destination,
    string Line,
    IReadOnlyList<OmsiTimetableStop> Stops);

public sealed record OmsiTimetableTourTrip(
    string TripName,
    int ProfileIndex,
    double DepartureMinutes);

public sealed record OmsiTimetableTour(
    string Number,
    string AiGroup,
    string Extra,
    IReadOnlyList<OmsiTimetableTourTrip> Trips);

public sealed record OmsiTimetableLine(
    string Name,
    string FilePath,
    bool UserAllowed,
    int Priority,
    IReadOnlyList<OmsiTimetableTour> Tours);

public sealed record OmsiTimetableCatalog(
    IReadOnlyList<OmsiTimetableTrip> Trips,
    IReadOnlyDictionary<string, OmsiTimetableTrack> Tracks,
    IReadOnlyList<OmsiTimetableLine>? LineDefinitions = null)
{
    public IReadOnlyList<OmsiTimetableLine> Lines { get; } =
        LineDefinitions ??
        Array.Empty<OmsiTimetableLine>();

    public static OmsiTimetableCatalog Empty { get; } =
        new(
            Array.Empty<OmsiTimetableTrip>(),
            new Dictionary<string, OmsiTimetableTrack>(
                StringComparer.OrdinalIgnoreCase),
            Array.Empty<OmsiTimetableLine>());
}

public static class OmsiTimetableCatalogReader
{
    public static OmsiTimetableCatalog Read(
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(
            map);

        var directories =
            GetTimetableDirectories(
                map.DirectoryPath);

        if (directories.Count == 0)
        {
            return OmsiTimetableCatalog.Empty;
        }

        var trips =
            new List<OmsiTimetableTrip>();
        var tracks =
            new Dictionary<string, OmsiTimetableTrack>(
                StringComparer.OrdinalIgnoreCase);

        var lines =
            new Dictionary<string, OmsiTimetableLine>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var directory in
                 directories)
        {
            var stopNames =
                ReadBusStopNames(
                    directory,
                    map.DirectoryPath);

            foreach (var trackPath in
                     EnumerateFilesSafe(
                         directory,
                         "*.ttr"))
            {
                var track =
                    ReadTrack(
                        trackPath);

                if (track is null ||
                    track.Entries.Count == 0)
                {
                    continue;
                }

                tracks[
                    track.Name] =
                    track;
            }

            foreach (var linePath in
                     EnumerateFilesSafe(
                         directory,
                         "*.ttl"))
            {
                var line =
                    ReadLine(
                        linePath);

                if (line is not null)
                {
                    lines[
                        line.Name] =
                        line;
                }
            }

            foreach (var tripPath in
                     EnumerateFilesSafe(
                         directory,
                         "*.ttp"))
            {
                var trip =
                    ReadTrip(
                        tripPath,
                        stopNames);

                if (trip is not null)
                {
                    trips.Add(
                        trip);
                }
            }
        }

        return new OmsiTimetableCatalog(
            trips
                .OrderBy(
                    static trip =>
                        trip.Line,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    static trip =>
                        trip.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            tracks,
            lines.Values
                .OrderBy(
                    static line =>
                        line.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static OmsiTimetableTrip? ReadTrip(
        string path,
        IReadOnlyDictionary<int, string> stopNames)
    {
        string[] lines;

        try
        {
            lines =
                File.ReadAllLines(
                    path);
        }
        catch
        {
            return null;
        }

        string trackName =
            string.Empty;
        string destination =
            string.Empty;
        string line =
            string.Empty;

        var stops =
            new List<OmsiTimetableStop>();

        for (var index = 0;
             index <
                 lines.Length;
             index++)
        {
            var token =
                Clean(
                    lines[index]);

            if (token.Equals(
                    "[trip]",
                    StringComparison.OrdinalIgnoreCase) &&
                index + 3 <
                    lines.Length)
            {
                trackName =
                    CleanValue(
                        lines[index + 1]);
                destination =
                    CleanValue(
                        lines[index + 2]);
                line =
                    CleanValue(
                        lines[index + 3]);

                continue;
            }

            if (token.Equals(
                    "[station]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var stopId =
                    ParseInt(
                        lines,
                        index + 1);
                var interval =
                    ParseDouble(
                        lines,
                        index + 2);
                var name =
                    index + 3 <
                            lines.Length
                        ? CleanValue(
                            lines[index + 3])
                        : string.Empty;
                var tileIndex =
                    ParseInt(
                        lines,
                        index + 4);

                if (string.IsNullOrWhiteSpace(
                        name) &&
                    stopId.HasValue &&
                    stopNames.TryGetValue(
                        stopId.Value,
                        out var resolvedName))
                {
                    name =
                        resolvedName;
                }

                AddStop(
                    stops,
                    stopId,
                    name,
                    interval,
                    tileIndex);

                continue;
            }

            if (token.Equals(
                    "[station_typ2]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var stopId =
                    ParseInt(
                        lines,
                        index + 1);

                if (stopId.HasValue &&
                    stopNames.TryGetValue(
                        stopId.Value,
                        out var name))
                {
                    AddStop(
                        stops,
                        stopId,
                        name,
                        ParseDouble(
                            lines,
                            index + 2),
                        ParseInt(
                            lines,
                            index + 3));
                }
            }
        }

        if (string.IsNullOrWhiteSpace(
                trackName))
        {
            return null;
        }

        return new OmsiTimetableTrip(
            Path.GetFileNameWithoutExtension(
                path),
            path,
            trackName,
            destination,
            line,
            stops.ToArray());
    }

    private static OmsiTimetableLine? ReadLine(
        string path)
    {
        string[] fileLines;

        try
        {
            fileLines =
                File.ReadAllLines(
                    path);
        }
        catch
        {
            return null;
        }

        var userAllowed =
            false;
        var priority =
            0;

        var tours =
            new List<OmsiTimetableTour>();

        for (var index = 0;
             index <
                 fileLines.Length;
             index++)
        {
            var token =
                Clean(
                    fileLines[index]);

            if (token.Equals(
                    "[userallowed]",
                    StringComparison.OrdinalIgnoreCase))
            {
                userAllowed =
                    true;
                continue;
            }

            if (token.Equals(
                    "[priority]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var cursor =
                    index;

                if (TryReadNextTimetableValue(
                        fileLines,
                        ref cursor,
                        out var value) &&
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedPriority))
                {
                    priority =
                        parsedPriority;
                    index =
                        cursor;
                }

                continue;
            }

            if (token.Equals(
                    "[newtour]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var cursor =
                    index;

                if (!TryReadNextTimetableValue(
                        fileLines,
                        ref cursor,
                        out var number) ||
                    !TryReadNextTimetableValue(
                        fileLines,
                        ref cursor,
                        out var aiGroup) ||
                    !TryReadNextTimetableValue(
                        fileLines,
                        ref cursor,
                        out var extra))
                {
                    continue;
                }

                tours.Add(
                    new OmsiTimetableTour(
                        number,
                        aiGroup,
                        extra,
                        Array.Empty<
                            OmsiTimetableTourTrip>()));

                index =
                    cursor;
                continue;
            }

            if (!token.Equals(
                    "[addtrip]",
                    StringComparison.OrdinalIgnoreCase) ||
                tours.Count ==
                    0)
            {
                continue;
            }

            var tripCursor =
                index;

            if (!TryReadNextTimetableValue(
                    fileLines,
                    ref tripCursor,
                    out var tripName) ||
                !TryReadNextTimetableValue(
                    fileLines,
                    ref tripCursor,
                    out var profileText) ||
                !TryReadNextTimetableValue(
                    fileLines,
                    ref tripCursor,
                    out var departureText) ||
                !int.TryParse(
                    profileText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var profileIndex))
            {
                continue;
            }

            var departure =
                ParseFlexibleDouble(
                    departureText);

            if (!departure.HasValue)
            {
                continue;
            }

            var current =
                tours[^1];

            tours[^1] =
                current with
                {
                    Trips =
                        current.Trips
                            .Append(
                                new OmsiTimetableTourTrip(
                                    tripName,
                                    profileIndex,
                                    departure.Value))
                            .ToArray()
                };

            index =
                tripCursor;
        }

        if (tours.Count ==
                0 &&
            !userAllowed &&
            priority ==
                0)
        {
            return null;
        }

        return new OmsiTimetableLine(
            Path.GetFileNameWithoutExtension(
                path),
            path,
            userAllowed,
            priority,
            tours.ToArray());
    }

    private static bool TryReadNextTimetableValue(
        IReadOnlyList<string> lines,
        ref int cursor,
        out string value)
    {
        for (var index =
                 cursor + 1;
             index <
                 lines.Count;
             index++)
        {
            var clean =
                Clean(
                    lines[index]);

            if (clean.Length ==
                0)
            {
                continue;
            }

            if (clean.StartsWith(
                    '[') &&
                clean.EndsWith(
                    ']'))
            {
                value =
                    string.Empty;
                return false;
            }

            cursor =
                index;
            value =
                CleanValue(
                    lines[index]);
            return true;
        }

        value =
            string.Empty;
        return false;
    }

    private static OmsiTimetableTrack? ReadTrack(
        string path)
    {
        string[] lines;

        try
        {
            lines =
                File.ReadAllLines(
                    path);
        }
        catch
        {
            return null;
        }

        var entries =
            new List<OmsiTimetableTrackEntry>();

        for (var index = 0;
             index <
                 lines.Length;
             index++)
        {
            if (!Clean(
                    lines[index])
                .Equals(
                    "[track_entry]",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 5 >=
                    lines.Length ||
                !long.TryParse(
                    CleanValue(
                        lines[index + 1]),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var objectId) ||
                !int.TryParse(
                    CleanValue(
                        lines[index + 2]),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var pathId) ||
                !int.TryParse(
                    CleanValue(
                        lines[index + 3]),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var tileIndex))
            {
                continue;
            }

            var pathLength =
                ParseFlexibleDouble(
                    lines[index + 5]);

            entries.Add(
                new OmsiTimetableTrackEntry(
                    objectId,
                    pathId,
                    tileIndex,
                    pathLength.HasValue &&
                        pathLength.Value >
                            0.0
                        ? pathLength.Value
                        : 0.0));
        }

        return new OmsiTimetableTrack(
            Path.GetFileNameWithoutExtension(
                path),
            path,
            entries.ToArray());
    }

    private static void AddStop(
        List<OmsiTimetableStop> stops,
        int? stopId,
        string name,
        double? interval,
        int? tileIndex)
    {
        var normalizedName =
            name.Trim();

        if (normalizedName.Length == 0)
        {
            return;
        }

        if (stops.Count > 0)
        {
            var previous =
                stops[^1];

            if (previous.StopId.HasValue &&
                stopId.HasValue &&
                previous.StopId.Value ==
                    stopId.Value)
            {
                return;
            }

            if (previous.Name.Equals(
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        stops.Add(
            new OmsiTimetableStop(
                stopId,
                normalizedName,
                interval,
                tileIndex));
    }

    private static IReadOnlyDictionary<int, string>
        ReadBusStopNames(
            string timetableDirectory,
            string mapDirectory)
    {
        var result =
            new Dictionary<int, string>();

        var candidates =
            new[]
            {
                Path.Combine(
                    timetableDirectory,
                    "Busstops.cfg"),
                Path.Combine(
                    mapDirectory,
                    "TTData",
                    "Busstops.cfg")
            };

        foreach (var path in
                 candidates.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(
                    path))
            {
                continue;
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
                continue;
            }

            for (var index = 0;
                 index + 3 <
                     lines.Length;
                 index++)
            {
                if (!Clean(
                        lines[index])
                    .Equals(
                        "[busstop]",
                        StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(
                        CleanValue(
                            lines[index + 3]),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var id))
                {
                    continue;
                }

                var name =
                    CleanValue(
                        lines[index + 1]);

                if (name.Length >
                    0)
                {
                    result[
                        id] =
                        name;
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<string>
        GetTimetableDirectories(
            string mapDirectory)
    {
        var result =
            new List<string>();

        var standard =
            Path.Combine(
                mapDirectory,
                "TTData");

        if (Directory.Exists(
                standard))
        {
            result.Add(
                standard);
        }

        var chronoRoot =
            Path.Combine(
                mapDirectory,
                "Chrono");

        if (Directory.Exists(
                chronoRoot))
        {
            try
            {
                foreach (var chrono in
                         Directory.EnumerateDirectories(
                             chronoRoot,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var timetable =
                        Path.Combine(
                            chrono,
                            "TTData");

                    if (Directory.Exists(
                            timetable))
                    {
                        result.Add(
                            timetable);
                    }
                }
            }
            catch
            {
            }
        }

        return result
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string>
        EnumerateFilesSafe(
            string directory,
            string searchPattern)
    {
        try
        {
            return Directory
                .EnumerateFiles(
                    directory,
                    searchPattern,
                    SearchOption.TopDirectoryOnly)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int? ParseInt(
        IReadOnlyList<string> lines,
        int index)
    {
        if (index < 0 ||
            index >=
                lines.Count)
        {
            return null;
        }

        return int.TryParse(
                CleanValue(
                    lines[index]),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : null;
    }

    private static double? ParseDouble(
        IReadOnlyList<string> lines,
        int index)
    {
        if (index < 0 ||
            index >=
                lines.Count)
        {
            return null;
        }

        return ParseFlexibleDouble(
            lines[index]);
    }

    private static double? ParseFlexibleDouble(
        string value)
    {
        var clean =
            CleanValue(
                value)
                .Replace(
                    ',',
                    '.');

        return double.TryParse(
                clean,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) &&
            double.IsFinite(
                parsed)
                ? parsed
                : null;
    }

    private static string Clean(
        string value)
    {
        var cleaned =
            CleanValue(
                value);

        if (cleaned.Length == 0 ||
            cleaned.StartsWith(
                '#') ||
            cleaned.StartsWith(
                "//",
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return cleaned;
    }

    private static string CleanValue(
        string value) =>
        value
            .Trim()
            .Trim('"');
}
