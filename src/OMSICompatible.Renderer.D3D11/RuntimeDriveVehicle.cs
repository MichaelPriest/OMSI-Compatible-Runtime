using System.Numerics;
using OmsiCompat.Physics.Ode;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeDriveGear
{
    Reverse = -1,
    Neutral = 0,
    Drive = 1
}

internal sealed class RuntimeDriveVehicle :
    IDisposable
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
    private readonly RuntimeVehicleSectionInfo[] _sections;
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
    private readonly float _drivenWheelRadiusMeters;
    private readonly int _primaryDrivenSectionIndex;
    private readonly RuntimeVehicleAxleInfo[] _axles;
    private readonly float[] _axleStaticCompressionMeters;
    private readonly float _rollingResistanceNewtons;
    private readonly float _suspensionSpringNewtonsPerMeter;
    private readonly float _frontSuspensionSpringNewtonsPerMeter;
    private readonly float _rearSuspensionSpringNewtonsPerMeter;
    private readonly float _frontSuspensionDamperNewtonSecondsPerMeter;
    private readonly float _rearSuspensionDamperNewtonSecondsPerMeter;
    private readonly float _suspensionResponse;
    private readonly float _pitchNaturalFrequencyRadiansPerSecond;
    private readonly float _pitchDampingRatio;
    private readonly float _frontStaticSuspensionMeters;
    private readonly float _rearStaticSuspensionMeters;
    private readonly float _frontMaximumSuspensionCompressionMeters;
    private readonly float _rearMaximumSuspensionCompressionMeters;
    private readonly float _yawInertiaKilogramSquareMeters;
    private readonly float _yawResponse;
    private OdeWorld? _odeWorld;
    private OdeRigidBody? _odeBody;
    private readonly Dictionary<int, OdeArticulatedSectionState>
        _odeArticulatedSections =
            [];
    private float _yawRateRadiansPerSecond;
    private float _groundPitchRadians;
    private float _groundRollRadians;
    private float _bodyPitchRadians;
    private float _bodyPitchVelocityRadiansPerSecond;
    private float _bodyRollRadians;
    private float _longitudinalAccelerationMetersPerSecondSquared;
    private float _verticalAccelerationMetersPerSecondSquared;
    private float _wheelRotationRadians;
    private bool _omsiScriptDynamicsEnabled;
    private float _omsiWheelTorqueNewtonMeters;
    private float _omsiBrakeForceNewtons;
    private readonly float[] _omsiAxleBrakeForceNewtons =
        new float[16];
    private readonly float[] _omsiAxleSpringFactorLeft =
        new float[16];
    private readonly float[] _omsiAxleSpringFactorRight =
        new float[16];
    private float _frontLeftSpringFactor = 1.0f;
    private float _frontRightSpringFactor = 1.0f;
    private float _rearLeftSpringFactor = 1.0f;
    private float _rearRightSpringFactor = 1.0f;
    private bool _odeSuspensionActive;
    private float _odeFrontLeftSuspensionMeters;
    private float _odeFrontRightSuspensionMeters;
    private float _odeRearLeftSuspensionMeters;
    private float _odeRearRightSuspensionMeters;

    public RuntimeDriveVehicle(
        IReadOnlyList<RuntimeTileInfo> tiles,
        RuntimeVehiclePhysicsInfo? physics,
        IReadOnlyList<RuntimeVehicleSectionInfo>? sections = null)
    {
        _terrain =
            new RuntimeTerrainSampler(tiles);

        _sections =
            sections?
                .OrderBy(
                    static section =>
                        section.Index)
                .ToArray() ??
            [];

        Array.Fill(
            _omsiAxleSpringFactorLeft,
            1.0f);
        Array.Fill(
            _omsiAxleSpringFactorRight,
            1.0f);

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

        var configuredMassTonnes =
            physics?.MassTonnes ??
            (DefaultMassKilograms /
             1000.0f);

        var articulatedMassTonnes =
            _sections.Sum(
                static section =>
                    section.Physics?.MassTonnes ??
                    section.MassTonnes ??
                    0.0);

        var leadingMassTonnes =
            articulatedMassTonnes >
                    0.0 &&
                configuredMassTonnes >
                    articulatedMassTonnes +
                    2.0
                ? configuredMassTonnes -
                  articulatedMassTonnes
                : configuredMassTonnes;

        _massKilograms =
            Math.Clamp(
                (float)leadingMassTonnes *
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

        var declaredAxles =
            physics?.Axles?
                .Where(
                    static axle =>
                        double.IsFinite(
                            axle.LongitudinalPositionMeters))
                .ToArray() ??
            [];

        _axles =
            declaredAxles
                .OrderByDescending(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                .ToArray();

        _wheelRadiusMeters =
            Math.Clamp(
                (float)(physics?.AverageWheelDiameterMeters ??
                    DefaultWheelDiameterMeters) *
                0.5f,
                0.20f,
                0.80f);

        _primaryDrivenSectionIndex =
            ResolvePrimaryDrivenSectionIndex();

        _drivenWheelRadiusMeters =
            ResolveOmsiDrivenWheelRadius(
                physics,
                declaredAxles);

        var configuredRollingResistance =
            physics?.RollingResistanceNewtons ??
            DefaultRollingResistanceNewtons;

        var articulatedRollingResistance =
            _sections.Sum(
                static section =>
                    section.Physics?.RollingResistanceNewtons ??
                    section.RollingResistanceNewtons ??
                    0.0);

        var leadingRollingResistance =
            articulatedRollingResistance >
                    0.0 &&
                configuredRollingResistance >
                    articulatedRollingResistance
                ? configuredRollingResistance -
                  articulatedRollingResistance
                : configuredRollingResistance;

        _rollingResistanceNewtons =
            Math.Clamp(
                (float)leadingRollingResistance,
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

        var frontAxleInfo =
            _axles
                .OrderByDescending(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                .FirstOrDefault();

        var rearAxleInfo =
            _axles
                .OrderBy(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                .FirstOrDefault();

        _frontMaximumSuspensionCompressionMeters =
            ResolveMaximumSuspensionCompression(
                frontAxleInfo,
                _frontSuspensionSpringNewtonsPerMeter);

        _rearMaximumSuspensionCompressionMeters =
            ResolveMaximumSuspensionCompression(
                rearAxleInfo,
                _rearSuspensionSpringNewtonsPerMeter);

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

        // OMSI Axle_Suspension_* is the actual spring deflection, not a
        // zero-based visual offset. Stock MAN scripts expect roughly
        // -0.105 m around normal ride height. Derive the static deflection
        // from axle load / per-side spring rate so each .bus starts from
        // its own physical equilibrium instead of an invented zero.
        var axleSpan =
            Math.Max(
                _frontAxleLongitudinalMeters -
                _rearAxleLongitudinalMeters,
                0.5f);

        var frontLoadShare =
            Math.Clamp(
                -_rearAxleLongitudinalMeters /
                axleSpan,
                0.05f,
                0.95f);

        var rearLoadShare =
            1.0f -
            frontLoadShare;

        var vehicleWeightNewtons =
            _massKilograms *
            Gravity;

        _frontStaticSuspensionMeters =
            -vehicleWeightNewtons *
            frontLoadShare /
            Math.Max(
                2.0f *
                    _frontSuspensionSpringNewtonsPerMeter,
                50_000.0f);

        _rearStaticSuspensionMeters =
            -vehicleWeightNewtons *
            rearLoadShare /
            Math.Max(
                2.0f *
                    _rearSuspensionSpringNewtonsPerMeter,
                50_000.0f);

        _axleStaticCompressionMeters =
            BuildAxleStaticCompressions();

        _odeFrontLeftSuspensionMeters =
            _frontStaticSuspensionMeters;
        _odeFrontRightSuspensionMeters =
            _frontStaticSuspensionMeters;
        _odeRearLeftSuspensionMeters =
            _rearStaticSuspensionMeters;
        _odeRearRightSuspensionMeters =
            _rearStaticSuspensionMeters;

        var yawInertia =
            Math.Clamp(
                (float)(physics?.MomentOfInertiaYawTonneSquareMeters ??
                    (DefaultYawInertiaKilogramSquareMeters /
                     1000.0f)) *
                1000.0f,
                20_000.0f,
                5_000_000.0f);

        _yawInertiaKilogramSquareMeters =
            yawInertia;

        _yawResponse =
            Math.Clamp(
                DefaultYawInertiaKilogramSquareMeters /
                yawInertia,
                0.25f,
                3.0f);

        InitializeOdeDynamics(
            physics,
            estimatedPitchInertia,
            yawInertia);
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

        SynchronizeOdeBodyFromRuntime();
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

    public float VerticalAccelerationMetersPerSecondSquared =>
        _verticalAccelerationMetersPerSecondSquared;

    public float LateralAccelerationMetersPerSecondSquared =>
        SpeedMetersPerSecond *
        _yawRateRadiansPerSecond;

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

    public bool TryGetOmsiAxleSuspension(
        int axleIndex,
        out float leftMeters,
        out float rightMeters)
    {
        if (axleIndex < 0)
        {
            leftMeters =
                0.0f;
            rightMeters =
                0.0f;
            return false;
        }

        var leadingAxleCount =
            Math.Max(
                _axles.Length,
                2);

        if (axleIndex <
            leadingAxleCount)
        {
            if (axleIndex == 0)
            {
                leftMeters =
                    FrontLeftSuspensionMeters;
                rightMeters =
                    FrontRightSuspensionMeters;
            }
            else
            {
                leftMeters =
                    RearLeftSuspensionMeters;
                rightMeters =
                    RearRightSuspensionMeters;
            }

            return true;
        }

        foreach (var state in
                 _odeArticulatedSections.Values)
        {
            var localAxleIndex =
                axleIndex -
                state.OmsiAxleStartIndex;

            if (localAxleIndex < 0 ||
                localAxleIndex >=
                    state.Axles.Length)
            {
                continue;
            }

            leftMeters =
                state.SuspensionLeftMeters[
                    localAxleIndex];

            rightMeters =
                state.SuspensionRightMeters[
                    localAxleIndex];

            return true;
        }

        leftMeters =
            0.0f;
        rightMeters =
            0.0f;
        return false;
    }

    public bool TryGetOdeArticulatedSectionState(
        int sectionIndex,
        out float absoluteHeadingRadians,
        out float relativeYawRadians,
        out float relativeYawRateRadiansPerSecond,
        out float relativePitchRadians,
        out float relativePitchRateRadiansPerSecond)
    {
        if (_odeArticulatedSections.TryGetValue(
                sectionIndex,
                out var state))
        {
            absoluteHeadingRadians =
                state.AbsoluteHeadingRadians;
            relativeYawRadians =
                state.RelativeYawRadians;
            relativeYawRateRadiansPerSecond =
                state.RelativeYawRateRadiansPerSecond;
            relativePitchRadians =
                state.RelativePitchRadians;
            relativePitchRateRadiansPerSecond =
                state.RelativePitchRateRadiansPerSecond;
            return true;
        }

        absoluteHeadingRadians =
            0.0f;
        relativeYawRadians =
            0.0f;
        relativeYawRateRadiansPerSecond =
            0.0f;
        relativePitchRadians =
            0.0f;
        relativePitchRateRadiansPerSecond =
            0.0f;
        return false;
    }

    public IReadOnlyList<string> BuildPhysicsDiagnostics()
    {
        static string F(
            float value) =>
            value.ToString(
                "0.###",
                System.Globalization.CultureInfo.InvariantCulture);

        var lines =
            new List<string>
            {
                $"odeActive={_odeWorld is not null && _odeBody is not null}",
                $"scriptDynamics={_omsiScriptDynamicsEnabled}",
                $"position={F(Position.X)},{F(Position.Y)},{F(Position.Z)}",
                $"speedKph={F(SpeedKph)}",
                $"verticalAccelerationMps2={F(_verticalAccelerationMetersPerSecondSquared)}",
                $"headingDeg={F(HeadingRadians * 180.0f / MathF.PI)}",
                $"yawRateDegPerSec={F(_yawRateRadiansPerSecond * 180.0f / MathF.PI)}",
                $"pitchDeg={F(BodyPitchRadians * 180.0f / MathF.PI)}",
                $"rollDeg={F(BodyRollRadians * 180.0f / MathF.PI)}",
                $"massKg={F(_massKilograms)}",
                $"drivenSection={_primaryDrivenSectionIndex}",
                $"drivenWheelRadiusM={F(_drivenWheelRadiusMeters)}",
                $"wheelTorqueNm={F(_omsiWheelTorqueNewtonMeters)}",
                $"brakeForceN={F(_omsiBrakeForceNewtons)}",
                $"suspensionM=FL:{F(FrontLeftSuspensionMeters)},FR:{F(FrontRightSuspensionMeters)},RL:{F(RearLeftSuspensionMeters)},RR:{F(RearRightSuspensionMeters)}",
                $"axleBrakeForcesN={string.Join(",", _omsiAxleBrakeForceNewtons.Select(F))}",
                $"axleSpringFactorL={string.Join(",", _omsiAxleSpringFactorLeft.Select(F))}",
                $"axleSpringFactorR={string.Join(",", _omsiAxleSpringFactorRight.Select(F))}"
            };

        foreach (var section in
                 _sections.OrderBy(
                     static item =>
                         item.Index))
        {
            if (!_odeArticulatedSections.TryGetValue(
                    section.Index,
                    out var state))
            {
                lines.Add(
                    $"section#{section.Index}|ode=False|parent={section.ParentIndex}|type={section.CouplingType}|maxYawDeg={F((float)section.MaximumYawDegrees)}|pitchDeg={F((float)section.MinimumPitchDegrees)}..{F((float)section.MaximumPitchDegrees)}");
                continue;
            }

            var bodyPosition =
                state.Body.Position;

            lines.Add(
                $"section#{section.Index}|ode=True|parent={section.ParentIndex}|type={section.CouplingType}|driven={section.Index == _primaryDrivenSectionIndex}|massKg={F(state.MassKilograms)}|axles={state.Axles.Length}|axleStart={state.OmsiAxleStartIndex}|alphaDeg={F(state.RelativeYawRadians * 180.0f / MathF.PI)}|alphaRateDegPerSec={F(state.RelativeYawRateRadiansPerSecond * 180.0f / MathF.PI)}|betaDeg={F(state.RelativePitchRadians * 180.0f / MathF.PI)}|betaRateDegPerSec={F(state.RelativePitchRateRadiansPerSecond * 180.0f / MathF.PI)}|maxYawDeg={F((float)section.MaximumYawDegrees)}|pitchDeg={F((float)section.MinimumPitchDegrees)}..{F((float)section.MaximumPitchDegrees)}|suspensionL={string.Join(",", state.SuspensionLeftMeters.Select(F))}|suspensionR={string.Join(",", state.SuspensionRightMeters.Select(F))}|position={F(bodyPosition.X)},{F(bodyPosition.Z)},{F(bodyPosition.Y)}");
        }

        return lines;
    }

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
        _verticalAccelerationMetersPerSecondSquared = 0.0f;
        _wheelRotationRadians = 0.0f;
        _omsiScriptDynamicsEnabled = false;
        _omsiWheelTorqueNewtonMeters = 0.0f;
        _omsiBrakeForceNewtons = 0.0f;
        Array.Clear(
            _omsiAxleBrakeForceNewtons);
        _frontLeftSpringFactor = 1.0f;
        _frontRightSpringFactor = 1.0f;
        _rearLeftSpringFactor = 1.0f;
        _rearRightSpringFactor = 1.0f;
        Array.Fill(
            _omsiAxleSpringFactorLeft,
            1.0f);
        Array.Fill(
            _omsiAxleSpringFactorRight,
            1.0f);
        _odeSuspensionActive = false;
        _odeFrontLeftSuspensionMeters =
            _frontStaticSuspensionMeters;
        _odeFrontRightSuspensionMeters =
            _frontStaticSuspensionMeters;
        _odeRearLeftSuspensionMeters =
            _rearStaticSuspensionMeters;
        _odeRearRightSuspensionMeters =
            _rearStaticSuspensionMeters;

        ElectricalSystemEnabled = false;
        EngineRunning = false;
        ParkingBrakeEngaged = true;
        StopBrakeEngaged = false;
        Gear = RuntimeDriveGear.Neutral;

        UpdateGroundAttitude();
        SynchronizeOdeBodyFromRuntime();
        ResetOdeArticulatedSections();
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

    public void SetOmsiSuspensionSpringFactors(
        double frontLeft,
        double frontRight,
        double rearLeft,
        double rearRight)
    {
        SetOmsiAxleSpringFactors(
            [frontLeft, rearLeft],
            [frontRight, rearRight]);
    }

    public void SetOmsiAxleSpringFactors(
        IReadOnlyList<double> leftFactors,
        IReadOnlyList<double> rightFactors)
    {
        ArgumentNullException.ThrowIfNull(
            leftFactors);
        ArgumentNullException.ThrowIfNull(
            rightFactors);

        Array.Fill(
            _omsiAxleSpringFactorLeft,
            1.0f);
        Array.Fill(
            _omsiAxleSpringFactorRight,
            1.0f);

        var count =
            Math.Min(
                Math.Min(
                    leftFactors.Count,
                    rightFactors.Count),
                _omsiAxleSpringFactorLeft.Length);

        for (var axle = 0;
             axle < count;
             axle++)
        {
            _omsiAxleSpringFactorLeft[
                axle] =
                NormalizeSpringFactor(
                    leftFactors[
                        axle]);

            _omsiAxleSpringFactorRight[
                axle] =
                NormalizeSpringFactor(
                    rightFactors[
                        axle]);
        }

        _frontLeftSpringFactor =
            _omsiAxleSpringFactorLeft[0];
        _frontRightSpringFactor =
            _omsiAxleSpringFactorRight[0];

        _rearLeftSpringFactor =
            _omsiAxleSpringFactorLeft[1];
        _rearRightSpringFactor =
            _omsiAxleSpringFactorRight[1];
    }

    public void SetOmsiScriptDynamics(
        bool enabled,
        double wheelTorqueNewtonMeters,
        double brakeForceNewtons,
        IReadOnlyList<double>? axleBrakeForcesNewtons = null)
    {
        _omsiScriptDynamicsEnabled =
            enabled;

        _omsiWheelTorqueNewtonMeters =
            enabled &&
            double.IsFinite(
                wheelTorqueNewtonMeters)
                ? Math.Clamp(
                    (float)wheelTorqueNewtonMeters,
                    -250_000.0f,
                    250_000.0f)
                : 0.0f;

        _omsiBrakeForceNewtons =
            enabled &&
            double.IsFinite(
                brakeForceNewtons)
                ? Math.Clamp(
                    Math.Abs(
                        (float)brakeForceNewtons),
                    0.0f,
                    1_000_000.0f)
                : 0.0f;

        Array.Clear(
            _omsiAxleBrakeForceNewtons);

        if (enabled &&
            axleBrakeForcesNewtons is not null)
        {
            var count =
                Math.Min(
                    axleBrakeForcesNewtons.Count,
                    _omsiAxleBrakeForceNewtons.Length);

            for (var index = 0;
                 index < count;
                 index++)
            {
                var value =
                    axleBrakeForcesNewtons[index];

                _omsiAxleBrakeForceNewtons[index] =
                    double.IsFinite(
                        value)
                        ? Math.Clamp(
                            Math.Abs(
                                (float)value),
                            0.0f,
                            500_000.0f)
                        : 0.0f;
            }
        }
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

        AcceleratorLevel = MoveTowards(
            AcceleratorLevel,
            acceleratorHeld
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

        AcceleratorLevel =
            accelerator;

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

        AcceleratorLevel = MoveTowards(
            AcceleratorLevel,
            accelerator,
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

        float driveForceNewtons;

        if (_omsiScriptDynamicsEnabled)
        {
            // OMSI's predefined M_Wheel variable is wheel torque in N*m.
            // The stock MAN scripts verify this themselves through the
            // mechanical-power identity:
            //   P[kW] = M_Wheel[N*m] * n_Wheel[rpm] * PI / 30000.
            // Do not apply an extra x1000 conversion here.
            driveForceNewtons =
                _omsiWheelTorqueNewtonMeters /
                Math.Max(
                    _drivenWheelRadiusMeters,
                    0.05f);
        }
        else
        {
            // Compatibility fallback only for vehicles whose scripts do not
            // expose OMSI's M_Wheel/Brakeforce physics interface.
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

            var canApplyFallbackPower =
                ElectricalSystemEnabled &&
                EngineRunning &&
                requestedDirection != 0 &&
                !ParkingBrakeEngaged &&
                !StopBrakeEngaged;

            driveForceNewtons =
                canApplyFallbackPower
                    ? AcceleratorLevel *
                      availableForwardForceNewtons *
                      (requestedDirection < 0
                          ? -0.48f
                          : 1.0f)
                    : 0.0f;
        }

        var gradeAcceleration =
            -MathF.Sin(
                _groundPitchRadians) *
            Gravity;

        if (TryApplyOdeDynamics(
                deltaSeconds,
                driveForceNewtons,
                gradeAcceleration,
                previousSpeed))
        {
            return;
        }

        _verticalAccelerationMetersPerSecondSquared =
            0.0f;

        var driveAcceleration =
            driveForceNewtons /
            _massKilograms;

        SpeedMetersPerSecond +=
            (driveAcceleration +
             gradeAcceleration) *
            deltaSeconds;

        var rollingAcceleration =
            _rollingResistanceNewtons /
            _massKilograms;

        var aerodynamicAcceleration =
            _omsiScriptDynamicsEnabled
                ? 0.0f
                : 0.0022f *
                  SpeedMetersPerSecond *
                  SpeedMetersPerSecond;

        float serviceBrakeAcceleration;
        float stopBrakeAcceleration;
        float parkingBrakeAcceleration;

        if (_omsiScriptDynamicsEnabled)
        {
            serviceBrakeAcceleration =
                _omsiBrakeForceNewtons /
                _massKilograms;

            // Parking/station brake forces should normally already be part of
            // Brakeforce/Axle_Brakeforce. Do not double-apply them here.
            stopBrakeAcceleration =
                0.0f;
            parkingBrakeAcceleration =
                0.0f;
        }
        else
        {
            // Compatibility fallback for vehicles without script-driven
            // brake force outputs.
            serviceBrakeAcceleration =
                BrakeLevel *
                5.4f;

            stopBrakeAcceleration =
                StopBrakeEngaged
                    ? 3.2f
                    : 0.0f;

            parkingBrakeAcceleration =
                ParkingBrakeEngaged
                    ? 7.0f
                    : 0.0f;
        }

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
        else if ((_omsiScriptDynamicsEnabled &&
                  _omsiBrakeForceNewtons >
                      1.0f) ||
                 (!_omsiScriptDynamicsEnabled &&
                  (BrakeLevel >
                       0.05f ||
                   StopBrakeEngaged ||
                   ParkingBrakeEngaged)))
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
        if (_odeSuspensionActive)
        {
            return front
                ? left
                    ? _odeFrontLeftSuspensionMeters
                    : _odeFrontRightSuspensionMeters
                : left
                    ? _odeRearLeftSuspensionMeters
                    : _odeRearRightSuspensionMeters;
        }

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

        var staticDeflection =
            front
                ? _frontStaticSuspensionMeters
                : _rearStaticSuspensionMeters;

        var springFactor =
            front
                ? left
                    ? _frontLeftSpringFactor
                    : _frontRightSpringFactor
                : left
                    ? _rearLeftSpringFactor
                    : _rearRightSpringFactor;

        // Axle_Springfactor_* is written by the OMSI pneumatic level-control
        // scripts. It scales the effective spring stiffness; for the same
        // static load a larger factor therefore yields less compression.
        staticDeflection /=
            Math.Max(
                springFactor,
                0.10f);

        var maximumCompression =
            front
                ? _frontMaximumSuspensionCompressionMeters
                : _rearMaximumSuspensionCompressionMeters;

        return Math.Clamp(
            staticDeflection +
            pitch +
            roll,
            -maximumCompression,
            0.10f);
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


    private void InitializeOdeDynamics(
        RuntimeVehiclePhysicsInfo? physics,
        float estimatedPitchInertia,
        float yawInertia)
    {
        try
        {
            var runtime =
                OdeRuntime.Inspect();

            if (!runtime.Available ||
                !runtime.Is64BitProcess ||
                !runtime.SinglePrecision)
            {
                return;
            }

            var rollInertia =
                Math.Clamp(
                    (float)(physics?.MomentOfInertiaY ??
                        (_massKilograms *
                         (_trackWidthMeters *
                              _trackWidthMeters +
                          4.0f *
                              _centerOfGravityHeightMeters *
                              _centerOfGravityHeightMeters) /
                         12_000.0f)) *
                    1_000.0f,
                    1_000.0f,
                    5_000_000.0f);

            var pitchInertia =
                Math.Clamp(
                    (float)(physics?.MomentOfInertiaX ??
                        (estimatedPitchInertia /
                         1_000.0f)) *
                    1_000.0f,
                    1_000.0f,
                    5_000_000.0f);

            var resolvedYawInertia =
                Math.Clamp(
                    (float)(physics?.MomentOfInertiaZ ??
                        (yawInertia /
                         1_000.0f)) *
                    1_000.0f,
                    1_000.0f,
                    5_000_000.0f);

            _odeWorld =
                new OdeWorld();

            _odeBody =
                new OdeRigidBody(
                    _odeWorld,
                    new OdeRigidBodyParameters(
                        _massKilograms,
                        0.0f,
                        0.0f,
                        0.0f,
                        pitchInertia,
                        rollInertia,
                        resolvedYawInertia));

            // The rigid-body origin is the OMSI center of gravity. Four
            // suspension contact points now support the body against gravity.
            _odeBody.SetGravityEnabled(
                true);
        }
        catch (Exception exception)
            when (exception is
                DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException or
                PlatformNotSupportedException or
                InvalidOperationException)
        {
            DisableOdeDynamics();
        }
    }

    private bool TryApplyOdeDynamics(
        float deltaSeconds,
        float driveForceNewtons,
        float gradeAcceleration,
        float previousSpeed)
    {
        var body =
            _odeBody;
        var world =
            _odeWorld;

        if (body is null ||
            world is null)
        {
            return false;
        }

        try
        {
            var forward =
                new Vector3(
                    MathF.Sin(
                        HeadingRadians),
                    MathF.Cos(
                        HeadingRadians),
                    0.0f);

            var right =
                new Vector3(
                    forward.Y,
                    -forward.X,
                    0.0f);

            var orientationBeforeStep =
                body.Orientation;

            var suspensionContactCount =
                ApplyOdeSuspensionForces(
                    body,
                    orientationBeforeStep);

            if (suspensionContactCount == 0)
            {
                // Missing terrain must not make the vehicle fall forever
                // while tiles are being streamed/replaced.
                body.AddWorldForce(
                    Vector3.UnitZ *
                    _massKilograms *
                    Gravity);

                var unsupportedVelocity =
                    body.LinearVelocity;

                body.SetLinearVelocity(
                    new Vector3(
                        unsupportedVelocity.X,
                        unsupportedVelocity.Y,
                        0.0f));
            }

            var velocity =
                body.LinearVelocity;

            var longitudinalSpeed =
                Vector3.Dot(
                    velocity,
                    forward);

            var lateralSpeed =
                Vector3.Dot(
                    velocity,
                    right);

            var rollingAcceleration =
                _rollingResistanceNewtons /
                _massKilograms;

            var aerodynamicAcceleration =
                _omsiScriptDynamicsEnabled
                    ? 0.0f
                    : 0.0022f *
                      longitudinalSpeed *
                      longitudinalSpeed;

            float serviceBrakeAcceleration;
            float stopBrakeAcceleration;
            float parkingBrakeAcceleration;

            if (_omsiScriptDynamicsEnabled)
            {
                serviceBrakeAcceleration =
                    ResolveLeadingOmsiBrakeForceNewtons() /
                    _massKilograms;
                stopBrakeAcceleration =
                    0.0f;
                parkingBrakeAcceleration =
                    0.0f;
            }
            else
            {
                serviceBrakeAcceleration =
                    BrakeLevel *
                    5.4f;
                stopBrakeAcceleration =
                    StopBrakeEngaged
                        ? 3.2f
                        : 0.0f;
                parkingBrakeAcceleration =
                    ParkingBrakeEngaged
                        ? 7.0f
                        : 0.0f;
            }

            var passiveDeceleration =
                rollingAcceleration +
                aerodynamicAcceleration +
                Math.Max(
                    serviceBrakeAcceleration +
                        stopBrakeAcceleration,
                    parkingBrakeAcceleration);

            var leadingDriveForceNewtons =
                _odeArticulatedSections.Count >
                        0 &&
                    _primaryDrivenSectionIndex >
                        0
                    ? 0.0f
                    : driveForceNewtons;

            var propulsionForce =
                leadingDriveForceNewtons +
                gradeAcceleration *
                _massKilograms;

            var resistanceForce =
                passiveDeceleration *
                _massKilograms;

            var opposingDirection =
                Math.Abs(
                    longitudinalSpeed) >
                0.01f
                    ? Math.Sign(
                        longitudinalSpeed)
                    : Math.Sign(
                        propulsionForce);

            if (opposingDirection !=
                0)
            {
                var maximumNonReversingForce =
                    Math.Abs(
                        longitudinalSpeed) >
                    0.01f
                        ? _massKilograms *
                          Math.Abs(
                              longitudinalSpeed) /
                          deltaSeconds +
                          Math.Abs(
                              propulsionForce)
                        : Math.Abs(
                            propulsionForce);

                resistanceForce =
                    Math.Min(
                        resistanceForce,
                        maximumNonReversingForce);
            }

            var longitudinalForce =
                propulsionForce -
                opposingDirection *
                resistanceForce;

            if (Math.Abs(
                    longitudinalSpeed) <
                    0.02f &&
                resistanceForce >=
                    Math.Abs(
                        propulsionForce))
            {
                longitudinalForce =
                    0.0f;
            }

            body.AddWorldForce(
                forward *
                Math.Clamp(
                    longitudinalForce,
                    -1_000_000.0f,
                    1_000_000.0f));

            var lateralGripLimit =
                0.62f *
                Gravity;

            var maximumLateralForce =
                lateralGripLimit *
                _massKilograms;

            var lateralCorrectionForce =
                Math.Clamp(
                    -lateralSpeed *
                    _massKilograms *
                    6.0f,
                    -maximumLateralForce,
                    maximumLateralForce);

            body.AddWorldForce(
                right *
                lateralCorrectionForce);

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
                    longitudinalSpeed) >
                0.02f
                    ? requestedCurvature *
                      longitudinalSpeed
                    : 0.0f;

            var maximumYawRate =
                lateralGripLimit /
                Math.Max(
                    Math.Abs(
                        longitudinalSpeed),
                    1.0f);

            targetYawRate =
                Math.Clamp(
                    targetYawRate,
                    -maximumYawRate,
                    maximumYawRate);

            var currentYawRate =
                -body.AngularVelocity.Z;

            var maximumYawAcceleration =
                2.6f +
                3.8f *
                _yawResponse;

            var desiredYawAcceleration =
                Math.Clamp(
                    (targetYawRate -
                     currentYawRate) /
                    Math.Max(
                        deltaSeconds,
                        0.001f),
                    -maximumYawAcceleration,
                    maximumYawAcceleration);

            body.AddWorldTorque(
                new Vector3(
                    0.0f,
                    0.0f,
                    -desiredYawAcceleration *
                    _yawInertiaKilogramSquareMeters));

            ApplyOdeArticulatedForces(
                deltaSeconds,
                driveForceNewtons);

            world.Step(
                deltaSeconds);

            UpdateOdeArticulatedSectionState();

            var orientation =
                body.Orientation;

            var bodyForward =
                Vector3.Transform(
                    Vector3.UnitY,
                    orientation);

            bodyForward.Z =
                0.0f;

            if (bodyForward.LengthSquared() <
                0.000001f)
            {
                bodyForward =
                    forward;
            }
            else
            {
                bodyForward =
                    Vector3.Normalize(
                        bodyForward);
            }

            var bodyRight =
                new Vector3(
                    bodyForward.Y,
                    -bodyForward.X,
                    0.0f);

            var nextVelocity =
                body.LinearVelocity;

            _verticalAccelerationMetersPerSecondSquared =
                Math.Clamp(
                    (nextVelocity.Z -
                     velocity.Z) /
                    Math.Max(
                        deltaSeconds,
                        0.001f),
                    -30.0f,
                    30.0f);

            var nextLongitudinalSpeed =
                Math.Clamp(
                    Vector3.Dot(
                        nextVelocity,
                        bodyForward),
                    -9.0f,
                    28.0f);

            var nextLateralSpeed =
                Math.Clamp(
                    Vector3.Dot(
                        nextVelocity,
                        bodyRight),
                    -3.0f,
                    3.0f);

            if (Math.Abs(
                    nextLongitudinalSpeed) <
                    0.02f &&
                ((_omsiScriptDynamicsEnabled &&
                  _omsiBrakeForceNewtons >
                      1.0f) ||
                 (!_omsiScriptDynamicsEnabled &&
                  (BrakeLevel >
                       0.05f ||
                   StopBrakeEngaged ||
                   ParkingBrakeEngaged))))
            {
                nextLongitudinalSpeed =
                    0.0f;
            }

            HeadingRadians =
                MathF.Atan2(
                    bodyForward.X,
                    bodyForward.Y);

            SpeedMetersPerSecond =
                nextLongitudinalSpeed;

            var angularVelocity =
                body.AngularVelocity;

            _yawRateRadiansPerSecond =
                -angularVelocity.Z;

            _longitudinalAccelerationMetersPerSecondSquared =
                (SpeedMetersPerSecond -
                 previousSpeed) /
                deltaSeconds;

            var travelledMeters =
                (previousSpeed +
                 SpeedMetersPerSecond) *
                0.5f *
                deltaSeconds;

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

            var odePosition =
                body.Position;

            var rotatedCenterOfGravity =
                Vector3.Transform(
                    new Vector3(
                        0.0f,
                        0.0f,
                        _centerOfGravityHeightMeters),
                    orientation);

            var modelOrigin =
                odePosition -
                rotatedCenterOfGravity;

            Position =
                new Vector3(
                    modelOrigin.X,
                    modelOrigin.Z,
                    modelOrigin.Y);

            var horizontalForward =
                new Vector3(
                    MathF.Sin(
                        HeadingRadians),
                    MathF.Cos(
                        HeadingRadians),
                    0.0f);

            var horizontalRight =
                new Vector3(
                    horizontalForward.Y,
                    -horizontalForward.X,
                    0.0f);

            body.SetLinearVelocity(
                horizontalForward *
                    nextLongitudinalSpeed +
                horizontalRight *
                    nextLateralSpeed +
                Vector3.UnitZ *
                    nextVelocity.Z);

            var maximumPhysicalYawRate =
                3.5f;

            if (Math.Abs(
                    angularVelocity.Z) >
                maximumPhysicalYawRate)
            {
                body.SetAngularVelocity(
                    new Vector3(
                        angularVelocity.X,
                        angularVelocity.Y,
                        Math.Clamp(
                            angularVelocity.Z,
                            -maximumPhysicalYawRate,
                            maximumPhysicalYawRate)));

                _yawRateRadiansPerSecond =
                    -Math.Clamp(
                        angularVelocity.Z,
                        -maximumPhysicalYawRate,
                        maximumPhysicalYawRate);
            }

            var absolutePitch =
                MathF.Atan2(
                    bodyForward.Z,
                    MathF.Sqrt(
                        bodyForward.X *
                            bodyForward.X +
                        bodyForward.Y *
                            bodyForward.Y));

            var bodyRight3D =
                Vector3.Transform(
                    Vector3.UnitX,
                    orientation);

            var bodyUp3D =
                Vector3.Transform(
                    Vector3.UnitZ,
                    orientation);

            var absoluteRoll =
                MathF.Atan2(
                    -bodyRight3D.Z,
                    bodyUp3D.Z);

            UpdateGroundAttitude();

            _bodyPitchRadians =
                Math.Clamp(
                    absolutePitch -
                    _groundPitchRadians,
                    DegreesToRadians(
                        -12.0),
                    DegreesToRadians(
                        12.0));

            _bodyRollRadians =
                Math.Clamp(
                    absoluteRoll -
                    _groundRollRadians,
                    DegreesToRadians(
                        -12.0),
                    DegreesToRadians(
                        12.0));

            return true;
        }
        catch (Exception exception)
            when (exception is
                DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException or
                InvalidOperationException)
        {
            DisableOdeDynamics();
            return false;
        }
    }

    private void SynchronizeOdeBodyFromRuntime()
    {
        var body =
            _odeBody;

        if (body is null)
        {
            return;
        }

        var yaw =
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitZ,
                -HeadingRadians);

        var pitch =
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitX,
                BodyPitchRadians);

        var roll =
            Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                BodyRollRadians);

        var orientation =
            Quaternion.Normalize(
                roll *
                pitch *
                yaw);

        var forward =
            new Vector3(
                MathF.Sin(
                    HeadingRadians),
                MathF.Cos(
                    HeadingRadians),
                0.0f);

        var modelOrigin =
            new Vector3(
                Position.X,
                Position.Z,
                Position.Y);

        var centerOfGravityWorld =
            modelOrigin +
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    0.0f,
                    _centerOfGravityHeightMeters),
                orientation);

        body.SetPosition(
            centerOfGravityWorld);

        body.SetOrientation(
            orientation);

        body.SetLinearVelocity(
            forward *
            SpeedMetersPerSecond);

        body.SetAngularVelocity(
            new Vector3(
                0.0f,
                0.0f,
                -_yawRateRadiansPerSecond));
    }

    private int ApplyOdeSuspensionForces(
        OdeRigidBody body,
        Quaternion orientation)
    {
        var bodyPosition =
            body.Position;
        var linearVelocity =
            body.LinearVelocity;
        var angularVelocity =
            body.AngularVelocity;

        var contactCount =
            0;

        if (_axles.Length == 0)
        {
            contactCount +=
                ApplyOdeAxleSuspension(
                    body,
                    orientation,
                    bodyPosition,
                    linearVelocity,
                    angularVelocity,
                    _frontAxleLongitudinalMeters,
                    _trackWidthMeters,
                    _frontSuspensionSpringNewtonsPerMeter,
                    _frontSuspensionDamperNewtonSecondsPerMeter,
                    _frontMaximumSuspensionCompressionMeters,
                    _frontSuspensionSpringNewtonsPerMeter *
                        _frontMaximumSuspensionCompressionMeters,
                    -_frontStaticSuspensionMeters,
                    front: true);

            contactCount +=
                ApplyOdeAxleSuspension(
                    body,
                    orientation,
                    bodyPosition,
                    linearVelocity,
                    angularVelocity,
                    _rearAxleLongitudinalMeters,
                    _trackWidthMeters,
                    _rearSuspensionSpringNewtonsPerMeter,
                    _rearSuspensionDamperNewtonSecondsPerMeter,
                    _rearMaximumSuspensionCompressionMeters,
                    _rearSuspensionSpringNewtonsPerMeter *
                        _rearMaximumSuspensionCompressionMeters,
                    -_rearStaticSuspensionMeters,
                    front: false);

            _odeSuspensionActive =
                contactCount >
                0;

            return contactCount;
        }

        for (var axleIndex = 0;
             axleIndex < _axles.Length;
             axleIndex++)
        {
            var axle =
                _axles[axleIndex];

            var front =
                Math.Abs(
                    axle.LongitudinalPositionMeters -
                    _frontAxleLongitudinalMeters) <=
                Math.Abs(
                    axle.LongitudinalPositionMeters -
                    _rearAxleLongitudinalMeters);

            var springRate =
                Math.Clamp(
                    (float)(axle.SpringRateKilonewtonsPerMeter ??
                        (_suspensionSpringNewtonsPerMeter /
                         1_000.0f)),
                    25.0f,
                    1_500.0f) *
                1_000.0f;

            var damperRate =
                Math.Clamp(
                    (float)(axle.DamperRateKilonewtonSecondsPerMeter ??
                        (_frontSuspensionDamperNewtonSecondsPerMeter /
                         1_000.0f)),
                    2.0f,
                    150.0f) *
                1_000.0f;

            var maximumCompression =
                ResolveMaximumSuspensionCompression(
                    axle,
                    springRate);

            var maximumForce =
                axle.MaximumForceKilonewtons is
                    { } declaredMaximumForce &&
                double.IsFinite(
                    declaredMaximumForce) &&
                declaredMaximumForce >
                    0.0
                    ? (float)(
                        declaredMaximumForce *
                        1_000.0)
                    : springRate *
                      maximumCompression;

            var trackWidth =
                Math.Clamp(
                    (float)(axle.MaximumWidthMeters ??
                        axle.MinimumWidthMeters ??
                        _trackWidthMeters),
                    1.2f,
                    3.5f);

            var staticCompression =
                axleIndex <
                    _axleStaticCompressionMeters.Length
                    ? _axleStaticCompressionMeters[
                        axleIndex]
                    : front
                        ? -_frontStaticSuspensionMeters
                        : -_rearStaticSuspensionMeters;

            contactCount +=
                ApplyOdeAxleSuspension(
                    body,
                    orientation,
                    bodyPosition,
                    linearVelocity,
                    angularVelocity,
                    (float)axle.LongitudinalPositionMeters,
                    trackWidth,
                    springRate,
                    damperRate,
                    maximumCompression,
                    maximumForce,
                    staticCompression,
                    front);
        }

        _odeSuspensionActive =
            contactCount >
            0;

        return contactCount;
    }

    private int ApplyOdeAxleSuspension(
        OdeRigidBody body,
        Quaternion orientation,
        Vector3 bodyPosition,
        Vector3 linearVelocity,
        Vector3 angularVelocity,
        float longitudinalMeters,
        float trackWidthMeters,
        float springNewtonsPerMeter,
        float damperNewtonSecondsPerMeter,
        float maximumCompressionMeters,
        float maximumForceNewtons,
        float staticCompressionMeters,
        bool front)
    {
        var contacts =
            0;

        for (var side = 0;
             side < 2;
             side++)
        {
            var left =
                side == 0;

            var lateral =
                (left
                    ? -0.5f
                    : 0.5f) *
                trackWidthMeters;

            var localPoint =
                new Vector3(
                    lateral,
                    longitudinalMeters,
                    -_centerOfGravityHeightMeters);

            var worldOffset =
                Vector3.Transform(
                    localPoint,
                    orientation);

            var worldPoint =
                bodyPosition +
                worldOffset;

            if (!_terrain.TrySample(
                    worldPoint.X,
                    worldPoint.Y,
                    out var groundHeight))
            {
                continue;
            }

            contacts++;

            var springFactor =
                front
                    ? left
                        ? _frontLeftSpringFactor
                        : _frontRightSpringFactor
                    : left
                        ? _rearLeftSpringFactor
                        : _rearRightSpringFactor;

            springFactor =
                Math.Clamp(
                    springFactor,
                    0.10f,
                    5.0f);

            var effectiveSpring =
                springNewtonsPerMeter *
                springFactor;

            var compression =
                staticCompressionMeters +
                groundHeight +
                ModelGroundPlaneOffsetMeters -
                worldPoint.Z;

            var pointVelocity =
                linearVelocity +
                Vector3.Cross(
                    angularVelocity,
                    worldOffset);

            var springForce =
                effectiveSpring *
                Math.Max(
                    compression,
                    0.0f);

            var damperForce =
                -damperNewtonSecondsPerMeter *
                pointVelocity.Z;

            var supportForce =
                Math.Clamp(
                    springForce +
                    damperForce,
                    0.0f,
                    Math.Max(
                        maximumForceNewtons,
                        1.0f));

            body.AddWorldForceAtLocalPosition(
                new Vector3(
                    0.0f,
                    0.0f,
                    supportForce),
                localPoint);

            var suspensionMeters =
                -Math.Clamp(
                    compression,
                    0.0f,
                    maximumCompressionMeters);

            if (front)
            {
                if (left)
                {
                    _odeFrontLeftSuspensionMeters =
                        suspensionMeters;
                }
                else
                {
                    _odeFrontRightSuspensionMeters =
                        suspensionMeters;
                }
            }
            else
            {
                if (left)
                {
                    _odeRearLeftSuspensionMeters =
                        suspensionMeters;
                }
                else
                {
                    _odeRearRightSuspensionMeters =
                        suspensionMeters;
                }
            }
        }

        return contacts;
    }

    private float[] BuildAxleStaticCompressions() =>
        BuildStaticCompressions(
            _massKilograms,
            _axles,
            _suspensionSpringNewtonsPerMeter);

    private int ResolvePrimaryDrivenSectionIndex()
    {
        var bestSectionIndex =
            0;

        var bestDriveFactor =
            SumDriveFactors(
                _axles);

        foreach (var section in
                 _sections)
        {
            var driveFactor =
                SumDriveFactors(
                    section.Physics?.Axles ??
                    Array.Empty<RuntimeVehicleAxleInfo>());

            if (driveFactor >
                bestDriveFactor +
                    0.0001)
            {
                bestDriveFactor =
                    driveFactor;
                bestSectionIndex =
                    section.Index;
            }
        }

        return bestSectionIndex;
    }

    private static double SumDriveFactors(
        IEnumerable<RuntimeVehicleAxleInfo> axles) =>
        axles.Sum(
            static axle =>
                axle.DriveFactor is
                    { } drive &&
                double.IsFinite(
                    drive)
                    ? Math.Abs(
                        drive)
                    : 0.0);

    private bool HasDetailedOmsiBrakeForces() =>
        _omsiAxleBrakeForceNewtons.Any(
            static force =>
                force >
                0.001f);

    private float ResolveLeadingOmsiBrakeForceNewtons()
    {
        if (HasDetailedOmsiBrakeForces())
        {
            var axleCount =
                Math.Clamp(
                    _axles.Length >
                        0
                        ? _axles.Length
                        : 2,
                    1,
                    _omsiAxleBrakeForceNewtons.Length);

            var total =
                0.0f;

            for (var index = 0;
                 index < axleCount;
                 index++)
            {
                total +=
                    _omsiAxleBrakeForceNewtons[
                        index];
            }

            return total;
        }

        if (_odeArticulatedSections.Count ==
            0)
        {
            return _omsiBrakeForceNewtons;
        }

        var totalMass =
            _massKilograms +
            _odeArticulatedSections.Values.Sum(
                static state =>
                    state.MassKilograms);

        return _omsiBrakeForceNewtons *
               _massKilograms /
               Math.Max(
                   totalMass,
                   1.0f);
    }

    private float ResolveSectionOmsiBrakeForceNewtons(
        OdeArticulatedSectionState state)
    {
        if (HasDetailedOmsiBrakeForces())
        {
            var total =
                0.0f;

            for (var axle = 0;
                 axle < state.Axles.Length;
                 axle++)
            {
                var index =
                    state.OmsiAxleStartIndex +
                    axle;

                if (index < 0 ||
                    index >=
                        _omsiAxleBrakeForceNewtons.Length)
                {
                    break;
                }

                total +=
                    _omsiAxleBrakeForceNewtons[
                        index];
            }

            return total;
        }

        var totalMass =
            _massKilograms +
            _odeArticulatedSections.Values.Sum(
                static sectionState =>
                    sectionState.MassKilograms);

        return _omsiBrakeForceNewtons *
               state.MassKilograms /
               Math.Max(
                   totalMass,
                   1.0f);
    }

    private void ResetOdeArticulatedSections()
    {
        DisposeOdeArticulatedSections();

        var world =
            _odeWorld;
        var leadingBody =
            _odeBody;

        if (world is null ||
            leadingBody is null ||
            _sections.Length == 0)
        {
            return;
        }

        try
        {
            var leadingOrientation =
                leadingBody.Orientation;

            var leadingCenter =
                leadingBody.Position;

            var leadingOrigin =
                leadingCenter -
                Vector3.Transform(
                    new Vector3(
                        0.0f,
                        0.0f,
                        _centerOfGravityHeightMeters),
                    leadingOrientation);

            var nextOmsiAxleIndex =
                Math.Max(
                    _axles.Length,
                    2);

            foreach (var section in
                     _sections)
            {
                var physics =
                    section.Physics;

                var massKilograms =
                    Math.Clamp(
                        (float)(physics?.MassTonnes ??
                            section.MassTonnes ??
                            6.0) *
                        1_000.0f,
                        1_000.0f,
                        45_000.0f);

                var centerOfGravityHeight =
                    Math.Clamp(
                        (float)(physics?.CenterOfGravityHeightMeters ??
                            DefaultCenterOfGravityHeightMeters),
                        0.35f,
                        3.5f);

                var wheelBase =
                    Math.Clamp(
                        (float)(physics?.WheelBaseMeters ??
                            section.WheelBaseMeters ??
                            section.FollowerLengthMeters),
                        1.0f,
                        15.0f);

                var trackWidth =
                    Math.Clamp(
                        (float)(physics?.TrackWidthMeters ??
                            DefaultTrackWidthMeters),
                        1.2f,
                        3.5f);

                var pitchInertia =
                    Math.Clamp(
                        (float)(physics?.MomentOfInertiaX ??
                            (massKilograms *
                             (wheelBase *
                                  wheelBase +
                              4.0f *
                                  centerOfGravityHeight *
                                  centerOfGravityHeight) /
                             12.0f /
                             1_000.0f)) *
                        1_000.0f,
                        1_000.0f,
                        8_000_000.0f);

                var rollInertia =
                    Math.Clamp(
                        (float)(physics?.MomentOfInertiaY ??
                            (massKilograms *
                             (trackWidth *
                                  trackWidth +
                              4.0f *
                                  centerOfGravityHeight *
                                  centerOfGravityHeight) /
                             12.0f /
                             1_000.0f)) *
                        1_000.0f,
                        1_000.0f,
                        8_000_000.0f);

                var yawInertia =
                    Math.Clamp(
                        (float)(physics?.MomentOfInertiaZ ??
                            section.YawInertiaTonneSquareMeters ??
                            (massKilograms *
                             (wheelBase *
                                  wheelBase +
                              trackWidth *
                                  trackWidth) /
                             12.0f /
                             1_000.0f)) *
                        1_000.0f,
                        5_000.0f,
                        8_000_000.0f);

                var body =
                    new OdeRigidBody(
                        world,
                        new OdeRigidBodyParameters(
                            massKilograms,
                            0.0f,
                            0.0f,
                            0.0f,
                            pitchInertia,
                            rollInertia,
                            yawInertia));

                body.SetGravityEnabled(
                    true);

                var localOrigin =
                    new Vector3(
                        (float)section.OriginX,
                        (float)section.OriginZ,
                        (float)section.OriginY);

                var sectionOrigin =
                    leadingOrigin +
                    Vector3.Transform(
                        localOrigin,
                        leadingOrientation);

                var sectionCenter =
                    sectionOrigin +
                    Vector3.Transform(
                        new Vector3(
                            0.0f,
                            0.0f,
                            centerOfGravityHeight),
                        leadingOrientation);

                body.SetPosition(
                    sectionCenter);

                body.SetOrientation(
                    leadingOrientation);

                var offsetFromLeadingCenter =
                    sectionCenter -
                    leadingCenter;

                body.SetLinearVelocity(
                    leadingBody.LinearVelocity +
                    Vector3.Cross(
                        leadingBody.AngularVelocity,
                        offsetFromLeadingCenter));

                body.SetAngularVelocity(
                    leadingBody.AngularVelocity);

                var parentBody =
                    section.ParentIndex >
                            0 &&
                        _odeArticulatedSections.TryGetValue(
                            section.ParentIndex,
                            out var parentState)
                        ? parentState.Body
                        : leadingBody;

                var localJoint =
                    new Vector3(
                        (float)section.JointX,
                        (float)section.JointZ,
                        (float)section.JointY);

                var jointWorld =
                    leadingOrigin +
                    Vector3.Transform(
                        localJoint,
                        leadingOrientation);

                var yawAxis =
                    Vector3.Transform(
                        Vector3.UnitZ,
                        leadingOrientation);

                var pitchAxis =
                    Vector3.Transform(
                        Vector3.UnitX,
                        leadingOrientation);

                var articulation =
                    new OdeUniversalJoint(
                        world,
                        parentBody,
                        body,
                        jointWorld,
                        yawAxis,
                        pitchAxis);

                var maximumYawRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            section.MaximumYawDegrees,
                            5.0,
                            89.0));

                var minimumPitchRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            section.MinimumPitchDegrees,
                            -45.0,
                            0.0));

                var maximumPitchRadians =
                    DegreesToRadians(
                        Math.Clamp(
                            section.MaximumPitchDegrees,
                            0.0,
                            45.0));

                articulation.SetYawStops(
                    -maximumYawRadians,
                    maximumYawRadians,
                    stopErp:
                        0.35f,
                    stopCfm:
                        0.00001f);

                articulation.SetPitchStops(
                    minimumPitchRadians,
                    maximumPitchRadians,
                    stopErp:
                        0.35f,
                    stopCfm:
                        0.00001f);

                var axles =
                    ResolveSectionAxles(
                        section,
                        physics,
                        wheelBase,
                        trackWidth);

                var averageSpring =
                    ResolveAverageAxleValue(
                        axles,
                        static axle =>
                            axle.SpringRateKilonewtonsPerMeter,
                        DefaultSpringKilonewtonsPerMeter);

                var averageDamper =
                    ResolveAverageAxleValue(
                        axles,
                        static axle =>
                            axle.DamperRateKilonewtonSecondsPerMeter,
                        DefaultDamperKilonewtonSecondsPerMeter);

                var staticCompressions =
                    BuildStaticCompressions(
                        massKilograms,
                        axles,
                        averageSpring *
                        1_000.0f);

                var state =
                    new OdeArticulatedSectionState(
                        section,
                        body,
                        articulation,
                        parentBody,
                        massKilograms,
                        centerOfGravityHeight,
                        Math.Clamp(
                            (float)(physics?.RollingResistanceNewtons ??
                                section.RollingResistanceNewtons ??
                                DefaultRollingResistanceNewtons),
                            0.0f,
                            15_000.0f),
                        yawInertia,
                        nextOmsiAxleIndex,
                        axles,
                        staticCompressions,
                        averageSpring *
                            1_000.0f,
                        averageDamper *
                            1_000.0f);

                state.AbsoluteHeadingRadians =
                    HeadingRadians;
                state.RelativeYawRadians =
                    0.0f;
                state.RelativeYawRateRadiansPerSecond =
                    0.0f;

                _odeArticulatedSections[
                    section.Index] =
                    state;

                nextOmsiAxleIndex +=
                    Math.Max(
                        axles.Length,
                        1);
            }
        }
        catch (Exception exception)
            when (exception is
                DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException or
                InvalidOperationException or
                ArgumentOutOfRangeException)
        {
            DisposeOdeArticulatedSections();
        }
    }

    private void ApplyOdeArticulatedForces(
        float deltaSeconds,
        float driveForceNewtons)
    {
        foreach (var state in
                 _odeArticulatedSections.Values
                     .OrderBy(
                         static item =>
                             item.Section.Index))
        {
            var body =
                state.Body;

            var orientation =
                body.Orientation;

            var contacts =
                ApplyOdeSectionSuspension(
                    state,
                    orientation);

            if (contacts == 0)
            {
                body.AddWorldForce(
                    Vector3.UnitZ *
                    state.MassKilograms *
                    Gravity);

                var velocityWithoutTerrain =
                    body.LinearVelocity;

                body.SetLinearVelocity(
                    new Vector3(
                        velocityWithoutTerrain.X,
                        velocityWithoutTerrain.Y,
                        0.0f));
            }

            var forward3D =
                Vector3.Transform(
                    Vector3.UnitY,
                    orientation);

            var forward =
                new Vector3(
                    forward3D.X,
                    forward3D.Y,
                    0.0f);

            if (forward.LengthSquared() <
                0.000001f)
            {
                forward =
                    new Vector3(
                        MathF.Sin(
                            state.AbsoluteHeadingRadians),
                        MathF.Cos(
                            state.AbsoluteHeadingRadians),
                        0.0f);
            }
            else
            {
                forward =
                    Vector3.Normalize(
                        forward);
            }

            var right =
                new Vector3(
                    forward.Y,
                    -forward.X,
                    0.0f);

            var velocity =
                body.LinearVelocity;

            var longitudinalSpeed =
                Vector3.Dot(
                    velocity,
                    forward);

            var sectionDriveForce =
                state.Section.Index ==
                    _primaryDrivenSectionIndex
                    ? driveForceNewtons
                    : 0.0f;

            var sectionGradeAcceleration =
                -forward3D.Z *
                Gravity;

            body.AddWorldForce(
                forward *
                (sectionDriveForce +
                 sectionGradeAcceleration *
                     state.MassKilograms));

            var sectionBrakeForce =
                ResolveSectionOmsiBrakeForceNewtons(
                    state);

            if (Math.Abs(
                    longitudinalSpeed) >
                0.01f)
            {
                body.AddWorldForce(
                    forward *
                    (-Math.Sign(
                         longitudinalSpeed) *
                     (state.RollingResistanceNewtons +
                      sectionBrakeForce)));
            }

            var lateralSpeed =
                Vector3.Dot(
                    velocity,
                    right);

            var lateralForceLimit =
                state.MassKilograms *
                Gravity *
                0.62f;

            body.AddWorldForce(
                right *
                Math.Clamp(
                    -lateralSpeed *
                    state.MassKilograms *
                    5.0f,
                    -lateralForceLimit,
                    lateralForceLimit));

            var physicalYaw =
                state.Articulation
                    .YawAngleRadians;

            var physicalYawRate =
                state.Articulation
                    .YawRateRadiansPerSecond;

            var maximumYaw =
                DegreesToRadians(
                    Math.Clamp(
                        state.Section.MaximumYawDegrees,
                        5.0,
                        89.0));

            var yawSoftLimit =
                maximumYaw *
                0.86f;

            var yawLimitError =
                Math.Abs(
                    physicalYaw) >
                    yawSoftLimit
                    ? Math.Sign(
                        physicalYaw) *
                      (Math.Abs(
                           physicalYaw) -
                       yawSoftLimit)
                    : 0.0f;

            var yawTorque =
                Math.Clamp(
                    -physicalYawRate *
                        state.YawInertiaKilogramSquareMeters *
                        0.55f -
                    yawLimitError *
                        state.YawInertiaKilogramSquareMeters *
                        8.0f,
                    -state.YawInertiaKilogramSquareMeters *
                        10.0f,
                    state.YawInertiaKilogramSquareMeters *
                        10.0f);

            var physicalPitch =
                state.Articulation
                    .PitchAngleRadians;

            var physicalPitchRate =
                state.Articulation
                    .PitchRateRadiansPerSecond;

            var minimumPitch =
                DegreesToRadians(
                    Math.Clamp(
                        state.Section.MinimumPitchDegrees,
                        -45.0,
                        0.0));

            var maximumPitch =
                DegreesToRadians(
                    Math.Clamp(
                        state.Section.MaximumPitchDegrees,
                        0.0,
                        45.0));

            var pitchSoftMinimum =
                minimumPitch *
                0.86f;

            var pitchSoftMaximum =
                maximumPitch *
                0.86f;

            var pitchLimitError =
                physicalPitch <
                    pitchSoftMinimum
                    ? physicalPitch -
                      pitchSoftMinimum
                    : physicalPitch >
                        pitchSoftMaximum
                        ? physicalPitch -
                          pitchSoftMaximum
                        : 0.0f;

            var pitchTorque =
                Math.Clamp(
                    -physicalPitchRate *
                        state.PitchInertiaKilogramSquareMeters *
                        0.50f -
                    pitchLimitError *
                        state.PitchInertiaKilogramSquareMeters *
                        7.0f,
                    -state.PitchInertiaKilogramSquareMeters *
                        8.0f,
                    state.PitchInertiaKilogramSquareMeters *
                        8.0f);

            state.Articulation.AddTorques(
                yawTorque,
                pitchTorque);
        }
    }

    private int ApplyOdeSectionSuspension(
        OdeArticulatedSectionState state,
        Quaternion orientation)
    {
        var body =
            state.Body;

        var bodyPosition =
            body.Position;
        var linearVelocity =
            body.LinearVelocity;
        var angularVelocity =
            body.AngularVelocity;

        var contacts =
            0;

        for (var axleIndex = 0;
             axleIndex < state.Axles.Length;
             axleIndex++)
        {
            var axle =
                state.Axles[axleIndex];

            var spring =
                Math.Clamp(
                    (float)(axle.SpringRateKilonewtonsPerMeter ??
                        (state.FallbackSpringNewtonsPerMeter /
                         1_000.0f)),
                    25.0f,
                    1_500.0f) *
                1_000.0f;

            var damper =
                Math.Clamp(
                    (float)(axle.DamperRateKilonewtonSecondsPerMeter ??
                        (state.FallbackDamperNewtonSecondsPerMeter /
                         1_000.0f)),
                    2.0f,
                    150.0f) *
                1_000.0f;

            var trackWidth =
                Math.Clamp(
                    (float)(axle.MaximumWidthMeters ??
                        axle.MinimumWidthMeters ??
                        DefaultTrackWidthMeters),
                    1.2f,
                    3.5f);

            var staticCompression =
                axleIndex <
                    state.StaticCompressionMeters.Length
                    ? state.StaticCompressionMeters[
                        axleIndex]
                    : 0.08f;

            var maximumForce =
                axle.MaximumForceKilonewtons is
                    { } declaredMaximumForce &&
                double.IsFinite(
                    declaredMaximumForce) &&
                declaredMaximumForce >
                    0.0
                    ? (float)(
                        declaredMaximumForce *
                        1_000.0)
                    : spring *
                      0.30f;

            var maximumCompression =
                Math.Clamp(
                    maximumForce /
                    Math.Max(
                        spring,
                        1.0f),
                    0.03f,
                    0.50f);

            for (var side = 0;
                 side < 2;
                 side++)
            {
                var localPoint =
                    new Vector3(
                        (side == 0
                            ? -0.5f
                            : 0.5f) *
                        trackWidth,
                        (float)axle.LongitudinalPositionMeters,
                        -state.CenterOfGravityHeightMeters);

                var worldOffset =
                    Vector3.Transform(
                        localPoint,
                        orientation);

                var worldPoint =
                    bodyPosition +
                    worldOffset;

                if (!_terrain.TrySample(
                        worldPoint.X,
                        worldPoint.Y,
                        out var groundHeight))
                {
                    continue;
                }

                contacts++;

                var compression =
                    staticCompression +
                    groundHeight +
                    ModelGroundPlaneOffsetMeters -
                    worldPoint.Z;

                var pointVelocity =
                    linearVelocity +
                    Vector3.Cross(
                        angularVelocity,
                        worldOffset);

                var omsiAxleIndex =
                    state.OmsiAxleStartIndex +
                    axleIndex;

                var springFactor =
                    ResolveOmsiAxleSpringFactor(
                        omsiAxleIndex,
                        left:
                            side == 0);

                var effectiveSpring =
                    spring *
                    springFactor;

                var supportForce =
                    Math.Clamp(
                        effectiveSpring *
                            Math.Max(
                                compression,
                                0.0f) -
                        damper *
                            pointVelocity.Z,
                        0.0f,
                        maximumForce);

                var suspensionMeters =
                    -Math.Clamp(
                        compression,
                        0.0f,
                        maximumCompression);

                if (side == 0)
                {
                    state.SuspensionLeftMeters[
                        axleIndex] =
                        suspensionMeters;
                }
                else
                {
                    state.SuspensionRightMeters[
                        axleIndex] =
                        suspensionMeters;
                }

                body.AddWorldForceAtLocalPosition(
                    new Vector3(
                        0.0f,
                        0.0f,
                        supportForce),
                    localPoint);
            }
        }

        return contacts;
    }

    private void UpdateOdeArticulatedSectionState()
    {
        foreach (var state in
                 _odeArticulatedSections.Values
                     .OrderBy(
                         static item =>
                             item.Section.Index))
        {
            var bodyHeading =
                ResolveOdeHeading(
                    state.Body.Orientation,
                    state.AbsoluteHeadingRadians);

            var parentHeading =
                state.ParentBody ==
                    _odeBody
                    ? HeadingRadians
                    : ResolveOdeHeading(
                        state.ParentBody.Orientation,
                        bodyHeading);

            state.RelativeYawRadians =
                NormalizeRadians(
                    bodyHeading -
                    parentHeading);

            state.RelativeYawRateRadiansPerSecond =
                -state.Articulation.YawRateRadiansPerSecond;

            var bodyPitch =
                ResolveOdePitch(
                    state.Body.Orientation);

            var parentPitch =
                state.ParentBody ==
                    _odeBody
                    ? BodyPitchRadians
                    : ResolveOdePitch(
                        state.ParentBody.Orientation);

            state.RelativePitchRadians =
                bodyPitch -
                parentPitch;

            state.RelativePitchRateRadiansPerSecond =
                state.Articulation.PitchRateRadiansPerSecond;

            state.AbsoluteHeadingRadians =
                bodyHeading;
        }
    }

    private float ResolveOmsiDrivenWheelRadius(
        RuntimeVehiclePhysicsInfo? leadingPhysics,
        IReadOnlyList<RuntimeVehicleAxleInfo> leadingDeclaredAxles)
    {
        IReadOnlyList<RuntimeVehicleAxleInfo> sourceAxles =
            leadingDeclaredAxles;

        if (_primaryDrivenSectionIndex >
                0)
        {
            sourceAxles =
                _sections
                    .FirstOrDefault(
                        section =>
                            section.Index ==
                            _primaryDrivenSectionIndex)?
                    .Physics?
                    .Axles ??
                Array.Empty<RuntimeVehicleAxleInfo>();
        }

        var firstDiameter =
            sourceAxles
                .FirstOrDefault()?
                .WheelDiameterMeters;

        var diameter =
            firstDiameter is
                    { } declared &&
                double.IsFinite(
                    declared) &&
                declared >
                    0.1
                ? declared
                : leadingPhysics?
                    .AverageWheelDiameterMeters ??
                  (2.0 *
                   _wheelRadiusMeters);

        return Math.Clamp(
            (float)diameter *
            0.5f,
            0.20f,
            0.80f);
    }

    private static float ResolveOdePitch(
        Quaternion orientation)
    {
        var forward =
            Vector3.Transform(
                Vector3.UnitY,
                orientation);

        return MathF.Atan2(
            forward.Z,
            MathF.Sqrt(
                forward.X *
                    forward.X +
                forward.Y *
                    forward.Y));
    }

    private static float ResolveOdeHeading(
        Quaternion orientation,
        float fallback)
    {
        var forward =
            Vector3.Transform(
                Vector3.UnitY,
                orientation);

        var horizontalLengthSquared =
            forward.X *
                forward.X +
            forward.Y *
                forward.Y;

        if (horizontalLengthSquared <
            0.000001f)
        {
            return fallback;
        }

        return MathF.Atan2(
            forward.X,
            forward.Y);
    }

    private static RuntimeVehicleAxleInfo[] ResolveSectionAxles(
        RuntimeVehicleSectionInfo section,
        RuntimeVehiclePhysicsInfo? physics,
        float wheelBaseMeters,
        float trackWidthMeters)
    {
        if (physics?.Axles is
            { Count: > 0 })
        {
            return physics.Axles
                .Where(
                    static axle =>
                        double.IsFinite(
                            axle.LongitudinalPositionMeters))
                .OrderByDescending(
                    static axle =>
                        axle.LongitudinalPositionMeters)
                .ToArray();
        }

        var diameter =
            physics?.AverageWheelDiameterMeters ??
            section.AverageWheelDiameterMeters ??
            DefaultWheelDiameterMeters;

        var spring =
            physics?.SuspensionSpringKilonewtonsPerMeter ??
            DefaultSpringKilonewtonsPerMeter;

        var damper =
            physics?.SuspensionDamperKilonewtonSecondsPerMeter ??
            DefaultDamperKilonewtonSecondsPerMeter;

        var halfWheelBase =
            wheelBaseMeters *
            0.5f;

        return
        [
            new RuntimeVehicleAxleInfo(
                halfWheelBase,
                diameter,
                null,
                trackWidthMeters,
                null,
                spring,
                null,
                damper),
            new RuntimeVehicleAxleInfo(
                -halfWheelBase,
                diameter,
                null,
                trackWidthMeters,
                null,
                spring,
                null,
                damper)
        ];
    }

    private float ResolveOmsiAxleSpringFactor(
        int axleIndex,
        bool left)
    {
        if (axleIndex < 0 ||
            axleIndex >=
                _omsiAxleSpringFactorLeft.Length)
        {
            return 1.0f;
        }

        return left
            ? _omsiAxleSpringFactorLeft[
                axleIndex]
            : _omsiAxleSpringFactorRight[
                axleIndex];
    }

    private static float ResolveAverageAxleValue(
        IReadOnlyList<RuntimeVehicleAxleInfo> axles,
        Func<RuntimeVehicleAxleInfo, double?> selector,
        float fallback)
    {
        var values =
            axles
                .Select(
                    selector)
                .Where(
                    static value =>
                        value.HasValue &&
                        double.IsFinite(
                            value.Value) &&
                        value.Value >
                            0.0)
                .Select(
                    static value =>
                        value!.Value)
                .ToArray();

        return values.Length ==
                0
                ? fallback
                : (float)values.Average();
    }

    private static float[] BuildStaticCompressions(
        float massKilograms,
        IReadOnlyList<RuntimeVehicleAxleInfo> axles,
        float fallbackSpringNewtonsPerMeter)
    {
        if (axles.Count == 0)
        {
            return [];
        }

        var stiffness =
            new double[
                axles.Count];

        double stiffnessSum =
            0.0;
        double weightedPositionSum =
            0.0;
        double weightedSquaredPositionSum =
            0.0;

        for (var index = 0;
             index < axles.Count;
             index++)
        {
            var axle =
                axles[index];

            var springPerSide =
                Math.Clamp(
                    (axle.SpringRateKilonewtonsPerMeter ??
                     (fallbackSpringNewtonsPerMeter /
                      1_000.0f)) *
                    1_000.0,
                    25_000.0,
                    1_500_000.0);

            var axleStiffness =
                2.0 *
                springPerSide;

            stiffness[index] =
                axleStiffness;

            var position =
                axle.LongitudinalPositionMeters;

            stiffnessSum +=
                axleStiffness;
            weightedPositionSum +=
                axleStiffness *
                position;
            weightedSquaredPositionSum +=
                axleStiffness *
                position *
                position;
        }

        var result =
            new float[
                axles.Count];

        var weight =
            massKilograms *
            Gravity;

        var determinant =
            stiffnessSum *
                weightedSquaredPositionSum -
            weightedPositionSum *
                weightedPositionSum;

        var fallbackCompression =
            weight /
            Math.Max(
                stiffnessSum,
                1.0);

        var constantTerm =
            Math.Abs(
                determinant) >
                0.000001
                ? weight *
                  weightedSquaredPositionSum /
                  determinant
                : fallbackCompression;

        var slopeTerm =
            Math.Abs(
                determinant) >
                0.000001
                ? -weight *
                  weightedPositionSum /
                  determinant
                : 0.0;

        for (var index = 0;
             index < axles.Count;
             index++)
        {
            var compression =
                constantTerm +
                slopeTerm *
                axles[index]
                    .LongitudinalPositionMeters;

            if (!double.IsFinite(
                    compression) ||
                compression <=
                    0.0)
            {
                compression =
                    fallbackCompression;
            }

            result[index] =
                Math.Clamp(
                    (float)compression,
                    0.005f,
                    0.50f);
        }

        return result;
    }

    private void DisposeOdeArticulatedSections()
    {
        foreach (var state in
                 _odeArticulatedSections.Values)
        {
            state.Dispose();
        }

        _odeArticulatedSections.Clear();
    }

    private sealed class OdeArticulatedSectionState :
        IDisposable
    {
        public OdeArticulatedSectionState(
            RuntimeVehicleSectionInfo section,
            OdeRigidBody body,
            OdeUniversalJoint articulation,
            OdeRigidBody parentBody,
            float massKilograms,
            float centerOfGravityHeightMeters,
            float rollingResistanceNewtons,
            float yawInertiaKilogramSquareMeters,
            int omsiAxleStartIndex,
            RuntimeVehicleAxleInfo[] axles,
            float[] staticCompressionMeters,
            float fallbackSpringNewtonsPerMeter,
            float fallbackDamperNewtonSecondsPerMeter)
        {
            Section =
                section;
            Body =
                body;
            Articulation =
                articulation;
            ParentBody =
                parentBody;
            MassKilograms =
                massKilograms;
            CenterOfGravityHeightMeters =
                centerOfGravityHeightMeters;
            RollingResistanceNewtons =
                rollingResistanceNewtons;
            YawInertiaKilogramSquareMeters =
                yawInertiaKilogramSquareMeters;
            PitchInertiaKilogramSquareMeters =
                Math.Max(
                    yawInertiaKilogramSquareMeters *
                        0.55f,
                    1_000.0f);
            OmsiAxleStartIndex =
                omsiAxleStartIndex;
            Axles =
                axles;
            StaticCompressionMeters =
                staticCompressionMeters;

            SuspensionLeftMeters =
                new float[
                    axles.Length];

            SuspensionRightMeters =
                new float[
                    axles.Length];

            for (var axle = 0;
                 axle < axles.Length;
                 axle++)
            {
                var initialSuspension =
                    axle <
                        staticCompressionMeters.Length
                        ? -staticCompressionMeters[
                            axle]
                        : 0.0f;

                SuspensionLeftMeters[
                    axle] =
                    initialSuspension;

                SuspensionRightMeters[
                    axle] =
                    initialSuspension;
            }

            FallbackSpringNewtonsPerMeter =
                fallbackSpringNewtonsPerMeter;
            FallbackDamperNewtonSecondsPerMeter =
                fallbackDamperNewtonSecondsPerMeter;
        }

        public RuntimeVehicleSectionInfo Section { get; }

        public OdeRigidBody Body { get; }

        public OdeUniversalJoint Articulation { get; }

        public OdeRigidBody ParentBody { get; }

        public float MassKilograms { get; }

        public float CenterOfGravityHeightMeters { get; }

        public float RollingResistanceNewtons { get; }

        public float YawInertiaKilogramSquareMeters { get; }

        public float PitchInertiaKilogramSquareMeters { get; }

        public int OmsiAxleStartIndex { get; }

        public RuntimeVehicleAxleInfo[] Axles { get; }

        public float[] StaticCompressionMeters { get; }

        public float[] SuspensionLeftMeters { get; }

        public float[] SuspensionRightMeters { get; }

        public float FallbackSpringNewtonsPerMeter { get; }

        public float FallbackDamperNewtonSecondsPerMeter { get; }

        public float AbsoluteHeadingRadians { get; set; }

        public float RelativeYawRadians { get; set; }

        public float RelativeYawRateRadiansPerSecond { get; set; }

        public float RelativePitchRadians { get; set; }

        public float RelativePitchRateRadiansPerSecond { get; set; }

        public void Dispose()
        {
            Articulation.Dispose();
            Body.Dispose();
        }
    }

    private static float ResolveMaximumSuspensionCompression(
        RuntimeVehicleAxleInfo? axle,
        float springNewtonsPerMeter)
    {
        if (axle?.MaximumForceKilonewtons is
                { } maximumForce &&
            double.IsFinite(
                maximumForce) &&
            maximumForce >
                0.0)
        {
            return Math.Clamp(
                (float)(
                    maximumForce *
                    1_000.0) /
                Math.Max(
                    springNewtonsPerMeter,
                    1.0f),
                0.03f,
                0.50f);
        }

        return 0.30f;
    }

    private void DisableOdeDynamics()
    {
        DisposeOdeArticulatedSections();

        _odeBody?.Dispose();
        _odeBody =
            null;

        _odeWorld?.Dispose();
        _odeWorld =
            null;
    }

    public void Dispose()
    {
        DisableOdeDynamics();
        GC.SuppressFinalize(
            this);
    }

    private static float NormalizeSpringFactor(
        double value)
    {
        if (!double.IsFinite(
                value) ||
            value <=
            0.0)
        {
            return 1.0f;
        }

        return Math.Clamp(
            (float)value,
            0.10f,
            5.0f);
    }

    private static float NormalizeRadians(
        float value)
    {
        while (value >
               MathF.PI)
        {
            value -=
                MathF.PI *
                2.0f;
        }

        while (value <
               -MathF.PI)
        {
            value +=
                MathF.PI *
                2.0f;
        }

        return value;
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
