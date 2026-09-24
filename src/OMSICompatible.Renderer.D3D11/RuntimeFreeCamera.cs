using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeFreeCamera
{
    private const float MouseSensitivity = 0.004f;

    public Vector3 Position { get; private set; }

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public float MoveSpeed { get; private set; } = 30.0f;

    public Vector3 Forward
    {
        get
        {
            var cosPitch = MathF.Cos(Pitch);

            var forward = new Vector3(
                cosPitch * MathF.Sin(Yaw),
                MathF.Sin(Pitch),
                cosPitch * MathF.Cos(Yaw));

            return forward.LengthSquared() > 0.000001f
                ? Vector3.Normalize(forward)
                : Vector3.UnitZ;
        }
    }

    public void Reset(RuntimeTerrainGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var span = MathF.Max(
            geometry.HorizontalSpan,
            300.0f);

        var elevationSpan = MathF.Max(
            geometry.MaximumHeight -
            geometry.MinimumHeight,
            10.0f);

        var target = geometry.Center;

        Position = new Vector3(
            target.X,
            geometry.MaximumHeight +
                span * 0.70f +
                elevationSpan * 0.5f,
            target.Z -
                span * 0.80f);

        var direction = Vector3.Normalize(
            target - Position);

        Pitch = MathF.Asin(
            Math.Clamp(
                direction.Y,
                -1.0f,
                1.0f));

        Yaw = MathF.Atan2(
            direction.X,
            direction.Z);

        MoveSpeed = Math.Clamp(
            span / 20.0f,
            10.0f,
            500.0f);
    }

    public void SetLookAt(
        Vector3 position,
        Vector3 target,
        float? moveSpeed = null)
    {
        Position =
            position;

        var delta =
            target -
            position;

        if (delta.LengthSquared() >
            0.000001f)
        {
            var direction =
                Vector3.Normalize(
                    delta);

            Pitch =
                MathF.Asin(
                    Math.Clamp(
                        direction.Y,
                        -1.0f,
                        1.0f));

            Yaw =
                MathF.Atan2(
                    direction.X,
                    direction.Z);
        }

        if (moveSpeed.HasValue)
        {
            MoveSpeed =
                Math.Clamp(
                    moveSpeed.Value,
                    1.0f,
                    2_000.0f);
        }
    }

    public void Rotate(
        float deltaX,
        float deltaY)
    {
        Yaw +=
            deltaX *
            MouseSensitivity;

        Pitch -=
            deltaY *
            MouseSensitivity;

        Pitch = Math.Clamp(
            Pitch,
            -1.45f,
            1.45f);
    }

    public void Move(
        float forwardInput,
        float rightInput,
        float upInput,
        float deltaSeconds,
        bool fast,
        bool slow)
    {
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        var forward = Forward;
        var horizontalForward =
            new Vector3(
                forward.X,
                0.0f,
                forward.Z);

        if (horizontalForward.LengthSquared() <= 0.000001f)
        {
            horizontalForward = Vector3.UnitZ;
        }
        else
        {
            horizontalForward =
                Vector3.Normalize(
                    horizontalForward);
        }

        var right = Vector3.Cross(
            Vector3.UnitY,
            horizontalForward);

        if (right.LengthSquared() > 0.000001f)
        {
            right = Vector3.Normalize(right);
        }

        var movement =
            horizontalForward * forwardInput +
            right * rightInput +
            Vector3.UnitY * upInput;

        if (movement.LengthSquared() <= 0.000001f)
        {
            return;
        }

        movement = Vector3.Normalize(movement);

        var multiplier = fast
            ? 4.0f
            : slow
                ? 0.25f
                : 1.0f;

        Position +=
            movement *
            MoveSpeed *
            multiplier *
            deltaSeconds;
    }

    public void Pan(
        float deltaX,
        float deltaY,
        float viewportHeight)
    {
        var forward =
            Forward;

        var horizontalForward =
            new Vector3(
                forward.X,
                0.0f,
                forward.Z);

        if (horizontalForward.LengthSquared() <=
            0.000001f)
        {
            horizontalForward =
                Vector3.UnitZ;
        }
        else
        {
            horizontalForward =
                Vector3.Normalize(
                    horizontalForward);
        }

        var right =
            Vector3.Cross(
                Vector3.UnitY,
                horizontalForward);

        if (right.LengthSquared() >
            0.000001f)
        {
            right =
                Vector3.Normalize(
                    right);
        }

        var scale =
            Math.Max(
                MoveSpeed,
                1.0f) /
            Math.Max(
                viewportHeight,
                180.0f) *
            1.6f;

        Position +=
            -right *
                deltaX *
                scale +
            Vector3.UnitY *
                deltaY *
                scale;
    }

    public void Dolly(float wheelSteps)
    {
        if (wheelSteps == 0.0f)
        {
            return;
        }

        Position +=
            Forward *
            MoveSpeed *
            wheelSteps *
            0.18f;
    }

    public void AdjustSpeed(float wheelSteps)
    {
        if (wheelSteps == 0.0f)
        {
            return;
        }

        MoveSpeed = Math.Clamp(
            MoveSpeed *
            MathF.Pow(
                1.15f,
                wheelSteps),
            1.0f,
            2_000.0f);
    }

    public Matrix4x4 CreateViewProjection(
        float aspect,
        RuntimeTerrainGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        aspect = MathF.Max(
            aspect,
            0.1f);

        var span = MathF.Max(
            geometry.HorizontalSpan,
            300.0f);

        var elevationSpan = MathF.Max(
            geometry.MaximumHeight -
            geometry.MinimumHeight,
            10.0f);

        var view = Matrix4x4.CreateLookAt(
            Position,
            Position + Forward,
            Vector3.UnitY);

        var nearPlane = MathF.Max(
            0.25f,
            span / 50_000.0f);

        var farPlane = MathF.Max(
            5_000.0f,
            span * 8.0f +
            elevationSpan * 4.0f);

        var projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3.0f,
                aspect,
                nearPlane,
                farPlane);

        return view * projection;
    }
}
