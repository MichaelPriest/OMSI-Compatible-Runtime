using System.Diagnostics;

namespace OmsiCompat.Plugins;

[Flags]
public enum OmsiPluginCapabilities : byte
{
    None = 0,
    Variable = 1,
    Trigger = 2,
    SystemVariable = 4,
    StringVariable = 8
}

public sealed class OmsiPluginRemoteClient :
    IDisposable
{
    private readonly Process _process;
    private readonly BinaryWriter _writer;
    private readonly BinaryReader _reader;
    private bool _finalized;

    private OmsiPluginRemoteClient(
        Process process,
        BinaryWriter writer,
        BinaryReader reader,
        OmsiPluginCapabilities capabilities)
    {
        _process =
            process;
        _writer =
            writer;
        _reader =
            reader;
        Capabilities =
            capabilities;
    }

    public OmsiPluginCapabilities Capabilities
    {
        get;
    }

    public static OmsiPluginRemoteClient Start(
        string hostPath,
        string pluginDllPath)
    {
        if (!File.Exists(
                hostPath))
        {
            throw new FileNotFoundException(
                "OMSI x86 plugin host was not found.",
                hostPath);
        }

        if (!File.Exists(
                pluginDllPath))
        {
            throw new FileNotFoundException(
                "OMSI plugin DLL was not found.",
                pluginDllPath);
        }

        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    hostPath,
                Arguments =
                    QuoteArgument(
                        pluginDllPath),
                WorkingDirectory =
                    Path.GetDirectoryName(
                        pluginDllPath) ??
                    AppContext.BaseDirectory,
                UseShellExecute =
                    false,
                RedirectStandardInput =
                    true,
                RedirectStandardOutput =
                    true,
                RedirectStandardError =
                    true,
                CreateNoWindow =
                    true
            };

        var process =
            Process.Start(
                startInfo) ??
            throw new InvalidOperationException(
                "Unable to start OMSI x86 plugin host.");

        var writer =
            new BinaryWriter(
                process.StandardInput.BaseStream);

        var reader =
            new BinaryReader(
                process.StandardOutput.BaseStream);

        writer.Write(
            (byte)OmsiPluginHostCommand.Start);
        writer.Flush();

        var started =
            reader.ReadByte();

        if (started !=
            1)
        {
            var error =
                process.StandardError.ReadToEnd();

            process.Kill(
                entireProcessTree:
                    true);

            process.Dispose();

            throw new InvalidOperationException(
                $"OMSI plugin host rejected startup. {error}");
        }

        var capabilities =
            (OmsiPluginCapabilities)
            reader.ReadByte();

        return new OmsiPluginRemoteClient(
            process,
            writer,
            reader,
            capabilities);
    }

    public OmsiPluginFrameReply Frame(
        OmsiPluginFrame frame)
    {
        ObjectDisposedException.ThrowIf(
            _finalized,
            this);

        _writer.Write(
            (byte)OmsiPluginHostCommand.Frame);

        WriteFloatList(
            frame.SystemVariables);

        WriteFloatList(
            frame.Variables);

        _writer.Write(
            checked(
                (ushort)frame.StringVariables.Count));

        foreach (var item in
                 frame.StringVariables)
        {
            _writer.Write(
                item.Index);

            WriteString(
                item.Value);
        }

        _writer.Write(
            checked(
                (ushort)frame.Triggers.Count));

        foreach (var trigger in
                 frame.Triggers)
        {
            _writer.Write(
                trigger);
        }

        _writer.Flush();

        var system =
            ReadFloatReplies(
                frame.SystemVariables.Count);

        var variables =
            ReadFloatReplies(
                frame.Variables.Count);

        var strings =
            new string?[
                frame.StringVariables.Count];

        for (var index = 0;
             index <
                 strings.Length;
             index++)
        {
            var wrote =
                _reader.ReadByte() !=
                0;

            var value =
                ReadString();

            strings[index] =
                wrote
                    ? value
                    : null;
        }

        var triggers =
            new bool[
                frame.Triggers.Count];

        for (var index = 0;
             index <
                 triggers.Length;
             index++)
        {
            triggers[index] =
                _reader.ReadByte() !=
                0;
        }

        return new OmsiPluginFrameReply(
            system,
            variables,
            strings,
            triggers);
    }

    public void FinalizePlugin()
    {
        if (_finalized)
        {
            return;
        }

        _finalized =
            true;

        try
        {
            if (!_process.HasExited)
            {
                _writer.Write(
                    (byte)OmsiPluginHostCommand.Finalize);

                _writer.Flush();

                _ =
                    _reader.ReadByte();

                _process.WaitForExit(
                    1000);
            }
        }
        catch
        {
        }

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(
                    entireProcessTree:
                        true);
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        FinalizePlugin();

        _writer.Dispose();
        _reader.Dispose();
        _process.Dispose();
    }

    private void WriteFloatList(
        IReadOnlyList<(ushort Index, float Value)> values)
    {
        _writer.Write(
            checked(
                (ushort)values.Count));

        foreach (var item in
                 values)
        {
            _writer.Write(
                item.Index);

            _writer.Write(
                item.Value);
        }
    }

    private float?[] ReadFloatReplies(
        int count)
    {
        var result =
            new float?[
                count];

        for (var index = 0;
             index <
                 count;
             index++)
        {
            var wrote =
                _reader.ReadByte() !=
                0;

            var value =
                _reader.ReadSingle();

            result[index] =
                wrote
                    ? value
                    : null;
        }

        return result;
    }

    private void WriteString(
        string value)
    {
        var count =
            Math.Min(
                value.Length,
                ushort.MaxValue);

        _writer.Write(
            (ushort)count);

        for (var index = 0;
             index <
                 count;
             index++)
        {
            _writer.Write(
                (ushort)value[index]);
        }
    }

    private string ReadString()
    {
        var count =
            _reader.ReadUInt16();

        var chars =
            new char[
                count];

        for (var index = 0;
             index <
                 count;
             index++)
        {
            chars[index] =
                (char)_reader.ReadUInt16();
        }

        return new string(
            chars);
    }

    private static string QuoteArgument(
        string value) =>
        """ +
        value.Replace(
            """,
            "\"",
            StringComparison.Ordinal) +
        """;
}
