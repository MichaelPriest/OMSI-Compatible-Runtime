using System.Globalization;
using OmsiCompat.Core;
using OmsiCompat.Vehicles;

namespace OmsiCompat.Scripting;

public static class OmsiScriptCatalogLoader
{
    public static OmsiScriptCatalog Load(
        OmsiVehicleScriptManifest manifest) =>
        LoadCore(
            manifest,
            null);

    public static OmsiScriptCatalog Load(
        OmsiContentRoot contentRoot,
        OmsiVehicleScriptManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(
            contentRoot);

        return LoadCore(
            manifest,
            contentRoot.ProgramPath);
    }

    private static OmsiScriptCatalog LoadCore(
        OmsiVehicleScriptManifest manifest,
        string? programPath)
    {
        ArgumentNullException.ThrowIfNull(
            manifest);

        var diagnostics =
            new List<string>();

        var numeric =
            LoadVariableNames(
                manifest.VariableLists,
                diagnostics);

        var strings =
            LoadVariableNames(
                manifest.StringVariableLists,
                diagnostics);

        var systemVariables =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var vehicleCallbacks =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var sceneryCallbacks =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var scriptTextureCallbacks =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(
                programPath))
        {
            UnionOptionalNameFile(
                numeric,
                Path.Combine(
                    programPath,
                    "varlist_roadvehicle.txt"));

            UnionOptionalNameFile(
                strings,
                Path.Combine(
                    programPath,
                    "stringvarlist_roadvehicle.txt"));

            UnionOptionalNameFile(
                systemVariables,
                Path.Combine(
                    programPath,
                    "varlist_system.txt"));

            UnionOptionalNameFile(
                vehicleCallbacks,
                Path.Combine(
                    programPath,
                    "callbacklist_roadvehicle.txt"));

            UnionOptionalNameFile(
                sceneryCallbacks,
                Path.Combine(
                    programPath,
                    "callbacklist_scenobj.txt"));

            UnionOptionalNameFile(
                scriptTextureCallbacks,
                Path.Combine(
                    programPath,
                    "callbacklist_scripttex.txt"));
        }

        var constants =
            new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);

        var curves =
            new Dictionary<string, OmsiScriptCurve>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var file in
                 manifest.ConstantFiles)
        {
            if (file.ResolvedPath is null)
            {
                diagnostics.Add(
                    $"Missing constfile: {file.DeclaredPath}");
                continue;
            }

            LoadConstFile(
                file.ResolvedPath,
                constants,
                curves,
                diagnostics);
        }

        var program =
            OmsiScriptProgramLoader.Load(
                manifest.ScriptFiles,
                diagnostics);

        return new OmsiScriptCatalog(
            numeric,
            strings,
            systemVariables,
            vehicleCallbacks,
            sceneryCallbacks,
            scriptTextureCallbacks,
            constants,
            curves,
            program,
            diagnostics);
    }

    private static HashSet<string>
        LoadVariableNames(
            IReadOnlyList<OmsiVehicleFileReference> files,
            ICollection<string> diagnostics)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file.ResolvedPath is null)
            {
                diagnostics.Add(
                    $"Missing variable list: {file.DeclaredPath}");
                continue;
            }

            UnionNameFile(
                result,
                file.ResolvedPath);
        }

        return result;
    }

    private static void UnionOptionalNameFile(
        ISet<string> target,
        string path)
    {
        if (!File.Exists(
                path))
        {
            return;
        }

        UnionNameFile(
            target,
            path);
    }

    private static void UnionNameFile(
        ISet<string> target,
        string path)
    {
        foreach (var raw in
                 OmsiText.ReadAllLines(
                     path))
        {
            if (IsComment(
                    raw))
            {
                continue;
            }

            var value =
                raw.Trim();

            if (value.Length > 0)
            {
                target.Add(
                    value);
            }
        }
    }

    private static void LoadConstFile(
        string path,
        IDictionary<string, double> constants,
        IDictionary<string, OmsiScriptCurve> curves,
        ICollection<string> diagnostics)
    {
        var lines =
            OmsiText.ReadAllLines(
                path);

        string? currentCurve = null;
        var currentPoints =
            new List<OmsiCurvePoint>();

        void CommitCurve()
        {
            if (currentCurve is null)
            {
                return;
            }

            if (currentPoints.Count > 0)
            {
                curves[currentCurve] =
                    new OmsiScriptCurve(
                        currentCurve,
                        currentPoints
                            .OrderBy(
                                static point =>
                                    point.X)
                            .ToArray());
            }

            currentCurve = null;
            currentPoints =
                new List<OmsiCurvePoint>();
        }

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var raw =
                lines[index];

            if (IsComment(
                    raw))
            {
                continue;
            }

            var token =
                raw.Trim();

            if (token.Length == 0)
            {
                continue;
            }

            if (token.Equals(
                    "[const]",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommitCurve();

                if (!TryReadValueLine(
                        lines,
                        ref index,
                        out var name) ||
                    !TryReadValueLine(
                        lines,
                        ref index,
                        out var valueText) ||
                    !TryDouble(
                        valueText,
                        out var value))
                {
                    diagnostics.Add(
                        $"Invalid [const] near {path}:{index + 1}");
                    continue;
                }

                constants[name] =
                    value;
                continue;
            }

            if (token.Equals(
                    "[newcurve]",
                    StringComparison.OrdinalIgnoreCase))
            {
                CommitCurve();

                if (!TryReadValueLine(
                        lines,
                        ref index,
                        out currentCurve))
                {
                    diagnostics.Add(
                        $"Invalid [newcurve] near {path}:{index + 1}");
                }

                continue;
            }

            if (token.Equals(
                    "[pnt]",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (currentCurve is null ||
                    !TryReadValueLine(
                        lines,
                        ref index,
                        out var xText) ||
                    !TryReadValueLine(
                        lines,
                        ref index,
                        out var yText) ||
                    !TryDouble(
                        xText,
                        out var x) ||
                    !TryDouble(
                        yText,
                        out var y))
                {
                    diagnostics.Add(
                        $"Invalid [pnt] near {path}:{index + 1}");
                    continue;
                }

                currentPoints.Add(
                    new OmsiCurvePoint(
                        x,
                        y));
            }
        }

        CommitCurve();
    }

    private static bool TryReadValueLine(
        IReadOnlyList<string> lines,
        ref int index,
        out string value)
    {
        while (++index <
               lines.Count)
        {
            var raw =
                lines[index];

            if (IsComment(
                    raw))
            {
                continue;
            }

            var trimmed =
                raw.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            value = trimmed;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsComment(
        string line) =>
            line.Length > 0 &&
            line[0] == (char)39;

    private static bool TryDouble(
        string value,
        out double result) =>
            double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result) &&
            double.IsFinite(
                result);
}
