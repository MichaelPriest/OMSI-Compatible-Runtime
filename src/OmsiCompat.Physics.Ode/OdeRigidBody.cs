using System.Numerics;
using System.Runtime.InteropServices;

namespace OmsiCompat.Physics.Ode;

public sealed record OdeRigidBodyParameters(
    float MassKilograms,
    float CenterOfMassX,
    float CenterOfMassY,
    float CenterOfMassZ,
    float InertiaXKilogramSquareMeters,
    float InertiaYKilogramSquareMeters,
    float InertiaZKilogramSquareMeters);

public sealed class OdeRigidBody :
    IDisposable
{
    private nint _body;

    public OdeRigidBody(
        OdeWorld world,
        OdeRigidBodyParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(
            world);

        if (parameters.MassKilograms <=
            0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                "Rigid body mass must be positive.");
        }

        _body =
            OdeNative.dBodyCreate(
                world.Handle);

        if (_body ==
            nint.Zero)
        {
            throw new InvalidOperationException(
                "dBodyCreate returned null.");
        }

        var mass =
            OdeMass.FromPrincipalInertia(
                parameters.MassKilograms,
                parameters.CenterOfMassX,
                parameters.CenterOfMassY,
                parameters.CenterOfMassZ,
                Math.Max(
                    parameters.InertiaXKilogramSquareMeters,
                    1.0f),
                Math.Max(
                    parameters.InertiaYKilogramSquareMeters,
                    1.0f),
                Math.Max(
                    parameters.InertiaZKilogramSquareMeters,
                    1.0f));

        OdeNative.dBodySetMass(
            _body,
            ref mass);
    }

    public Vector3 Position =>
        ReadVector3(
            OdeNative.dBodyGetPosition(
                RequireHandle()));

    public Vector3 LinearVelocity =>
        ReadVector3(
            OdeNative.dBodyGetLinearVel(
                RequireHandle()));

    public void SetPosition(
        Vector3 position)
    {
        OdeNative.dBodySetPosition(
            RequireHandle(),
            position.X,
            position.Y,
            position.Z);
    }

    public void SetLinearVelocity(
        Vector3 velocity)
    {
        OdeNative.dBodySetLinearVel(
            RequireHandle(),
            velocity.X,
            velocity.Y,
            velocity.Z);
    }

    public void AddWorldForce(
        Vector3 forceNewtons)
    {
        OdeNative.dBodyAddForce(
            RequireHandle(),
            forceNewtons.X,
            forceNewtons.Y,
            forceNewtons.Z);
    }

    public void AddLocalForce(
        Vector3 forceNewtons)
    {
        OdeNative.dBodyAddRelForce(
            RequireHandle(),
            forceNewtons.X,
            forceNewtons.Y,
            forceNewtons.Z);
    }

    public void Dispose()
    {
        var body =
            Interlocked.Exchange(
                ref _body,
                nint.Zero);

        if (body !=
            nint.Zero)
        {
            OdeNative.dBodyDestroy(
                body);
        }

        GC.SuppressFinalize(
            this);
    }

    private nint RequireHandle()
    {
        ObjectDisposedException.ThrowIf(
            _body ==
                nint.Zero,
            this);

        return _body;
    }

    private static Vector3 ReadVector3(
        nint pointer)
    {
        if (pointer ==
            nint.Zero)
        {
            return Vector3.Zero;
        }

        return new Vector3(
            Marshal.PtrToStructure<float>(
                pointer),
            Marshal.PtrToStructure<float>(
                pointer +
                sizeof(float)),
            Marshal.PtrToStructure<float>(
                pointer +
                2 *
                sizeof(float)));
    }
}
