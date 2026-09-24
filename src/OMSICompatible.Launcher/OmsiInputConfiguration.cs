namespace OMSICompatible.Launcher;

internal sealed record OmsiKeyboardKeyDefinition(
    int Index,
    string Name)
{
    public override string ToString() =>
        $"{Name}  [{Index}]";
}

internal sealed record OmsiKeyboardEntryBinding(
    string Trigger,
    int KeyIndex,
    int Flags,
    int TriggerLine,
    int KeyLine,
    int FlagsLine);

internal sealed record OmsiControllerAxisBinding(
    int AxisIndex,
    int Function,
    int Flags,
    int FunctionLine,
    int FlagsLine);

internal sealed record OmsiControllerButtonBinding(
    int ButtonIndex,
    string Trigger,
    bool Continuous,
    int TriggerLine,
    int ContinuousLine);

internal sealed record OmsiControllerBinding(
    string Name,
    bool Active,
    int ActiveLine,
    IReadOnlyList<OmsiControllerAxisBinding> Axes,
    IReadOnlyList<OmsiControllerButtonBinding> Buttons,
    double ForceFeedbackCentering,
    int? ForceFeedbackCenteringLine,
    double ForceFeedbackEffects,
    int? ForceFeedbackEffectsLine);

internal static class OmsiInputConfiguration
{
    public static IReadOnlyList<OmsiKeyboardEntryBinding>
        ParseKeyboard(
            string text)
    {
        var lines =
            SplitLines(
                text);

        var result =
            new List<
                OmsiKeyboardEntryBinding>();

        for (var index = 0;
             index < lines.Length;
             index++)
        {
            if (!string.Equals(
                    lines[index].Trim(),
                    "[entry]",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values =
                NextMeaningfulLines(
                    lines,
                    index + 1,
                    3);

            if (values.Count < 3)
            {
                continue;
            }

            if (!int.TryParse(
                    lines[values[1]].Trim(),
                    out var keyIndex) ||
                !int.TryParse(
                    lines[values[2]].Trim(),
                    out var flags))
            {
                continue;
            }

            result.Add(
                new OmsiKeyboardEntryBinding(
                    lines[values[0]].Trim(),
                    keyIndex,
                    flags,
                    values[0],
                    values[1],
                    values[2]));
        }

        return result;
    }

    public static IReadOnlyList<OmsiKeyboardKeyDefinition>
        ReadKeyboardKeys(
            string contentRoot,
            string? preferredLanguage)
    {
        var inputs =
            Path.Combine(
                contentRoot,
                "Inputs");

        if (!Directory.Exists(
                inputs))
        {
            return Array.Empty<
                OmsiKeyboardKeyDefinition>();
        }

        var candidates =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                preferredLanguage))
        {
            candidates.Add(
                Path.Combine(
                    inputs,
                    preferredLanguage +
                    ".kyb"));
        }

        candidates.Add(
            Path.Combine(
                inputs,
                "PTB.kyb"));
        candidates.Add(
            Path.Combine(
                inputs,
                "ENG.kyb"));
        candidates.Add(
            Path.Combine(
                inputs,
                "DEU.kyb"));

        string? path =
            candidates
                .FirstOrDefault(
                    File.Exists);

        path ??=
            Directory
                .EnumerateFiles(
                    inputs,
                    "*.kyb",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

        if (path is null)
        {
            return Array.Empty<
                OmsiKeyboardKeyDefinition>();
        }

        var result =
            new List<
                OmsiKeyboardKeyDefinition>();

        foreach (var line in
                 File.ReadLines(
                     path))
        {
            var trimmed =
                line.Trim();

            if (trimmed.Length == 0 ||
                trimmed.StartsWith(
                    '#') ||
                trimmed.StartsWith(
                    "//",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var tab =
                trimmed.IndexOf(
                    '\t');

            string indexText;
            string name;

            if (tab > 0)
            {
                indexText =
                    trimmed[..tab]
                        .Trim();
                name =
                    trimmed[(tab + 1)..]
                        .Trim();
            }
            else
            {
                var split =
                    trimmed.Split(
                        [' ', '\t'],
                        2,
                        StringSplitOptions.RemoveEmptyEntries);

                if (split.Length < 2)
                {
                    continue;
                }

                indexText =
                    split[0];

                name =
                    split[1];
            }

            if (!int.TryParse(
                    indexText,
                    out var keyIndex) ||
                string.IsNullOrWhiteSpace(
                    name))
            {
                continue;
            }

            result.Add(
                new OmsiKeyboardKeyDefinition(
                    keyIndex,
                    name));
        }

        return result
            .GroupBy(
                static key =>
                    key.Index)
            .Select(
                static group =>
                    group.First())
            .OrderBy(
                static key =>
                    key.Index)
            .ToArray();
    }

    public static IReadOnlyList<OmsiControllerBinding>
        ParseControllers(
            string text)
    {
        var lines =
            SplitLines(
                text);

        var starts =
            Enumerable
                .Range(
                    0,
                    lines.Length)
                .Where(
                    index =>
                        string.Equals(
                            lines[index].Trim(),
                            "[ctrl]",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var result =
            new List<
                OmsiControllerBinding>();

        for (var controllerIndex = 0;
             controllerIndex < starts.Length;
             controllerIndex++)
        {
            var start =
                starts[controllerIndex];

            var end =
                controllerIndex + 1 <
                    starts.Length
                    ? starts[
                        controllerIndex + 1]
                    : lines.Length;

            var ctrlValues =
                NextMeaningfulLines(
                    lines,
                    start + 1,
                    2,
                    end);

            if (ctrlValues.Count < 2)
            {
                continue;
            }

            var name =
                lines[
                    ctrlValues[0]]
                    .Trim();

            var active =
                int.TryParse(
                    lines[
                        ctrlValues[1]]
                        .Trim(),
                    out var activeValue) &&
                activeValue != 0;

            var axes =
                new List<
                    OmsiControllerAxisBinding>();

            var buttons =
                new List<
                    OmsiControllerButtonBinding>();

            double ffCenter =
                0.0;
            double ffEffects =
                0.0;
            int? ffCenterLine =
                null;
            int? ffEffectsLine =
                null;

            for (var index = start + 1;
                 index < end;
                 index++)
            {
                var marker =
                    lines[index]
                        .Trim();

                if (string.Equals(
                        marker,
                        "[axis]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var values =
                        NextMeaningfulLines(
                            lines,
                            index + 1,
                            16,
                            end);

                    for (var axis = 0;
                         axis < 8 &&
                         axis * 2 + 1 <
                             values.Count;
                         axis++)
                    {
                        var functionLine =
                            values[
                                axis *
                                2];

                        var flagsLine =
                            values[
                                axis *
                                2 +
                                1];

                        if (!int.TryParse(
                                lines[
                                    functionLine]
                                    .Trim(),
                                out var function))
                        {
                            function =
                                -1;
                        }

                        if (!int.TryParse(
                                lines[
                                    flagsLine]
                                    .Trim(),
                                out var flags))
                        {
                            flags =
                                0;
                        }

                        axes.Add(
                            new OmsiControllerAxisBinding(
                                axis,
                                function,
                                flags,
                                functionLine,
                                flagsLine));
                    }

                    continue;
                }

                if (string.Equals(
                        marker,
                        "[buttons]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var countLines =
                        NextMeaningfulLines(
                            lines,
                            index + 1,
                            1,
                            end);

                    if (countLines.Count == 0 ||
                        !int.TryParse(
                            lines[
                                countLines[0]]
                                .Trim(),
                            out var count) ||
                        count < 0)
                    {
                        continue;
                    }

                    var values =
                        NextMeaningfulLines(
                            lines,
                            countLines[0] + 1,
                            count * 2,
                            end);

                    for (var button = 0;
                         button < count &&
                         button * 2 + 1 <
                             values.Count;
                         button++)
                    {
                        var triggerLine =
                            values[
                                button *
                                2];

                        var continuousLine =
                            values[
                                button *
                                2 +
                                1];

                        var trigger =
                            lines[
                                triggerLine]
                                .Trim();

                        var continuous =
                            int.TryParse(
                                lines[
                                    continuousLine]
                                    .Trim(),
                                out var continuousValue) &&
                            continuousValue != 0;

                        buttons.Add(
                            new OmsiControllerButtonBinding(
                                button,
                                trigger,
                                continuous,
                                triggerLine,
                                continuousLine));
                    }

                    continue;
                }

                if (string.Equals(
                        marker,
                        "[FFScale]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var values =
                        NextMeaningfulLines(
                            lines,
                            index + 1,
                            2,
                            end);

                    if (values.Count > 0)
                    {
                        ffCenterLine =
                            values[0];

                        double.TryParse(
                            lines[
                                values[0]]
                                .Trim()
                                .Replace(
                                    ',',
                                    '.'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out ffCenter);
                    }

                    if (values.Count > 1)
                    {
                        ffEffectsLine =
                            values[1];

                        double.TryParse(
                            lines[
                                values[1]]
                                .Trim()
                                .Replace(
                                    ',',
                                    '.'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out ffEffects);
                    }
                }
            }

            result.Add(
                new OmsiControllerBinding(
                    name,
                    active,
                    ctrlValues[1],
                    axes,
                    buttons,
                    ffCenter,
                    ffCenterLine,
                    ffEffects,
                    ffEffectsLine));
        }

        return result;
    }

    public static string ApplyLineChanges(
        string text,
        IReadOnlyDictionary<int, string> changes)
    {
        var lines =
            SplitLines(
                text);

        foreach (var pair in
                 changes)
        {
            if (pair.Key < 0 ||
                pair.Key >=
                    lines.Length)
            {
                continue;
            }

            lines[pair.Key] =
                pair.Value;
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }

    private static string[] SplitLines(
        string text) =>
        text
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                '\n');

    private static List<int>
        NextMeaningfulLines(
            IReadOnlyList<string> lines,
            int start,
            int count,
            int? end = null)
    {
        var result =
            new List<int>(
                count);

        var maximum =
            Math.Min(
                end ??
                lines.Count,
                lines.Count);

        for (var index = start;
             index < maximum &&
             result.Count < count;
             index++)
        {
            var value =
                lines[index]
                    .Trim();

            if (value.Length == 0 ||
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
                break;
            }

            result.Add(
                index);
        }

        return result;
    }
}
