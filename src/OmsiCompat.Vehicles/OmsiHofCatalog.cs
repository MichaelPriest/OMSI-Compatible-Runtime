using System.Globalization;

namespace OmsiCompat.Vehicles;

public sealed record OmsiHofTerminus(
    int Code,
    string Identifier,
    IReadOnlyList<string> Strings,
    bool AllExit);

public sealed record OmsiHofCatalog(
    string Name,
    IReadOnlyList<OmsiHofTerminus> Termini)
{
    public int? FindTerminusIndex(
        string destination)
    {
        var wanted =
            (destination ??
             string.Empty)
                .Trim();

        if (wanted.Length ==
            0)
        {
            return null;
        }

        for (var index = 0;
             index <
                 Termini.Count;
             index++)
        {
            if (Termini[index]
                .Identifier.Equals(
                    wanted,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        var bestIndex =
            -1;
        var bestScore =
            0;

        for (var index = 0;
             index <
                 Termini.Count;
             index++)
        {
            var terminus =
                Termini[index];

            var score =
                MatchScore(
                    terminus.Identifier,
                    wanted);

            foreach (var value in
                     terminus.Strings)
            {
                score =
                    Math.Max(
                        score,
                        MatchScore(
                            value,
                            wanted));
            }

            if (score >
                bestScore)
            {
                bestScore =
                    score;
                bestIndex =
                    index;
            }
        }

        return bestIndex >=
                    0
            ? bestIndex
            : null;
    }

    private static int MatchScore(
        string candidate,
        string wanted)
    {
        var normalizedCandidate =
            (candidate ??
             string.Empty)
                .Trim();
        var normalizedWanted =
            wanted.Trim();

        if (normalizedCandidate.Length ==
                0 ||
            normalizedWanted.Length ==
                0)
        {
            return 0;
        }

        if (normalizedCandidate.Equals(
                normalizedWanted,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return normalizedWanted.StartsWith(
                   normalizedCandidate +
                   " ",
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedWanted +
                   " ",
                   StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
    }
}

public static class OmsiHofCatalogReader
{
    public static OmsiHofCatalog ReadFile(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        return ParseLines(
            File.ReadAllLines(
                path));
    }

    public static OmsiHofCatalog ParseText(
        string text) =>
        ParseLines(
            (text ??
             string.Empty)
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n'));

    private static OmsiHofCatalog ParseLines(
        IReadOnlyList<string> lines)
    {
        var stringCountTerminus =
            0;
        var name =
            string.Empty;
        var termini =
            new List<OmsiHofTerminus>();

        for (var index = 0;
             index <
                 lines.Count;
             index++)
        {
            var token =
                CleanKeyword(
                    lines[index]);

            if (token.Equals(
                    "stringcount_terminus",
                    StringComparison.OrdinalIgnoreCase))
            {
                var valueIndex =
                    NextDataLine(
                        lines,
                        index + 1);

                if (valueIndex >=
                        0 &&
                    int.TryParse(
                        CleanValue(
                            lines[
                                valueIndex]),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    stringCountTerminus =
                        Math.Max(
                            parsed,
                            0);
                }

                continue;
            }

            if (token.Equals(
                    "[name]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var valueIndex =
                    NextDataLine(
                        lines,
                        index + 1);

                if (valueIndex >=
                    0)
                {
                    name =
                        CleanValue(
                            lines[
                                valueIndex]);
                }

                continue;
            }

            if (token.Equals(
                    "[addterminus]",
                    StringComparison.OrdinalIgnoreCase) ||
                token.Equals(
                    "[addterminus_allexit]",
                    StringComparison.OrdinalIgnoreCase))
            {
                var codeLine =
                    NextDataLine(
                        lines,
                        index + 1);
                var identifierLine =
                    codeLine < 0
                        ? -1
                        : NextDataLine(
                            lines,
                            codeLine + 1);

                if (codeLine < 0 ||
                    identifierLine < 0 ||
                    !int.TryParse(
                        CleanValue(
                            lines[
                                codeLine]),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var code))
                {
                    continue;
                }

                var strings =
                    new List<string>(
                        stringCountTerminus);

                var cursor =
                    identifierLine +
                    1;

                for (var stringIndex = 0;
                     stringIndex <
                         stringCountTerminus &&
                     cursor <
                         lines.Count;
                     stringIndex++,
                     cursor++)
                {
                    strings.Add(
                        CleanDisplayValue(
                            lines[
                                cursor]));
                }

                termini.Add(
                    new OmsiHofTerminus(
                        code,
                        CleanValue(
                            lines[
                                identifierLine]),
                        strings,
                        token.Equals(
                            "[addterminus_allexit]",
                            StringComparison.OrdinalIgnoreCase)));
                continue;
            }

            if (token.Equals(
                    "[addterminus_list]",
                    StringComparison.OrdinalIgnoreCase))
            {
                for (var cursor =
                         index + 1;
                     cursor <
                         lines.Count;
                     cursor++)
                {
                    var row =
                        lines[cursor]
                            .TrimEnd();

                    if (CleanKeyword(
                            row)
                        .Equals(
                            "[end]",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        index =
                            cursor;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(
                            row))
                    {
                        continue;
                    }

                    var columns =
                        row.Split(
                            '\t');

                    if (columns.Length <
                            3 ||
                        !int.TryParse(
                            CleanValue(
                                columns[1]),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var code))
                    {
                        continue;
                    }

                    var strings =
                        columns
                            .Skip(3)
                            .Select(
                                CleanDisplayValue)
                            .ToList();

                    if (stringCountTerminus >
                        0)
                    {
                        while (strings.Count <
                               stringCountTerminus)
                        {
                            strings.Add(
                                string.Empty);
                        }

                        if (strings.Count >
                            stringCountTerminus)
                        {
                            strings =
                                strings
                                    .Take(
                                        stringCountTerminus)
                                    .ToList();
                        }
                    }

                    termini.Add(
                        new OmsiHofTerminus(
                            code,
                            CleanValue(
                                columns[2]),
                            strings,
                            CleanValue(
                                    columns[0])
                                .Equals(
                                    "{ALLEX}",
                                    StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        return new OmsiHofCatalog(
            name,
            termini);
    }

    private static int NextDataLine(
        IReadOnlyList<string> lines,
        int start)
    {
        for (var index =
                 start;
             index <
                 lines.Count;
             index++)
        {
            var value =
                CleanValue(
                    lines[index]);

            if (value.Length ==
                    0 ||
                value.StartsWith(
                    '#') ||
                value.StartsWith(
                    "//",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (value.StartsWith(
                    '[') &&
                value.EndsWith(
                    ']'))
            {
                return -1;
            }

            return index;
        }

        return -1;
    }

    private static string CleanKeyword(
        string value) =>
        (value ??
         string.Empty)
            .Trim()
            .TrimEnd(
                '\t')
            .Trim();

    private static string CleanValue(
        string value) =>
        (value ??
         string.Empty)
            .Trim()
            .Trim('"');

    private static string CleanDisplayValue(
        string value) =>
        (value ??
         string.Empty)
            .TrimEnd(
                '\r',
                '\n',
                '\t')
            .Trim('"');
}
