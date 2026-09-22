using System.Text;
using OmsiCompat.Core;
using OmsiCompat.Vehicles;

namespace OmsiCompat.Scripting;

public static class OmsiScriptProgramLoader
{
    public static OmsiScriptProgram Load(
        IReadOnlyList<OmsiVehicleFileReference> files,
        ICollection<string>? diagnostics = null)
    {
        var init =
            new List<OmsiScriptBlock>();
        var frame =
            new List<OmsiScriptBlock>();
        var frameAi =
            new List<OmsiScriptBlock>();
        var macros =
            new Dictionary<string, OmsiScriptBlock>(
                StringComparer.Ordinal);
        var triggers =
            new Dictionary<string, OmsiScriptBlock>(
                StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (file.ResolvedPath is null)
            {
                diagnostics?.Add(
                    $"Missing script: {file.DeclaredPath}");
                continue;
            }

            ParseFile(
                file.ResolvedPath,
                init,
                frame,
                frameAi,
                macros,
                triggers,
                diagnostics);
        }

        return new OmsiScriptProgram(
            init,
            frame,
            frameAi,
            macros,
            triggers);
    }

    private static void ParseFile(
        string path,
        ICollection<OmsiScriptBlock> init,
        ICollection<OmsiScriptBlock> frame,
        ICollection<OmsiScriptBlock> frameAi,
        IDictionary<string, OmsiScriptBlock> macros,
        IDictionary<string, OmsiScriptBlock> triggers,
        ICollection<string>? diagnostics)
    {
        var lines =
            OmsiText.ReadAllLines(
                path);

        OmsiScriptBlockKind? currentKind =
            null;
        string? currentName =
            null;
        var headerLine =
            0;
        var tokens =
            new List<string>();

        void Commit()
        {
            if (!currentKind.HasValue)
            {
                return;
            }

            var block =
                new OmsiScriptBlock(
                    currentKind.Value,
                    currentName,
                    path,
                    headerLine,
                    tokens.ToArray());

            switch (block.Kind)
            {
                case OmsiScriptBlockKind.Init:
                    init.Add(block);
                    break;

                case OmsiScriptBlockKind.Frame:
                    frame.Add(block);
                    break;

                case OmsiScriptBlockKind.FrameAi:
                    frameAi.Add(block);
                    break;

                case OmsiScriptBlockKind.Macro:
                    if (block.Name is not null)
                    {
                        macros[block.Name] =
                            block;
                    }
                    break;

                case OmsiScriptBlockKind.Trigger:
                    if (block.Name is not null)
                    {
                        triggers[block.Name] =
                            block;
                    }
                    break;
            }

            currentKind = null;
            currentName = null;
            tokens =
                new List<string>();
        }

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var raw =
                lines[index];

            if (raw.Length > 0 &&
                raw[0] == '\'')
            {
                continue;
            }

            var trimmed =
                raw.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (TryEntryPoint(
                    trimmed,
                    out var kind,
                    out var name))
            {
                if (currentKind.HasValue)
                {
                    diagnostics?.Add(
                        $"Nested OMSI script entry point ignored: {path}:{index + 1}");
                    Commit();
                }

                currentKind =
                    kind;
                currentName =
                    name;
                headerLine =
                    index + 1;
                continue;
            }

            if (trimmed.Equals(
                    "{end}",
                    StringComparison.OrdinalIgnoreCase))
            {
                Commit();
                continue;
            }

            if (!currentKind.HasValue)
            {
                continue;
            }

            foreach (var token in
                     TokenizeLine(
                         trimmed))
            {
                tokens.Add(
                    token);
            }
        }

        if (currentKind.HasValue)
        {
            diagnostics?.Add(
                $"Unclosed OMSI script block: {path}:{headerLine}");
            Commit();
        }
    }

    private static IReadOnlyList<string>
        TokenizeLine(
            string line)
    {
        var tokens =
            new List<string>();

        var current =
            new StringBuilder();

        var inString = false;

        void Commit()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(
                current.ToString());
            current.Clear();
        }

        foreach (var character in line)
        {
            if (!inString &&
                character == (char)39)
            {
                break;
            }

            if (character == '"')
            {
                current.Append(
                    character);
                inString =
                    !inString;
                continue;
            }

            if (!inString &&
                char.IsWhiteSpace(
                    character))
            {
                Commit();
                continue;
            }

            current.Append(
                character);
        }

        Commit();

        return tokens;
    }

    private static bool TryEntryPoint(
        string text,
        out OmsiScriptBlockKind kind,
        out string? name)
    {
        name = null;

        if (text.Equals(
                "{init}",
                StringComparison.OrdinalIgnoreCase))
        {
            kind =
                OmsiScriptBlockKind.Init;
            return true;
        }

        if (text.Equals(
                "{frame}",
                StringComparison.OrdinalIgnoreCase))
        {
            kind =
                OmsiScriptBlockKind.Frame;
            return true;
        }

        if (text.Equals(
                "{frame_ai}",
                StringComparison.OrdinalIgnoreCase))
        {
            kind =
                OmsiScriptBlockKind.FrameAi;
            return true;
        }

        if (TryNamedEntryPoint(
                text,
                "macro",
                out name))
        {
            kind =
                OmsiScriptBlockKind.Macro;
            return true;
        }

        if (TryNamedEntryPoint(
                text,
                "trigger",
                out name))
        {
            kind =
                OmsiScriptBlockKind.Trigger;
            return true;
        }

        kind =
            default;
        return false;
    }

    private static bool TryNamedEntryPoint(
        string text,
        string prefix,
        out string? name)
    {
        name = null;

        if (text.Length <
                prefix.Length + 3 ||
            text[0] != '{' ||
            text[^1] != '}')
        {
            return false;
        }

        var body =
            text[1..^1];

        var marker =
            prefix + ":";

        if (!body.StartsWith(
                marker,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parsed =
            body[marker.Length..]
                .Trim();

        if (parsed.Length == 0)
        {
            return false;
        }

        name = parsed;
        return true;
    }
}
