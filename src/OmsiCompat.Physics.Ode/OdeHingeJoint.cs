using System.Numerics;

namespace OmsiCompat.Physics.Ode;

public sealed class OdeHingeJoint :
    IDisposable
{
    private nint _joint;

    public OdeHingeJoint(
        OdeWorld world,
        OdeRigidBody body1,
        OdeRigidBody body2,
        Vector3 anchorWorldMeters,
        Vector3 axisWorld)
    {
        ArgumentNullException.ThrowIfNull(
            world);
        ArgumentNullException.ThrowIfNull(
            body1);
        ArgumentNullException.ThrowIfNull(
            body2);

        if (axisWorld.LengthSquared() <
            0.000001f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(axisWorld),
                "Hinge axis must be non-zero.");
        }

        _joint =
            OdeNative.dJointCreateHinge(
                world.Handle,
                nint.Zero);

        if (_joint ==
            nint.Zero)
        {
            throw new InvalidOperationException(
                "dJointCreateHinge returned null.");
        }

        OdeNative.dJointAttach(
            _joint,
            body1.Handle,
            body2.Handle);

        OdeNative.dJointSetHingeAnchor(
            _joint,
            anchorWorldMeters.X,
            anchorWorldMeters.Y,
            anchorWorldMeters.Z);

        var axis =
            Vector3.Normalize(
                axisWorld);

        OdeNative.dJointSetHingeAxis(
            _joint,
            axis.X,
            axis.Y,
            axis.Z);
    }

    public float AngleRadians
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                _joint ==
                    nint.Zero,
                this);

            return OdeNative.dJointGetHingeAngle(
                _joint);
        }
    }

    public float AngularRateRadiansPerSecond
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                _joint ==
                    nint.Zero,
                this);

            return OdeNative.dJointGetHingeAngleRate(
                _joint);
        }
    }

    public void Dispose()
    {
        var joint =
            Interlocked.Exchange(
                ref _joint,
                nint.Zero);

        if (joint !=
            nint.Zero)
        {
            OdeNative.dJointDestroy(
                joint);
        }

        GC.SuppressFinalize(
            this);
    }
}
