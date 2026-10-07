using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

public readonly record struct RuntimeFuelTrackState(
    double? Percent,
    double? ContentLiters,
    string Kind,
    bool ReturnToGarageRecommended)
{
    public static RuntimeFuelTrackState Unknown { get; } =
        new(
            null,
            null,
            "COMBUSTÍVEL",
            false);
}

internal sealed class RuntimeNavPulsePanel : Panel
{
    private readonly record struct RoadSegment(
        Vector2 A,
        Vector2 B,
        Vector2 Midpoint);

    private readonly RoadSegment[] _roads;
    private readonly Button _modeButton;
    private readonly Button _expandButton;
    private readonly Button _guidanceButton;
    private Vector3 _position;
    private float _headingRadians;
    private Vector2[] _remoteVehicles =
        Array.Empty<Vector2>();
    private Vector2[] _route =
        Array.Empty<Vector2>();
    private RuntimeFuelTrackState _fuel =
        RuntimeFuelTrackState.Unknown;
    private bool _circleMode;
    private bool _expanded;
    private bool _guidanceEnabled =
        true;
    private float _rangeMeters =
        450.0f;

    public bool Expanded =>
        _expanded;

    public bool GuidanceEnabled =>
        _guidanceEnabled;

    public event EventHandler?
        DisplayModeChanged;

    public event EventHandler?
        GuidanceVisibilityChanged;

    public RuntimeNavPulsePanel(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        DoubleBuffered = true;
        Visible = false;
        BackColor =
            Color.FromArgb(
                35,
                42,
                48);
        ForeColor = Color.White;
        BorderStyle =
            BorderStyle.FixedSingle;

        _roads =
            BuildRoadSegments(
                splines);

        _modeButton =
            new Button
            {
                Text = "○",
                Width = 30,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold),
                TabStop = false
            };

        _modeButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _modeButton.Click +=
            (_, _) =>
            {
                _circleMode =
                    !_circleMode;
                _modeButton.Text =
                    _circleMode
                        ? "□"
                        : "○";
                Invalidate();
            };

        _expandButton =
            new Button
            {
                Text = "MAX",
                Width = 40,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        7.5f,
                        FontStyle.Bold),
                TabStop = false
            };

        _guidanceButton =
            new Button
            {
                Text = "SETAS",
                Width = 52,
                Height = 24,
                Top = 4,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        205,
                        210,
                        216),
                ForeColor =
                    Color.FromArgb(
                        30,
                        35,
                        40),
                Font =
                    new Font(
                        "Segoe UI",
                        7.0f,
                        FontStyle.Bold),
                TabStop = false
            };

        _guidanceButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _guidanceButton.Click +=
            (_, _) =>
            {
                _guidanceEnabled =
                    !_guidanceEnabled;
                _guidanceButton.Text =
                    _guidanceEnabled
                        ? "SETAS"
                        : "SEM SETA";

                GuidanceVisibilityChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            };

        _expandButton.FlatAppearance.BorderColor =
            Color.FromArgb(
                130,
                138,
                146);
        _expandButton.Click +=
            (_, _) =>
            {
                _expanded =
                    !_expanded;

                if (_expanded)
                {
                    _circleMode =
                        false;
                    _modeButton.Text =
                        "○";
                }

                _expandButton.Text =
                    _expanded
                        ? "MIN"
                        : "MAX";

                DisplayModeChanged?.Invoke(
                    this,
                    EventArgs.Empty);
                Invalidate();
            };

        Controls.Add(
            _modeButton);
        Controls.Add(
            _expandButton);
        Controls.Add(
            _guidanceButton);

        Resize +=
            (_, _) =>
            {
                _modeButton.Left =
                    Math.Max(
                        4,
                        Width -
                        _modeButton.Width -
                        5);

                _expandButton.Left =
                    Math.Max(
                        4,
                        _modeButton.Left -
                        _expandButton.Width -
                        4);

                _guidanceButton.Left =
                    Math.Max(
                        4,
                        _expandButton.Left -
                        _guidanceButton.Width -
                        4);
            };
    }

    public void TogglePanel()
    {
        Visible =
            !Visible;

        if (Visible)
        {
            BringToFront();
            Invalidate();
        }
    }

    public void SetRoute(
        IReadOnlyList<RuntimeTrafficPathPointInfo> points)
    {
        _route =
            points
                .Select(
                    static point =>
                        new Vector2(
                            (float)point.X,
                            (float)point.Z))
                .ToArray();

        if (Visible)
        {
            Invalidate();
        }
    }

    public void UpdateState(
        Vector3 position,
        float headingRadians,
        float speedMetersPerSecond,
        IReadOnlyList<RuntimeTrafficAgentInfo> traffic,
        RuntimeFuelTrackState fuel)
    {
        _position =
            position;
        _headingRadians =
            headingRadians;
        _fuel =
            fuel;
        _rangeMeters =
            SuggestedRadius(
                Math.Abs(
                    speedMetersPerSecond) *
                3.6f);

        _remoteVehicles =
            traffic
                .Where(
                    static agent =>
                        (
                            unchecked(
                                (uint)agent.AgentIndex) &
                            0x60000000u
                        ) ==
                        0x60000000u)
                .Select(
                    static agent =>
                        new Vector2(
                            (float)agent.X,
                            (float)agent.Z))
                .ToArray();

        if (Visible)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(
        PaintEventArgs e)
    {
        base.OnPaint(
            e);

        var graphics =
            e.Graphics;

        graphics.SmoothingMode =
            System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var background =
            new SolidBrush(
                Color.FromArgb(
                    38,
                    44,
                    50));

        graphics.FillRectangle(
            background,
            ClientRectangle);

        var mapTop =
            31.0f;
        var footerHeight =
            45.0f;
        var mapHeight =
            Math.Max(
                80.0f,
                Height -
                mapTop -
                footerHeight -
                5.0f);
        var mapWidth =
            Math.Max(
                80.0f,
                Width -
                10.0f);

        var mapRect =
            new RectangleF(
                5.0f,
                mapTop,
                mapWidth,
                mapHeight);

        var previousClip =
            graphics.Clip.Clone();

        if (_circleMode)
        {
            using var path =
                new System.Drawing.Drawing2D.GraphicsPath();

            var size =
                Math.Min(
                    mapRect.Width,
                    mapRect.Height);

            var circle =
                new RectangleF(
                    mapRect.X +
                    (mapRect.Width - size) /
                    2.0f,
                    mapRect.Y +
                    (mapRect.Height - size) /
                    2.0f,
                    size,
                    size);

            path.AddEllipse(
                circle);
            graphics.SetClip(
                path);
            mapRect =
                circle;
        }
        else
        {
            graphics.SetClip(
                mapRect);
        }

        using var mapBrush =
            new SolidBrush(
                Color.FromArgb(
                    28,
                    33,
                    38));
        graphics.FillRectangle(
            mapBrush,
            mapRect);

        var center =
            new PointF(
                mapRect.Left +
                mapRect.Width /
                2.0f,
                mapRect.Top +
                mapRect.Height /
                2.0f);

        var scale =
            Math.Min(
                mapRect.Width,
                mapRect.Height) /
            (_rangeMeters * 2.0f);

        using var roadPen =
            new Pen(
                Color.FromArgb(
                    112,
                    122,
                    130),
                1.25f);

        foreach (var road in
                 _roads)
        {
            var dx =
                road.Midpoint.X -
                _position.X;
            var dz =
                road.Midpoint.Y -
                _position.Z;

            if (dx * dx +
                dz * dz >
                (_rangeMeters + 80.0f) *
                (_rangeMeters + 80.0f))
            {
                continue;
            }

            var a =
                ToScreen(
                    road.A,
                    center,
                    scale);
            var b =
                ToScreen(
                    road.B,
                    center,
                    scale);

            graphics.DrawLine(
                roadPen,
                a,
                b);
        }

        if (_route.Length >
            1)
        {
            using var routePen =
                new Pen(
                    Color.FromArgb(
                        205,
                        35,
                        190,
                        245),
                    3.0f);

            for (var index = 1;
                 index <
                     _route.Length;
                 index++)
            {
                var aWorld =
                    _route[
                        index -
                        1];
                var bWorld =
                    _route[
                        index];

                var midpoint =
                    (
                        aWorld +
                        bWorld
                    ) /
                    2.0f;

                var delta =
                    midpoint -
                    new Vector2(
                        _position.X,
                        _position.Z);

                if (delta.LengthSquared() >
                    (_rangeMeters + 120.0f) *
                    (_rangeMeters + 120.0f))
                {
                    continue;
                }

                graphics.DrawLine(
                    routePen,
                    ToScreen(
                        aWorld,
                        center,
                        scale),
                    ToScreen(
                        bWorld,
                        center,
                        scale));
            }
        }

        using var peerBrush =
            new SolidBrush(
                Color.FromArgb(
                    87,
                    174,
                    221));

        foreach (var peer in
                 _remoteVehicles)
        {
            var delta =
                peer -
                new Vector2(
                    _position.X,
                    _position.Z);

            if (delta.LengthSquared() >
                _rangeMeters *
                _rangeMeters)
            {
                continue;
            }

            var p =
                ToScreen(
                    peer,
                    center,
                    scale);

            graphics.FillEllipse(
                peerBrush,
                p.X - 4.0f,
                p.Y - 4.0f,
                8.0f,
                8.0f);
        }

        using var playerBrush =
            new SolidBrush(
                Color.FromArgb(
                    245,
                    245,
                    245));

        var triangle =
            new[]
            {
                new PointF(
                    center.X,
                    center.Y - 9.0f),
                new PointF(
                    center.X - 6.0f,
                    center.Y + 6.0f),
                new PointF(
                    center.X + 6.0f,
                    center.Y + 6.0f)
            };

        graphics.FillPolygon(
            playerBrush,
            triangle);

        graphics.Clip =
            previousClip;

        using var titleFont =
            new Font(
                "Segoe UI",
                9.0f,
                FontStyle.Bold);
        using var bodyFont =
            new Font(
                "Segoe UI",
                8.0f,
                FontStyle.Bold);
        using var titleBrush =
            new SolidBrush(
                Color.FromArgb(
                    225,
                    229,
                    233));

        graphics.DrawString(
            $"NAVPULSE · {_rangeMeters:0} m",
            titleFont,
            titleBrush,
            8.0f,
            7.0f);

        DrawFuelTrack(
            graphics,
            bodyFont,
            mapRect.Bottom + 5.0f);
    }

    private static float SuggestedRadius(
        float speedKph) =>
        speedKph switch
        {
            < 10.0f => 450.0f,
            < 25.0f => 650.0f,
            < 45.0f => 900.0f,
            < 65.0f => 1250.0f,
            < 90.0f => 1750.0f,
            _ => 2400.0f
        };

    private PointF ToScreen(
        Vector2 world,
        PointF center,
        float scale)
    {
        var dx =
            world.X -
            _position.X;
        var dz =
            world.Y -
            _position.Z;

        var sin =
            MathF.Sin(
                _headingRadians);
        var cos =
            MathF.Cos(
                _headingRadians);

        var right =
            dx *
                cos -
            dz *
                sin;
        var forward =
            dx *
                sin +
            dz *
                cos;

        return new PointF(
            center.X +
            right *
                scale,
            center.Y -
            forward *
                scale);
    }

    private void DrawFuelTrack(
        Graphics graphics,
        Font font,
        float y)
    {
        var percent =
            _fuel.Percent;

        var text =
            percent.HasValue
                ? $"{_fuel.Kind} {percent.Value:0}%"
                : _fuel.ContentLiters.HasValue
                    ? $"{_fuel.Kind} {_fuel.ContentLiters.Value:0.0} L"
                    : $"{_fuel.Kind} N/D";

        if (_fuel.ReturnToGarageRecommended)
        {
            text +=
                "  ·  RETORNAR À GARAGEM";
        }

        using var brush =
            new SolidBrush(
                _fuel.ReturnToGarageRecommended
                    ? Color.FromArgb(
                        235,
                        92,
                        80)
                    : Color.FromArgb(
                        212,
                        218,
                        223));

        graphics.DrawString(
            text,
            font,
            brush,
            8.0f,
            y);
    }

    private static RoadSegment[] BuildRoadSegments(
        IReadOnlyList<RuntimeSplineInfo> splines)
    {
        var result =
            new List<RoadSegment>();

        foreach (var spline in
                 splines)
        {
            if (spline.LengthMeters <=
                0.01)
            {
                continue;
            }

            var steps =
                Math.Clamp(
                    (int)Math.Ceiling(
                        spline.LengthMeters /
                        15.0),
                    1,
                    64);

            var previous =
                EvaluateCenter(
                    spline,
                    0.0);

            for (var index = 1;
                 index <= steps;
                 index++)
            {
                var distance =
                    spline.LengthMeters *
                    index /
                    steps;

                var current =
                    EvaluateCenter(
                        spline,
                        distance);

                result.Add(
                    new RoadSegment(
                        previous,
                        current,
                        (
                            previous +
                            current
                        ) /
                        2.0f));

                previous =
                    current;
            }
        }

        return result.ToArray();
    }

    private static Vector2 EvaluateCenter(
        RuntimeSplineInfo spline,
        double distance)
    {
        var clamped =
            Math.Clamp(
                distance,
                0.0,
                spline.LengthMeters);

        var yaw =
            spline.HeadingDegrees *
            Math.PI /
            180.0;

        var curved =
            Math.Abs(
                spline.RadiusMeters) >
            0.001;

        var angle =
            curved
                ? clamped /
                  spline.RadiusMeters
                : 0.0;

        var localX =
            curved
                ? spline.RadiusMeters *
                  (
                      1.0 -
                      Math.Cos(
                          angle)
                  )
                : 0.0;

        var localZ =
            curved
                ? spline.RadiusMeters *
                  Math.Sin(
                      angle)
                : clamped;

        var cosYaw =
            Math.Cos(
                yaw);
        var sinYaw =
            Math.Sin(
                yaw);

        var startX =
            spline.TileX *
            300.0 +
            spline.X;
        var startZ =
            spline.TileY *
            300.0 +
            spline.Z;

        return new Vector2(
            (float)(
                startX +
                localX *
                    cosYaw +
                localZ *
                    sinYaw),
            (float)(
                startZ -
                localX *
                    sinYaw +
                localZ *
                    cosYaw));
    }
}
