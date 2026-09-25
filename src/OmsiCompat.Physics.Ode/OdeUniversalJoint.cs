using System.Numerics;

namespace OmsiCompat.Physics.Ode;

public sealed class OdeUniversalJoint :
    IDisposable
{
    private const int ParamLowStopAxis1 = 0x000;
    private const int ParamHighStopAxis1 = 0x001;
    private const int ParamStopErpAxis1 = 0x007;
    private const int ParamStopCfmAxis1 = 0x008;

    private const int ParamLowStopAxis2 = 0x100;
    private const int ParamHighStopAxis2 = 0x101;
    private const int ParamStopErpAxis2 = 0x107;
    private const int ParamStopCfmAxis2 = 0x108;

    private nint _joint;

    public OdeUniversalJoint(
        OdeWorld world,
        OdeRigidBody body1,
        OdeRigidBody body2,
        Vector3 anchorWorldMeters,
        Vector3 axis1World,
        Vector3 axis2World)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(body1);
        ArgumentNullException.ThrowIfNull(body2);

        if (axis1World.LengthSquared() < 0.000001f ||
            axis2World.LengthSquared() < 0.000001f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(axis1World),
                "Universal joint axes must be non-zero.");
        }

        var axis1 = Vector3.Normalize(axis1World);
        var axis2 = Vector3.Normalize(axis2World);

        if (Math.Abs(Vector3.Dot(axis1, axis2)) > 0.01f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(axis2World),
                "Universal joint axes must be perpendicular.");
        }

        _joint = OdeNative.dJointCreateUniversal(
            world.Handle,
            nint.Zero);

        if (_joint == nint.Zero)
        {
            throw new InvalidOperationException(
                "dJointCreateUniversal returned null.");
        }

        OdeNative.dJointAttach(
            _joint,
            body1.Handle,
            body2.Handle);

        OdeNative.dJointSetUniversalAnchor(
            _joint,
            anchorWorldMeters.X,
            anchorWorldMeters.Y,
            anchorWorldMeters.Z);

        OdeNative.dJointSetUniversalAxis1(
            _joint,
            axis1.X,
            axis1.Y,
            axis1.Z);

        OdeNative.dJointSetUniversalAxis2(
            _joint,
            axis2.X,
            axis2.Y,
            axis2.Z);
    }

    public float YawAngleRadians =>
        GetFinite(OdeNative.dJointGetUniversalAngle1(RequireHandle()));

    public float PitchAngleRadians =>
        GetFinite(OdeNative.dJointGetUniversalAngle2(RequireHandle()));

    public float YawRateRadiansPerSecond =>
        GetFinite(OdeNative.dJointGetUniversalAngle1Rate(RequireHandle()));

    public float PitchRateRadiansPerSecond =>
        GetFinite(OdeNative.dJointGetUniversalAngle2Rate(RequireHandle()));

    public void SetYawStops(
        float lowRadians,
        float highRadians,
        float stopErp = 0.35f,
        float stopCfm = 0.00001f) =>
        SetStops(
            ParamLowStopAxis1,
            ParamHighStopAxis1,
            ParamStopErpAxis1,
            ParamStopCfmAxis1,
            lowRadians,
            highRadians,
            stopErp,
            stopCfm);

    public void SetPitchStops(
        float lowRadians,
        float highRadians,
        float stopErp = 0.35f,
        float stopCfm = 0.00001f) =>
        SetStops(
            ParamLowStopAxis2,
            ParamHighStopAxis2,
            ParamStopErpAxis2,
            ParamStopCfmAxis2,
            lowRadians,
            highRadians,
            stopErp,
            stopCfm);

    private void SetStops(
        int lowParameter,
        int highParameter,
        int erpParameter,
        int cfmParameter,
        float lowRadians,
        float highRadians,
        float stopErp,
        float stopCfm)
    {
        var joint = RequireHandle();

        if (!float.IsFinite(lowRadians) ||
            !float.IsFinite(highRadians) ||
            lowRadians > highRadians)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lowRadians),
                "Universal joint limits must be finite and low <= high.");
        }

        OdeNative.dJointSetUniversalParam(
            joint,
            lowParameter,
            lowRadians);

        OdeNative.dJointSetUniversalParam(
            joint,
            highParameter,
            highRadians);

        OdeNative.dJointSetUniversalParam(
            joint,
            erpParameter,
            Math.Clamp(stopErp, 0.0f, 1.0f));

        OdeNative.dJointSetUniversalParam(
            joint,
            cfmParameter,
            Math.Max(stopCfm, 0.0f));
    }

    private nint RequireHandle()
    {
        ObjectDisposedException.ThrowIf(
            _joint == nint.Zero,
            this);

        return _joint;
    }

    private static float GetFinite(float value) =>
        float.IsFinite(value)
            ? value
            : 0.0f;

    public void Dispose()
    {
        var joint =
            Interlocked.Exchange(
                ref _joint,
                nint.Zero);

        if (joint != nint.Zero)
        {
            OdeNative.dJointDestroy(joint);
        }

        GC.SuppressFinalize(this);
    }
}
