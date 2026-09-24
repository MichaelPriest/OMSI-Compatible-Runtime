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
        action =
            default;

        if (binding.Shift &&
            !binding.Control &&
            binding.Key == Keys.Z)
        {
            action =
                RuntimeOmsiHostInputAction.StatusInfoCycle;
            return true;
        }

        if (binding.Shift ||
            binding.Control)
        {
            return false;
        }

        action =
            binding.Key switch
            {
                Keys.NumPad8 =>
                    RuntimeOmsiHostInputAction.Accelerate,
                Keys.NumPad2 =>
                    RuntimeOmsiHostInputAction.BrakeIncrease,
                Keys.Add =>
                    RuntimeOmsiHostInputAction.BrakeRelease,
                Keys.NumPad4 =>
                    RuntimeOmsiHostInputAction.SteerLeft,
                Keys.NumPad6 =>
                    RuntimeOmsiHostInputAction.SteerRight,
                Keys.NumPad5 =>
                    RuntimeOmsiHostInputAction.SteerCenter,
                Keys.Tab =>
                    RuntimeOmsiHostInputAction.Clutch,
                Keys.E =>
                    RuntimeOmsiHostInputAction.ElectricalToggle,
                Keys.M =>
                    RuntimeOmsiHostInputAction.EngineToggle,
                Keys.D =>
                    RuntimeOmsiHostInputAction.GearDrive,
                Keys.N =>
                    RuntimeOmsiHostInputAction.GearNeutral,
                Keys.R =>
                    RuntimeOmsiHostInputAction.GearReverse,
                Keys.Decimal or
                Keys.OemPeriod =>
                    RuntimeOmsiHostInputAction.ParkingBrakeToggle,
                Keys.Subtract =>
                    RuntimeOmsiHostInputAction.StopBrakeToggle,
                Keys.O =>
                    RuntimeOmsiHostInputAction.MouseDriveToggle,
                Keys.F1 =>
                    RuntimeOmsiHostInputAction.DriverView,
                Keys.F2 =>
                    RuntimeOmsiHostInputAction.PassengerView,
                Keys.F3 =>
                    RuntimeOmsiHostInputAction.ExteriorView,
                Keys.F4 =>
                    RuntimeOmsiHostInputAction.FreeCameraView,
                Keys.Insert =>
                    RuntimeOmsiHostInputAction.ScheduleView,
                Keys.Home =>
                    RuntimeOmsiHostInputAction.TicketSellingView,
                Keys.Left =>
                    RuntimeOmsiHostInputAction.InteriorViewNext,
                Keys.Right =>
                    RuntimeOmsiHostInputAction.InteriorViewPrevious,
                Keys.K =>
                    RuntimeOmsiHostInputAction.ControllerToggle,
                Keys.P =>
                    RuntimeOmsiHostInputAction.PauseToggle,
                Keys.S =>
                    RuntimeOmsiHostInputAction.ScrollViews,
                Keys.C =>
                    RuntimeOmsiHostInputAction.ResetCurrentView,
                Keys.Space =>
                    RuntimeOmsiHostInputAction.ResetAllViews,
                _ =>
                    default
            };

        return binding.Key is
            Keys.NumPad8 or
            Keys.NumPad2 or
            Keys.Add or
            Keys.NumPad4 or
            Keys.NumPad6 or
            Keys.NumPad5 or
            Keys.Tab or
            Keys.E or
            Keys.M or
            Keys.D or
            Keys.N or
            Keys.R or
            Keys.Decimal or
            Keys.OemPeriod or
            Keys.Subtract or
            Keys.O or
            Keys.F1 or
            Keys.F2 or
            Keys.F3 or
            Keys.F4 or
            Keys.Insert or
            Keys.Home or
            Keys.Right or
            Keys.Left or
            Keys.K or
            Keys.P or
            Keys.S or
            Keys.C or
            Keys.Space;
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
