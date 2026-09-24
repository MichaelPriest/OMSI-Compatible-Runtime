using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal sealed record RuntimeOmsiKeyboardBinding(
    Keys Key,
    string Trigger,
    bool Continuous,
    bool Shift,
    bool Control);

internal static class RuntimeOmsiKeyboardBindings
{
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
                preferredLanguage);

        if (keyIndexToWindowsKey.Count == 0)
        {
            return Array.Empty<
                RuntimeOmsiKeyboardBinding>();
        }

        string[] lines;

        try
        {
            lines =
                File.ReadAllLines(
                    keyboardPath);
        }
        catch
        {
            return Array.Empty<
                RuntimeOmsiKeyboardBinding>();
        }

        var result =
            new List<
                RuntimeOmsiKeyboardBinding>();

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
                    .Trim();

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
                new RuntimeOmsiKeyboardBinding(
                    key,
                    trigger,
                    Continuous:
                        (flags & 1) != 0,
                    Shift:
                        (flags & 2) != 0,
                    Control:
                        (flags & 4) != 0));
        }

        return result
            .Distinct()
            .ToArray();
    }

    private static Dictionary<int, Keys> LoadKeyTable(
        string inputs,
        string? preferredLanguage)
    {
        var candidates =
            new List<string>
            {
                Path.Combine(
                    inputs,
                    "ENG.kyb")
            };

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
                ["tab"] = Keys.Tab,
                ["backspace"] = Keys.Back,
                ["back"] = Keys.Back,
                ["insert"] = Keys.Insert,
                ["delete"] = Keys.Delete,
                ["home"] = Keys.Home,
                ["end"] = Keys.End,
                ["pageup"] = Keys.PageUp,
                ["pgup"] = Keys.PageUp,
                ["pagedown"] = Keys.PageDown,
                ["pgdn"] = Keys.PageDown,
                ["left"] = Keys.Left,
                ["arrowleft"] = Keys.Left,
                ["right"] = Keys.Right,
                ["arrowright"] = Keys.Right,
                ["up"] = Keys.Up,
                ["arrowup"] = Keys.Up,
                ["down"] = Keys.Down,
                ["arrowdown"] = Keys.Down,
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
                ["shift"] = Keys.ShiftKey,
                ["ctrl"] = Keys.ControlKey,
                ["control"] = Keys.ControlKey,
                ["alt"] = Keys.Menu,
                ["capslock"] = Keys.CapsLock,
                ["scrolllock"] = Keys.Scroll,
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
