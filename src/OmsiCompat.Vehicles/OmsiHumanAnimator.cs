using System.Numerics;
namespace OmsiCompat.Vehicles;

public enum OmsiHumanActivity
{
    Stand,
    Walk,
    Sit,
    Run
}

public sealed class OmsiHumanRig
{
    private OmsiHumanRig()
    {
    }

    public Vector3 Hip { get; private init; }
    public Vector3 Knee { get; private init; }
    public Vector3 Waist { get; private init; }
    public Vector3 Shoulder { get; private init; }
    public Vector3 Elbow { get; private init; }
    public Vector3 Neck { get; private init; }
    public Vector3 Hand { get; private init; }
    public Vector3 Finger { get; private init; }
    public float FeetDistance { get; private init; }
    public float Height { get; private init; }
    public float SeatHeight { get; private init; }
    public float Stride { get; private init; }
    public float Beta { get; private init; }
    public float ArmSwing { get; private init; }
    public float HipTurn { get; private init; }
    public float WaistBend { get; private init; }
    public Vector3[] UpperArm { get; private init; } = [];
    public Vector3[] Forearm { get; private init; } = [];
    public Vector3[] Thigh { get; private init; } = [];

    public static OmsiHumanRig FromDefinition(
        OmsiHumanDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(
            definition);

        static float Finite(
            IReadOnlyList<double> values,
            int index,
            double fallback = 0.0)
        {
            if (index < 0 ||
                index >=
                    values.Count)
            {
                return (float)fallback;
            }

            var value =
                values[index];

            return double.IsFinite(
                    value)
                ? (float)value
                : (float)fallback;
        }

        var links =
            definition.Links;

        // OMSI .hum [links] is x-right, y-forward, z-up.
        // The human animation matrices use Direct3D model coordinates:
        // x-right, y-up, z-forward.
        var hip =
            new Vector3(
                Finite(links, 0),
                Finite(links, 2),
                Finite(links, 1));

        var knee =
            new Vector3(
                Finite(links, 3),
                Finite(links, 5),
                Finite(links, 4));

        var waist =
            new Vector3(
                0.0f,
                Finite(links, 7),
                Finite(links, 6));

        var shoulder =
            new Vector3(
                Finite(links, 8),
                Finite(links, 10),
                Finite(links, 9));

        var elbow =
            new Vector3(
                Finite(links, 11),
                Finite(links, 13),
                Finite(links, 12));

        var neck =
            new Vector3(
                0.0f,
                Finite(links, 15),
                Finite(links, 14));

        var hand =
            new Vector3(
                Finite(links, 16),
                Finite(links, 18),
                Finite(links, 17));

        var finger =
            new Vector3(
                Finite(links, 19),
                Finite(links, 21),
                Finite(links, 20));

        static Vector3 Mirror(
            Vector3 value) =>
            new(
                -value.X,
                value.Y,
                value.Z);

        var upperArm =
            elbow -
            shoulder;

        var forearm =
            finger -
            elbow;

        var thigh =
            knee -
            hip;

        var walk =
            definition.WalkParameters;

        return new OmsiHumanRig
        {
            Hip =
                hip,
            Knee =
                knee,
            Waist =
                waist,
            Shoulder =
                shoulder,
            Elbow =
                elbow,
            Neck =
                neck,
            Hand =
                hand,
            Finger =
                finger,
            FeetDistance =
                (float)(
                    double.IsFinite(
                            definition.FeetDistance)
                        ? definition.FeetDistance
                        : 0.0),
            Height =
                (float)(
                    double.IsFinite(
                            definition.Height)
                        ? definition.Height
                        : 0.0),
            SeatHeight =
                (float)(
                    double.IsFinite(
                            definition.SeatHeight)
                        ? definition.SeatHeight
                        : 0.0),
            Stride =
                Math.Max(
                    0.01f,
                    Finite(
                        walk,
                        0,
                        1.4)),
            Beta =
                Finite(
                    walk,
                    1,
                    66.0),
            ArmSwing =
                Finite(
                    walk,
                    2,
                    1.0),
            HipTurn =
                Finite(
                    walk,
                    3,
                    1.0),
            WaistBend =
                Finite(
                    walk,
                    4,
                    0.0),
            UpperArm =
            [
                Mirror(
                    upperArm),
                upperArm
            ],
            Forearm =
            [
                Mirror(
                    forearm),
                forearm
            ],
            Thigh =
            [
                Mirror(
                    thigh),
                thigh
            ]
        };
    }
}

public sealed class OmsiHumanAnimator
{
    public const int BoneCount = 13;

    private const float Degrees =
        MathF.PI /
        180.0f;

    private static readonly (
        float X,
        float Y)[] ThighCurve =
    [
        (0.0f, 0.75f),
        (0.2f, 1.10f),
        (0.8f, -1.10f),
        (1.0f, 0.75f)
    ];

    private static readonly (
        float X,
        float Y)[] KneeCurve =
    [
        (0.0f, 1.0f),
        (0.2f, 0.0f),
        (0.8f, 0.0f),
        (1.0f, 1.0f)
    ];

    private readonly OmsiHumanRig _rig;
    private readonly float[] _angles =
        new float[30];

    private float _phase;
    private float _bob;

    public OmsiHumanAnimator(
        OmsiHumanRig rig)
    {
        _rig =
            rig ??
            throw new ArgumentNullException(
                nameof(rig));
    }

    public float Phase =>
        _phase;

    public IReadOnlyList<float> Angles =>
        _angles;

    public Matrix4x4[] Advance(
        OmsiHumanActivity activity,
        float speedMetersPerSecond,
        float movedMeters,
        float deltaSeconds,
        float roomHeightMeters = 50.0f,
        float seatHeightMeters = 0.0f,
        bool smooth = false)
    {
        var kind =
            activity switch
            {
                OmsiHumanActivity.Walk =>
                    1,
                OmsiHumanActivity.Run =>
                    1,
                OmsiHumanActivity.Sit =>
                    2,
                _ =>
                    0
            };

        AdvanceAngles(
            kind,
            speedMetersPerSecond,
            movedMeters,
            deltaSeconds,
            roomHeightMeters,
            seatHeightMeters,
            smooth);

        return BuildBoneMatrices();
    }

    private void AdvanceAngles(
        int kind,
        float speed,
        float moved,
        float deltaSeconds,
        float roomHeight,
        float seatHeight,
        bool smooth)
    {
        Span<float> target =
            stackalloc float[30];

        var relative =
            Math.Min(
                Math.Abs(
                    float.IsFinite(
                            speed)
                        ? speed
                        : 0.0f) /
                1.2f,
                1.0f);

        var stride =
            relative *
            _rig.Stride;

        var remainingRoom =
            (
                float.IsFinite(
                    roomHeight)
                    ? roomHeight
                    : 50.0f
            ) -
            _rig.Waist.Y;

        var bodyAboveWaist =
            _rig.Height -
            _rig.Waist.Y;

        var stoop =
            0.0f;

        if (remainingRoom <
                bodyAboveWaist &&
            bodyAboveWaist >
                0.0001f)
        {
            var ratio =
                Math.Clamp(
                    Math.Max(
                        remainingRoom,
                        0.0f) /
                    bodyAboveWaist,
                    -1.0f,
                    1.0f);

            stoop =
                MathF.Acos(
                    ratio) /
                Degrees;

            target[12] =
                stoop /
                2.0f;

            target[11] =
                stoop /
                2.0f;
        }

        if (kind ==
            1)
        {
            target[12] =
                Math.Max(
                    target[12],
                    3.0f);

            target[11] =
                Math.Max(
                    target[11],
                    3.0f);

            target[9] =
                Math.Min(
                    target[9],
                    -5.0f);
        }

        var hipHeight =
            Math.Max(
                Math.Abs(
                    _rig.Hip.Y),
                0.01f);

        var swing =
            (
                stride /
                (
                    4.0f *
                    hipHeight
                )
            ) /
            Degrees;

        var bob =
            0.0f;

        switch (kind)
        {
            case 0:
            {
                var spread =
                    -(
                        _rig.FeetDistance /
                        (
                            2.0f *
                            hipHeight
                        )
                    ) /
                    Degrees;

                target[2] =
                    spread;

                target[3] =
                    spread;
                break;
            }

            case 1:
            {
                if (stride >
                    0.000001f)
                {
                    _phase +=
                        (
                            float.IsFinite(
                                moved)
                                ? Math.Max(
                                    moved,
                                    0.0f)
                                : 0.0f
                        ) /
                        stride;
                }

                while (_phase >
                       2.0f)
                {
                    _phase -=
                        2.0f;
                }

                var phase =
                    _phase;

                var half =
                    Fraction(
                        phase +
                        0.5f);

                var whole =
                    Fraction(
                        phase);

                target[0] =
                    Curve(
                        ThighCurve,
                        half) *
                    swing;

                target[4] =
                    Curve(
                        KneeCurve,
                        half) *
                    swing *
                    3.0f;

                target[1] =
                    Curve(
                        ThighCurve,
                        whole) *
                    swing;

                target[5] =
                    Curve(
                        KneeCurve,
                        whole) *
                    swing *
                    3.0f;

                var spread =
                    -(
                        _rig.FeetDistance /
                        (
                            3.0f *
                            hipHeight
                        )
                    ) /
                    Degrees;

                target[2] =
                    spread;

                target[3] =
                    spread;

                var bobWave =
                    (
                        MathF.Cos(
                            4.0f *
                            MathF.PI *
                            phase) -
                        1.0f
                    ) /
                    2.0f;

                bob =
                    (
                        1.0f -
                        MathF.Cos(
                            Degrees *
                            swing)
                    ) *
                    (
                        0.8f *
                        hipHeight
                    ) *
                    bobWave;

                target[11] -=
                    swing *
                    bobWave *
                    _rig.WaistBend;

                target[12] +=
                    swing *
                    bobWave *
                    _rig.WaistBend;

                var bodyWave =
                    MathF.Sin(
                        2.0f *
                        MathF.PI *
                        phase);

                target[29] =
                    bodyWave *
                    5.0f;

                target[10] =
                    bodyWave *
                    _rig.HipTurn;
                break;
            }

            case 2:
            {
                var thighLength =
                    Math.Max(
                        _rig.Thigh[0]
                            .Length(),
                        0.01f);

                var seat =
                    float.IsFinite(
                            seatHeight)
                        ? seatHeight
                        : 0.0f;

                var clearance =
                    -(
                        thighLength -
                        _rig.SeatHeight
                    ) -
                    seat +
                    0.1f;

                var extra =
                    0.0f;

                if (clearance >
                    0.0f)
                {
                    extra =
                        MathF.Min(
                            40.0f,
                            MathF.Asin(
                                Math.Clamp(
                                    clearance /
                                    thighLength,
                                    -1.0f,
                                    0.99f)) /
                            Degrees);
                }

                target[9] =
                    -20.0f;

                target[0] =
                    60.0f +
                    extra;

                target[1] =
                    60.0f +
                    extra;

                var spread =
                    -(
                        _rig.FeetDistance /
                        (
                            1.5f *
                            hipHeight
                        )
                    ) /
                    Degrees;

                target[2] =
                    spread;

                target[3] =
                    spread;

                target[4] =
                    90.0f +
                    extra;

                target[5] =
                    90.0f +
                    extra;
                break;
            }
        }

        var upright =
            Math.Max(
                (
                    90.0f -
                    stoop
                ) /
                90.0f,
                0.0f);

        var sin2 =
            MathF.Sin(
                2.0f *
                MathF.PI *
                _phase);

        switch (kind)
        {
            case 2:
                target[13] =
                    -3.0f -
                    0.4f *
                    stoop;
                target[15] =
                    67.0f;
                target[17] =
                    -58.0f;
                target[19] =
                    35.0f +
                    stoop;
                target[21] =
                    46.0f;
                target[23] =
                    41.0f;
                target[25] =
                    -26.0f;
                target[27] =
                    5.0f;

                target[14] =
                    -3.0f -
                    0.4f *
                    stoop;
                target[16] =
                    67.0f;
                target[18] =
                    -58.0f;
                target[20] =
                    35.0f +
                    stoop;
                target[22] =
                    46.0f;
                target[24] =
                    41.0f;
                target[26] =
                    -26.0f;
                target[28] =
                    5.0f;
                break;

            case 1:
                target[13] =
                    -3.0f;
                target[15] =
                    _rig.Beta -
                    3.0f;
                target[17] =
                    -30.0f;
                target[19] =
                    (
                        sin2 -
                        0.2f
                    ) *
                    (
                        upright *
                        _rig.ArmSwing *
                        swing
                    ) +
                    stoop *
                    0.5f;
                target[21] =
                    (
                        sin2 +
                        1.0f
                    ) *
                    (
                        upright *
                        _rig.ArmSwing *
                        swing
                    ) *
                    1.5f;

                target[14] =
                    -3.0f;
                target[16] =
                    _rig.Beta -
                    3.0f;
                target[18] =
                    -30.0f;
                target[20] =
                    (
                        -sin2 -
                        0.2f
                    ) *
                    (
                        upright *
                        _rig.ArmSwing *
                        swing
                    ) +
                    stoop *
                    0.5f;
                target[22] =
                    (
                        -sin2 +
                        1.0f
                    ) *
                    (
                        upright *
                        _rig.ArmSwing *
                        swing
                    ) *
                    1.2f;
                break;

            default:
                target[13] =
                    -3.0f;
                target[15] =
                    _rig.Beta;
                target[17] =
                    -30.0f;
                target[19] =
                    0.6f *
                    stoop -
                    3.0f;
                target[21] =
                    29.0f;

                target[14] =
                    -3.0f;
                target[16] =
                    _rig.Beta;
                target[18] =
                    -30.0f;
                target[20] =
                    0.6f *
                    stoop -
                    3.0f;
                target[22] =
                    29.0f;
                break;
        }

        var deltaMs =
            Math.Clamp(
                float.IsFinite(
                        deltaSeconds)
                    ? deltaSeconds *
                      1000.0f
                    : 0.0f,
                0.0f,
                100.0f);

        var ease =
            Math.Min(
                deltaMs /
                1000.0f *
                10.0f,
                1.0f);

        var limit =
            18000.0f *
            deltaMs /
            1000.0f;

        for (var index = 0;
             index <
                 _angles.Length;
             index++)
        {
            if (smooth)
            {
                _angles[index] +=
                    Math.Clamp(
                        (
                            target[index] -
                            _angles[index]
                        ) *
                        ease,
                        -limit,
                        limit);
            }
            else
            {
                _angles[index] =
                    target[index];
            }
        }

        _bob =
            bob;
    }

    private Matrix4x4[] BuildBoneMatrices()
    {
        var a =
            _angles;

        var hip =
            _rig.Hip;

        var knee =
            _rig.Knee;

        var waistPoint =
            _rig.Waist;

        var shoulder =
            _rig.Shoulder;

        var elbow =
            _rig.Elbow;

        var neck =
            _rig.Neck;

        var hand =
            _rig.Hand;

        var bones =
            Enumerable.Repeat(
                    Matrix4x4.Identity,
                    BoneCount)
                .ToArray();

        var pelvis =
            Translation(
                -hip) *
            Matrix4x4.CreateRotationX(
                Degrees *
                a[11]) *
            Matrix4x4.CreateTranslation(
                0.0f,
                _bob,
                0.0f) *
            Translation(
                hip);

        var waist =
            Translation(
                -waistPoint) *
            Matrix4x4.CreateRotationY(
                Degrees *
                a[10]) *
            Matrix4x4.CreateRotationX(
                Degrees *
                a[9]) *
            Translation(
                waistPoint);

        bones[8] =
            waist *
            pelvis;

        var leftHip =
            new Vector3(
                -hip.X,
                hip.Y,
                hip.Z);

        bones[0] =
            Translation(
                -leftHip) *
            Matrix4x4.CreateRotationZ(
                -a[2] *
                Degrees) *
            Matrix4x4.CreateRotationX(
                (
                    -a[11] -
                    a[0]
                ) *
                Degrees) *
            Matrix4x4.CreateRotationY(
                -a[10] *
                Degrees) *
            Translation(
                leftHip) *
            bones[8];

        bones[1] =
            Translation(
                -hip) *
            Matrix4x4.CreateRotationZ(
                a[3] *
                Degrees) *
            Matrix4x4.CreateRotationX(
                (
                    -a[11] -
                    a[1]
                ) *
                Degrees) *
            Matrix4x4.CreateRotationY(
                -a[10] *
                Degrees) *
            Translation(
                hip) *
            bones[8];

        var leftKnee =
            new Vector3(
                -knee.X,
                knee.Y,
                knee.Z);

        bones[2] =
            Translation(
                -leftKnee) *
            Matrix4x4.CreateRotationX(
                a[4] *
                Degrees) *
            Translation(
                leftKnee) *
            bones[0];

        bones[3] =
            Translation(
                -knee) *
            Matrix4x4.CreateRotationX(
                a[5] *
                Degrees) *
            Translation(
                knee) *
            bones[1];

        bones[9] =
            Translation(
                -waistPoint) *
            Matrix4x4.CreateRotationX(
                a[12] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                a[29] *
                Degrees) *
            Translation(
                waistPoint) *
            pelvis;

        var leftShoulder =
            new Vector3(
                -shoulder.X,
                shoulder.Y,
                shoulder.Z);

        bones[4] =
            Translation(
                -leftShoulder) *
            Matrix4x4.CreateFromAxisAngle(
                SafeAxis(
                    _rig.UpperArm[0]),
                a[17] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                a[19] *
                Degrees) *
            Matrix4x4.CreateRotationZ(
                a[15] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                a[13] *
                Degrees) *
            Translation(
                leftShoulder) *
            bones[9];

        bones[5] =
            Translation(
                -shoulder) *
            Matrix4x4.CreateFromAxisAngle(
                SafeAxis(
                    _rig.UpperArm[1]),
                -a[18] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                -a[20] *
                Degrees) *
            Matrix4x4.CreateRotationZ(
                -a[16] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                -a[14] *
                Degrees) *
            Translation(
                shoulder) *
            bones[9];

        var leftElbow =
            new Vector3(
                -elbow.X,
                elbow.Y,
                elbow.Z);

        bones[6] =
            Translation(
                -leftElbow) *
            Matrix4x4.CreateRotationY(
                a[21] *
                Degrees) *
            Translation(
                leftElbow) *
            bones[4];

        bones[7] =
            Translation(
                -elbow) *
            Matrix4x4.CreateRotationY(
                -a[22] *
                Degrees) *
            Translation(
                elbow) *
            bones[5];

        var leftHand =
            new Vector3(
                -hand.X,
                hand.Y,
                hand.Z);

        bones[11] =
            Translation(
                -leftHand) *
            Matrix4x4.CreateRotationY(
                a[27] *
                Degrees) *
            Matrix4x4.CreateRotationZ(
                a[25] *
                Degrees) *
            Matrix4x4.CreateRotationX(
                -a[23] *
                Degrees) *
            Translation(
                leftHand) *
            bones[6];

        bones[12] =
            Translation(
                -hand) *
            Matrix4x4.CreateRotationY(
                -a[28] *
                Degrees) *
            Matrix4x4.CreateRotationZ(
                -a[26] *
                Degrees) *
            Matrix4x4.CreateRotationX(
                -a[24] *
                Degrees) *
            Translation(
                hand) *
            bones[7];

        bones[10] =
            Translation(
                -neck) *
            Matrix4x4.CreateRotationZ(
                a[8] *
                Degrees) *
            Matrix4x4.CreateRotationX(
                a[7] *
                Degrees) *
            Matrix4x4.CreateRotationY(
                (
                    a[6] -
                    a[29]
                ) *
                Degrees) *
            Translation(
                neck) *
            bones[9];

        return bones;
    }

    private static Matrix4x4 Translation(
        Vector3 value) =>
        Matrix4x4.CreateTranslation(
            value);

    private static Vector3 SafeAxis(
        Vector3 value) =>
        value.LengthSquared() >
            0.000001f
            ? Vector3.Normalize(
                value)
            : Vector3.UnitX;

    private static float Fraction(
        float value) =>
        value -
        MathF.Truncate(
            value);

    private static float Curve(
        IReadOnlyList<(
            float X,
            float Y)> points,
        float x)
    {
        if (x <
            points[0].X)
        {
            return points[0].Y;
        }

        var last =
            points[
                points.Count -
                1];

        if (x >=
            last.X)
        {
            return last.Y;
        }

        var index =
            1;

        while (index <
                   points.Count &&
               points[index].X <=
                   x)
        {
            index++;
        }

        var a =
            points[
                index -
                1];

        var b =
            points[
                index];

        return Math.Abs(
                   b.X -
                   a.X) <
               0.000001f
            ? (
                a.Y +
                b.Y
              ) /
              2.0f
            : (
                  x -
                  a.X
              ) *
              (
                  (
                      b.Y -
                      a.Y
                  ) /
                  (
                      b.X -
                      a.X
                  )
              ) +
              a.Y;
    }
}
