namespace OmsiCompat.Scripting;

public sealed class OmsiScriptCallbackContext
{
    private readonly Func<double> _peekFloat;
    private readonly Func<double> _popFloat;
    private readonly Action<double> _pushFloat;
    private readonly Func<string> _peekString;
    private readonly Func<string> _popString;
    private readonly Action<string> _pushString;

    internal OmsiScriptCallbackContext(
        Func<double> peekFloat,
        Func<double> popFloat,
        Action<double> pushFloat,
        Func<string> peekString,
        Func<string> popString,
        Action<string> pushString)
    {
        _peekFloat = peekFloat;
        _popFloat = popFloat;
        _pushFloat = pushFloat;
        _peekString = peekString;
        _popString = popString;
        _pushString = pushString;
    }

    public double PeekFloat() =>
        _peekFloat();

    public double PopFloat() =>
        _popFloat();

    public void PushFloat(
        double value) =>
        _pushFloat(
            value);

    public string PeekString() =>
        _peekString();

    public string PopString() =>
        _popString();

    public void PushString(
        string value) =>
        _pushString(
            value);
}

public delegate bool OmsiSystemMacroHandler(
    string name,
    OmsiScriptCallbackContext context);
