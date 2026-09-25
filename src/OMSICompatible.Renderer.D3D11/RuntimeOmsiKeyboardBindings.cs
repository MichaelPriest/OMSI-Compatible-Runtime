using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeOmsiHostInputAction
{
    Accelerate,
    BrakeIncrease,
    BrakeRelease,
    SteerLeft,
    SteerRight,
    SteerCenter,
    Clutch,
    ElectricalToggle,
    EngineToggle,
    EngineStart,
    EngineOff,
    GearDrive,
    GearNeutral,
    GearReverse,
    ParkingBrakeToggle,
    ParkingBrakeSet,
    ParkingBrakeRelease,
    StopBrakeToggle,
    MouseDriveToggle,
    DriverView,
    PassengerView,
    ExteriorView,
    FreeCameraView,
    ScheduleView,
    TicketSellingView,
    InteriorViewNext,
    InteriorViewPrevious,
    ResetDriverView,
    ControllerToggle,
    PauseToggle,
    ResetCurrentView,
    ResetAllViews,
    ScrollViews,
    StatusInfoCycle
}

internal sealed record RuntimeOmsiKeyboardBinding(
    Keys Key,
    string Trigger,
    bool Continuous,
    bool Shift,
    bool Control,
    RuntimeOmsiHostInputAction? HostAction);

internal static class RuntimeOmsiKeyboardBindings
{
    private sealed record ParsedBinding(
        Keys Key,
        string Trigger,
        bool Continuous,
        bool Shift,
        bool Control);

    public static IReadOnlyList<RuntimeOmsiKeyboardBinding> Load(
        string contentRoot,
        string? preferredLanguage)
    {
        var inputs =
            Path.Combine(
                contentRoot,
                "Inputs");

        var keyboardPath =
            Path.Combine(
                inputs,
                "keyboard.cfg");

        if (!File.Exists(
                keyboardPath) ||
            !Directory.Exists(
                inputs))
        {
            return Array.Empty<
                RuntimeOmsiKeyboardBinding>();
        }

        var keyIndexToWindowsKey =
            LoadKeyTable(
                inputs,
                preferredLanguage,
                out var keyTablePath);

        if (keyIndexToWindowsKey.Count == 0)
        {
            return Array.Empty<
                RuntimeOmsiKeyboardBinding>();
        }

        var hostActions =
            LoadHostActions(
                inputs,
                keyIndexToWindowsKey);

        var parsed =
            ParseBindings(
                keyboardPath,
                keyIndexToWindowsKey);

        var bindings =
            parsed
                .Select(
                    binding =>
                        new RuntimeOmsiKeyboardBinding(
                            binding.Key,
                            binding.Trigger,
                            binding.Continuous,
                            binding.Shift,
                            binding.Control,
                            hostActions.TryGetValue(
                                binding.Trigger,
                                out var hostAction)
                                ? hostAction
                                : null))
                .Distinct()
                .ToArray();

        WriteKeyboardAudit(
            keyboardPath,
            keyTablePath,
            CountEntries(
                keyboardPath),
            parsed.Count,
            bindings);

        return bindings;
    }

    public static IReadOnlyDictionary<
        string,
        RuntimeOmsiHostInputAction>
        LoadHostActions(
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
            return new Dictionary<
                string,
                RuntimeOmsiHostInputAction>(
                StringComparer.OrdinalIgnoreCase);
        }

        var keyIndexToWindowsKey =
            LoadKeyTable(
                inputs,
                preferredLanguage,
                out _);

        return LoadHostActions(
            inputs,
            keyIndexToWindowsKey);
    }

    private static IReadOnlyDictionary<
        string,
        RuntimeOmsiHostInputAction>
        LoadHostActions(
            string inputs,
            IReadOnlyDictionary<int, Keys>
                keyIndexToWindowsKey)
    {
        var result =
            new Dictionary<
                string,
                RuntimeOmsiHostInputAction>(
                StringComparer.OrdinalIgnoreCase);

        var resetPath =
            Path.Combine(
                inputs,
                "keyboard_reset.cfg");

        if (!File.Exists(
                resetPath) ||
            keyIndexToWindowsKey.Count == 0)
        {
            return result;
        }

        foreach (var binding in
                 ParseBindings(
                     resetPath,
                     keyIndexToWindowsKey))
        {
            if (TryResolveDefaultHostAction(
                    binding,
                    out var action))
            {
                result[
                    binding.Trigger] =
                    action;
            }
        }

        return result;
    }

    private static bool TryResolveDefaultHostAction(
        ParsedBinding binding,
        out RuntimeOmsiHostInputAction action)
    {
        // keyboard_reset.cfg describes OMSI's default bindings, but the
        // semantic host action belongs to the trigger, not to whichever
        // physical key happens to be assigned there. Mapping by key made
        // user remaps such as a door on NumPad3 accidentally release the
        // brake, or blinker_off on Decimal toggle the parking brake.
        var trigger =
            binding.Trigger
                .Trim()
                .ToLowerInvariant();

        action =
            trigger switch
            {
                "throttle" =>
                    RuntimeOmsiHostInputAction.Accelerate,
                "brake" =>
                    RuntimeOmsiHostInputAction.BrakeIncrease,
                "steering_left" =>
                    RuntimeOmsiHostInputAction.SteerLeft,
                "steering_right" =>
                    RuntimeOmsiHostInputAction.SteerRight,
                "steering_neutral" =>
                    RuntimeOmsiHostInputAction.SteerCenter,
                "clutch" =>
                    RuntimeOmsiHostInputAction.Clutch,

                "kw_batterietrennschalter" or
                "cp_batterietrennschalter_toggle" =>
                    RuntimeOmsiHostInputAction.ElectricalToggle,

                "kw_m_enginestart" or
                "kw_m_engine_startbutton" =>
                    RuntimeOmsiHostInputAction.EngineStart,

                "kw_m_engineshutdown" =>
                    RuntimeOmsiHostInputAction.EngineOff,

                "automatic_d" or
                "automatic_1" or
                "automatic_2" or
                "kw_s_1" or
                "kw_s_2" =>
                    RuntimeOmsiHostInputAction.GearDrive,

                "automatic_n" or
                "kw_s_n" =>
                    RuntimeOmsiHostInputAction.GearNeutral,

                "automatic_r" or
                "kw_s_r" =>
                    RuntimeOmsiHostInputAction.GearReverse,

                "parking_brake_toggle" or
                "parking_brake_mouse" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeToggle,

                "parking_brake_set" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeSet,

                "parking_brake_release" =>
                    RuntimeOmsiHostInputAction.ParkingBrakeRelease,

                "toggel_mouse_ctrl" or
                "toggle_mouse_ctrl" =>
                    RuntimeOmsiHostInputAction.MouseDriveToggle,

                "toggel_ctrler" or
                "toggle_ctrler" or
                "toggle_controller" =>
                    RuntimeOmsiHostInputAction.ControllerToggle,

                "view_set_driver" =>
                    RuntimeOmsiHostInputAction.DriverView,

                "view_set_passenger" =>
                    RuntimeOmsiHostInputAction.PassengerView,

                "view_set_outside" =>
                    RuntimeOmsiHostInputAction.ExteriorView,

                "view_set_map" =>
                    RuntimeOmsiHostInputAction.FreeCameraView,

                "view_set_schedule" or
                "view_schedule" =>
                    RuntimeOmsiHostInputAction.ScheduleView,

                "view_set_ticketselling" or
                "view_ticketselling" =>
                    RuntimeOmsiHostInputAction.TicketSellingView,

                "view_interiorcam_minus" =>
                    RuntimeOmsiHostInputAction.InteriorViewNext,

                "view_interiorcam_plus" =>
                    RuntimeOmsiHostInputAction.InteriorViewPrevious,

                "view_reset_direction" =>
                    RuntimeOmsiHostInputAction.ResetCurrentView,

                "view_reset_all_directions" =>
                    RuntimeOmsiHostInputAction.ResetAllViews,

                "view_toggle_viewpoint" =>
                    RuntimeOmsiHostInputAction.ScrollViews,

                "view_toggle_informationdisplay" or
                "view_status" or
                "view_information" or
                "view_info" =>
                    RuntimeOmsiHostInputAction.StatusInfoCycle,

                "sim_pause" or
                "pause" =>
                    RuntimeOmsiHostInputAction.PauseToggle,

                _ =>
                    default
            };

        return trigger is
            "throttle" or
            "brake" or
            "steering_left" or
            "steering_right" or
            "steering_neutral" or
            "clutch" or
            "kw_batterietrennschalter" or
            "cp_batterietrennschalter_toggle" or
            "kw_m_enginestart" or
            "kw_m_engine_startbutton" or
            "kw_m_engineshutdown" or
            "automatic_d" or
            "automatic_1" or
            "automatic_2" or
            "kw_s_1" or
            "kw_s_2" or
            "automatic_n" or
            "kw_s_n" or
            "automatic_r" or
            "kw_s_r" or
            "parking_brake_toggle" or
            "parking_brake_mouse" or
            "parking_brake_set" or
            "parking_brake_release" or
            "toggel_mouse_ctrl" or
            "toggle_mouse_ctrl" or
            "toggel_ctrler" or
            "toggle_ctrler" or
            "toggle_controller" or
            "view_set_driver" or
            "view_set_passenger" or
            "view_set_outside" or
            "view_set_map" or
            "view_set_schedule" or
            "view_schedule" or
            "view_set_ticketselling" or
            "view_ticketselling" or
            "view_interiorcam_minus" or
            "view_interiorcam_plus" or
            "view_reset_direction" or
            "view_reset_all_directions" or
            "view_toggle_viewpoint" or
            "view_toggle_informationdisplay" or
            "view_status" or
            "view_information" or
            "view_info" or
            "sim_pause" or
            "pause";
    }

    private static IReadOnlyList<ParsedBinding>
        ParseBindings(
            string path,
            IReadOnlyDictionary<int, Keys>
                keyIndexToWindowsKey)
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
            return Array.Empty<
                ParsedBinding>();
        }

        var result =
            new List<
                ParsedBinding>();

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
                NextMeaningful(
                    lines,
                    index + 1,
                    3);

            if (values.Count < 3)
            {
                continue;
            }

            var trigger =
                lines[values[0]]
                    .Trim()
                    .Trim('"');

            if (trigger.Length == 0 ||
                !int.TryParse(
                    lines[values[1]]
                        .Trim(),
                    out var keyIndex) ||
                !int.TryParse(
                    lines[values[2]]
                        .Trim(),
                    out var flags) ||
                !keyIndexToWindowsKey.TryGetValue(
                    keyIndex,
                    out var key))
            {
                continue;
            }

            result.Add(
                new ParsedBinding(
                    key,
                    trigger,
                    Continuous:
                        (flags & 1) != 0,
                    Shift:
                        (flags & 2) != 0,
                    Control:
                        (flags & 4) != 0));
        }

        return result;
    }

    private static Dictionary<int, Keys> LoadKeyTable(
        string inputs,
        string? preferredLanguage,
        out string? selectedPath)
    {
        var candidates =
            new List<string>();

        // keyboard.cfg stores key-table indices, so those indices must be
        // interpreted with the same language table selected by OMSI.
        // Falling back to ENG first can map physical keys incorrectly.
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

        var path =
            candidates.FirstOrDefault(
                File.Exists) ??
            Directory
                .EnumerateFiles(
                    inputs,
                    "*.kyb",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

        selectedPath =
            path;

        var result =
            new Dictionary<int, Keys>();

        if (path is null)
        {
            return result;
        }

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

            var split =
                trimmed.Split(
                    [' ', '\t'],
                    2,
                    StringSplitOptions.RemoveEmptyEntries);

            if (split.Length < 2 ||
                !int.TryParse(
                    split[0],
                    out var index) ||
                !TryResolveWindowsKey(
                    split[1],
                    out var key))
            {
                continue;
            }

            result[index] =
                key;
        }

        return result;
    }

    private static bool TryResolveWindowsKey(
        string name,
        out Keys key)
    {
        key =
            Keys.None;

        var normalized =
            Normalize(
                name);

        if (normalized.Length == 1)
        {
            var character =
                normalized[0];

            if (character is >= 'a' and <= 'z')
            {
                key =
                    (Keys)(
                        (int)Keys.A +
                        (character - 'a'));
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                key =
                    (Keys)(
                        (int)Keys.D0 +
                        (character - '0'));
                return true;
            }
        }

        if (normalized.StartsWith(
                "f",
                StringComparison.Ordinal) &&
            int.TryParse(
                normalized[1..],
                out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key =
                (Keys)(
                    (int)Keys.F1 +
                    (functionKey - 1));
            return true;
        }

        for (var number = 0;
             number <= 9;
             number++)
        {
            if (normalized is
                var value &&
                (value ==
                     $"num{number}" ||
                 value ==
                     $"numpad{number}" ||
                 value ==
                     $"numeric{number}"))
            {
                key =
                    (Keys)(
                        (int)Keys.NumPad0 +
                        number);
                return true;
            }
        }

        var special =
            new Dictionary<string, Keys>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["escape"] = Keys.Escape,
                ["esc"] = Keys.Escape,
                ["space"] = Keys.Space,
                ["spacebar"] = Keys.Space,
                ["enter"] = Keys.Enter,
                ["return"] = Keys.Enter,
                ["numenter"] = Keys.Enter,
                ["numpadenter"] = Keys.Enter,
                ["tab"] = Keys.Tab,
                ["backspace"] = Keys.Back,
                ["back"] = Keys.Back,
                ["insert"] = Keys.Insert,
                ["einfg"] = Keys.Insert,
                ["delete"] = Keys.Delete,
                ["del"] = Keys.Delete,
                ["entf"] = Keys.Delete,
                ["home"] = Keys.Home,
                ["pos1"] = Keys.Home,
                ["end"] = Keys.End,
                ["pageup"] = Keys.PageUp,
                ["pgup"] = Keys.PageUp,
                ["bildauf"] = Keys.PageUp,
                ["pagedown"] = Keys.PageDown,
                ["pgdn"] = Keys.PageDown,
                ["bildab"] = Keys.PageDown,
                ["left"] = Keys.Left,
                ["arrowleft"] = Keys.Left,
                ["leftarrow"] = Keys.Left,
                ["right"] = Keys.Right,
                ["arrowright"] = Keys.Right,
                ["rightarrow"] = Keys.Right,
                ["up"] = Keys.Up,
                ["arrowup"] = Keys.Up,
                ["uparrow"] = Keys.Up,
                ["down"] = Keys.Down,
                ["arrowdown"] = Keys.Down,
                ["downarrow"] = Keys.Down,
                ["num+"] = Keys.Add,
                ["numpad+"] = Keys.Add,
                ["num-"] = Keys.Subtract,
                ["numpad-"] = Keys.Subtract,
                ["num*"] = Keys.Multiply,
                ["numpad*"] = Keys.Multiply,
                ["num/"] = Keys.Divide,
                ["numpad/"] = Keys.Divide,
                ["num."] = Keys.Decimal,
                ["numpad."] = Keys.Decimal,
                ["decimal"] = Keys.Decimal,
                ["numdecimal"] = Keys.Decimal,
                ["."] = Keys.OemPeriod,
                ["period"] = Keys.OemPeriod,
                ["fullstop"] = Keys.OemPeriod,
                ["dot"] = Keys.OemPeriod,
                ["punkt"] = Keys.OemPeriod,
                [","] = Keys.Oemcomma,
                ["comma"] = Keys.Oemcomma,
                ["komma"] = Keys.Oemcomma,
                ["minus"] = Keys.OemMinus,
                ["plus"] = Keys.Oemplus,
                ["shift"] = Keys.ShiftKey,
                ["ctrl"] = Keys.ControlKey,
                ["control"] = Keys.ControlKey,
                ["alt"] = Keys.Menu,
                ["capslock"] = Keys.CapsLock,
                ["scrolllock"] = Keys.Scroll,
                ["rollen"] = Keys.Scroll,
                ["pause"] = Keys.Pause
            };

        if (special.TryGetValue(
                normalized,
                out key))
        {
            return true;
        }

        return Enum.TryParse(
            name
                .Replace(
                    " ",
                    string.Empty),
            ignoreCase:
                true,
            out key);
    }

    private static int CountEntries(
        string path)
    {
        try
        {
            return File.ReadLines(
                    path)
                .Count(
                    line =>
                        string.Equals(
                            line.Trim(),
                            "[entry]",
                            StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return 0;
        }
    }

    private static void WriteKeyboardAudit(
        string keyboardPath,
        string? keyTablePath,
        int entryCount,
        int resolvedEntryCount,
        IReadOnlyList<RuntimeOmsiKeyboardBinding> bindings)
    {
        try
        {
            var lines =
                new List<string>
                {
                    $"timestamp={DateTimeOffset.Now:O}",
                    $"keyboard={keyboardPath}",
                    $"keyTable={keyTablePath ?? "<none>"}",
                    $"entries={entryCount}",
                    $"resolvedEntries={resolvedEntryCount}",
                    $"distinctBindings={bindings.Count}",
                    $"unresolvedEntries={Math.Max(entryCount - resolvedEntryCount, 0)}",
                    $"hostMapped={bindings.Count(binding => binding.HostAction.HasValue)}",
                    "",
                    "bindings:"
                };

            foreach (var binding in
                     bindings.OrderBy(
                         static item =>
                             item.Key)
                     .ThenBy(
                         static item =>
                             item.Trigger,
                         StringComparer.OrdinalIgnoreCase))
            {
                var modifiers =
                    string.Join(
                        "+",
                        new[]
                        {
                            binding.Control
                                ? "Ctrl"
                                : null,
                            binding.Shift
                                ? "Shift"
                                : null
                        }.Where(
                            static item =>
                                item is not null));

                var key =
                    string.IsNullOrWhiteSpace(
                        modifiers)
                        ? binding.Key.ToString()
                        : modifiers +
                          "+" +
                          binding.Key;

                lines.Add(
                    $"{key} | trigger={binding.Trigger} | continuous={binding.Continuous} | host={binding.HostAction?.ToString() ?? "<script>"}");
            }

            File.WriteAllLines(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "keyboard-runtime-audit.log"),
                lines);
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[input-keyboard] audit unavailable: {exception.Message}");
        }
    }

    private static string Normalize(
        string value) =>
        new string(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(
                    static character =>
                        char.IsLetterOrDigit(
                            character) ||
                        character is
                            '+' or '-' or
                            '*' or '/' or '.')
                .ToArray());

    private static List<int> NextMeaningful(
        IReadOnlyList<string> lines,
        int start,
        int count)
    {
        var result =
            new List<int>(
                count);

        for (var index = start;
             index < lines.Count &&
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
