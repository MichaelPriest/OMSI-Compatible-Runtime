using Vortice.DirectInput;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeOmsiControllerAxisBinding(
    int AxisIndex,
    int Function,
    int Flags);

internal sealed record RuntimeOmsiControllerButtonBinding(
    int ButtonIndex,
    string Trigger,
    bool Continuous);

internal sealed record RuntimeOmsiControllerConfig(
    string Name,
    bool Active,
    IReadOnlyList<RuntimeOmsiControllerAxisBinding> Axes,
    IReadOnlyList<RuntimeOmsiControllerButtonBinding> Buttons,
    double ForceFeedbackCentering,
    double ForceFeedbackEffects);

internal sealed record RuntimeOmsiControllerFrame(
    float? Steering,
    float? Brake,
    float? Accelerator,
    float? Clutch,
    IReadOnlyList<string> Triggered,
    IReadOnlyList<string> Released)
{
    public static RuntimeOmsiControllerFrame Empty { get; } =
        new(
            null,
            null,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>());

    public bool HasDrivingAxis =>
        Steering.HasValue ||
        Brake.HasValue ||
        Accelerator.HasValue ||
        Clutch.HasValue;
}

internal sealed class RuntimeOmsiGameControllerHost :
    IDisposable
{
    private sealed class DeviceBinding :
        IDisposable
    {
        public DeviceBinding(
            RuntimeOmsiControllerConfig config,
            IDirectInputDevice8 device)
        {
            Config =
                config;
            Device =
                device;
            PreviousButtons =
                new bool[128];
        }

        public RuntimeOmsiControllerConfig Config { get; }

        public IDirectInputDevice8 Device { get; }

        public JoystickState State { get; } =
            new();

        public bool[] PreviousButtons { get; }

        public void Dispose()
        {
            try
            {
                Device.Unacquire();
            }
            catch
            {
            }

            Device.Dispose();
        }
    }

    private readonly IDirectInput8 _directInput;
    private readonly List<DeviceBinding> _devices;

    private RuntimeOmsiGameControllerHost(
        IDirectInput8 directInput,
        List<DeviceBinding> devices)
    {
        _directInput =
            directInput;
        _devices =
            devices;
    }

    public int ConnectedDeviceCount =>
        _devices.Count;

    public static RuntimeOmsiGameControllerHost?
        TryCreate(
            string contentRoot,
            IntPtr windowHandle)
    {
        var configs =
            LoadConfiguration(
                contentRoot)
                .Where(
                    static config =>
                        config.Active)
                .ToArray();

        if (configs.Length == 0)
        {
            return null;
        }

        IDirectInput8? directInput =
            null;

        var devices =
            new List<DeviceBinding>();

        try
        {
            directInput =
                DInput.DirectInput8Create();

            var available =
                directInput
                    .GetDevices(
                        DeviceClass.GameControl,
                        DeviceEnumerationFlags.AttachedOnly)
                    .ToList();

            var used =
                new HashSet<Guid>();

            foreach (var config in
                     configs)
            {
                var instance =
                    FindMatchingDevice(
                        config.Name,
                        available,
                        used);

                if (instance is null)
                {
                    Console.WriteLine(
                        $"[input] OMSI controller not connected: {config.Name}");
                    continue;
                }

                IDirectInputDevice8? device =
                    null;

                try
                {
                    device =
                        directInput.CreateDevice(
                            instance.InstanceGuid);

                    var format =
                        device.SetDataFormat<
                            RawJoystickState>();

                    if (format.Failure)
                    {
                        device.Dispose();
                        continue;
                    }

                    var cooperative =
                        device.SetCooperativeLevel(
                            windowHandle,
                            CooperativeLevel.NonExclusive |
                            CooperativeLevel.Foreground);

                    if (cooperative.Failure)
                    {
                        device.Dispose();
                        continue;
                    }

                    device.Properties.BufferSize =
                        64;

                    device.Acquire();

                    devices.Add(
                        new DeviceBinding(
                            config,
                            device));

                    used.Add(
                        instance.InstanceGuid);

                    Console.WriteLine(
                        $"[input] OMSI controller active: {instance.ProductName} -> {config.Name}");
                }
                catch (Exception ex)
                {
                    device?.Dispose();

                    Console.WriteLine(
                        $"[input] unable to initialize controller {config.Name}: {ex.Message}");
                }
            }

            if (devices.Count == 0)
            {
                directInput.Dispose();
                return null;
            }

            return new RuntimeOmsiGameControllerHost(
                directInput,
                devices);
        }
        catch (Exception ex)
        {
            foreach (var device in
                     devices)
            {
                device.Dispose();
            }

            directInput?.Dispose();

            Console.WriteLine(
                $"[input] DirectInput unavailable: {ex.Message}");

            return null;
        }
    }

    public RuntimeOmsiControllerFrame Poll()
    {
        float? steering =
            null;
        float? brake =
            null;
        float? accelerator =
            null;
        float? clutch =
            null;

        var triggered =
            new List<string>();

        var released =
            new List<string>();

        foreach (var binding in
                 _devices)
        {
            if (!TryReadState(
                    binding))
            {
                continue;
            }

            foreach (var axis in
                     binding.Config.Axes)
            {
                if (axis.Function < 0)
                {
                    continue;
                }

                var raw =
                    GetAxis(
                        binding.State,
                        axis.AxisIndex);

                var value =
                    TransformAxis(
                        raw,
                        axis.Flags,
                        bidirectional:
                            axis.Function is
                                0 or 4);

                switch (axis.Function)
                {
                    case 0:
                        steering =
                            value;
                        break;

                    case 1:
                        brake =
                            value;
                        break;

                    case 2:
                        accelerator =
                            value;
                        break;

                    case 3:
                        clutch =
                            value;
                        break;

                    case 4:
                        if (value >= 0.0f)
                        {
                            accelerator =
                                value;
                            brake =
                                0.0f;
                        }
                        else
                        {
                            accelerator =
                                0.0f;
                            brake =
                                -value;
                        }

                        break;
                }
            }

            foreach (var button in
                     binding.Config.Buttons)
            {
                if (button.ButtonIndex < 0 ||
                    button.ButtonIndex >=
                        binding.State.Buttons.Length ||
                    button.ButtonIndex >=
                        binding.PreviousButtons.Length ||
                    string.IsNullOrWhiteSpace(
                        button.Trigger) ||
                    button.Trigger ==
                        "0")
                {
                    continue;
                }

                var current =
                    binding.State.Buttons[
                        button.ButtonIndex];

                var previous =
                    binding.PreviousButtons[
                        button.ButtonIndex];

                if (current &&
                    (!previous ||
                     button.Continuous))
                {
                    triggered.Add(
                        button.Trigger);
                }

                if (!current &&
                    previous)
                {
                    released.Add(
                        button.Trigger +
                        "_off");
                }

                binding.PreviousButtons[
                    button.ButtonIndex] =
                    current;
            }
        }

        return new RuntimeOmsiControllerFrame(
            steering,
            brake,
            accelerator,
            clutch,
            triggered,
            released);
    }

    public void Dispose()
    {
        foreach (var device in
                 _devices)
        {
            device.Dispose();
        }

        _devices.Clear();
        _directInput.Dispose();
    }

    private static bool TryReadState(
        DeviceBinding binding)
    {
        try
        {
            var poll =
                binding.Device.Poll();

            if (poll.Failure)
            {
                var acquire =
                    binding.Device.Acquire();

                if (acquire.Failure)
                {
                    return false;
                }

                poll =
                    binding.Device.Poll();

                if (poll.Failure)
                {
                    return false;
                }
            }

            binding.Device.GetCurrentJoystickState(
                ref binding.State);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetAxis(
        JoystickState state,
        int axisIndex) =>
        axisIndex switch
        {
            0 =>
                state.X,
            1 =>
                state.Y,
            2 =>
                state.Z,
            3 =>
                state.RotationX,
            4 =>
                state.RotationY,
            5 =>
                state.RotationZ,
            6 =>
                state.Sliders.Length > 0
                    ? state.Sliders[0]
                    : 32767,
            7 =>
                state.Sliders.Length > 1
                    ? state.Sliders[1]
                    : 32767,
            _ =>
                32767
        };

    private static float TransformAxis(
        int raw,
        int flags,
        bool bidirectional)
    {
        var unit =
            Math.Clamp(
                raw /
                65535.0f,
                0.0f,
                1.0f);

        if ((flags & 1) != 0)
        {
            unit =
                1.0f -
                unit;
        }

        if ((flags & 2) != 0)
        {
            const float lower =
                0.10f;

            const float upper =
                0.90f;

            unit =
                Math.Clamp(
                    (unit - lower) /
                    (upper - lower),
                    0.0f,
                    1.0f);
        }

        var characteristic =
            flags &
            ~3;

        if (bidirectional)
        {
            var signed =
                unit *
                2.0f -
                1.0f;

            var magnitude =
                Math.Abs(
                    signed);

            var curved =
                characteristic switch
                {
                    20 =>
                        MathF.Sqrt(
                            magnitude),
                    24 =>
                        magnitude *
                        magnitude,
                    4 =>
                        MathF.Sqrt(
                            magnitude),
                    8 =>
                        magnitude *
                        magnitude,
                    _ =>
                        magnitude
                };

            return MathF.CopySign(
                curved,
                signed);
        }

        return characteristic switch
        {
            4 =>
                MathF.Sqrt(
                    unit),
            8 =>
                unit *
                unit,
            20 =>
                MathF.Sqrt(
                    unit),
            24 =>
                unit *
                unit,
            _ =>
                unit
        };
    }

    private static DeviceInstance? FindMatchingDevice(
        string configuredName,
        IReadOnlyList<DeviceInstance> available,
        IReadOnlySet<Guid> used)
    {
        var configured =
            NormalizeName(
                configuredName);

        var exact =
            available.FirstOrDefault(
                device =>
                    !used.Contains(
                        device.InstanceGuid) &&
                    string.Equals(
                        NormalizeName(
                            device.ProductName ??
                            string.Empty),
                        configured,
                        StringComparison.Ordinal));

        if (exact is not null)
        {
            return exact;
        }

        return available.FirstOrDefault(
            device =>
            {
                if (used.Contains(
                        device.InstanceGuid))
                {
                    return false;
                }

                var product =
                    NormalizeName(
                        device.ProductName ??
                        string.Empty);

                return product.Length > 0 &&
                       configured.Length > 0 &&
                       (product.Contains(
                            configured,
                            StringComparison.Ordinal) ||
                        configured.Contains(
                            product,
                            StringComparison.Ordinal));
            });
    }

    private static string NormalizeName(
        string value) =>
        new(
            value
                .ToLowerInvariant()
                .Where(
                    char.IsLetterOrDigit)
                .ToArray());

    private static IReadOnlyList<RuntimeOmsiControllerConfig>
        LoadConfiguration(
            string contentRoot)
    {
        var path =
            Path.Combine(
                contentRoot,
                "Inputs",
                "gamectrler.cfg");

        if (!File.Exists(
                path))
        {
            return Array.Empty<
                RuntimeOmsiControllerConfig>();
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
            return Array.Empty<
                RuntimeOmsiControllerConfig>();
        }

        var starts =
            Enumerable
                .Range(
                    0,
                    lines.Length)
                .Where(
                    index =>
                        string.Equals(
                            lines[index]
                                .Trim(),
                            "[ctrl]",
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        var result =
            new List<
                RuntimeOmsiControllerConfig>();

        for (var controllerIndex = 0;
             controllerIndex <
                 starts.Length;
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
                    : lines.Length;

            var header =
                NextMeaningful(
                    lines,
                    start + 1,
                    2,
                    end);

            if (header.Count < 2)
            {
                continue;
            }

            var name =
                lines[
                    header[0]]
                    .Trim()
                    .Trim('"');

            var active =
                int.TryParse(
                    lines[
                        header[1]]
                        .Trim(),
                    out var activeValue) &&
                activeValue != 0;

            var axes =
                new List<
                    RuntimeOmsiControllerAxisBinding>();

            var buttons =
                new List<
                    RuntimeOmsiControllerButtonBinding>();

            var ffCenter =
                0.0;

            var ffEffects =
                0.0;

            for (var index =
                     header[1] +
                     1;
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
                        NextMeaningful(
                            lines,
                            index + 1,
                            16,
                            end);

                    for (var axisIndex = 0;
                         axisIndex < 8 &&
                         axisIndex *
                             2 +
                             1 <
                             values.Count;
                         axisIndex++)
                    {
                        var function =
                            TryParseInt(
                                lines[
                                    values[
                                        axisIndex *
                                        2]],
                                -1);

                        var flags =
                            TryParseInt(
                                lines[
                                    values[
                                        axisIndex *
                                        2 +
                                        1]],
                                0);

                        axes.Add(
                            new RuntimeOmsiControllerAxisBinding(
                                axisIndex,
                                function,
                                flags));
                    }

                    continue;
                }

                if (string.Equals(
                        marker,
                        "[buttons]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var countLines =
                        NextMeaningful(
                            lines,
                            index + 1,
                            1,
                            end);

                    if (countLines.Count ==
                            0)
                    {
                        continue;
                    }

                    var count =
                        Math.Clamp(
                            TryParseInt(
                                lines[
                                    countLines[0]],
                                0),
                            0,
                            128);

                    var values =
                        NextMeaningful(
                            lines,
                            countLines[0] +
                            1,
                            count *
                            2,
                            end);

                    for (var buttonIndex = 0;
                         buttonIndex <
                             count &&
                         buttonIndex *
                             2 +
                             1 <
                             values.Count;
                         buttonIndex++)
                    {
                        var trigger =
                            lines[
                                values[
                                    buttonIndex *
                                    2]]
                                .Trim()
                                .Trim('"');

                        var continuous =
                            TryParseInt(
                                lines[
                                    values[
                                        buttonIndex *
                                        2 +
                                        1]],
                                0) !=
                            0;

                        buttons.Add(
                            new RuntimeOmsiControllerButtonBinding(
                                buttonIndex,
                                trigger,
                                continuous));
                    }

                    continue;
                }

                if (string.Equals(
                        marker,
                        "[FFScale]",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var values =
                        NextMeaningful(
                            lines,
                            index + 1,
                            2,
                            end);

                    if (values.Count > 0)
                    {
                        ffCenter =
                            TryParseDouble(
                                lines[
                                    values[0]]);
                    }

                    if (values.Count > 1)
                    {
                        ffEffects =
                            TryParseDouble(
                                lines[
                                    values[1]]);
                    }
                }
            }

            if (name.Length > 0)
            {
                result.Add(
                    new RuntimeOmsiControllerConfig(
                        name,
                        active,
                        axes,
                        buttons,
                        ffCenter,
                        ffEffects));
            }
        }

        return result;
    }

    private static List<int> NextMeaningful(
        IReadOnlyList<string> lines,
        int start,
        int count,
        int end)
    {
        var result =
            new List<int>(
                count);

        for (var index =
                 Math.Max(
                     start,
                     0);
             index < end &&
             index <
                 lines.Count &&
             result.Count <
                 count;
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

    private static int TryParseInt(
        string value,
        int fallback) =>
        int.TryParse(
            value.Trim(),
            out var parsed)
            ? parsed
            : fallback;

    private static double TryParseDouble(
        string value) =>
        double.TryParse(
            value
                .Trim()
                .Replace(
                    ',',
                    '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) &&
        double.IsFinite(
            parsed)
            ? parsed
            : 0.0;
}
