using System.Diagnostics;

namespace OMSICompatible.Launcher;

internal sealed class RuntimeProcessHost : IDisposable
{
    private Process? _process;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public bool IsRunning =>
        _process is { HasExited: false };

    public string ResolveRuntimePath()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(
                baseDirectory,
                "Runtime",
                "OMSICompatible.Runtime.exe"),
            Path.Combine(
                baseDirectory,
                "OMSICompatible.Runtime.exe")
        };

        return candidates.FirstOrDefault(File.Exists)
               ?? candidates[0];
    }

    public bool Start(
        string contentPath,
        string mapName)
    {
        if (IsRunning)
        {
            return false;
        }

        var runtimePath = ResolveRuntimePath();

        if (!File.Exists(runtimePath))
        {
            throw new FileNotFoundException(
                "Runtime executável não encontrado.",
                runtimePath);
        }

        var logsDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Logs");

        Directory.CreateDirectory(logsDirectory);

        var logPath =
            Path.Combine(
                logsDirectory,
                $"runtime-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var startInfo =
            new ProcessStartInfo
            {
                FileName = runtimePath,
                WorkingDirectory =
                    Path.GetDirectoryName(runtimePath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

        startInfo.ArgumentList.Add("--content");
        startInfo.ArgumentList.Add(contentPath);
        startInfo.ArgumentList.Add("--map");
        startInfo.ArgumentList.Add(mapName);

        var process =
            new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

        process.OutputDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                AppendLog(logPath, eventArgs.Data);
                OutputReceived?.Invoke(eventArgs.Data);
            };

        process.ErrorDataReceived +=
            (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                var line = "[ERROR] " + eventArgs.Data;

                AppendLog(logPath, line);
                OutputReceived?.Invoke(line);
            };

        process.Exited +=
            (_, _) =>
            {
                Exited?.Invoke(process.ExitCode);
            };

        if (!process.Start())
        {
            process.Dispose();
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _process = process;
        return true;
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }

    private static void AppendLog(
        string path,
        string line)
    {
        try
        {
            File.AppendAllText(
                path,
                $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
