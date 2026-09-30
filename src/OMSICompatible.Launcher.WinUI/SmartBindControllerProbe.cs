using System.Diagnostics;
using System.Globalization;
using Vortice.DirectInput;

namespace OMSICompatible.Launcher.WinUI;

internal sealed record SmartBindDeviceInfo(
    Guid InstanceGuid,
    string Name)
{
    public string Display =>
        Name;
}

internal enum SmartBindCaptureKind
{
    Axis,
    Button
}

internal sealed record SmartBindCaptureResult(
    SmartBindCaptureKind Kind,
    int Index,
    int RawValue);

internal static class SmartBindControllerProbe
{
    public static IReadOnlyList<SmartBindDeviceInfo>
        EnumerateDevices()
    {
        using var directInput =
            DInput.DirectInput8Create();

        return directInput
            .GetDevices(
                DeviceClass.GameControl,
                DeviceEnumerationFlags.AttachedOnly)
            .Select(
                device =>
                    new SmartBindDeviceInfo(
                        device.InstanceGuid,
                        string.IsNullOrWhiteSpace(
                            device.ProductName)
                            ? device.InstanceName
                            : device.ProductName))
            .OrderBy(
                device =>
                    device.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static Task<SmartBindCaptureResult?>
        CaptureAsync(
            SmartBindDeviceInfo deviceInfo,
            IntPtr windowHandle,
            SmartBindCaptureKind kind,
            CancellationToken cancellationToken) =>
        Task.Run(
            () =>
                Capture(
                    deviceInfo,
                    windowHandle,
                    kind,
                    cancellationToken),
            cancellationToken);

    private static SmartBindCaptureResult?
        Capture(
            SmartBindDeviceInfo deviceInfo,
            IntPtr windowHandle,
            SmartBindCaptureKind kind,
            CancellationToken cancellationToken)
    {
        using var directInput =
            DInput.DirectInput8Create();

        using var device =
            directInput.CreateDevice(
                deviceInfo.InstanceGuid);

        if (device.SetDataFormat<
                RawJoystickState>()
            .Failure)
        {
            throw new InvalidOperationException(
                "DirectInput não aceitou o formato do dispositivo.");
        }

        if (device.SetCooperativeLevel(
                windowHandle,
                CooperativeLevel.NonExclusive |
                CooperativeLevel.Foreground)
            .Failure)
        {
            throw new InvalidOperationException(
                "DirectInput não conseguiu adquirir o dispositivo.");
        }

        device.Properties.BufferSize =
            64;
        device.Acquire();

        var baseline =
            ReadState(
                device) ??
            throw new InvalidOperationException(
                "O dispositivo não respondeu ao teste inicial.");

        var baselineAxes =
            GetAxes(
                baseline);

        var previousButtons =
            baseline.Buttons
                .ToArray();

        var stopwatch =
            Stopwatch.StartNew();

        while (stopwatch.Elapsed <
               TimeSpan.FromSeconds(
                   7))
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Thread.Sleep(
                20);

            var state =
                ReadState(
                    device);

            if (state is null)
            {
                continue;
            }

            if (kind ==
                SmartBindCaptureKind.Axis)
            {
                var axes =
                    GetAxes(
                        state);

                var bestIndex =
                    -1;
                var bestDelta =
                    0;

                for (var index = 0;
                     index < axes.Length;
                     index++)
                {
                    var delta =
                        Math.Abs(
                            axes[index] -
                            baselineAxes[index]);

                    if (delta >
                        bestDelta)
                    {
                        bestDelta =
                            delta;
                        bestIndex =
                            index;
                    }
                }

                if (bestIndex >= 0 &&
                    bestDelta >=
                        5000)
                {
                    return new SmartBindCaptureResult(
                        SmartBindCaptureKind.Axis,
                        bestIndex,
                        axes[bestIndex]);
                }
            }
            else
            {
                var buttons =
                    state.Buttons;

                for (var index = 0;
                     index < buttons.Length;
                     index++)
                {
                    var previous =
                        index <
                            previousButtons.Length &&
                        previousButtons[index];

                    if (buttons[index] &&
                        !previous)
                    {
                        return new SmartBindCaptureResult(
                            SmartBindCaptureKind.Button,
                            index,
                            1);
                    }
                }

                previousButtons =
                    buttons.ToArray();
            }
        }

        return null;
    }

    public static void BindAxis(
        string path,
        string deviceName,
        int axisIndex,
        int function)
    {
        if (axisIndex is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(axisIndex));
        }

        var text =
            EnsureController(
                ReadText(
                    path),
                deviceName);

        var config =
            OmsiInputConfiguration
                .ParseControllers(
                    text)
                .First(
                    controller =>
                        controller.Name.Equals(
                            deviceName,
                            StringComparison.OrdinalIgnoreCase));

        var axis =
            config.Axes.First(
                binding =>
                    binding.AxisIndex ==
                    axisIndex);

        var changes =
            new Dictionary<int, string>
            {
                [config.ActiveLine] =
                    "1",
                [axis.FunctionLine] =
                    function.ToString(
                        CultureInfo.InvariantCulture)
            };

        WriteText(
            path,
            OmsiInputConfiguration.ApplyLineChanges(
                text,
                changes));
    }

    public static void BindButton(
        string path,
        string deviceName,
        int buttonIndex,
        string trigger,
        bool continuous)
    {
        if (buttonIndex < 0 ||
            string.IsNullOrWhiteSpace(
                trigger))
        {
            throw new ArgumentException(
                "Botão ou trigger inválido.");
        }

        var text =
            EnsureController(
                ReadText(
                    path),
                deviceName);

        var lines =
            SplitLines(
                text);

        var block =
            FindControllerBlock(
                lines,
                deviceName);

        var buttonsMarker =
            FindMarker(
                lines,
                block.Start,
                block.End,
                "[buttons]");

        if (buttonsMarker < 0)
        {
            throw new InvalidDataException(
                "Bloco [buttons] não encontrado.");
        }

        var countLine =
            NextMeaningfulLine(
                lines,
                buttonsMarker + 1,
                block.End);

        if (countLine < 0)
        {
            throw new InvalidDataException(
                "Contagem de botões não encontrada.");
        }

        var oldCount =
            int.TryParse(
                lines[countLine].Trim(),
                out var parsed)
                ? Math.Max(
                    0,
                    parsed)
                : 0;

        var sectionEnd =
            FindNextMarker(
                lines,
                countLine + 1,
                block.End);

        var pairs =
            new List<(string Trigger, bool Continuous)>();

        var cursor =
            countLine + 1;

        for (var index = 0;
             index < oldCount;
             index++)
        {
            var triggerLine =
                NextMeaningfulLine(
                    lines,
                    cursor,
                    sectionEnd);

            if (triggerLine < 0)
            {
                break;
            }

            var continuousLine =
                NextMeaningfulLine(
                    lines,
                    triggerLine + 1,
                    sectionEnd);

            if (continuousLine < 0)
            {
                break;
            }

            pairs.Add(
                (
                    lines[triggerLine].Trim(),
                    int.TryParse(
                        lines[continuousLine].Trim(),
                        out var continuousValue) &&
                    continuousValue != 0));

            cursor =
                continuousLine + 1;
        }

        var newCount =
            Math.Max(
                oldCount,
                buttonIndex + 1);

        while (pairs.Count <
               newCount)
        {
            pairs.Add(
                ("0", false));
        }

        pairs[
            buttonIndex] =
            (
                trigger.Trim(),
                continuous);

        var replacement =
            new List<string>
            {
                newCount.ToString(
                    CultureInfo.InvariantCulture)
            };

        foreach (var pair in
                 pairs.Take(
                     newCount))
        {
            replacement.Add(
                pair.Trigger);
            replacement.Add(
                pair.Continuous
                    ? "1"
                    : "0");
        }

        lines.RemoveRange(
            countLine,
            sectionEnd -
            countLine);

        lines.InsertRange(
            countLine,
            replacement);

        var rebuilt =
            string.Join(
                Environment.NewLine,
                lines);

        var controller =
            OmsiInputConfiguration
                .ParseControllers(
                    rebuilt)
                .First(
                    item =>
                        item.Name.Equals(
                            deviceName,
                            StringComparison.OrdinalIgnoreCase));

        rebuilt =
            OmsiInputConfiguration.ApplyLineChanges(
                rebuilt,
                new Dictionary<int, string>
                {
                    [controller.ActiveLine] =
                        "1"
                });

        WriteText(
            path,
            rebuilt);
    }

    private static JoystickState?
        ReadState(
            IDirectInputDevice8 device)
    {
        try
        {
            var poll =
                device.Poll();

            if (poll.Failure)
            {
                if (device.Acquire()
                    .Failure ||
                    device.Poll()
                        .Failure)
                {
                    return null;
                }
            }

            return device
                .GetCurrentJoystickState();
        }
        catch
        {
            return null;
        }
    }

    private static int[] GetAxes(
        JoystickState state) =>
        [
            state.X,
            state.Y,
            state.Z,
            state.RotationX,
            state.RotationY,
            state.RotationZ,
            state.Sliders.Length > 0
                ? state.Sliders[0]
                : 32767,
            state.Sliders.Length > 1
                ? state.Sliders[1]
                : 32767
        ];

    private static string ReadText(
        string path) =>
        File.Exists(
            path)
            ? File.ReadAllText(
                path)
            : string.Empty;

    private static void WriteText(
        string path,
        string text)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                path) ??
            ".");

        var backup =
            path +
            ".smartbind.bak";

        if (File.Exists(
                path) &&
            !File.Exists(
                backup))
        {
            File.Copy(
                path,
                backup,
                overwrite:
                    false);
        }

        File.WriteAllText(
            path,
            text);
    }

    private static string EnsureController(
        string text,
        string deviceName)
    {
        if (OmsiInputConfiguration
            .ParseControllers(
                text)
            .Any(
                controller =>
                    controller.Name.Equals(
                        deviceName,
                        StringComparison.OrdinalIgnoreCase)))
        {
            return text;
        }

        var builder =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                text))
        {
            builder.Add(
                text.TrimEnd(
                    '\r',
                    '\n'));
            builder.Add(
                string.Empty);
        }

        builder.Add(
            "[ctrl]");
        builder.Add(
            deviceName);
        builder.Add(
            "1");
        builder.Add(
            "[axis]");

        for (var axis = 0;
             axis < 8;
             axis++)
        {
            builder.Add(
                "-1");
            builder.Add(
                "0");
        }

        builder.Add(
            "[buttons]");
        builder.Add(
            "0");
        builder.Add(
            "[FFScale]");
        builder.Add(
            "0");
        builder.Add(
            "0");

        return string.Join(
            Environment.NewLine,
            builder) +
            Environment.NewLine;
    }

    private static (int Start, int End)
        FindControllerBlock(
            IReadOnlyList<string> lines,
            string deviceName)
    {
        for (var index = 0;
             index < lines.Count;
             index++)
        {
            if (!lines[index].Trim()
                .Equals(
                    "[ctrl]",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nameLine =
                NextMeaningfulLine(
                    lines,
                    index + 1,
                    lines.Count);

            if (nameLine < 0 ||
                !lines[nameLine].Trim()
                    .Equals(
                        deviceName,
                        StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end =
                lines.Count;

            for (var cursor =
                     nameLine + 1;
                 cursor < lines.Count;
                 cursor++)
            {
                if (lines[cursor].Trim()
                    .Equals(
                        "[ctrl]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    end =
                        cursor;
                    break;
                }
            }

            return (
                index,
                end);
        }

        throw new InvalidDataException(
            $"Controlador não encontrado: {deviceName}");
    }

    private static int FindMarker(
        IReadOnlyList<string> lines,
        int start,
        int end,
        string marker)
    {
        for (var index = start;
             index < end;
             index++)
        {
            if (lines[index].Trim()
                .Equals(
                    marker,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNextMarker(
        IReadOnlyList<string> lines,
        int start,
        int end)
    {
        for (var index = start;
             index < end;
             index++)
        {
            var value =
                lines[index].Trim();

            if (value.StartsWith(
                    '[') &&
                value.EndsWith(
                    ']'))
            {
                return index;
            }
        }

        return end;
    }

    private static int NextMeaningfulLine(
        IReadOnlyList<string> lines,
        int start,
        int end)
    {
        for (var index = start;
             index < end &&
             index < lines.Count;
             index++)
        {
            var value =
                lines[index].Trim();

            if (value.Length == 0 ||
                value.StartsWith(
                    '#') ||
                value.StartsWith(
                    "//",
                    StringComparison.Ordinal))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

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
}
