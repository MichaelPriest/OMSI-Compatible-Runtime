using System.Diagnostics;

namespace OMSICompatible.Launcher.WinUI;

internal sealed class RuntimeProcessHost :
    IDisposable
{
    private Process? _process;
    private Process? _previewProcess;

    public event Action<string>?
        OutputReceived;

    public event Action<int>?
        Exited;

    public bool IsRunning =>
        _process is
        {
            HasExited: false
        };

    public bool IsPreviewRunning =>
        _previewProcess is
        {
            HasExited: false
        };

    public string ResolveRuntimePath()
    {
        var baseDirectory =
            AppContext.BaseDirectory;

        var candidates =
            new[]
            {
                Path.Combine(
                    baseDirectory,
                    "Runtime",
                    "OMSICompatible.Runtime.exe"),
                Path.Combine(
                    baseDirectory,
                    "OMSICompatible.Runtime.exe"),
                Path.GetFullPath(
                    Path.Combine(
                        baseDirectory,
                        "..",
                        "Runtime",
                        "OMSICompatible.Runtime.exe"))
            };

        return candidates
                   .FirstOrDefault(
                       File.Exists)
               ?? candidates[0];
    }

    public bool Start(
        string contentPath,
        string mapName,
        string? busRelativePath,
        string entryPointName,
        string? repaintName = null,
        string? repaintCtiRelativePath = null)
    {
        if (IsRunning)
        {
            return false;
        }

        var runtimePath =
            ResolveRuntimePath();

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

        Directory.CreateDirectory(
            logsDirectory);

        var logPath =
            Path.Combine(
                logsDirectory,
                $"runtime-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var startInfo =
            new ProcessStartInfo
            {
                FileName = runtimePath,
                WorkingDirectory =
                    Path.GetDirectoryName(
                        runtimePath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

        Add(
            startInfo,
            "--content",
            contentPath);

        Add(
            startInfo,
            "--map",
            mapName);

        if (string.IsNullOrWhiteSpace(
                busRelativePath))
        {
            startInfo.ArgumentList.Add(
                "--no-bus");
        }
        else
        {
            Add(
                startInfo,
                "--bus",
                busRelativePath);
        }

        if (!string.IsNullOrWhiteSpace(
                repaintName) &&
            !string.IsNullOrWhiteSpace(
                busRelativePath))
        {
            Add(
                startInfo,
                "--repaint",
                repaintName);
        }

        if (!string.IsNullOrWhiteSpace(
                repaintCtiRelativePath) &&
            !string.IsNullOrWhiteSpace(
                busRelativePath))
        {
            Add(
                startInfo,
                "--repaint-cti",
                repaintCtiRelativePath);
        }

        Add(
            startInfo,
            "--spawn",
            entryPointName);

        startInfo.ArgumentList.Add(
            "--external-loading");

        var process =
            new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

        process.OutputDataReceived +=
            (_, args) =>
                HandleLine(
                    logPath,
                    args.Data,
                    false);

        process.ErrorDataReceived +=
            (_, args) =>
                HandleLine(
                    logPath,
                    args.Data,
                    true);

        process.Exited +=
            (_, _) =>
                Exited?.Invoke(
                    process.ExitCode);

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

    public bool StartVehiclePreview(
        string contentPath,
        string busRelativePath,
        string? repaintName = null,
        string? repaintCtiRelativePath = null)
    {
        if (IsPreviewRunning)
        {
            return false;
        }

        var runtimePath =
            ResolveRuntimePath();

        if (!File.Exists(
                runtimePath))
        {
            throw new FileNotFoundException(
                "Runtime executável não encontrado.",
                runtimePath);
        }

        var startInfo =
            new ProcessStartInfo
            {
                FileName = runtimePath,
                WorkingDirectory =
                    Path.GetDirectoryName(
                        runtimePath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = false
            };

        Add(
            startInfo,
            "--content",
            contentPath);

        Add(
            startInfo,
            "--bus",
            busRelativePath);

        if (!string.IsNullOrWhiteSpace(
                repaintName))
        {
            Add(
                startInfo,
                "--repaint",
                repaintName);
        }

        if (!string.IsNullOrWhiteSpace(
                repaintCtiRelativePath))
        {
            Add(
                startInfo,
                "--repaint-cti",
                repaintCtiRelativePath);
        }

        startInfo.ArgumentList.Add(
            "--vehicle-preview");

        var process =
            new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

        process.Exited +=
            (_, _) =>
            {
                process.Dispose();

                if (ReferenceEquals(
                        _previewProcess,
                        process))
                {
                    _previewProcess =
                        null;
                }
            };

        if (!process.Start())
        {
            process.Dispose();
            return false;
        }

        _previewProcess =
            process;

        return true;
    }

    public void Dispose()
    {
        _previewProcess?.Dispose();
        _previewProcess = null;

        _process?.Dispose();
        _process = null;
    }

    private void HandleLine(
        string logPath,
        string? data,
        bool isError)
    {
        if (data is null)
        {
            return;
        }

        var line =
            isError
                ? "[ERROR] " + data
                : data;

        try
        {
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
        }

        OutputReceived?.Invoke(
            line);
    }

    private static void Add(
        ProcessStartInfo info,
        string key,
        string value)
    {
        info.ArgumentList.Add(
            key);

        info.ArgumentList.Add(
            value);
    }
}
