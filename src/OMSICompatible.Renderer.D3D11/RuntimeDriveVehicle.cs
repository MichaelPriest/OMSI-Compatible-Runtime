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
    // OMSI vehicle meshes are authored against a ground plane: axle/wheel
    // placement already carries the wheel-centre height and tyre radius.
    // Adding a generic ride-height offset lifts every bus above the road.
    private const float ModelGroundPlaneOffsetMeters = 0.0f;
    private const float Gravity = 9.80665f;
    private const float DefaultWheelBaseMeters = 5.8f;
    private const float DefaultMaximumSteeringDegrees = 32.0f;
    private const float DefaultMassKilograms = 11_000.0f;
    private const float DefaultCenterOfGravityHeightMeters = 1.2f;
    private const float DefaultTrackWidthMeters = 2.4f;
    private const float DefaultWheelDiameterMeters = 0.94f;
    private const float DefaultRollingResistanceNewtons = 1_000.0f;
    private const float DefaultSpringKilonewtonsPerMeter = 240.0f;
    private const float DefaultDamperKilonewtonSecondsPerMeter = 20.0f;
    private const float DefaultYawInertiaKilogramSquareMeters = 300_000.0f;

    private RuntimeTerrainSampler _terrain;
    private readonly float _wheelBaseMeters;
    private readonly float _frontAxleLongitudinalMeters;
    private readonly float _rearAxleLongitudinalMeters;
    private readonly float _rotationPointLongitudinalMeters;
    private readonly float _maximumCurvaturePerMeter;
    private readonly float _steeringAxleDistanceMeters;
    private readonly float _maximumSteeringRadians;
    private readonly float _massKilograms;
    private readonly float _centerOfGravityHeightMeters;
    private readonly float _trackWidthMeters;
    private readonly float _wheelRadiusMeters;
    private readonly float _rollingResistanceNewtons;
    private readonly float _suspensionSpringNewtonsPerMeter;
    private readonly float _frontSuspensionSpringNewtonsPerMeter;
    private readonly float _rearSuspensionSpringNewtonsPerMeter;
    private readonly float _frontSuspensionDamperNewtonSecondsPerMeter;
    private readonly float _rearSuspensionDamperNewtonSecondsPerMeter;
    private readonly float _suspensionResponse;
    private readonly float _pitchNaturalFrequencyRadiansPerSecond;
    private readonly float _pitchDampingRatio;
    private readonly float _yawResponse;
    private float _yawRateRadiansPerSecond;
    private float _groundPitchRadians;
    private float _groundRollRadians;
    private float _bodyPitchRadians;
    private float _bodyPitchVelocityRadiansPerSecond;
    private float _bodyRollRadians;
    private float _longitudinalAccelerationMetersPerSecondSquared;
    private float _wheelRotationRadians;

    public RuntimeDriveVehicle(
        IReadOnlyList<RuntimeTileInfo> tiles,
        RuntimeVehiclePhysicsInfo? physics)
    {
        _terrain =
            new RuntimeTerrainSampler(tiles);

        _wheelBaseMeters =
            Math.Clamp(
                (float)(physics?.WheelBaseMeters ??
                    DefaultWheelBaseMeters),
                1.5f,
                12.0f);

        _frontAxleLongitudinalMeters =
            Math.Clamp(
                (float)(physics?.FrontAxleLongitudinalMeters ??
                    (_wheelBaseMeters * 0.5f)),
                -12.0f,
                12.0f);

        _rearAxleLongitudinalMeters =
            Math.Clamp(
                (float)(physics?.RearAxleLongitudinalMeters ??
                    (-_wheelBaseMeters * 0.5f)),
                -12.0f,
                12.0f);

        _rotationPointLongitudinalMeters =
            Math.Clamp(
                (float)(physics?.RotationPointLongitudinalMeters ??
                    _rearAxleLongitudinalMeters),
                -12.0f,
                12.0f);

        _maximumCurvaturePerMeter =
            Math.Clamp(
                (float)Math.Abs(
                    physics?.InverseMinimumTurnRadius ??
                    0.0),
                0.0f,
                1.0f);

        _steeringAxleDistanceMeters =
            Math.Max(
                Math.Abs(
                    _frontAxleLongitudinalMeters -
                    _rotationPointLongitudinalMeters),
                0.75f);

        var steeringDegrees =
            Math.Clamp(
                (float)(physics?.MaximumSteeringAngleDegrees ??
                    DefaultMaximumSteeringDegrees),
                10.0f,
                55.0f);

        _maximumSteeringRadians =
            steeringDegrees *
            MathF.PI /
            180.0f;

        _massKilograms =
            Math.Clamp(
                (float)(physics?.MassTonnes ??
                    (DefaultMassKilograms /
                     1000.0f)) *
                1000.0f,
                2_000.0f,
                45_000.0f);

        _centerOfGravityHeightMeters =
            Math.Clamp(
                (float)(physics?.CenterOfGravityHeightMeters ??
                    DefaultCenterOfGravityHeightMeters),
                0.35f,
                3.5f);

        _trackWidthMeters =
            Math.Clamp(
                (float)(physics?.TrackWidthMeters ??
                    DefaultTrackWidthMeters),
                1.2f,
                3.5f);

        _wheelRadiusMeters =
            Math.Clamp(
                (float)(physics?.AverageWheelDiameterMeters ??
                    DefaultWheelDiameterMeters) *
                0.5f,
                0.20f,
                0.80f);

        _rollingResistanceNewtons =
            Math.Clamp(
                (float)(physics?.RollingResistanceNewtons ??
                    DefaultRollingResistanceNewtons),
                0.0f,
                15_000.0f);

        var springRate =
            Math.Clamp(
                (float)(physics?.SuspensionSpringKilonewtonsPerMeter ??
                    DefaultSpringKilonewtonsPerMeter),
                25.0f,
                1_500.0f);

        _suspensionSpringNewtonsPerMeter =
            springRate *
            1_000.0f;

        var damperRate =
            Math.Clamp(
                (float)(physics?.SuspensionDamperKilonewtonSecondsPerMeter ??
                    DefaultDamperKilonewtonSecondsPerMeter),
                2.0f,
                150.0f);

        var frontSpringRate =
            Math.Clamp(
                (float)(physics?.FrontSuspensionSpringKilonewtonsPerMeter ??
                    springRate),
                25.0f,
                1_500.0f);

        var rearSpringRate =
            Math.Clamp(
                (float)(physics?.RearSuspensionSpringKilonewtonsPerMeter ??
                    springRate),
                25.0f,
                1_500.0f);

        var frontDamperRate =
            Math.Clamp(
                (float)(physics?.FrontSuspensionDamperKilonewtonSecondsPerMeter ??
                    damperRate),
                2.0f,
                150.0f);

        var rearDamperRate =
            Math.Clamp(
                (float)(physics?.RearSuspensionDamperKilonewtonSecondsPerMeter ??
                    damperRate),
                2.0f,
                150.0f);

        _frontSuspensionSpringNewtonsPerMeter =
            frontSpringRate *
            1_000.0f;
        _rearSuspensionSpringNewtonsPerMeter =
            rearSpringRate *
            1_000.0f;
        _frontSuspensionDamperNewtonSecondsPerMeter =
            frontDamperRate *
            1_000.0f;
        _rearSuspensionDamperNewtonSecondsPerMeter =
            rearDamperRate *
            1_000.0f;

        _suspensionResponse =
            Math.Clamp(
                MathF.Sqrt(
                    springRate /
                    DefaultSpringKilonewtonsPerMeter) *
                MathF.Sqrt(
                    DefaultDamperKilonewtonSecondsPerMeter /
                    damperRate),
                0.45f,
                2.5f);

        var frontPitchLever =
            Math.Max(
                Math.Abs(
                    _frontAxleLongitudinalMeters),
                0.5f);

        var rearPitchLever =
            Math.Max(
                Math.Abs(
                    _rearAxleLongitudinalMeters),
                0.5f);

        // OMSI achse_feder/achse_daempfer are specified per side.
        // Two spring/damper units therefore contribute at each axle.
        var pitchStiffness =
            2.0f *
                _frontSuspensionSpringNewtonsPerMeter *
                frontPitchLever *
                frontPitchLever +
            2.0f *
                _rearSuspensionSpringNewtonsPerMeter *
                rearPitchLever *
                rearPitchLever;

        var estimatedPitchInertia =
            Math.Max(
                _massKilograms *
                (_wheelBaseMeters *
                     _wheelBaseMeters +
                 4.0f *
                     _centerOfGravityHeightMeters *
                     _centerOfGravityHeightMeters) /
                12.0f,
                1_000.0f);

        var pitchDamping =
            2.0f *
                _frontSuspensionDamperNewtonSecondsPerMeter *
                frontPitchLever *
                frontPitchLever +
            2.0f *
                _rearSuspensionDamperNewtonSecondsPerMeter *
                rearPitchLever *
                rearPitchLever;

        _pitchNaturalFrequencyRadiansPerSecond =
            Math.Clamp(
                MathF.Sqrt(
                    pitchStiffness /
                    estimatedPitchInertia),
                1.5f,
                8.0f);

        _pitchDampingRatio =
            Math.Clamp(
                pitchDamping /
                (2.0f *
                 MathF.Sqrt(
                     pitchStiffness *
                     estimatedPitchInertia)),
                0.35f,
                1.35f);

        var yawInertia =
            Math.Clamp(
                (float)(physics?.MomentOfInertiaYawTonneSquareMeters ??
                    (DefaultYawInertiaKilogramSquareMeters /
                     1000.0f)) *
                1000.0f,
                20_000.0f,
                5_000_000.0f);

        _yawResponse =
            Math.Clamp(
                DefaultYawInertiaKilogramSquareMeters /
                yawInertia,
                0.25f,
                3.0f);
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
                    groundHeight + ModelGroundPlaneOffsetMeters,
                    Position.Z);
        }
    }

    public Vector3 Position { get; private set; }

    public float HeadingRadians { get; private set; }

    public float SpeedMetersPerSecond { get; private set; }

    public float SteeringInput { get; private set; }

    public float SteeringAngleRadians =>
        SteeringInput *
        _maximumSteeringRadians;

    public float FrontLeftSteeringRadians =>
        ResolveAckermannSteeringAngle(
            leftWheel:
                true);

    public float FrontRightSteeringRadians =>
        ResolveAckermannSteeringAngle(
            leftWheel:
                false);

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

    public float LongitudinalAccelerationMetersPerSecondSquared =>
        _longitudinalAccelerationMetersPerSecondSquared;

    public float BodyPitchRadians =>
        _groundPitchRadians +
        _bodyPitchRadians;

    public float BodyRollRadians =>
        _groundRollRadians +
        _bodyRollRadians;

    public float YawRateRadiansPerSecond =>
        _yawRateRadiansPerSecond;

    public float WheelRotationRadians =>
        _wheelRotationRadians;

    public float WheelRotationSpeedRpm =>
        SpeedMetersPerSecond /
        (2.0f *
         MathF.PI *
         _wheelRadiusMeters) *
        60.0f;

    public float FrontLeftSuspensionMeters =>
        ResolveSuspensionOffset(
            front: true,
            left: true);

    public float FrontRightSuspensionMeters =>
        ResolveSuspensionOffset(
            front: true,
            left: false);

    public float RearLeftSuspensionMeters =>
        ResolveSuspensionOffset(
            front: false,
            left: true);

    public float RearRightSuspensionMeters =>
        ResolveSuspensionOffset(
            front: false,
            left: false);

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
                    ? sampled + ModelGroundPlaneOffsetMeters
                    : (float)selectedSpawn.Y +
                      ModelGroundPlaneOffsetMeters;

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
                        ? sampled + ModelGroundPlaneOffsetMeters
                        : (float)spawn.Y +
                          ModelGroundPlaneOffsetMeters;

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
                    ? sampled + ModelGroundPlaneOffsetMeters
                    : center.Y + ModelGroundPlaneOffsetMeters;

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
        _yawRateRadiansPerSecond = 0.0f;
        _groundPitchRadians = 0.0f;
        _groundRollRadians = 0.0f;
        _bodyPitchRadians = 0.0f;
        _bodyPitchVelocityRadiansPerSecond = 0.0f;
        _bodyRollRadians = 0.0f;
        _longitudinalAccelerationMetersPerSecondSquared = 0.0f;
        _wheelRotationRadians = 0.0f;

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

    public void SetElectricalSystemEnabled(
        bool enabled)
    {
        ElectricalSystemEnabled =
            enabled;

        if (!enabled)
        {
            EngineRunning =
                false;
        }
    }

    public void SetEngineRunning(
        bool running)
    {
        EngineRunning =
            running &&
            ElectricalSystemEnabled;
    }

    public void SetParkingBrake(
        bool engaged)
    {
        ParkingBrakeEngaged =
            engaged;
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

    public void UpdateOmsiControllerControls(
        float accelerator,
        float brake,
        float steering,
        float deltaSeconds)
    {
        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0.0f,
                0.1f);

        accelerator =
            Math.Clamp(
                accelerator,
                0.0f,
                1.0f);

        brake =
            Math.Clamp(
                brake,
                0.0f,
                1.0f);

        steering =
            Math.Clamp(
                steering,
                -1.0f,
                1.0f);

        if (Math.Abs(
                steering) <
            0.018f)
        {
            steering =
                0.0f;
        }

        // OMSI maps a configured game-controller steering axis
        // directly to the in-game steering position. Do not add a
        // speed/turn-rate limiter here; the gamectrler.cfg characteristic
        // and Rev. flag are the authority for the physical wheel.
        SteeringInput =
            steering;

        var canApplyPower =
            ElectricalSystemEnabled &&
            EngineRunning &&
            Gear !=
                RuntimeDriveGear.Neutral &&
            !ParkingBrakeEngaged &&
            !StopBrakeEngaged;

        AcceleratorLevel =
            canApplyPower
                ? accelerator
                : 0.0f;

        BrakeLevel =
            brake;

        ApplyDynamics(
            deltaSeconds);
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
            2.8f * deltaSeconds);

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
        deltaSeconds =
            Math.Clamp(
                deltaSeconds,
                0.0f,
                0.1f);

        if (deltaSeconds <=
            0.0f)
        {
            return;
        }

        var previousSpeed =
            SpeedMetersPerSecond;

        UpdateGroundAttitude();

        var requestedDirection =
            (int)Gear;

        var absoluteSpeed =
            Math.Abs(
                SpeedMetersPerSecond);

        var maximumForwardForceNewtons =
            Math.Clamp(
                _massKilograms *
                    2.55f,
                18_000.0f,
                48_000.0f);

        var nominalEnginePowerWatts =
            Math.Clamp(
                180_000.0f *
                    (_massKilograms /
                     DefaultMassKilograms),
                120_000.0f,
                300_000.0f);

        var powerLimitedForceNewtons =
            nominalEnginePowerWatts /
            Math.Max(
                absoluteSpeed,
                3.0f);

        var availableForwardForceNewtons =
            Math.Min(
                maximumForwardForceNewtons,
                powerLimitedForceNewtons);

        var driveForceNewtons =
            requestedDirection == 0
                ? 0.0f
                : AcceleratorLevel *
                  availableForwardForceNewtons *
                  (requestedDirection < 0
                      ? 0.48f
                      : 1.0f) *
                  requestedDirection;

        var driveAcceleration =
            driveForceNewtons /
            _massKilograms;

        var gradeAcceleration =
            -MathF.Sin(
                _groundPitchRadians) *
            Gravity;

        SpeedMetersPerSecond +=
            (driveAcceleration +
             gradeAcceleration) *
            deltaSeconds;

        var rollingAcceleration =
            _rollingResistanceNewtons /
            _massKilograms;

        var aerodynamicAcceleration =
            0.0022f *
            SpeedMetersPerSecond *
            SpeedMetersPerSecond;

        // Service braking on a city bus is deliberately below the tyre
        // friction ceiling; stop/parking brakes add their own demand rather
        // than being folded into an exaggerated generic 7.2 m/s² scale.
        var serviceBrakeAcceleration =
            BrakeLevel *
            5.4f;

        var stopBrakeAcceleration =
            StopBrakeEngaged
                ? 3.2f
                : 0.0f;

        var parkingBrakeAcceleration =
            ParkingBrakeEngaged
                ? 7.0f
                : 0.0f;

        var passiveDeceleration =
            rollingAcceleration +
            aerodynamicAcceleration +
            Math.Max(
                serviceBrakeAcceleration +
                    stopBrakeAcceleration,
                parkingBrakeAcceleration);

        if (Math.Abs(
                SpeedMetersPerSecond) >
            0.01f)
        {
            SpeedMetersPerSecond =
                MoveTowards(
                    SpeedMetersPerSecond,
                    0.0f,
                    passiveDeceleration *
                    deltaSeconds);
        }
        else if (BrakeLevel >
                     0.05f ||
                 StopBrakeEngaged ||
                 ParkingBrakeEngaged)
        {
            SpeedMetersPerSecond =
                0.0f;
        }

        SpeedMetersPerSecond =
            Math.Clamp(
                SpeedMetersPerSecond,
                -9.0f,
                28.0f);

        _longitudinalAccelerationMetersPerSecondSquared =
            (SpeedMetersPerSecond -
             previousSpeed) /
            deltaSeconds;

        var steeringAngle =
            SteeringInput *
            _maximumSteeringRadians;

        var requestedCurvature =
            _maximumCurvaturePerMeter >
                0.000001f
                ? SteeringInput *
                  _maximumCurvaturePerMeter
                : MathF.Tan(
                      steeringAngle) /
                  _steeringAxleDistanceMeters;

        var targetYawRate =
            Math.Abs(
                SpeedMetersPerSecond) >
            0.02f
                ? requestedCurvature *
                  SpeedMetersPerSecond
                : 0.0f;

        var lateralGripLimit =
            0.62f *
            Gravity;

        var maximumYawRate =
            lateralGripLimit /
            Math.Max(
                absoluteSpeed,
                1.0f);

        targetYawRate =
            Math.Clamp(
                targetYawRate,
                -maximumYawRate,
                maximumYawRate);

        _yawRateRadiansPerSecond =
            MoveTowards(
                _yawRateRadiansPerSecond,
                targetYawRate,
                (2.6f +
                 3.8f *
                 _yawResponse) *
                deltaSeconds);

        if (Math.Abs(
                SpeedMetersPerSecond) <
            0.02f)
        {
            _yawRateRadiansPerSecond =
                MoveTowards(
                    _yawRateRadiansPerSecond,
                    0.0f,
                    5.0f *
                    deltaSeconds);
        }

        var previousHeading =
            HeadingRadians;

        var nextHeading =
            previousHeading +
            _yawRateRadiansPerSecond *
            deltaSeconds;

        var previousForward =
            new Vector3(
                MathF.Sin(
                    previousHeading),
                0.0f,
                MathF.Cos(
                    previousHeading));

        var nextForward =
            new Vector3(
                MathF.Sin(
                    nextHeading),
                0.0f,
                MathF.Cos(
                    nextHeading));

        var middleHeading =
            previousHeading +
            _yawRateRadiansPerSecond *
            deltaSeconds *
            0.5f;

        var middleForward =
            new Vector3(
                MathF.Sin(
                    middleHeading),
                0.0f,
                MathF.Cos(
                    middleHeading));

        var travelledMeters =
            SpeedMetersPerSecond *
            deltaSeconds;

        // OMSI's [rot_pnt_long] is the longitudinal body rotation point.
        // Integrate that point along the road and reconstruct the model
        // origin after yawing; rotating the model around its origin makes
        // long/articulated buses visibly cut corners.
        var rotationPoint =
            Position +
            previousForward *
            _rotationPointLongitudinalMeters;

        rotationPoint +=
            middleForward *
            travelledMeters;

        HeadingRadians =
            nextHeading;

        Position =
            rotationPoint -
            nextForward *
            _rotationPointLongitudinalMeters;

        _wheelRotationRadians +=
            travelledMeters /
            _wheelRadiusMeters;

        if (Math.Abs(
                _wheelRotationRadians) >
            MathF.PI *
            10_000.0f)
        {
            _wheelRotationRadians =
                MathF.IEEERemainder(
                    _wheelRotationRadians,
                    MathF.PI *
                    2.0f);
        }

        if (_terrain.TrySample(
                Position.X,
                Position.Z,
                out var groundHeight))
        {
            Position =
                new Vector3(
                    Position.X,
                    groundHeight +
                    ModelGroundPlaneOffsetMeters,
                    Position.Z);
        }

        UpdateGroundAttitude();
        UpdateBodyDynamics(
            deltaSeconds);
    }

    private void UpdateGroundAttitude()
    {
        var forward =
            new Vector3(
                MathF.Sin(
                    HeadingRadians),
                0.0f,
                MathF.Cos(
                    HeadingRadians));

        var right =
            new Vector3(
                forward.Z,
                0.0f,
                -forward.X);

        var halfTrack =
            _trackWidthMeters *
            0.5f;

        var front =
            Position +
            forward *
            _frontAxleLongitudinalMeters;

        var rear =
            Position +
            forward *
            _rearAxleLongitudinalMeters;

        if (_terrain.TrySample(
                front.X,
                front.Z,
                out var frontHeight) &&
            _terrain.TrySample(
                rear.X,
                rear.Z,
                out var rearHeight))
        {
            _groundPitchRadians =
                MathF.Atan2(
                    frontHeight -
                    rearHeight,
                    Math.Max(
                        Math.Abs(
                            _frontAxleLongitudinalMeters -
                            _rearAxleLongitudinalMeters),
                        0.5f));
        }

        var rightPoint =
            Position +
            right *
            halfTrack;

        var leftPoint =
            Position -
            right *
            halfTrack;

        if (_terrain.TrySample(
                rightPoint.X,
                rightPoint.Z,
                out var rightHeight) &&
            _terrain.TrySample(
                leftPoint.X,
                leftPoint.Z,
                out var leftHeight))
        {
            _groundRollRadians =
                MathF.Atan2(
                    leftHeight -
                    rightHeight,
                    _trackWidthMeters);
        }
    }

    private void UpdateBodyDynamics(
        float deltaSeconds)
    {
        var lateralAcceleration =
            SpeedMetersPerSecond *
            _yawRateRadiansPerSecond;

        var rollTarget =
            Math.Clamp(
                -lateralAcceleration /
                Gravity *
                (_centerOfGravityHeightMeters /
                 Math.Max(
                     _trackWidthMeters,
                     0.5f)) *
                0.55f,
                DegreesToRadians(
                    -8.0),
                DegreesToRadians(
                    8.0));

        // Dynamic load transfer is distributed through the actual front
        // and rear axle spring rates from the .bus file. achse_feder is per
        // side, so each axle has two springs in parallel.
        var longitudinalLoadTransferNewtons =
            -_longitudinalAccelerationMetersPerSecondSquared *
            _massKilograms *
            _centerOfGravityHeightMeters /
            Math.Max(
                _wheelBaseMeters,
                1.0f);

        var frontCompressionMeters =
            longitudinalLoadTransferNewtons /
            Math.Max(
                2.0f *
                    _frontSuspensionSpringNewtonsPerMeter,
                50_000.0f);

        var rearExtensionMeters =
            longitudinalLoadTransferNewtons /
            Math.Max(
                2.0f *
                    _rearSuspensionSpringNewtonsPerMeter,
                50_000.0f);

        var suspensionPitchTravelMeters =
            frontCompressionMeters +
            rearExtensionMeters;

        var pitchTarget =
            Math.Clamp(
                MathF.Atan2(
                    suspensionPitchTravelMeters,
                    Math.Max(
                        _wheelBaseMeters,
                        1.0f)),
                DegreesToRadians(
                    -1.6),
                DegreesToRadians(
                    1.6));

        var response =
            (2.0f +
             3.5f *
             _suspensionResponse) *
            deltaSeconds;

        _bodyRollRadians =
            MoveTowards(
                _bodyRollRadians,
                rollTarget,
                response);

        var pitchError =
            pitchTarget -
            _bodyPitchRadians;

        var pitchAcceleration =
            _pitchNaturalFrequencyRadiansPerSecond *
                _pitchNaturalFrequencyRadiansPerSecond *
                pitchError -
            2.0f *
                _pitchDampingRatio *
                _pitchNaturalFrequencyRadiansPerSecond *
                _bodyPitchVelocityRadiansPerSecond;

        _bodyPitchVelocityRadiansPerSecond +=
            pitchAcceleration *
            deltaSeconds;

        _bodyPitchRadians +=
            _bodyPitchVelocityRadiansPerSecond *
            deltaSeconds;

        _bodyPitchRadians =
            Math.Clamp(
                _bodyPitchRadians,
                DegreesToRadians(
                    -1.8),
                DegreesToRadians(
                    1.8));
    }

    private float ResolveAckermannSteeringAngle(
        bool leftWheel)
    {
        var centerAngle =
            SteeringAngleRadians;

        if (Math.Abs(
                centerAngle) <
            0.00001f)
        {
            return 0.0f;
        }

        var sign =
            Math.Sign(
                centerAngle);

        var centerRadius =
            _maximumCurvaturePerMeter >
                0.000001f
                ? 1.0f /
                  Math.Max(
                      Math.Abs(
                          SteeringInput) *
                      _maximumCurvaturePerMeter,
                      0.0001f)
                : _steeringAxleDistanceMeters /
                  Math.Max(
                      Math.Abs(
                          MathF.Tan(
                              centerAngle)),
                      0.0001f);

        var halfTrack =
            _trackWidthMeters *
            0.5f;

        var isInnerWheel =
            sign > 0
                ? !leftWheel
                : leftWheel;

        var wheelRadius =
            Math.Max(
                centerRadius +
                (isInnerWheel
                    ? -halfTrack
                    : halfTrack),
                0.25f);

        return sign *
               MathF.Atan(
                   _steeringAxleDistanceMeters /
                   wheelRadius);
    }

    private float ResolveSuspensionOffset(
        bool front,
        bool left)
    {
        var axleLongitudinal =
            front
                ? _frontAxleLongitudinalMeters
                : _rearAxleLongitudinalMeters;

        // Compensate wheel position for body rotation. A positive
        // nose-down pitch lowers the front body, so the front wheel mesh
        // must move upward relative to the body to remain on the road.
        // The old negative sign doubled the visual dive/squat.
        var pitch =
            _bodyPitchRadians *
            axleLongitudinal;

        var roll =
            _bodyRollRadians *
            _trackWidthMeters *
            0.5f *
            (left
                ? -1.0f
                : 1.0f);

        return Math.Clamp(
            pitch +
            roll,
            -0.22f,
            0.22f);
    }

    public Vector3 GetDriverCameraPosition(
        RuntimeDriverCameraInfo camera)
    {
        var localEye =
            new Vector3(
                (float)camera.X,
                (float)camera.Y,
                (float)camera.Z);

        var vehicleRotation =
            Matrix4x4.CreateRotationY(
                HeadingRadians);

        return Vector3.Transform(
                   localEye,
                   vehicleRotation) +
               Position;
    }

    public Vector3 GetPassengerCameraPosition(
        RuntimePassengerCameraInfo camera) =>
        GetDriverCameraPosition(
            new RuntimeDriverCameraInfo(
                camera.X,
                camera.Y,
                camera.Z,
                camera.EyeDistance,
                camera.FieldOfViewDegrees,
                camera.HeadingDegrees,
                camera.PitchDegrees));

    public Vector3 GetChaseCameraPosition(
        RuntimeOutsideCameraCenterInfo? outsideCenter,
        float orbitYawRadians = 0.0f,
        float orbitPitchRadians = 0.0f,
        float distanceScale = 1.0f)
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

        var orbitHeading =
            HeadingRadians +
            orbitYawRadians;

        var orbitForward =
            new Vector3(
                MathF.Sin(
                    orbitHeading),
                0.0f,
                MathF.Cos(
                    orbitHeading));

        var baseDistance =
            MathF.Sqrt(
                14.0f * 14.0f +
                4.4f * 4.4f);

        var basePitch =
            MathF.Atan2(
                4.4f,
                14.0f);

        var orbitPitch =
            Math.Clamp(
                basePitch +
                orbitPitchRadians,
                -1.15f,
                1.25f);

        var distance =
            baseDistance *
            Math.Clamp(
                distanceScale,
                0.35f,
                4.0f);

        return center -
               orbitForward *
                   (MathF.Cos(
                        orbitPitch) *
                    distance) +
               Vector3.UnitY *
                   (MathF.Sin(
                        orbitPitch) *
                    distance);
    }

    public Matrix4x4 CreateDriverViewProjection(
        RuntimeDriverCameraInfo camera,
        float aspect,
        RuntimeTerrainGeometry terrainGeometry,
        float headingOffsetRadians = 0.0f,
        float pitchOffsetRadians = 0.0f,
        float fieldOfViewScale = 1.0f)
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
                camera.HeadingDegrees) +
            headingOffsetRadians;

        var localPitch =
            Math.Clamp(
                DegreesToRadians(
                    camera.PitchDegrees) +
                pitchOffsetRadians,
                -1.45f,
                1.45f);

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
                camera.FieldOfViewDegrees *
                Math.Clamp(
                    fieldOfViewScale,
                    0.35f,
                    2.0f),
                18.0,
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

    public Matrix4x4 CreateReflectionViewProjection(
        RuntimeReflectionCameraInfo camera,
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

    public Matrix4x4 CreatePassengerViewProjection(
        RuntimePassengerCameraInfo camera,
        float aspect,
        RuntimeTerrainGeometry terrainGeometry,
        float headingOffsetRadians = 0.0f,
        float pitchOffsetRadians = 0.0f,
        float fieldOfViewScale = 1.0f)
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
            terrainGeometry,
            headingOffsetRadians,
            pitchOffsetRadians,
            fieldOfViewScale);
    }

    public Matrix4x4 CreateChaseViewProjection(
        float aspect,
        RuntimeTerrainGeometry terrainGeometry,
        RuntimeOutsideCameraCenterInfo? outsideCenter,
        float orbitYawRadians = 0.0f,
        float orbitPitchRadians = 0.0f,
        float distanceScale = 1.0f)
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

        var orbitHeading =
            HeadingRadians +
            orbitYawRadians;

        var orbitForward =
            new Vector3(
                MathF.Sin(
                    orbitHeading),
                0.0f,
                MathF.Cos(
                    orbitHeading));

        var baseDistance =
            MathF.Sqrt(
                14.0f * 14.0f +
                4.4f * 4.4f);

        var basePitch =
            MathF.Atan2(
                4.4f,
                14.0f);

        var orbitPitch =
            Math.Clamp(
                basePitch +
                orbitPitchRadians,
                -1.15f,
                1.25f);

        var distance =
            baseDistance *
            Math.Clamp(
                distanceScale,
                0.35f,
                4.0f);

        var horizontalDistance =
            MathF.Cos(
                orbitPitch) *
            distance;

        var verticalDistance =
            MathF.Sin(
                orbitPitch) *
            distance;

        var eye =
            center -
            orbitForward *
                horizontalDistance +
            Vector3.UnitY *
                verticalDistance;

        var target =
            center +
            forward * 1.5f;

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
            Matrix4x4.CreateRotationZ(
                BodyRollRadians) *
            Matrix4x4.CreateRotationX(
                BodyPitchRadians) *
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
