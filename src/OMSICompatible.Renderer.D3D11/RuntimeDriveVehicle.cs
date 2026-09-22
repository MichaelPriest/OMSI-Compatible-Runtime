using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal sealed class RuntimeDriveVehicle
{
    private const float RideHeight = 0.45f;
    private const float WheelBase = 5.8f;
    private const float MaximumSteeringRadians =
        32.0f * MathF.PI / 180.0f;

    private readonly RuntimeTerrainSampler _terrain;

    public RuntimeDriveVehicle(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        _terrain =
            new RuntimeTerrainSampler(tiles);
    }

    public Vector3 Position { get; private set; }

    public float HeadingRadians { get; private set; }

    public float SpeedMetersPerSecond { get; private set; }

    public float SteeringInput { get; private set; }

    public float SpeedKph =>
        SpeedMetersPerSecond * 3.6f;

    public void Reset(
        IReadOnlyList<RuntimeSplineInfo> splines,
        RuntimeTerrainGeometry terrainGeometry)
    {
        var spawn =
            splines.FirstOrDefault(
                static spline =>
                    spline.LengthMeters > 2.0);

        if (spawn is not null)
        {
            var x =
                spawn.TileX * 300.0 +
                spawn.X;
            var z =
                spawn.TileY * 300.0 +
                spawn.Z;

            var y =
                _terrain.TrySample(
                    x,
                    z,
                    out var sampled)
                    ? sampled + RideHeight
                    : (float)spawn.Y + RideHeight;

            Position =
                new Vector3(
                    (float)x,
                    y,
                    (float)z);

            HeadingRadians =
                (float)(
                    spawn.HeadingDegrees *
                    Math.PI /
                    180.0);
        }
        else
        {
            var center =
                terrainGeometry.Center;

            var y =
                _terrain.TrySample(
                    center.X,
                    center.Z,
                    out var sampled)
                    ? sampled + RideHeight
                    : center.Y + RideHeight;

            Position =
                new Vector3(
                    center.X,
                    y,
                    center.Z);

            HeadingRadians = 0.0f;
        }

        SpeedMetersPerSecond = 0.0f;
        SteeringInput = 0.0f;
    }

    public void Update(
        float driveInput,
        float steeringInput,
        bool handBrake,
        float deltaSeconds)
    {
        deltaSeconds = Math.Clamp(
            deltaSeconds,
            0.0f,
            0.1f);

        steeringInput = Math.Clamp(
            steeringInput,
            -1.0f,
            1.0f);

        SteeringInput =
            MoveTowards(
                SteeringInput,
                steeringInput,
                3.5f * deltaSeconds);

        var requestedDirection =
            Math.Sign(driveInput);

        var acceleration =
            requestedDirection == 0
                ? 0.0f
                : requestedDirection *
                  (requestedDirection < 0
                      ? 2.1f
                      : 3.0f);

        if (requestedDirection != 0 &&
            Math.Sign(SpeedMetersPerSecond) != 0 &&
            Math.Sign(SpeedMetersPerSecond) !=
            requestedDirection)
        {
            acceleration =
                requestedDirection * 6.0f;
        }

        SpeedMetersPerSecond +=
            acceleration *
            deltaSeconds;

        var rollingDrag =
            handBrake
                ? 10.0f
                : requestedDirection == 0
                    ? 1.15f
                    : 0.22f;

        SpeedMetersPerSecond =
            MoveTowards(
                SpeedMetersPerSecond,
                0.0f,
                rollingDrag *
                deltaSeconds);

        SpeedMetersPerSecond = Math.Clamp(
            SpeedMetersPerSecond,
            -7.0f,
            22.5f);

        var steeringAngle =
            SteeringInput *
            MaximumSteeringRadians;

        if (Math.Abs(SpeedMetersPerSecond) > 0.02f)
        {
            HeadingRadians +=
                MathF.Tan(steeringAngle) *
                SpeedMetersPerSecond /
                WheelBase *
                deltaSeconds;
        }

        var forward =
            new Vector3(
                MathF.Sin(HeadingRadians),
                0.0f,
                MathF.Cos(HeadingRadians));

        Position +=
            forward *
            SpeedMetersPerSecond *
            deltaSeconds;

        if (_terrain.TrySample(
                Position.X,
                Position.Z,
                out var groundHeight))
        {
            Position =
                new Vector3(
                    Position.X,
                    groundHeight + RideHeight,
                    Position.Z);
        }
    }

    public Matrix4x4 CreateChaseViewProjection(
        float aspect,
        RuntimeTerrainGeometry terrainGeometry)
    {
        var forward =
            new Vector3(
                MathF.Sin(HeadingRadians),
                0.0f,
                MathF.Cos(HeadingRadians));

        var eye =
            Position -
            forward * 14.0f +
            Vector3.UnitY * 6.0f;

        var target =
            Position +
            forward * 8.0f +
            Vector3.UnitY * 1.6f;

        var view =
            Matrix4x4.CreateLookAt(
                eye,
                target,
                Vector3.UnitY);

        var span = MathF.Max(
            terrainGeometry.HorizontalSpan,
            300.0f);

        var projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.PI / 3.0f,
                MathF.Max(aspect, 0.1f),
                0.2f,
                MathF.Max(
                    5_000.0f,
                    span * 8.0f));

        return view * projection;
    }

    public Matrix4x4 CreateWorldMatrix()
    {
        return
            Matrix4x4.CreateRotationY(
                HeadingRadians) *
            Matrix4x4.CreateTranslation(
                Position);
    }

    private static float MoveTowards(
        float current,
        float target,
        float maximumDelta)
    {
        if (Math.Abs(target - current) <=
            maximumDelta)
        {
            return target;
        }

        return current +
               Math.Sign(target - current) *
               maximumDelta;
    }
}
