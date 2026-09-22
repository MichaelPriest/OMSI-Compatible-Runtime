using System.Numerics;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeDriveGear
{
    Reverse = -1,
    Neutral = 0,
    Drive = 1
}

internal sealed class RuntimeDriveVehicle
{
    private const float RideHeight = 0.45f;
    private const float WheelBase = 5.8f;
    private const float MaximumSteeringRadians =
        32.0f * MathF.PI / 180.0f;

    private RuntimeTerrainSampler _terrain;

    public RuntimeDriveVehicle(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        _terrain =
            new RuntimeTerrainSampler(tiles);
    }

    public void ReplaceTerrainTiles(
        IReadOnlyList<RuntimeTileInfo> tiles)
    {
        _terrain =
            new RuntimeTerrainSampler(
                tiles);

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

    public Vector3 Position { get; private set; }

    public float HeadingRadians { get; private set; }

    public float SpeedMetersPerSecond { get; private set; }

    public float SteeringInput { get; private set; }

    public float BrakeLevel { get; private set; }

    public float AcceleratorLevel { get; private set; }

    public bool ElectricalSystemEnabled { get; private set; }

    public bool EngineRunning { get; private set; }

    public bool ParkingBrakeEngaged { get; private set; }

    public bool StopBrakeEngaged { get; private set; }

    public RuntimeDriveGear Gear { get; private set; } =
        RuntimeDriveGear.Neutral;

    public float SpeedKph =>
        SpeedMetersPerSecond * 3.6f;

    public void Reset(
        IReadOnlyList<RuntimeSplineInfo> splines,
        RuntimeTerrainGeometry terrainGeometry,
        RuntimeSpawnInfo? selectedSpawn = null)
    {
        if (selectedSpawn is not null)
        {
            var y =
                _terrain.TrySample(
                    selectedSpawn.X,
                    selectedSpawn.Z,
                    out var sampled)
                    ? sampled + RideHeight
                    : (float)selectedSpawn.Y +
                      RideHeight;

            Position =
                new Vector3(
                    (float)selectedSpawn.X,
                    y,
                    (float)selectedSpawn.Z);

            HeadingRadians =
                (float)(
                    selectedSpawn.HeadingDegrees *
                    Math.PI /
                    180.0);
        }
        else
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
                        : (float)spawn.Y +
                          RideHeight;

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
        }

        SpeedMetersPerSecond = 0.0f;
        SteeringInput = 0.0f;
        BrakeLevel = 0.0f;
        AcceleratorLevel = 0.0f;

        ElectricalSystemEnabled = false;
        EngineRunning = false;
        ParkingBrakeEngaged = true;
        StopBrakeEngaged = false;
        Gear = RuntimeDriveGear.Neutral;
    }

    public void ToggleElectricalSystem()
    {
        ElectricalSystemEnabled =
            !ElectricalSystemEnabled;

        if (!ElectricalSystemEnabled)
        {
            EngineRunning = false;
        }
    }

    public void ToggleEngine()
    {
        if (EngineRunning)
        {
            EngineRunning = false;
            return;
        }

        if (ElectricalSystemEnabled)
        {
            EngineRunning = true;
        }
    }

    public void SelectGear(RuntimeDriveGear gear)
    {
        Gear = gear;
    }

    public void ToggleParkingBrake()
    {
        ParkingBrakeEngaged =
            !ParkingBrakeEngaged;
    }

    public void ToggleStopBrake()
    {
        StopBrakeEngaged =
            !StopBrakeEngaged;
    }

    public void UpdateOmsiControls(
        bool acceleratorHeld,
        bool brakeIncreaseHeld,
        bool brakeReleaseHeld,
        float steeringDirection,
        bool centerSteeringHeld,
        float deltaSeconds)
    {
        deltaSeconds = Math.Clamp(
            deltaSeconds,
            0.0f,
            0.1f);

        if (brakeIncreaseHeld)
        {
            BrakeLevel = Math.Clamp(
                BrakeLevel +
                1.15f * deltaSeconds,
                0.0f,
                1.0f);
        }

        if (brakeReleaseHeld)
        {
            BrakeLevel = MoveTowards(
                BrakeLevel,
                0.0f,
                1.45f * deltaSeconds);
        }

        if (acceleratorHeld)
        {
            // OMSI keyboard driving releases the held service brake
            // again when the accelerator is applied.
            BrakeLevel = MoveTowards(
                BrakeLevel,
                0.0f,
                3.0f * deltaSeconds);
        }

        var canApplyPower =
            ElectricalSystemEnabled &&
            EngineRunning &&
            Gear != RuntimeDriveGear.Neutral &&
            !ParkingBrakeEngaged &&
            !StopBrakeEngaged;

        AcceleratorLevel = MoveTowards(
            AcceleratorLevel,
            acceleratorHeld &&
            canApplyPower
                ? 1.0f
                : 0.0f,
            2.8f * deltaSeconds);

        if (centerSteeringHeld)
        {
            SteeringInput = MoveTowards(
                SteeringInput,
                0.0f,
                4.5f * deltaSeconds);
        }
        else if (Math.Abs(steeringDirection) > 0.01f)
        {
            SteeringInput = Math.Clamp(
                SteeringInput +
                steeringDirection *
                1.8f *
                deltaSeconds,
                -1.0f,
                1.0f);
        }

        ApplyDynamics(deltaSeconds);
    }

    public void UpdateOmsiMouseControls(
        float accelerator,
        float brake,
        float steering,
        float deltaSeconds)
    {
        deltaSeconds = Math.Clamp(
            deltaSeconds,
            0.0f,
            0.1f);

        accelerator = Math.Clamp(
            accelerator,
            0.0f,
            1.0f);
        brake = Math.Clamp(
            brake,
            0.0f,
            1.0f);
        steering = Math.Clamp(
            steering,
            -1.0f,
            1.0f);

        // OMSI makes mouse steering progressively more precise
        // as speed increases.
        var speedPrecision =
            Math.Clamp(
                Math.Abs(SpeedKph) / 60.0f,
                0.0f,
                1.0f);

        var steeringScale =
            1.0f -
            speedPrecision * 0.65f;

        var steeringTarget =
            steering *
            steeringScale;

        SteeringInput = MoveTowards(
            SteeringInput,
            steeringTarget,
            5.0f * deltaSeconds);

        var canApplyPower =
            ElectricalSystemEnabled &&
            EngineRunning &&
            Gear != RuntimeDriveGear.Neutral &&
            !ParkingBrakeEngaged &&
            !StopBrakeEngaged;

        AcceleratorLevel = MoveTowards(
            AcceleratorLevel,
            canApplyPower
                ? accelerator
                : 0.0f,
            4.0f * deltaSeconds);

        if (accelerator > 0.02f)
        {
            BrakeLevel = MoveTowards(
                BrakeLevel,
                0.0f,
                5.0f * deltaSeconds);
        }
        else
        {
            BrakeLevel = MoveTowards(
                BrakeLevel,
                brake,
                5.0f * deltaSeconds);
        }

        ApplyDynamics(deltaSeconds);
    }

    private void ApplyDynamics(float deltaSeconds)
    {
        var requestedDirection =
            (int)Gear;

        var propulsion =
            requestedDirection *
            AcceleratorLevel *
            (requestedDirection < 0
                ? 2.0f
                : 3.1f);

        SpeedMetersPerSecond +=
            propulsion *
            deltaSeconds;

        var effectiveBrake =
            Math.Clamp(
                BrakeLevel +
                (StopBrakeEngaged ? 0.55f : 0.0f) +
                (ParkingBrakeEngaged ? 1.0f : 0.0f),
                0.0f,
                1.0f);

        SpeedMetersPerSecond =
            MoveTowards(
                SpeedMetersPerSecond,
                0.0f,
                effectiveBrake *
                8.5f *
                deltaSeconds);

        if (AcceleratorLevel <= 0.001f)
        {
            SpeedMetersPerSecond =
                MoveTowards(
                    SpeedMetersPerSecond,
                    0.0f,
                    0.22f *
                    deltaSeconds);
        }

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

    public Matrix4x4 CreateDriverViewProjection(
        RuntimeDriverCameraInfo camera,
        float aspect,
        RuntimeTerrainGeometry terrainGeometry)
    {
        var localEye =
            new Vector3(
                (float)camera.X,
                (float)camera.Y,
                (float)camera.Z);

        var vehicleRotation =
            Matrix4x4.CreateRotationY(
                HeadingRadians);

        var eye =
            Vector3.Transform(
                localEye,
                vehicleRotation) +
            Position;

        var localHeading =
            DegreesToRadians(
                camera.HeadingDegrees);

        var localPitch =
            DegreesToRadians(
                camera.PitchDegrees);

        var localForward =
            new Vector3(
                MathF.Sin(localHeading) *
                MathF.Cos(localPitch),
                MathF.Sin(localPitch),
                MathF.Cos(localHeading) *
                MathF.Cos(localPitch));

        var forward =
            Vector3.Normalize(
                Vector3.TransformNormal(
                    localForward,
                    vehicleRotation));

        var target =
            eye +
            forward *
            MathF.Max(
                (float)camera.EyeDistance,
                2.0f);

        var view =
            Matrix4x4.CreateLookAt(
                eye,
                target,
                Vector3.UnitY);

        var span =
            MathF.Max(
                terrainGeometry.HorizontalSpan,
                300.0f);

        var fovDegrees =
            Math.Clamp(
                camera.FieldOfViewDegrees,
                25.0,
                120.0);

        var projection =
            Matrix4x4.CreatePerspectiveFieldOfView(
                DegreesToRadians(
                    fovDegrees),
                MathF.Max(
                    aspect,
                    0.1f),
                0.04f,
                MathF.Max(
                    5_000.0f,
                    span * 8.0f));

        return view * projection;
    }

    public Matrix4x4 CreatePassengerViewProjection(
        RuntimePassengerCameraInfo camera,
        float aspect,
        RuntimeTerrainGeometry terrainGeometry)
    {
        return CreateDriverViewProjection(
            new RuntimeDriverCameraInfo(
                camera.X,
                camera.Y,
                camera.Z,
                camera.EyeDistance,
                camera.FieldOfViewDegrees,
                camera.HeadingDegrees,
                camera.PitchDegrees),
            aspect,
            terrainGeometry);
    }

    public Matrix4x4 CreateChaseViewProjection(
        float aspect,
        RuntimeTerrainGeometry terrainGeometry,
        RuntimeOutsideCameraCenterInfo? outsideCenter)
    {
        var forward =
            new Vector3(
                MathF.Sin(HeadingRadians),
                0.0f,
                MathF.Cos(HeadingRadians));

        var vehicleRotation =
            Matrix4x4.CreateRotationY(
                HeadingRadians);

        var localCenter =
            outsideCenter is null
                ? new Vector3(
                    0.0f,
                    1.6f,
                    0.0f)
                : new Vector3(
                    (float)outsideCenter.X,
                    (float)outsideCenter.Y,
                    (float)outsideCenter.Z);

        var center =
            Vector3.Transform(
                localCenter,
                vehicleRotation) +
            Position;

        var eye =
            center -
            forward * 14.0f +
            Vector3.UnitY * 4.4f;

        var target =
            center +
            forward * 8.0f;

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

    private static float DegreesToRadians(
        double value) =>
        (float)(
            value *
            Math.PI /
            180.0);

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
