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

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dBodyCreate(
        nint world);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodyDestroy(
        nint body);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodySetMass(
        nint body,
        ref OdeMass mass);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodySetPosition(
        nint body,
        float x,
        float y,
        float z);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodySetLinearVel(
        nint body,
        float x,
        float y,
        float z);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dBodyGetPosition(
        nint body);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dBodyGetLinearVel(
        nint body);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodyAddForce(
        nint body,
        float x,
        float y,
        float z);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodyAddRelForce(
        nint body,
        float x,
        float y,
        float z);
}
