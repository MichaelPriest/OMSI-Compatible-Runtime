using System.Globalization;

namespace OmsiCompat.Scripting;

public sealed class OmsiScriptRuntime
{
    private readonly OmsiScriptCatalog _catalog;
    private readonly Dictionary<string, double>
        _locals;
    private readonly Dictionary<string, string>
        _stringLocals;
    private readonly double[] _registers =
        new double[8];

    public OmsiScriptRuntime(
        OmsiScriptCatalog catalog)
    {
        _catalog =
            catalog ??
            throw new ArgumentNullException(
                nameof(catalog));

        _locals =
            catalog.NumericVariables.ToDictionary(
                static name => name,
                static _ => 0.0,
                StringComparer.Ordinal);

        _stringLocals =
            catalog.StringVariables.ToDictionary(
                static name => name,
                static _ => string.Empty,
                StringComparer.Ordinal);
    }

    public void ExecuteInit()
    {
        foreach (var block in
                 _catalog.Program.InitBlocks)
        {
            ExecuteBlock(
                block,
                0);
        }
    }

    public void ExecuteFrame()
    {
        foreach (var block in
                 _catalog.Program.FrameBlocks)
        {
            ExecuteBlock(
                block,
                0);
        }
    }

    public void ExecuteTrigger(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        if (_catalog.Program.Triggers.TryGetValue(
                name,
                out var block))
        {
            ExecuteBlock(
                block,
                0);
        }
    }

    public double GetLocal(
        string name) =>
            _locals.TryGetValue(
                name,
                out var value)
                ? value
                : 0.0;

    public void SetLocal(
        string name,
        double value)
    {
        if (_catalog.NumericVariables.Contains(
                name))
        {
            _locals[name] =
                value;
        }
    }

    public string GetStringLocal(
        string name) =>
            _stringLocals.TryGetValue(
                name,
                out var value)
                ? value
                : string.Empty;

    public void SetStringLocal(
        string name,
        string value)
    {
        if (_catalog.StringVariables.Contains(
                name))
        {
            _stringLocals[name] =
                value ?? string.Empty;
        }
    }

    private void ExecuteBlock(
        OmsiScriptBlock block,
        int depth)
    {
        if (depth > 64)
        {
            throw new InvalidOperationException(
                "OMSI macro recursion limit exceeded.");
        }

        var stack =
            new Stack<double>();

        foreach (var token in
                 block.Tokens)
        {
            if (double.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var literal) &&
                double.IsFinite(
                    literal))
            {
                stack.Push(
                    literal);
                continue;
            }

            if (TryCommand(
                    token,
                    "(L.L.",
                    out var localName))
            {
                stack.Push(
                    GetLocal(
                        localName));
                continue;
            }

            if (TryCommand(
                    token,
                    "(S.L.",
                    out localName))
            {
                SetLocal(
                    localName,
                    Peek(
                        stack));
                continue;
            }

            if (TryCommand(
                    token,
                    "(C.L.",
                    out var constantName))
            {
                stack.Push(
                    _catalog.Constants.TryGetValue(
                        constantName,
                        out var constant)
                            ? constant
                            : 0.0);
                continue;
            }

            if (TryCommand(
                    token,
                    "(F.L.",
                    out var curveName))
            {
                var input =
                    Pop(
                        stack);

                stack.Push(
                    _catalog.Curves.TryGetValue(
                        curveName,
                        out var curve)
                            ? curve.Evaluate(
                                input)
                            : 0.0);
                continue;
            }

            if (TryCommand(
                    token,
                    "(M.L.",
                    out var macroName))
            {
                if (_catalog.Program.Macros.TryGetValue(
                        macroName,
                        out var macro))
                {
                    ExecuteBlock(
                        macro,
                        depth + 1);
                }

                continue;
            }

            if (TryRegister(
                    token,
                    's',
                    out var registerIndex))
            {
                _registers[
                    registerIndex] =
                    Peek(
                        stack);
                continue;
            }

            if (TryRegister(
                    token,
                    'l',
                    out registerIndex))
            {
                stack.Push(
                    _registers[
                        registerIndex]);
                continue;
            }

            switch (token)
            {
                case "+":
                    Binary(
                        stack,
                        static (a, b) =>
                            a + b);
                    break;

                case "-":
                    Binary(
                        stack,
                        static (a, b) =>
                            a - b);
                    break;

                case "*":
                    Binary(
                        stack,
                        static (a, b) =>
                            a * b);
                    break;

                case "/":
                    Binary(
                        stack,
                        static (a, b) =>
                            Math.Abs(b) <
                                double.Epsilon
                                ? 0.0
                                : a / b);
                    break;

                case "=":
                    Binary(
                        stack,
                        static (a, b) =>
                            a == b
                                ? 1.0
                                : 0.0);
                    break;

                case "!=":
                    Binary(
                        stack,
                        static (a, b) =>
                            a != b
                                ? 1.0
                                : 0.0);
                    break;

                case "<":
                    Binary(
                        stack,
                        static (a, b) =>
                            a < b
                                ? 1.0
                                : 0.0);
                    break;

                case ">":
                    Binary(
                        stack,
                        static (a, b) =>
                            a > b
                                ? 1.0
                                : 0.0);
                    break;

                case "<=":
                    Binary(
                        stack,
                        static (a, b) =>
                            a <= b
                                ? 1.0
                                : 0.0);
                    break;

                case ">=":
                    Binary(
                        stack,
                        static (a, b) =>
                            a >= b
                                ? 1.0
                                : 0.0);
                    break;

                case "&&":
                    Binary(
                        stack,
                        static (a, b) =>
                            a != 0.0 &&
                            b != 0.0
                                ? 1.0
                                : 0.0);
                    break;

                case "||":
                    Binary(
                        stack,
                        static (a, b) =>
                            a != 0.0 ||
                            b != 0.0
                                ? 1.0
                                : 0.0);
                    break;

                case "!":
                    stack.Push(
                        Pop(
                            stack) == 0.0
                            ? 1.0
                            : 0.0);
                    break;
            }
        }
    }

    private static bool TryCommand(
        string token,
        string prefix,
        out string name)
    {
        name =
            string.Empty;

        if (!token.StartsWith(
                prefix,
                StringComparison.Ordinal) ||
            !token.EndsWith(
                ')'))
        {
            return false;
        }

        name =
            token[
                prefix.Length..
                ^1];

        return name.Length > 0;
    }

    private static bool TryRegister(
        string token,
        char prefix,
        out int index)
    {
        index =
            -1;

        return token.Length == 2 &&
            token[0] == prefix &&
            token[1] >= '0' &&
            token[1] <= '7' &&
            (index =
                token[1] - '0') >= 0;
    }

    private static void Binary(
        Stack<double> stack,
        Func<double, double, double> operation)
    {
        var right =
            Pop(
                stack);
        var left =
            Pop(
                stack);

        stack.Push(
            operation(
                left,
                right));
    }

    private static double Peek(
        Stack<double> stack) =>
            stack.Count > 0
                ? stack.Peek()
                : 0.0;

    private static double Pop(
        Stack<double> stack) =>
            stack.Count > 0
                ? stack.Pop()
                : 0.0;
}
