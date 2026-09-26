using System.Globalization;

namespace OmsiCompat.Scripting;

public sealed class OmsiScriptRuntime
{
    private sealed class FloatStack
    {
        private readonly double[] _values =
            new double[8];

        public double Top =>
            _values[0];

        public double this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        public void Push(
            double value)
        {
            for (var index = _values.Length - 1;
                 index > 0;
                 index--)
            {
                _values[index] =
                    _values[index - 1];
            }

            _values[0] =
                double.IsFinite(value)
                    ? value
                    : 0.0;
        }

        public double Pop()
        {
            var value =
                _values[0];

            for (var index = 0;
                 index < _values.Length - 1;
                 index++)
            {
                _values[index] =
                    _values[index + 1];
            }

            _values[^1] =
                0.0;

            return value;
        }

        public void Clear() =>
            Array.Clear(
                _values);
    }

    private sealed class StringStack
    {
        private readonly string[] _values =
            Enumerable.Repeat(
                string.Empty,
                8)
                .ToArray();

        public string Top =>
            _values[0];

        public void Push(
            string value)
        {
            for (var index = _values.Length - 1;
                 index > 0;
                 index--)
            {
                _values[index] =
                    _values[index - 1];
            }

            _values[0] =
                value ?? string.Empty;
        }

        public string Pop()
        {
            var value =
                _values[0];

            for (var index = 0;
                 index < _values.Length - 1;
                 index++)
            {
                _values[index] =
                    _values[index + 1];
            }

            _values[^1] =
                string.Empty;

            return value;
        }

        public void ReplaceTop(
            string value) =>
            _values[0] =
                value ?? string.Empty;
    }

    private sealed record ConditionalFrame(
        bool ParentActive,
        bool Condition,
        bool InElse)
    {
        public bool Active =>
            ParentActive &&
            (InElse
                ? !Condition
                : Condition);
    }

    private readonly OmsiScriptCatalog _catalog;
    private readonly Dictionary<string, double>
        _locals;
    private readonly Dictionary<string, string>
        _stringLocals;
    private readonly HashSet<string>
        _writtenLocalVariables;
    private readonly HashSet<string>
        _writtenStringLocalVariables;
    private readonly Dictionary<string, double>
        _mapVariables =
            new(
                StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double>
        _systemVariables =
            new(
                StringComparer.OrdinalIgnoreCase);
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
                StringComparer.OrdinalIgnoreCase);

        _stringLocals =
            catalog.StringVariables.ToDictionary(
                static name => name,
                static _ => string.Empty,
                StringComparer.OrdinalIgnoreCase);

        _writtenLocalVariables =
            CollectWrittenLocalVariables(
                catalog.Program,
                "(S.L.");

        _writtenStringLocalVariables =
            CollectWrittenLocalVariables(
                catalog.Program,
                "(S.$.");
    }

    public event Action<string>?
        SoundTriggerRequested;

    public event Action<string, string>?
        FileSoundTriggerRequested;

    public event Action<string>?
        DebugMessageRequested;

    public event Action<string>?
        UnhandledSystemMacro;

    public OmsiSystemMacroHandler?
        SystemMacroHandler
    {
        get;
        set;
    }

    public void ExecuteInit()
    {
        foreach (var block in
                 _catalog.Program.InitBlocks)
        {
            ExecuteEntryBlock(
                block);
        }
    }

    public void ExecuteFrame()
    {
        foreach (var block in
                 _catalog.Program.FrameBlocks)
        {
            ExecuteEntryBlock(
                block);
        }
    }

    public void ExecuteFrameAi()
    {
        foreach (var block in
                 _catalog.Program.FrameAiBlocks)
        {
            ExecuteEntryBlock(
                block);
        }
    }

    public bool HasTrigger(
        string name) =>
        !string.IsNullOrWhiteSpace(
            name) &&
        _catalog.Program.Triggers.ContainsKey(
            name);

    public void ExecuteTrigger(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        if (_catalog.Program.Triggers.TryGetValue(
                name,
                out var block))
        {
            ExecuteEntryBlock(
                block);
        }
    }

    public double GetLocal(
        string name) =>
            Read(
                _locals,
                name);

    public bool HasLocalVariable(
        string name) =>
        _catalog.NumericVariables.Contains(
            name);

    public bool WritesLocalVariable(
        string name) =>
        !string.IsNullOrWhiteSpace(
            name) &&
        _writtenLocalVariables.Contains(
            name);

    public bool WritesStringLocalVariable(
        string name) =>
        !string.IsNullOrWhiteSpace(
            name) &&
        _writtenStringLocalVariables.Contains(
            name);

    public void SetLocal(
        string name,
        double value)
    {
        if (_catalog.NumericVariables.Contains(
                name))
        {
            _locals[name] =
                Normalize(
                    value);
        }
    }

    public double GetMap(
        string name) =>
            Read(
                _mapVariables,
                name);

    public void SetMap(
        string name,
        double value) =>
            _mapVariables[name] =
                Normalize(
                    value);

    public double GetSystem(
        string name) =>
            Read(
                _systemVariables,
                name);

    public void SetSystem(
        string name,
        double value) =>
            _systemVariables[name] =
                Normalize(
                    value);

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

    private void ExecuteEntryBlock(
        OmsiScriptBlock block)
    {
        var stack =
            new FloatStack();

        var stringStack =
            new StringStack();

        ExecuteBlock(
            block,
            stack,
            stringStack,
            0);
    }

    private void ExecuteBlock(
        OmsiScriptBlock block,
        FloatStack stack,
        StringStack stringStack,
        int depth)
    {
        if (depth > 64)
        {
            throw new InvalidOperationException(
                "OMSI macro recursion limit exceeded.");
        }

        var conditions =
            new Stack<ConditionalFrame>();

        foreach (var token in
                 block.Tokens)
        {
            if (token.Equals(
                    "{if}",
                    StringComparison.OrdinalIgnoreCase))
            {
                var parentActive =
                    IsActive(
                        conditions);

                conditions.Push(
                    new ConditionalFrame(
                        parentActive,
                        parentActive &&
                        stack.Top != 0.0,
                        false));
                continue;
            }

            if (token.Equals(
                    "{else}",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (conditions.Count > 0)
                {
                    var current =
                        conditions.Pop();

                    conditions.Push(
                        current with
                        {
                            InElse = true
                        });
                }

                continue;
            }

            if (token.Equals(
                    "{endif}",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (conditions.Count > 0)
                {
                    conditions.Pop();
                }

                continue;
            }

            if (!IsActive(
                    conditions))
            {
                continue;
            }

            if (TryStringLiteral(
                    token,
                    out var stringLiteral))
            {
                stringStack.Push(
                    stringLiteral);
                continue;
            }

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
                    "(L.M.",
                    out var mapName))
            {
                stack.Push(
                    GetMap(
                        mapName));
                continue;
            }

            if (TryCommand(
                    token,
                    "(L.S.",
                    out var systemName))
            {
                stack.Push(
                    GetSystem(
                        systemName));
                continue;
            }

            if (TryCommand(
                    token,
                    "(L.$.",
                    out var stringLocalName))
            {
                stringStack.Push(
                    GetStringLocal(
                        stringLocalName));
                continue;
            }

            if (TryCommand(
                    token,
                    "(S.$.",
                    out stringLocalName))
            {
                SetStringLocal(
                    stringLocalName,
                    stringStack.Top);
                continue;
            }

            if (TryCommand(
                    token,
                    "(S.L.",
                    out localName))
            {
                SetLocal(
                    localName,
                    stack.Top);
                continue;
            }

            if (TryCommand(
                    token,
                    "(S.M.",
                    out mapName))
            {
                SetMap(
                    mapName,
                    stack.Top);
                continue;
            }

            if (TryCommand(
                    token,
                    "(S.S.",
                    out systemName))
            {
                SetSystem(
                    systemName,
                    stack.Top);
                continue;
            }

            if (TryCommand(
                    token,
                    "(T.L.",
                    out var triggerName))
            {
                SoundTriggerRequested?.Invoke(
                    triggerName);
                continue;
            }

            if (TryCommand(
                    token,
                    "(T.F.",
                    out triggerName))
            {
                FileSoundTriggerRequested?.Invoke(
                    triggerName,
                    stringStack.Top);
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
                    stack.Pop();

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
                    "(M.V.",
                    out var systemMacroName))
            {
                var knownMacro =
                    _catalog.VehicleCallbacks.Contains(
                        systemMacroName) ||
                    _catalog.SceneryCallbacks.Contains(
                        systemMacroName) ||
                    _catalog.ScriptTextureCallbacks.Contains(
                        systemMacroName);

                var handled =
                    knownMacro &&
                    SystemMacroHandler?.Invoke(
                        systemMacroName,
                        new OmsiScriptCallbackContext(
                            () => stack.Top,
                            stack.Pop,
                            stack.Push,
                            () => stringStack.Top,
                            stringStack.Pop,
                            stringStack.Push)) ==
                    true;

                if (!handled)
                {
                    UnhandledSystemMacro?.Invoke(
                        systemMacroName);
                }

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
                        stack,
                        stringStack,
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
                    stack.Top;
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
                case "$d":
                    stringStack.Push(
                        stringStack.Top);
                    break;

                case "$msg":
                    DebugMessageRequested?.Invoke(
                        stringStack.Top);
                    break;

                case "$+":
                    StringBinary(
                        stringStack,
                        static (left, right) =>
                            left + right);
                    break;

                case "$=":
                    StringCompare(
                        stack,
                        stringStack,
                        static comparison =>
                            comparison == 0);
                    break;

                case "$<":
                    StringCompare(
                        stack,
                        stringStack,
                        static comparison =>
                            comparison < 0);
                    break;

                case "$>":
                    StringCompare(
                        stack,
                        stringStack,
                        static comparison =>
                            comparison > 0);
                    break;

                case "$<=":
                    StringCompare(
                        stack,
                        stringStack,
                        static comparison =>
                            comparison <= 0);
                    break;

                case "$>=":
                    StringCompare(
                        stack,
                        stringStack,
                        static comparison =>
                            comparison >= 0);
                    break;

                case "$length":
                    stack.Push(
                        stringStack.Top.Length);
                    break;

                case "$*":
                {
                    var targetLength =
                        Math.Max(
                            (int)Math.Truncate(
                                stack.Pop()),
                            0);

                    var source =
                        stringStack.Pop();

                    if (targetLength == 0 ||
                        source.Length == 0)
                    {
                        stringStack.Push(
                            string.Empty);
                        break;
                    }

                    var builder =
                        new System.Text.StringBuilder(
                            targetLength);

                    while (builder.Length <
                           targetLength)
                    {
                        builder.Append(
                            source);
                    }

                    if (builder.Length >
                        targetLength)
                    {
                        builder.Length =
                            targetLength;
                    }

                    stringStack.Push(
                        builder.ToString());
                    break;
                }

                case "$cutBegin":
                {
                    var count =
                        Math.Max(
                            (int)Math.Truncate(
                                stack.Pop()),
                            0);

                    var value =
                        stringStack.Pop();

                    stringStack.Push(
                        count >= value.Length
                            ? string.Empty
                            : value[count..]);
                    break;
                }

                case "$cutEnd":
                {
                    var count =
                        Math.Max(
                            (int)Math.Truncate(
                                stack.Pop()),
                            0);

                    var value =
                        stringStack.Pop();

                    stringStack.Push(
                        count >= value.Length
                            ? string.Empty
                            : value[..^count]);
                    break;
                }

                case "$SetLengthL":
                    stringStack.ReplaceTop(
                        SetStringLength(
                            stringStack.Top,
                            stack.Top,
                            StringAlignment.Left));
                    break;

                case "$SetLengthR":
                    stringStack.ReplaceTop(
                        SetStringLength(
                            stringStack.Top,
                            stack.Top,
                            StringAlignment.Right));
                    break;

                case "$SetLengthC":
                    stringStack.ReplaceTop(
                        SetStringLength(
                            stringStack.Top,
                            stack.Top,
                            StringAlignment.Center));
                    break;

                case "$IntToStr":
                    stringStack.Push(
                        Math.Truncate(
                            stack.Pop())
                            .ToString(
                                "0",
                                CultureInfo.InvariantCulture));
                    break;

                case "$IntToStrEnh":
                {
                    var format =
                        stringStack.Pop();

                    var value =
                        Math.Truncate(
                            stack.Pop());

                    stringStack.Push(
                        FormatInteger(
                            value,
                            format));
                    break;
                }

                case "$StrToFloat":
                {
                    var value =
                        stringStack.Pop();

                    stack.Push(
                        double.TryParse(
                            value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsed) &&
                        double.IsFinite(
                            parsed)
                            ? parsed
                            : -1.0);
                    break;
                }

                case "$RemoveSpaces":
                    stringStack.ReplaceTop(
                        stringStack.Top.Trim());
                    break;

                case "+":
                    Binary(
                        stack,
                        static (left, right) =>
                            left + right);
                    break;

                case "-":
                    Binary(
                        stack,
                        static (left, right) =>
                            left - right);
                    break;

                case "*":
                    Binary(
                        stack,
                        static (left, right) =>
                            left * right);
                    break;

                case "/":
                    Binary(
                        stack,
                        static (left, right) =>
                            Math.Abs(right) <
                                double.Epsilon
                                ? 0.0
                                : left / right);
                    break;

                case "=":
                    Binary(
                        stack,
                        static (left, right) =>
                            left == right
                                ? 1.0
                                : 0.0);
                    break;

                case "!=":
                    Binary(
                        stack,
                        static (left, right) =>
                            left != right
                                ? 1.0
                                : 0.0);
                    break;

                case "<":
                    Binary(
                        stack,
                        static (left, right) =>
                            left < right
                                ? 1.0
                                : 0.0);
                    break;

                case ">":
                    Binary(
                        stack,
                        static (left, right) =>
                            left > right
                                ? 1.0
                                : 0.0);
                    break;

                case "<=":
                    Binary(
                        stack,
                        static (left, right) =>
                            left <= right
                                ? 1.0
                                : 0.0);
                    break;

                case ">=":
                    Binary(
                        stack,
                        static (left, right) =>
                            left >= right
                                ? 1.0
                                : 0.0);
                    break;

                case "&&":
                    Binary(
                        stack,
                        static (left, right) =>
                            left != 0.0 &&
                            right != 0.0
                                ? 1.0
                                : 0.0);
                    break;

                case "||":
                    Binary(
                        stack,
                        static (left, right) =>
                            left != 0.0 ||
                            right != 0.0
                                ? 1.0
                                : 0.0);
                    break;

                case "!":
                    Unary(
                        stack,
                        static value =>
                            value == 0.0
                                ? 1.0
                                : 0.0);
                    break;

                case "d":
                    stack.Push(
                        stack.Top);
                    break;

                case "pi":
                    stack.Push(
                        Math.PI);
                    break;

                case "sin":
                    Unary(
                        stack,
                        Math.Sin);
                    break;

                case "arcsin":
                    Unary(
                        stack,
                        Math.Asin);
                    break;

                case "arctan":
                    Unary(
                        stack,
                        Math.Atan);
                    break;

                case "/-/":
                    Unary(
                        stack,
                        static value =>
                            -value);
                    break;

                case "abs":
                    Unary(
                        stack,
                        Math.Abs);
                    break;

                case "sqrt":
                    Unary(
                        stack,
                        static value =>
                            value < 0.0
                                ? 0.0
                                : Math.Sqrt(value));
                    break;

                case "sqr":
                    Unary(
                        stack,
                        static value =>
                            value * value);
                    break;

                case "sgn":
                    Unary(
                        stack,
                        static value =>
                            (double)Math.Sign(
                                value));
                    break;

                case "trunc":
                    Unary(
                        stack,
                        Math.Truncate);
                    break;

                case "exp":
                    Unary(
                        stack,
                        Math.Exp);
                    break;

                case "min":
                    Binary(
                        stack,
                        Math.Min);
                    break;

                case "max":
                    Binary(
                        stack,
                        Math.Max);
                    break;

                case "%":
                    Binary(
                        stack,
                        static (left, right) =>
                            Math.Abs(right) <
                                double.Epsilon
                                ? 0.0
                                : left -
                                  Math.Truncate(
                                      left / right) *
                                  right);
                    break;

                case "random":
                    Unary(
                        stack,
                        static maximum =>
                        {
                            var limit =
                                (int)Math.Max(
                                    Math.Truncate(maximum),
                                    0.0);

                            return limit <= 0
                                ? 0.0
                                : Random.Shared.Next(
                                    limit);
                        });
                    break;
            }
        }
    }

    private enum StringAlignment
    {
        Left,
        Center,
        Right
    }

    private static bool TryStringLiteral(
        string token,
        out string value)
    {
        if (token.Length >= 2 &&
            token[0] == '"' &&
            token[^1] == '"')
        {
            value =
                token[1..^1];
            return true;
        }

        value =
            string.Empty;
        return false;
    }

    private static void StringBinary(
        StringStack stack,
        Func<string, string, string> operation)
    {
        var right =
            stack.Pop();
        var left =
            stack.Pop();

        stack.Push(
            operation(
                left,
                right));
    }

    private static void StringCompare(
        FloatStack floatStack,
        StringStack stringStack,
        Func<int, bool> predicate)
    {
        var right =
            stringStack.Pop();
        var left =
            stringStack.Pop();

        var comparison =
            StringComparer.OrdinalIgnoreCase.Compare(
                left,
                right);

        floatStack.Push(
            predicate(
                comparison)
                ? 1.0
                : 0.0);
    }

    private static string SetStringLength(
        string value,
        double rawLength,
        StringAlignment alignment)
    {
        var length =
            Math.Clamp(
                (int)Math.Truncate(
                    rawLength),
                0,
                4096);

        if (value.Length == length)
        {
            return value;
        }

        if (value.Length < length)
        {
            var padding =
                length -
                value.Length;

            return alignment switch
            {
                StringAlignment.Right =>
                    new string(
                        ' ',
                        padding) +
                    value,
                StringAlignment.Center =>
                    new string(
                        ' ',
                        padding / 2) +
                    value +
                    new string(
                        ' ',
                        padding - padding / 2),
                _ =>
                    value +
                    new string(
                        ' ',
                        padding)
            };
        }

        var remove =
            value.Length -
            length;

        return alignment switch
        {
            StringAlignment.Right =>
                value[remove..],
            StringAlignment.Center =>
                value[
                    (remove / 2)..
                    (remove / 2 + length)],
            _ =>
                value[..length]
        };
    }

    private static string FormatInteger(
        double value,
        string format)
    {
        if (format.Length < 2 ||
            !int.TryParse(
                format[1..],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var width) ||
            width < 0 ||
            width > 4096)
        {
            return "ERROR";
        }

        var raw =
            value.ToString(
                "0",
                CultureInfo.InvariantCulture);

        if (raw.Length >= width)
        {
            return raw;
        }

        return raw.PadLeft(
            width,
            format[0]);
    }

    private static bool IsActive(
        Stack<ConditionalFrame> conditions) =>
            conditions.Count == 0 ||
            conditions.Peek().Active;

    private static HashSet<string> CollectWrittenLocalVariables(
        OmsiScriptProgram program,
        string storePrefix)
    {
        var result =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        var blocks =
            program.InitBlocks
                .Concat(
                    program.FrameBlocks)
                .Concat(
                    program.FrameAiBlocks)
                .Concat(
                    program.Macros.Values)
                .Concat(
                    program.Triggers.Values);

        foreach (var block in
                 blocks)
        {
            foreach (var token in
                     block.Tokens)
            {
                if (TryCommand(
                        token,
                        storePrefix,
                        out var name))
                {
                    result.Add(
                        name);
                }
            }
        }

        return result;
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
        FloatStack stack,
        Func<double, double, double> operation)
    {
        var right =
            stack.Pop();
        var left =
            stack.Pop();

        stack.Push(
            operation(
                left,
                right));
    }

    private static void Unary(
        FloatStack stack,
        Func<double, double> operation)
    {
        var value =
            stack.Pop();

        stack.Push(
            operation(
                value));
    }

    private static double Read(
        IReadOnlyDictionary<string, double> source,
        string name) =>
            source.TryGetValue(
                name,
                out var value)
                ? value
                : 0.0;

    private static double Normalize(
        double value) =>
            double.IsFinite(
                value)
                ? value
                : 0.0;
}
