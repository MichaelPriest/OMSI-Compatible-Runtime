using System.Runtime.InteropServices;

namespace OmsiCompat.Physics.Ode;

public sealed record OdeRuntimeInfo(
    bool Available,
    bool Is64BitProcess,
    bool SinglePrecision,
    string Configuration,
    string? Error);

public static class OdeRuntime
{
    private static readonly object Gate =
        new();

    private static bool _initialized;

    public static OdeRuntimeInfo Inspect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OdeRuntimeInfo(
                false,
                Environment.Is64BitProcess,
                false,
                string.Empty,
                "ODE backend currently targets Windows x64.");
        }

        if (!Environment.Is64BitProcess)
        {
            return new OdeRuntimeInfo(
                false,
                false,
                false,
                string.Empty,
                "OMSI Compatible Runtime requires a 64-bit process.");
        }

        try
        {
            EnsureInitialized();

            var configurationPointer =
                OdeNative.dGetConfiguration();

            var configuration =
                configurationPointer ==
                    nint.Zero
                    ? string.Empty
                    : Marshal.PtrToStringAnsi(
                          configurationPointer) ??
                      string.Empty;

            var singlePrecision =
                OdeNative.dCheckConfiguration(
                    "ODE_single_precision") !=
                0;

            return new OdeRuntimeInfo(
                true,
                true,
                singlePrecision,
                configuration,
                null);
        }
        catch (Exception exception)
            when (exception is
                DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException)
        {
            return new OdeRuntimeInfo(
                false,
                true,
                false,
                string.Empty,
                exception.Message);
        }
    }

    internal static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            if (!OperatingSystem.IsWindows() ||
                !Environment.Is64BitProcess)
            {
                throw new PlatformNotSupportedException(
                    "ODE vehicle physics requires Windows x64.");
            }

            if (OdeNative.dInitODE2(
                    0) ==
                0)
            {
                throw new InvalidOperationException(
                    "dInitODE2 failed.");
            }

            _initialized =
                true;
        }
    }
}
