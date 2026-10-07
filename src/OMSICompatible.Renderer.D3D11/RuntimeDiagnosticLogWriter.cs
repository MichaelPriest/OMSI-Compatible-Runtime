using System.Collections.Concurrent;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Keeps diagnostic file I/O away from the render/simulation thread.
/// Diagnostic lines may be dropped under extreme backlog rather than
/// stalling a frame.
/// </summary>
internal static class RuntimeDiagnosticLogWriter
{
    private const int MaximumQueuedLines = 1024;

    private readonly record struct PendingLine(
        string FileName,
        string Line);

    private static readonly ConcurrentQueue<PendingLine>
        Pending = new();
    private static readonly SemaphoreSlim Signal =
        new(0);
    private static readonly object LifetimeGate =
        new();

    private static Task? _worker;
    private static int _queuedLines;

    public static void Enqueue(
        string fileName,
        string line)
    {
        if (string.IsNullOrWhiteSpace(
                fileName) ||
            string.IsNullOrWhiteSpace(
                line))
        {
            return;
        }

        EnsureStarted();

        var queued =
            Interlocked.Increment(
                ref _queuedLines);

        if (queued >
            MaximumQueuedLines)
        {
            Interlocked.Decrement(
                ref _queuedLines);
            return;
        }

        Pending.Enqueue(
            new PendingLine(
                fileName,
                line));

        try
        {
            Signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static void EnsureStarted()
    {
        if (Volatile.Read(
                ref _worker) is not
            null)
        {
            return;
        }

        lock (LifetimeGate)
        {
            if (_worker is not
                null)
            {
                return;
            }

            _worker =
                Task.Run(
                    RunAsync);
        }
    }

    private static async Task RunAsync()
    {
        while (true)
        {
            await Signal.WaitAsync()
                .ConfigureAwait(false);

            while (Pending.TryDequeue(
                       out var pending))
            {
                Interlocked.Decrement(
                    ref _queuedLines);

                try
                {
                    File.AppendAllText(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            pending.FileName),
                        pending.Line);
                }
                catch
                {
                    // Diagnostics must never destabilize the runtime.
                }
            }
        }
    }
}
