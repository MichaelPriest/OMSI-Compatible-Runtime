using System;
using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

public readonly record struct RuntimeTeleMatrixState(
    bool Available,
    string Line,
    string Destination,
    string NextStop,
    int? StopNumber,
    int? StopCount,
    int? DelaySeconds,
    double? DistanceToStopMeters)
{
    public static RuntimeTeleMatrixState Unavailable { get; } =
        new(
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null);
}

/// <summary>
/// Compact OMSI-style operational HUD. It consumes the same vehicle,
/// DriveOps and FuelTrack state used by the runtime instead of maintaining
/// a second simulation model.
/// </summary>
internal sealed class RuntimeLiveBoardPanel : Panel
{
    private readonly Label _route;
    private readonly Label _speed;
    private readonly Label _systems;
    private readonly Label _fuel;
    private readonly Label _network;
    private readonly Label _nextStop;
    private readonly Label _punctuality;

    public RuntimeLiveBoardPanel()
    {
        Visible = true;
        BorderStyle =
            BorderStyle.FixedSingle;
        BackColor =
            Color.FromArgb(
                214,
                218,
                224);
        TabStop = false;

        var header =
            new Panel
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor =
                    Color.FromArgb(
                        52,
                        62,
                        72)
            };

        header.Controls.Add(
            new Label
            {
                Dock = DockStyle.Fill,
                Text =
                    "LIVEBOARD  |  OPERAÇÃO",
                ForeColor =
                    Color.White,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleLeft,
                Padding =
                    new Padding(
                        8,
                        0,
                        0,
                        0)
            });

        _speed =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        8,
                        31,
                        94,
                        31),
                Text =
                    "0 km/h",
                ForeColor =
                    Color.FromArgb(
                        27,
                        31,
                        36),
                Font =
                    new Font(
                        "Segoe UI",
                        15.0f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleLeft
            };

        _route =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        108,
                        30,
                        330,
                        22),
                Text =
                    "LINHA --  |  SEM DESTINO",
                ForeColor =
                    Color.FromArgb(
                        52,
                        62,
                        72),
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold),
                AutoEllipsis =
                    true
            };

        _systems =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        108,
                        52,
                        330,
                        18),
                Text =
                    "E OFF · M OFF · DRIVERPASS -- · TURNO --",
                ForeColor =
                    Color.FromArgb(
                        72,
                        78,
                        85),
                Font =
                    new Font(
                        "Segoe UI",
                        8.0f,
                        FontStyle.Bold),
                AutoEllipsis =
                    true
            };

        _nextStop =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        8,
                        76,
                        300,
                        20),
                Text =
                    "PRÓXIMA PARADA --",
                ForeColor =
                    Color.FromArgb(
                        42,
                        70,
                        92),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                AutoEllipsis =
                    true
            };

        _punctuality =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        313,
                        76,
                        125,
                        20),
                Text =
                    "HORÁRIO --",
                ForeColor =
                    Color.FromArgb(
                        72,
                        78,
                        85),
                Font =
                    new Font(
                        "Segoe UI",
                        8.0f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleRight,
                AutoEllipsis =
                    true
            };

        _fuel =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        8,
                        101,
                        205,
                        22),
                Text =
                    "COMBUSTÍVEL N/D",
                ForeColor =
                    Color.FromArgb(
                        67,
                        108,
                        143),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold)
            };

        _network =
            new Label
            {
                AutoSize = false,
                Bounds =
                    new Rectangle(
                        218,
                        101,
                        220,
                        22),
                Text =
                    "REDE OFFLINE",
                ForeColor =
                    Color.FromArgb(
                        110,
                        45,
                        45),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleRight,
                AutoEllipsis =
                    true
            };

        Controls.Add(
            _nextStop);
        Controls.Add(
            _punctuality);
        Controls.Add(
            _speed);
        Controls.Add(
            _route);
        Controls.Add(
            _systems);
        Controls.Add(
            _fuel);
        Controls.Add(
            _network);
        Controls.Add(
            header);
    }

    public void TogglePanel()
    {
        Visible =
            !Visible;

        if (Visible)
        {
            BringToFront();
        }
    }

    public void SetNetworkState(
        string role,
        bool connected,
        int peerCount,
        string session)
    {
        _network.Text =
            connected
                ? $"{role} · {peerCount + 1} ONLINE"
                : $"{role} · OFFLINE";

        _network.ForeColor =
            connected
                ? Color.FromArgb(
                    30,
                    105,
                    45)
                : Color.FromArgb(
                    110,
                    45,
                    45);
    }

    public void UpdateTeleMatrix(
        RuntimeTeleMatrixState state)
    {
        if (!state.Available)
        {
            _nextStop.Text =
                "PRÓXIMA PARADA --";
            _punctuality.Text =
                "HORÁRIO --";
            _punctuality.ForeColor =
                Color.FromArgb(
                    72,
                    78,
                    85);
            return;
        }

        var position =
            state.StopNumber.HasValue &&
            state.StopCount.HasValue
                ? $" {state.StopNumber.Value}/{state.StopCount.Value}"
                : string.Empty;

        var distance =
            state.DistanceToStopMeters.HasValue
                ? $" · {state.DistanceToStopMeters.Value:0} m"
                : string.Empty;

        _nextStop.Text =
            $"PRÓXIMA {position} · {state.NextStop}{distance}";

        if (!state.DelaySeconds.HasValue)
        {
            _punctuality.Text =
                "HORÁRIO --";
            _punctuality.ForeColor =
                Color.FromArgb(
                    72,
                    78,
                    85);
            return;
        }

        var delay =
            state.DelaySeconds.Value;

        _punctuality.Text =
            Math.Abs(
                delay) <=
                    30
                ? "NO HORÁRIO"
                : delay >
                    0
                    ? $"+{delay / 60}:{Math.Abs(delay % 60):00}"
                    : $"-{Math.Abs(delay) / 60}:{Math.Abs(delay % 60):00}";

        _punctuality.ForeColor =
            delay >
                120
                ? Color.FromArgb(
                    155,
                    55,
                    45)
                : delay <
                    -120
                    ? Color.FromArgb(
                        185,
                        115,
                        25)
                    : Color.FromArgb(
                        35,
                        115,
                        58);
    }

    public void UpdateState(
        bool electrical,
        bool engine,
        float speedMetersPerSecond,
        RuntimeFuelTrackState fuel,
        bool startAuthorized,
        bool shiftActive,
        string line,
        string destination)
    {
        _speed.Text =
            $"{Math.Abs(speedMetersPerSecond) * 3.6f:0} km/h";

        var safeLine =
            string.IsNullOrWhiteSpace(
                line)
                ? "--"
                : line.Trim();

        var safeDestination =
            string.IsNullOrWhiteSpace(
                destination)
                ? "SEM DESTINO"
                : destination.Trim();

        _route.Text =
            $"LINHA {safeLine}  |  {safeDestination}";

        _systems.Text =
            $"E {(electrical ? "ON" : "OFF")} · M {(engine ? "ON" : "OFF")} · DRIVERPASS {(startAuthorized ? "OK" : "BLOQ")} · TURNO {(shiftActive ? "ATIVO" : "OFF")}";

        _systems.ForeColor =
            startAuthorized
                ? Color.FromArgb(
                    72,
                    78,
                    85)
                : Color.FromArgb(
                    135,
                    35,
                    35);

        var fuelText =
            fuel.Percent.HasValue
                ? $"{fuel.Kind} {fuel.Percent.Value:0}%"
                : fuel.ContentLiters.HasValue
                    ? $"{fuel.Kind} {fuel.ContentLiters.Value:0.0} L"
                    : $"{fuel.Kind} N/D";

        if (fuel.ReturnToGarageRecommended)
        {
            fuelText +=
                " · GARAGEM";
        }

        _fuel.Text =
            fuelText;

        _fuel.ForeColor =
            fuel.ReturnToGarageRecommended
                ? Color.FromArgb(
                    145,
                    60,
                    30)
                : Color.FromArgb(
                    67,
                    108,
                    143);
    }

    protected override void OnSizeChanged(
        EventArgs e)
    {
        base.OnSizeChanged(
            e);

        var rightWidth =
            Math.Max(
                140,
                Width -
                126);

        _route.Width =
            rightWidth;
        _systems.Width =
            rightWidth;

        _network.Width =
            Math.Max(
                130,
                Width -
                _network.Left -
                10);

        _nextStop.Width =
            Math.Max(
                180,
                Width -
                    158);

        _punctuality.Left =
            Math.Max(
                190,
                Width -
                    135);
        _punctuality.Width =
            125;
    }
}
