namespace OmsiCompat.Scripting;

public sealed record OmsiCurvePoint(
    double X,
    double Y);

public sealed record OmsiScriptCurve(
    string Name,
    IReadOnlyList<OmsiCurvePoint> Points)
{
    public double Evaluate(
        double x)
    {
        if (Points.Count == 0)
        {
            return 0.0;
        }

        if (x <= Points[0].X)
        {
            return Points[0].Y;
        }

        if (x >= Points[^1].X)
        {
            return Points[^1].Y;
        }

        for (var index = 1;
             index < Points.Count;
             index++)
        {
            var right =
                Points[index];

            if (x > right.X)
            {
                continue;
            }

            var left =
                Points[index - 1];

            var span =
                right.X -
                left.X;

            if (Math.Abs(span) <
                double.Epsilon)
            {
                return right.Y;
            }

            var t =
                (x - left.X) /
                span;

            return left.Y +
                (right.Y - left.Y) *
                t;
        }

        return Points[^1].Y;
    }
}

public enum OmsiScriptBlockKind
{
    Init,
    Frame,
    FrameAi,
    Macro,
    Trigger
}

public sealed record OmsiScriptBlock(
    OmsiScriptBlockKind Kind,
    string? Name,
    string SourcePath,
    int HeaderLineNumber,
    IReadOnlyList<string> Tokens);

public sealed record OmsiScriptProgram(
    IReadOnlyList<OmsiScriptBlock> InitBlocks,
    IReadOnlyList<OmsiScriptBlock> FrameBlocks,
    IReadOnlyList<OmsiScriptBlock> FrameAiBlocks,
    IReadOnlyDictionary<string, OmsiScriptBlock> Macros,
    IReadOnlyDictionary<string, OmsiScriptBlock> Triggers);

public sealed record OmsiScriptCatalog(
    IReadOnlySet<string> NumericVariables,
    IReadOnlySet<string> StringVariables,
    IReadOnlyDictionary<string, double> Constants,
    IReadOnlyDictionary<string, OmsiScriptCurve> Curves,
    OmsiScriptProgram Program,
    IReadOnlyList<string> Diagnostics);
