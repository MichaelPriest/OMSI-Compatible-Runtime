using System.Runtime.InteropServices;

namespace OmsiCompat.Physics.Ode;

internal static class OdeNative
{
    internal const string LibraryName =
        "ode_single";

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int dInitODE2(
        uint initFlags);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dGetConfiguration();

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Ansi)]
    internal static extern int dCheckConfiguration(
        string token);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dWorldCreate();

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dWorldDestroy(
        nint world);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dWorldSetGravity(
        nint world,
        float x,
        float y,
        float z);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dWorldSetERP(
        nint world,
        float erp);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dWorldSetCFM(
        nint world,
        float cfm);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dWorldSetQuickStepNumIterations(
        nint world,
        int iterations);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int dWorldQuickStep(
        nint world,
        float stepSize);
}
