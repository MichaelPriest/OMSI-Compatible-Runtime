using System.Globalization;
using System.Runtime.InteropServices;

namespace OMSICompatible.Launcher;

internal sealed record OmsiKeyboardBinding(
    int Ordinal,
    string EventName,
    int ScanCode,
    int Flags,
    int EventLineIndex,
    int ScanCodeLineIndex,
    int FlagsLineIndex)
{
    public bool Continuous =>
        (Flags & 1) != 0;

    public bool Shift =>
        (Flags & 2) != 0;

    public bool Control =>
        (Flags & 4) != 0;
}

internal sealed record OmsiControllerAxisBinding(
    int AxisIndex,
    int Assignment,
    int Flags,
    int AssignmentLineIndex,
    int FlagsLineIndex)
{
    public bool Reversed =>
        (Flags & 1) != 0;

    public bool Narrowed =>
        (Flags & 2) != 0;

    public int Characteristic =>
        Flags & ~3;
}

internal sealed record OmsiControllerButtonBinding(
    int ButtonIndex,
    string EventName,
    bool Continuous,
    int EventLineIndex,
    int ContinuousLineIndex);

internal sealed record OmsiControllerBinding(
    int Ordinal,
    string Name,
    bool Active,
    int ActiveLineIndex,
    IReadOnlyList<OmsiControllerAxisBinding> Axes,
    IReadOnlyList<OmsiControllerButtonBinding> Buttons,
    int ButtonsCountLineIndex,
    int? ForceFeedbackActionLineIndex,
    int? ForceFeedbackIntensityLineIndex,
    double ForceFeedbackAction,
    double ForceFeedbackIntensity);

internal static class OmsiInputConfigEditor
{
    public static IReadOnlyList<OmsiKeyboardBinding>
        ParseKeyboard(
            string text)
    {
        var lines =
            SplitLines(
                text);

        var result =
            new List<
                OmsiKeyboardBinding>();

        var ordinal =
            0;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (!IsMarker(
                    lines[index],
                    "entry"))
            {
                continue;
            }

            var eventIndex =
                NextValueIndex(
                    lines,
                    index + 1,
                    lines.Count);

            if (eventIndex < 0)
            {
                continue;
            }

            var scanIndex =
                NextValueIndex(
                    lines,
                    eventIndex + 1,
                    lines.Count);

            var flagsIndex =
                scanIndex < 0
                    ? -1
                    : NextValueIndex(
                        lines,
                        scanIndex + 1,
                        lines.Count);

            var scan =
                scanIndex >= 0 &&
                int.TryParse(
                    lines[scanIndex].Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedScan)
                    ? parsedScan
                    : 0;

            var flags =
                flagsIndex >= 0 &&
                int.TryParse(
                    lines[flagsIndex].Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedFlags)
                    ? parsedFlags
                    : 0;

            result.Add(
                new OmsiKeyboardBinding(
                    ordinal++,
                    lines[eventIndex]
                        .Trim()
                        .Trim('"'),
                    scan,
                    flags,
                    eventIndex,
                    scanIndex,
                    flagsIndex));
        }

        return result;
    }

    public static string UpdateKeyboardBinding(
        string text,
        int ordinal,
        int scanCode,
        bool continuous,
        bool shift,
        bool control)
    {
        var lines =
            SplitLines(
                text);

        var binding =
            ParseKeyboard(
                text)
                .FirstOrDefault(
                    item =>
                        item.Ordinal ==
                        ordinal);

        if (binding is null ||
            binding.ScanCodeLineIndex < 0 ||
            binding.FlagsLineIndex < 0)
        {
            return text;
        }

        var flags =
            (continuous
                ? 1
                : 0) |
            (shift
                ? 2
                : 0) |
            (control
                ? 4
                : 0);

        lines[
            binding.ScanCodeLineIndex] =
            Math.Max(
                scanCode,
                0)
                .ToString(
                    CultureInfo.InvariantCulture);

        lines[
            binding.FlagsLineIndex] =
            flags.ToString(
                CultureInfo.InvariantCulture);

        return JoinLines(
            lines);
    }

    public static string AddKeyboardEvent(
        string text,
        string eventName)
    {
        var normalized =
            eventName
                .Trim();

        if (normalized.Length == 0)
        {
            return text;
        }

        var lines =
            SplitLines(
                text);

        if (lines.Count > 0 &&
            lines[^1].Length > 0)
        {
            lines.Add(
                string.Empty);
        }

        lines.Add(
            "[entry]");
        lines.Add(
            normalized);
        lines.Add(
            "0");
        lines.Add(
            "0");

        return JoinLines(
            lines);
    }

    public static IReadOnlyList<OmsiControllerBinding>
        ParseControllers(
            string text)
    {
        var lines =
            SplitLines(
                text);

        var result =
            new List<
                OmsiControllerBinding>();

        var starts =
            lines
                .Select(
                    (line, index) =>
                        new
                        {
                            line,
                            index
                        })
                .Where(
                    item =>
                        IsMarker(
                            item.line,
                            "ctrl"))
                .Select(
                    item =>
                        item.index)
                .ToArray();

        for (var controllerIndex = 0;
             controllerIndex < starts.Length;
             controllerIndex++)
        {
            var start =
                starts[
                    controllerIndex];

            var end =
                controllerIndex + 1 <
                    starts.Length
                    ? starts[
                        controllerIndex +
                        1]
                    : lines.Count;

            var nameIndex =
                NextValueIndex(
                    lines,
                    start + 1,
                    end);

            var activeIndex =
                nameIndex < 0
                    ? -1
                    : NextValueIndex(
                        lines,
                        nameIndex + 1,
                        end);

            if (nameIndex < 0 ||
                activeIndex < 0)
            {
                continue;
            }

            var axes =
                new List<
                    OmsiControllerAxisBinding>();

            var buttons =
                new List<
                    OmsiControllerButtonBinding>();

            var buttonsCountLineIndex =
                -1;

            int? ffActionIndex =
                null;

            int? ffIntensityIndex =
                null;

            var ffAction =
                0.0;

            var ffIntensity =
                0.0;

            for (var lineIndex =
                     activeIndex + 1;
                 lineIndex < end;
                 lineIndex++)
            {
                if (IsMarker(
                        lines[lineIndex],
                        "axis"))
                {
                    var cursor =
                        lineIndex + 1;

                    for (var axis = 0;
                         axis < 8;
                         axis++)
                    {
                        var assignmentIndex =
                            NextValueIndex(
                                lines,
                                cursor,
                                end);

                        if (assignmentIndex < 0)
                        {
                            break;
                        }

                        var flagsIndex =
                            NextValueIndex(
                                lines,
                                assignmentIndex +
                                1,
                                end);

                        if (flagsIndex < 0)
                        {
                            break;
                        }

                        int.TryParse(
                            lines[
                                assignmentIndex]
                                .Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var assignment);

                        int.TryParse(
                            lines[
                                flagsIndex]
                                .Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var flags);

                        axes.Add(
                            new OmsiControllerAxisBinding(
                                axis,
                                assignment,
                                flags,
                                assignmentIndex,
                                flagsIndex));

                        cursor =
                            flagsIndex +
                            1;
                    }

                    continue;
                }

                if (IsMarker(
                        lines[lineIndex],
                        "buttons"))
                {
                    buttonsCountLineIndex =
                        NextValueIndex(
                            lines,
                            lineIndex + 1,
                            end);

                    if (buttonsCountLineIndex < 0 ||
                        !int.TryParse(
                            lines[
                                buttonsCountLineIndex]
                                .Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var count))
                    {
                        continue;
                    }

                    count =
                        Math.Clamp(
                            count,
                            0,
                            512);

                    var cursor =
                        buttonsCountLineIndex +
                        1;

                    for (var button = 0;
                         button < count;
                         button++)
                    {
                        var eventIndex =
                            NextValueIndex(
                                lines,
                                cursor,
                                end);

                        if (eventIndex < 0)
                        {
                            break;
                        }

                        var continuousIndex =
                            NextValueIndex(
                                lines,
                                eventIndex + 1,
                                end);

                        if (continuousIndex < 0)
                        {
                            break;
                        }

                        var continuous =
                            int.TryParse(
                                lines[
                                    continuousIndex]
                                    .Trim(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var parsed) &&
                            parsed != 0;

                        buttons.Add(
                            new OmsiControllerButtonBinding(
                                button,
                                lines[eventIndex]
                                    .Trim()
                                    .Trim('"'),
                                continuous,
                                eventIndex,
                                continuousIndex));

                        cursor =
                            continuousIndex +
                            1;
                    }

                    continue;
                }

                if (IsMarker(
                        lines[lineIndex],
                        "FFScale"))
                {
                    var first =
                        NextValueIndex(
                            lines,
                            lineIndex + 1,
                            end);

                    var second =
                        first < 0
                            ? -1
                            : NextValueIndex(
                                lines,
                                first + 1,
                                end);

                    if (first >= 0)
                    {
                        ffActionIndex =
                            first;

                        double.TryParse(
                            lines[first]
                                .Trim()
                                .Replace(
                                    ',',
                                    '.'),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out ffAction);
                    }

                    if (second >= 0)
                    {
                        ffIntensityIndex =
                            second;

                        double.TryParse(
                            lines[second]
                                .Trim()
                                .Replace(
                                    ',',
                                    '.'),
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out ffIntensity);
                    }
                }
            }

            result.Add(
                new OmsiControllerBinding(
                    result.Count,
                    lines[nameIndex]
                        .Trim()
                        .Trim('"'),
                    lines[activeIndex]
                        .Trim() !=
                        "0",
                    activeIndex,
                    axes,
                    buttons,
                    buttonsCountLineIndex,
                    ffActionIndex,
                    ffIntensityIndex,
                    ffAction,
                    ffIntensity));
        }

        return result;
    }

    public static string UpdateController(
        string text,
        OmsiControllerBinding controller,
        bool active,
        IReadOnlyList<
            (int Assignment,
             bool Reversed,
             bool Narrowed,
             int Characteristic)> axes,
        IReadOnlyList<
            (string EventName,
             bool Continuous)> buttons,
        double forceFeedbackAction,
        double forceFeedbackIntensity)
    {
        var lines =
            SplitLines(
                text);

        if (controller.ActiveLineIndex >= 0 &&
            controller.ActiveLineIndex <
                lines.Count)
        {
            lines[
                controller.ActiveLineIndex] =
                active
                    ? "1"
                    : "0";
        }

        for (var index = 0;
             index <
                 Math.Min(
                     axes.Count,
                     controller.Axes.Count);
             index++)
        {
            var source =
                controller.Axes[
                    index];

            var value =
                axes[
                    index];

            if (source.AssignmentLineIndex >= 0 &&
                source.AssignmentLineIndex <
                    lines.Count)
            {
                lines[
                    source.AssignmentLineIndex] =
                    value.Assignment
                        .ToString(
                            CultureInfo.InvariantCulture);
            }

            var flags =
                (value.Reversed
                    ? 1
                    : 0) |
                (value.Narrowed
                    ? 2
                    : 0) |
                value.Characteristic;

            if (source.FlagsLineIndex >= 0 &&
                source.FlagsLineIndex <
                    lines.Count)
            {
                lines[
                    source.FlagsLineIndex] =
                    flags.ToString(
                        CultureInfo.InvariantCulture);
            }
        }

        for (var index = 0;
             index <
                 Math.Min(
                     buttons.Count,
                     controller.Buttons.Count);
             index++)
        {
            var source =
                controller.Buttons[
                    index];

            var value =
                buttons[
                    index];

            if (source.EventLineIndex >= 0 &&
                source.EventLineIndex <
                    lines.Count)
            {
                lines[
                    source.EventLineIndex] =
                    value.EventName
                        .Trim();
            }

            if (source.ContinuousLineIndex >= 0 &&
                source.ContinuousLineIndex <
                    lines.Count)
            {
                lines[
                    source.ContinuousLineIndex] =
                    value.Continuous
                        ? "1"
                        : "0";
            }
        }

        if (controller.ForceFeedbackActionLineIndex is
                int actionIndex &&
            actionIndex >= 0 &&
            actionIndex < lines.Count)
        {
            lines[
                actionIndex] =
                forceFeedbackAction
                    .ToString(
                        "0.000",
                        CultureInfo.InvariantCulture);
        }

        if (controller.ForceFeedbackIntensityLineIndex is
                int intensityIndex &&
            intensityIndex >= 0 &&
            intensityIndex < lines.Count)
        {
            lines[
                intensityIndex] =
                forceFeedbackIntensity
                    .ToString(
                        "0.000",
                        CultureInfo.InvariantCulture);
        }

        return JoinLines(
            lines);
    }

    public static int BuildKeyboardFlags(
        bool continuous,
        bool shift,
        bool control) =>
        (continuous
            ? 1
            : 0) |
        (shift
            ? 2
            : 0) |
        (control
            ? 4
            : 0);

    public static string KeyboardScanCodeName(
        int scanCode)
    {
        if (scanCode <= 0)
        {
            return "<sem tecla>";
        }

        return DirectInputNames.TryGetValue(
                scanCode,
                out var name)
            ? name
            : $"DIK {scanCode}";
    }

    public static int VirtualKeyToDirectInputScanCode(
        Keys key)
    {
        var scan =
            MapVirtualKey(
                (uint)key,
                0);

        if (scan == 0)
        {
            return 0;
        }

        var low =
            (int)(scan & 0xFF);

        var high =
            (scan >> 8) &
            0xFF;

        if (high == 0xE0)
        {
            low |=
                0x80;
        }

        return low;
    }

    public static string AxisName(
        int axisIndex) =>
        axisIndex switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "Rx",
            4 => "Ry",
            5 => "Rz",
            6 => "Slider 1",
            7 => "Slider 2",
            _ => $"Axis {axisIndex}"
        };

    public static string AxisAssignmentName(
        int assignment) =>
        assignment switch
        {
            0 => "Direção",
            1 => "Freio",
            2 => "Acelerador",
            3 => "Embreagem",
            4 => "Acelerador + freio",
            _ => "<nenhum>"
        };

    public static int AxisAssignmentValue(
        string? name) =>
        name switch
        {
            "Direção" => 0,
            "Freio" => 1,
            "Acelerador" => 2,
            "Embreagem" => 3,
            "Acelerador + freio" => 4,
            _ => -1
        };

    public static string AxisCharacteristicName(
        int characteristic) =>
        characteristic switch
        {
            4 => "Degressiva",
            8 => "Progressiva",
            20 => "Bi-degressiva",
            24 => "Bi-progressiva",
            _ => "Linear"
        };

    public static int AxisCharacteristicValue(
        string? name) =>
        name switch
        {
            "Degressiva" => 4,
            "Progressiva" => 8,
            "Bi-degressiva" => 20,
            "Bi-progressiva" => 24,
            _ => 0
        };

    private static List<string> SplitLines(
        string text) =>
        text
            .Replace(
                "\r\n",
                "\n")
            .Replace(
                '\r',
                '\n')
            .Split(
                '\n')
            .ToList();

    private static string JoinLines(
        IEnumerable<string> lines) =>
        string.Join(
            Environment.NewLine,
            lines);

    private static int NextValueIndex(
        IReadOnlyList<string> lines,
        int start,
        int end)
    {
        for (var index =
                 Math.Max(
                     start,
                     0);
             index < end &&
             index < lines.Count;
             index++)
        {
            var value =
                lines[index]
                    .Trim();

            if (value.Length == 0 ||
                value.StartsWith(
                    "//",
                    StringComparison.Ordinal) ||
                value.StartsWith(
                    '#'))
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

    private static bool IsMarker(
        string value,
        string marker) =>
        string.Equals(
            value.Trim(),
            $"[{marker}]",
            StringComparison.OrdinalIgnoreCase);

    [DllImport(
        "user32.dll",
        SetLastError = false)]
    private static extern uint MapVirtualKey(
        uint uCode,
        uint uMapType);

    private static readonly IReadOnlyDictionary<
        int,
        string> DirectInputNames =
        new Dictionary<int, string>
        {
            [1] = "Esc",
            [2] = "1",
            [3] = "2",
            [4] = "3",
            [5] = "4",
            [6] = "5",
            [7] = "6",
            [8] = "7",
            [9] = "8",
            [10] = "9",
            [11] = "0",
            [12] = "-",
            [13] = "=",
            [14] = "Backspace",
            [15] = "Tab",
            [16] = "Q",
            [17] = "W",
            [18] = "E",
            [19] = "R",
            [20] = "T",
            [21] = "Y",
            [22] = "U",
            [23] = "I",
            [24] = "O",
            [25] = "P",
            [26] = "[",
            [27] = "]",
            [28] = "Enter",
            [29] = "Ctrl esquerdo",
            [30] = "A",
            [31] = "S",
            [32] = "D",
            [33] = "F",
            [34] = "G",
            [35] = "H",
            [36] = "J",
            [37] = "K",
            [38] = "L",
            [39] = ";",
            [40] = "'",
            [41] = "`",
            [42] = "Shift esquerdo",
            [43] = "\\",
            [44] = "Z",
            [45] = "X",
            [46] = "C",
            [47] = "V",
            [48] = "B",
            [49] = "N",
            [50] = "M",
            [51] = ",",
            [52] = ".",
            [53] = "/",
            [54] = "Shift direito",
            [55] = "Num *",
            [56] = "Alt",
            [57] = "Espaço",
            [58] = "Caps Lock",
            [59] = "F1",
            [60] = "F2",
            [61] = "F3",
            [62] = "F4",
            [63] = "F5",
            [64] = "F6",
            [65] = "F7",
            [66] = "F8",
            [67] = "F9",
            [68] = "F10",
            [69] = "Num Lock",
            [70] = "Scroll Lock",
            [71] = "Num 7",
            [72] = "Num 8",
            [73] = "Num 9",
            [74] = "Num -",
            [75] = "Num 4",
            [76] = "Num 5",
            [77] = "Num 6",
            [78] = "Num +",
            [79] = "Num 1",
            [80] = "Num 2",
            [81] = "Num 3",
            [82] = "Num 0",
            [83] = "Num .",
            [87] = "F11",
            [88] = "F12",
            [156] = "Num Enter",
            [157] = "Ctrl direito",
            [181] = "Num /",
            [184] = "Alt direito",
            [199] = "Home",
            [200] = "↑",
            [201] = "Page Up",
            [203] = "←",
            [205] = "→",
            [207] = "End",
            [208] = "↓",
            [209] = "Page Down",
            [210] = "Insert",
            [211] = "Delete"
        };
}
