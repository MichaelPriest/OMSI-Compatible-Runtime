using System.Runtime.InteropServices;

namespace OmsiCompat.Physics.Ode;

[StructLayout(LayoutKind.Sequential)]
internal struct OdeQuaternion
{
    public float W;
    public float X;
    public float Y;
    public float Z;
}

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
    internal static extern void dWorldQuickStep(
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
    internal static extern void dBodySetAngularVel(
        nint body,
        float x,
        float y,
        float z);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodySetQuaternion(
        nint body,
        ref OdeQuaternion quaternion);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodySetGravityMode(
        nint body,
        int mode);

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
    internal static extern nint dBodyGetAngularVel(
        nint body);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint dBodyGetQuaternion(
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

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodyAddForceAtRelPos(
        nint body,
        float forceX,
        float forceY,
        float forceZ,
        float positionX,
        float positionY,
        float positionZ);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void dBodyAddTorque(
        nint body,
        float x,
        float y,
        float z);
}
