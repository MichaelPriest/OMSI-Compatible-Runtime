using System.Globalization;

namespace OmsiCompat.Map;

public sealed record OmsiMapHoliday(
    int DateCode,
    string Name);

public sealed record OmsiMapHolidayRange(
    int StartDateCode,
    int EndDateCode,
    string Name);

public sealed record OmsiMapCalendar(
    IReadOnlyList<OmsiMapHoliday> PublicHolidays,
    IReadOnlyList<OmsiMapHolidayRange> SchoolHolidayRanges)
{
    public static OmsiMapCalendar Empty { get; } =
        new(
            Array.Empty<OmsiMapHoliday>(),
            Array.Empty<OmsiMapHolidayRange>());

    public bool IsPublicHoliday(
        int dateCode) =>
        PublicHolidays.Any(
            holiday =>
                holiday.DateCode ==
                dateCode);

    public bool IsSchoolHoliday(
        int dateCode) =>
        SchoolHolidayRanges.Any(
            range =>
                dateCode >=
                    range.StartDateCode &&
                dateCode <=
                    range.EndDateCode);
}

public readonly record struct OmsiTimetableDayBits(
    int DayBit,
    int SchoolBit)
{
    public bool Allows(
        int mask) =>
        (mask &
         DayBit) !=
            0 &&
        (mask &
         SchoolBit) !=
            0;
}

public static class OmsiTimetableDayMask
{
    public const int AllDays =
        1023;

    public static int Parse(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return AllDays;
        }

        return int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
            ? parsed
            : AllDays;
    }

    public static OmsiTimetableDayBits Resolve(
        DateOnly date,
        OmsiMapCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(
            calendar);

        var dateCode =
            date.Year *
                10000 +
            date.Month *
                100 +
            date.Day;

        var weekdayIndex =
            date.DayOfWeek ==
                DayOfWeek.Sunday
                ? 6
                : (int)date.DayOfWeek -
                  1;

        var dayBit =
            calendar.IsPublicHoliday(
                dateCode)
                ? 1 <<
                  7
                : 1 <<
                  weekdayIndex;

        var schoolBit =
            calendar.IsSchoolHoliday(
                dateCode)
                ? 1 <<
                  8
                : 1 <<
                  9;

        return new OmsiTimetableDayBits(
            dayBit,
            schoolBit);
    }
}

public static class OmsiMapCalendarReader
{
    public static OmsiMapCalendar Read(
        OmsiMapInfo map)
    {
        ArgumentNullException.ThrowIfNull(
            map);

        var path =
            ResolveCalendarPath(
                map.DirectoryPath);

        if (path is null)
        {
            return OmsiMapCalendar.Empty;
        }

        OmsiSectionDocument document;

        try
        {
            document =
                OmsiSectionDocument.ParseFile(
                    path);
        }
        catch
        {
            return OmsiMapCalendar.Empty;
        }

        var holidays =
            new List<OmsiMapHoliday>();
        var ranges =
            new List<OmsiMapHolidayRange>();

        foreach (var section in
                 document.Sections)
        {
            var values =
                section.Lines
                    .Select(
                        static line =>
                            line.Value.Trim()
                                .Trim('"'))
                    .Where(
                        static value =>
                            value.Length >
                                0 &&
                            !value.StartsWith(
                                '#') &&
                            !value.StartsWith(
                                "//",
                                StringComparison.Ordinal))
                    .ToArray();

            if (section.Name.Equals(
                    "holiday",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (values.Length >=
                        1 &&
                    int.TryParse(
                        values[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var date))
                {
                    holidays.Add(
                        new OmsiMapHoliday(
                            date,
                            values.Length >
                                    1
                                ? values[1]
                                : string.Empty));
                }

                continue;
            }

            if (!section.Name.Equals(
                    "holidays",
                    StringComparison.OrdinalIgnoreCase) ||
                values.Length <
                    2 ||
                !int.TryParse(
                    values[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var start) ||
                !int.TryParse(
                    values[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var end))
            {
                continue;
            }

            ranges.Add(
                new OmsiMapHolidayRange(
                    start,
                    end,
                    values.Length >
                            2
                        ? values[2]
                        : string.Empty));
        }

        return new OmsiMapCalendar(
            holidays.ToArray(),
            ranges.ToArray());
    }

    private static string? ResolveCalendarPath(
        string mapDirectory)
    {
        var standard =
            Path.Combine(
                mapDirectory,
                "Holidays.txt");

        if (File.Exists(
                standard))
        {
            return standard;
        }

        try
        {
            return Directory
                .EnumerateFiles(
                    mapDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault(
                    path =>
                        Path.GetFileName(
                                path)
                            .Equals(
                                "Holidays.txt",
                                StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
