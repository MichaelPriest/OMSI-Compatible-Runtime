using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

public sealed record RuntimeDriveOpsMessageRequest(
    string DriverName,
    string CompanyName,
    string EmployeeNumber,
    string Module,
    string Kind,
    string Text,
    string Target);

public sealed record RuntimeDriveOpsInboundMessage(
    uint SenderId,
    string SenderName,
    string CompanyName,
    string EmployeeNumber,
    string Module,
    string Kind,
    string Text,
    string Target,
    long TimestampUnixMilliseconds);

internal readonly record struct RuntimeFleetCareState(
    string EngineTemperature,
    string OilPressure,
    string AirPressure,
    string BatteryVoltage,
    string FaultState,
    bool FaultActive);

internal readonly record struct RuntimePassengerFlowState(
    string PassengerCount,
    string DoorState,
    string RampState,
    string KneelingState,
    string WheelchairState);

internal sealed class RuntimeDriveOpsPanel : Panel
{
    private sealed class DriverPassProfile
    {
        public string DriverName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string EmployeeNumber { get; set; } = string.Empty;
        public string Role { get; set; } = "Motorista";
        public bool RequireForStart { get; set; } = true;
    }

    private readonly Panel _contentHost;
    private readonly Label _status;
    private readonly Label _networkState;
    private readonly Label _vehicleState;
    private readonly TextBox _driverName;
    private readonly TextBox _companyName;
    private readonly TextBox _employeeNumber;
    private readonly TextBox _role;
    private readonly CheckBox _requireForStart;
    private readonly Button _insertBadgeButton;

    private readonly ListBox _fleetMessages =
        OmsiListBox();
    private readonly TextBox _fleetInput =
        OmsiTextBox(string.Empty);
    private readonly ListBox _supportMessages =
        OmsiListBox();
    private readonly ComboBox _supportReason =
        OmsiComboBox();
    private readonly TextBox _supportDetails =
        OmsiTextBox(string.Empty);
    private readonly ListBox _controlMessages =
        OmsiListBox();
    private readonly TextBox _controlInput =
        OmsiTextBox(string.Empty);
    private readonly ListBox _routeMessages =
        OmsiListBox();
    private readonly ListBox _shiftEvents =
        OmsiListBox();
    private readonly ListBox _incidentMessages =
        OmsiListBox();
    private readonly Label _fleetCareState =
        OmsiTelemetryLabel(
            "TEMPERATURA MOTOR: N/D\r\n" +
            "PRESSÃO ÓLEO: N/D\r\n" +
            "PRESSÃO AR: N/D\r\n" +
            "TENSÃO BATERIA: N/D\r\n" +
            "FALHAS: N/D");
    private readonly Label _passengerFlowState =
        OmsiTelemetryLabel(
            "PASSAGEIROS: N/D\r\n" +
            "PORTAS: N/D\r\n" +
            "RAMPA: N/D\r\n" +
            "KNEELING: N/D\r\n" +
            "CADEIRANTE: N/D");
    private RuntimeFleetCareState _lastFleetCareState =
        new(
            "N/D",
            "N/D",
            "N/D",
            "N/D",
            "N/D",
            false);
    private bool _fleetCareFaultReported;
    private readonly Label _shiftState =
        new()
        {
            AutoSize = false,
            Text = "TURNO ENCERRADO",
            ForeColor = OmsiText,
            Font =
                new Font(
                    "Segoe UI",
                    9.0f,
                    FontStyle.Bold)
        };
    private readonly Button _shiftToggleButton;
    private readonly ListBox _commsMessages =
        OmsiListBox();
    private readonly TextBox _commsInput =
        OmsiTextBox(string.Empty);
    private readonly Label _commsVoiceState =
        new()
        {
            AutoSize = false,
            Text = "VOZ: PTT F10 · aguardando sessão",
            ForeColor = OmsiText,
            Font =
                new Font(
                    "Segoe UI",
                    9.0f,
                    FontStyle.Bold)
        };
    private readonly TextBox _routeLine =
        OmsiTextBox(string.Empty);
    private readonly TextBox _routeCode =
        OmsiTextBox(string.Empty);
    private readonly TextBox _routeDestination =
        OmsiTextBox(string.Empty);

    private DriverPassProfile _profile;
    private bool _badgeInserted;
    private bool _shiftActive;
    private DateTimeOffset? _shiftStartedAt;
    private bool? _lastElectricalState;
    private bool? _lastEngineState;

    private static readonly Color OmsiPanel =
        Color.FromArgb(214, 218, 224);
    private static readonly Color OmsiDark =
        Color.FromArgb(52, 62, 72);
    private static readonly Color OmsiBlue =
        Color.FromArgb(67, 108, 143);
    private static readonly Color OmsiText =
        Color.FromArgb(27, 31, 36);

    public RuntimeDriveOpsPanel()
    {
        Visible = false;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = OmsiPanel;
        Padding = new Padding(0);
        TabStop = true;

        _profile = LoadProfile();
        _badgeInserted = !_profile.RequireForStart;

        _supportReason.Items.AddRange(
            new object[]
            {
                "Pane mecânica",
                "Pane elétrica",
                "Colisão",
                "Acidente",
                "Pneu / rodagem",
                "Guincho",
                "Troca de veículo",
                "Apoio operacional"
            });
        _supportReason.SelectedIndex = 0;

        var header =
            new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = OmsiDark
            };

        var title =
            new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = "DRIVEOPS  |  CENTRAL OPERACIONAL",
                ForeColor = Color.White,
                Font =
                    new Font(
                        "Segoe UI",
                        10.5f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };

        var close =
            OmsiButton(
                "X",
                38,
                (_, _) =>
                    HidePanel());

        close.Dock = DockStyle.Right;
        close.BackColor =
            Color.FromArgb(198, 204, 211);

        header.Controls.Add(title);
        header.Controls.Add(close);

        var footer =
            new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor =
                    Color.FromArgb(199, 204, 211),
                Padding = new Padding(8)
            };

        _status =
            new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = OmsiText,
                Font = new Font("Segoe UI", 9.0f),
                TextAlign =
                    ContentAlignment.MiddleLeft
            };

        _networkState =
            new Label
            {
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 180,
                Text = "REDE OFFLINE",
                ForeColor =
                    Color.FromArgb(110, 45, 45),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleRight
            };

        _vehicleState =
            new Label
            {
                AutoSize = false,
                Dock = DockStyle.Right,
                Width = 255,
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                TextAlign =
                    ContentAlignment.MiddleRight
            };

        footer.Controls.Add(_status);
        footer.Controls.Add(_networkState);
        footer.Controls.Add(_vehicleState);

        var navigation =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 158,
                FlowDirection =
                    FlowDirection.TopDown,
                WrapContents = false,
                BackColor =
                    Color.FromArgb(190, 196, 203),
                Padding = new Padding(6),
                AutoScroll = true
            };

        _contentHost =
            new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OmsiPanel,
                Padding = new Padding(14)
            };

        AddModuleButton(navigation, "DRIVERPASS", ShowDriverPass);
        AddModuleButton(navigation, "FLEETLINK", ShowFleetLink);
        AddModuleButton(navigation, "COMMSLINK", ShowCommsLink);
        AddModuleButton(navigation, "ASSISTLINK", ShowAssistLink);
        AddModuleButton(navigation, "CONTROLHUB", ShowControlHub);
        AddModuleButton(navigation, "ROUTECORE", ShowRouteCore);
        AddModuleButton(navigation, "FLEETCARE", ShowFleetCare);
        AddModuleButton(navigation, "PASSENGERFLOW", ShowPassengerFlow);
        AddModuleButton(navigation, "SHIFTFLOW", ShowShiftFlow);
        AddModuleButton(navigation, "INCIDENTLOG", ShowIncidentLog);

        Controls.Add(_contentHost);
        Controls.Add(navigation);
        Controls.Add(footer);
        Controls.Add(header);

        _driverName =
            OmsiTextBox(_profile.DriverName);
        _companyName =
            OmsiTextBox(_profile.CompanyName);
        _employeeNumber =
            OmsiTextBox(_profile.EmployeeNumber);
        _role =
            OmsiTextBox(_profile.Role);

        _requireForStart =
            new CheckBox
            {
                Text =
                    "Exigir DriverPass para liberar elétrica/partida",
                Checked =
                    _profile.RequireForStart,
                AutoSize = true,
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f)
            };

        _insertBadgeButton =
            OmsiButton(
                string.Empty,
                154,
                (_, _) =>
                    ToggleBadge());

        _shiftToggleButton =
            OmsiButton(
                "INICIAR TURNO",
                160,
                (_, _) =>
                    ToggleShift());

        LoadIncidentHistory();

        ShowDriverPass();
        RefreshDriverPassStatus();
    }

    public event Action<RuntimeDriveOpsMessageRequest>?
        MessageRequested;

    public bool StartAuthorized =>
        !_profile.RequireForStart ||
        _badgeInserted;

    public bool ShiftActive =>
        _shiftActive;

    public string CurrentLine =>
        _routeLine.Text.Trim();

    public string CurrentDestination =>
        _routeDestination.Text.Trim();

    public void TogglePanel()
    {
        if (Visible)
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    public void ShowPanel()
    {
        Visible = true;
        BringToFront();
        Focus();
        RefreshDriverPassStatus();
    }

    public void HidePanel() =>
        Visible = false;

    public void SetNetworkState(
        string role,
        bool connected,
        int peerCount,
        string session)
    {
        _networkState.Text =
            connected
                ? $"{role} · {peerCount + 1} ONLINE · {session}"
                : $"{role} · OFFLINE";

        _networkState.ForeColor =
            connected
                ? Color.FromArgb(30, 105, 45)
                : Color.FromArgb(110, 45, 45);
    }

    public void ReceiveMessage(
        RuntimeDriveOpsInboundMessage message)
    {
        var localCompany =
            _companyName.Text.Trim();

        if (!string.IsNullOrWhiteSpace(localCompany) &&
            !string.IsNullOrWhiteSpace(message.CompanyName) &&
            !localCompany.Equals(
                message.CompanyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var timestamp =
            message.TimestampUnixMilliseconds >
                    0
                ? DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        message.TimestampUnixMilliseconds)
                    .ToLocalTime()
                    .ToString("HH:mm")
                : DateTime.Now.ToString("HH:mm");

        var sender =
            string.IsNullOrWhiteSpace(
                message.SenderName)
                ? "SISTEMA"
                : message.SenderName;

        if (message.Module.Equals(
                "COMMSLINK",
                StringComparison.OrdinalIgnoreCase) &&
            message.Kind.Equals(
                "VOICE_STATE",
                StringComparison.OrdinalIgnoreCase))
        {
            _commsVoiceState.Text =
                message.Text;
            return;
        }

        var line =
            $"[{timestamp}] {sender}: {message.Text}";

        switch (message.Module.ToUpperInvariant())
        {
            case "FLEETLINK":
            case "DRIVERPASS":
                AppendMessage(
                    _fleetMessages,
                    line);
                break;

            case "COMMSLINK":
                AppendMessage(
                    _commsMessages,
                    line);
                break;

            case "ASSISTLINK":
                AppendMessage(
                    _supportMessages,
                    line);
                break;

            case "CONTROLHUB":
                AppendMessage(
                    _controlMessages,
                    line);
                break;

            case "ROUTECORE":
                AppendMessage(
                    _routeMessages,
                    line);
                break;

            default:
                AppendMessage(
                    _fleetMessages,
                    line);
                break;
        }

        if (message.Module.Equals(
                "ASSISTLINK",
                StringComparison.OrdinalIgnoreCase) &&
            message.Kind.Equals(
                "SUPPORT",
                StringComparison.OrdinalIgnoreCase))
        {
            RecordIncident(
                "SUPORTE",
                $"{sender}: {message.Text}");
        }
        else if (message.Module.Equals(
                     "CONTROLHUB",
                     StringComparison.OrdinalIgnoreCase) &&
                 message.Kind.Equals(
                     "DISPATCH",
                     StringComparison.OrdinalIgnoreCase))
        {
            RecordIncident(
                "CCO",
                $"{sender}: {message.Text}");
        }

        if (message.Kind.Equals(
                "ERROR",
                StringComparison.OrdinalIgnoreCase))
        {
            _status.Text =
                message.Text;
            _status.ForeColor =
                Color.FromArgb(135, 35, 35);
        }
    }

    public void NotifyStartBlocked(
        string action)
    {
        _status.Text =
            $"DRIVERPASS: ação bloqueada ({action}). Insira um crachá válido.";
        _status.ForeColor =
            Color.FromArgb(135, 35, 35);
        ShowDriverPass();
    }

    public void UpdateVehicleState(
        bool electrical,
        bool engine,
        float speedMetersPerSecond)
    {
        _vehicleState.Text =
            $"ELÉTRICA {(electrical ? "ON" : "OFF")} | MOTOR {(engine ? "ON" : "OFF")} | {Math.Abs(speedMetersPerSecond) * 3.6f:0} km/h";

        if (_shiftActive &&
            _shiftStartedAt is
                { } started)
        {
            var elapsed =
                DateTimeOffset.Now -
                started;

            _shiftState.Text =
                $"TURNO ATIVO · {elapsed:hh\\:mm\\:ss} · {Math.Abs(speedMetersPerSecond) * 3.6f:0} km/h";
        }

        if (_lastElectricalState.HasValue &&
            _lastElectricalState.Value !=
            electrical)
        {
            AppendShiftEvent(
                electrical
                    ? "Elétrica ligada."
                    : "Elétrica desligada.");
        }

        if (_lastEngineState.HasValue &&
            _lastEngineState.Value !=
            engine)
        {
            AppendShiftEvent(
                engine
                    ? "Motor ligado."
                    : "Motor desligado.");
        }

        _lastElectricalState =
            electrical;
        _lastEngineState =
            engine;
    }

    public void UpdateFleetCareState(
        RuntimeFleetCareState state)
    {
        _lastFleetCareState =
            state;

        _fleetCareState.Text =
            $"TEMPERATURA MOTOR: {state.EngineTemperature}\r\n" +
            $"PRESSÃO ÓLEO: {state.OilPressure}\r\n" +
            $"PRESSÃO AR: {state.AirPressure}\r\n" +
            $"TENSÃO BATERIA: {state.BatteryVoltage}\r\n" +
            $"FALHAS: {state.FaultState}";

        _fleetCareState.ForeColor =
            state.FaultActive
                ? Color.FromArgb(
                    150,
                    32,
                    32)
                : OmsiText;

        if (state.FaultActive &&
            !_fleetCareFaultReported)
        {
            _fleetCareFaultReported =
                true;

            RecordIncident(
                "FLEETCARE",
                state.FaultState);
        }
        else if (!state.FaultActive)
        {
            _fleetCareFaultReported =
                false;
        }
    }

    public void UpdatePassengerFlowState(
        RuntimePassengerFlowState state)
    {
        _passengerFlowState.Text =
            $"PASSAGEIROS: {state.PassengerCount}\r\n" +
            $"PORTAS: {state.DoorState}\r\n" +
            $"RAMPA: {state.RampState}\r\n" +
            $"KNEELING: {state.KneelingState}\r\n" +
            $"CADEIRANTE: {state.WheelchairState}";
    }

    public void RecordIncident(
        string kind,
        string detail)
    {
        var safeKind =
            string.IsNullOrWhiteSpace(
                kind)
                ? "EVENTO"
                : kind.Trim();

        var safeDetail =
            string.IsNullOrWhiteSpace(
                detail)
                ? "Sem detalhes."
                : detail.Trim();

        var line =
            $"[{DateTime.Now:dd/MM HH:mm:ss}] {safeKind} · {safeDetail}";

        AppendMessage(
            _incidentMessages,
            line);

        try
        {
            var path =
                IncidentLogPath();

            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            File.AppendAllText(
                path,
                line +
                Environment.NewLine);
        }
        catch
        {
        }
    }

    private void AddModuleButton(
        Control parent,
        string text,
        Action action)
    {
        var button =
            OmsiButton(
                text,
                140,
                (_, _) =>
                    action());

        button.Height = 38;
        button.Margin =
            new Padding(0, 0, 0, 5);
        parent.Controls.Add(button);
    }

    private Panel ModuleRoot(
        string title,
        string subtitle)
    {
        var root =
            new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

        root.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = title,
                Font =
                    new Font(
                        "Segoe UI",
                        15.0f,
                        FontStyle.Bold),
                ForeColor = OmsiDark,
                Location = new Point(8, 8)
            });

        root.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = subtitle,
                Font =
                    new Font(
                        "Segoe UI",
                        9.5f,
                        FontStyle.Bold),
                ForeColor = OmsiBlue,
                Location = new Point(8, 42)
            });

        return root;
    }

    private void ShowDriverPass()
    {
        _contentHost.Controls.Clear();

        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 8,
                BackColor = Color.Transparent
            };

        root.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                180));
        root.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        AddHeading(
            root,
            "DRIVERPASS",
            "Crachá operacional do motorista");
        AddField(root, 1, "Motorista", _driverName);
        AddField(root, 2, "Empresa", _companyName);
        AddField(root, 3, "Matrícula", _employeeNumber);
        AddField(root, 4, "Função", _role);

        root.Controls.Add(
            _requireForStart,
            0,
            5);
        root.SetColumnSpan(
            _requireForStart,
            2);

        var buttons =
            new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection =
                    FlowDirection.LeftToRight,
                WrapContents = false,
                Margin =
                    new Padding(0, 10, 0, 0)
            };

        buttons.Controls.Add(
            OmsiButton(
                "GRAVAR CRACHÁ",
                154,
                (_, _) =>
                    SaveBadge()));
        buttons.Controls.Add(_insertBadgeButton);

        root.Controls.Add(
            buttons,
            0,
            6);
        root.SetColumnSpan(
            buttons,
            2);

        var note =
            new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text =
                    "A física e os scripts do ônibus continuam sendo os do OMSI. O DriverPass atua somente como autorização operacional para elétrica e partida.",
                ForeColor =
                    Color.FromArgb(72, 78, 85),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f),
                Margin =
                    new Padding(0, 12, 0, 0)
            };

        root.Controls.Add(note, 0, 7);
        root.SetColumnSpan(note, 2);

        _contentHost.Controls.Add(root);
        RefreshDriverPassStatus();
    }

    private void ShowFleetLink()
    {
        _contentHost.Controls.Clear();
        var root =
            ModuleRoot(
                "FLEETLINK",
                "Rede da empresa · chat e presença operacional");

        _fleetMessages.SetBounds(
            8, 72, 520, 190);
        _fleetInput.SetBounds(
            8, 276, 375, 28);

        var send =
            OmsiButton(
                "ENVIAR",
                132,
                (_, _) =>
                {
                    var text =
                        _fleetInput.Text.Trim();

                    if (RequestMessage(
                            "FLEETLINK",
                            "CHAT",
                            text,
                            string.Empty))
                    {
                        _fleetInput.Clear();
                    }
                });

        send.SetBounds(
            396, 274, 132, 32);

        root.Controls.Add(_fleetMessages);
        root.Controls.Add(_fleetInput);
        root.Controls.Add(send);
        _contentHost.Controls.Add(root);
    }

    private void ShowCommsLink()
    {
        _contentHost.Controls.Clear();

        var root =
            ModuleRoot(
                "COMMSLINK",
                "Chat texto e voz PTT da operação");

        _commsMessages.SetBounds(
            8,
            72,
            520,
            166);

        _commsVoiceState.SetBounds(
            8,
            244,
            520,
            28);

        _commsInput.SetBounds(
            8,
            278,
            375,
            28);

        var send =
            OmsiButton(
                "ENVIAR CHAT",
                132,
                (_, _) =>
                {
                    var text =
                        _commsInput.Text.Trim();

                    if (RequestMessage(
                            "COMMSLINK",
                            "CHAT",
                            text,
                            string.Empty))
                    {
                        _commsInput.Clear();
                    }
                });

        send.SetBounds(
            396,
            276,
            132,
            32);

        var hint =
            new Label
            {
                AutoSize = false,
                Text =
                    "F10 = pressione e segure para falar. Áudio e chat usam a mesma sessão multiplayer.",
                ForeColor =
                    Color.FromArgb(
                        72,
                        78,
                        84),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f),
                Bounds =
                    new Rectangle(
                        8,
                        314,
                        520,
                        34)
            };

        root.Controls.Add(
            _commsMessages);
        root.Controls.Add(
            _commsVoiceState);
        root.Controls.Add(
            _commsInput);
        root.Controls.Add(
            send);
        root.Controls.Add(
            hint);

        _contentHost.Controls.Add(
            root);
    }

    private void ShowAssistLink()
    {
        _contentHost.Controls.Clear();
        var root =
            ModuleRoot(
                "ASSISTLINK",
                "Solicitação de suporte operacional");

        _supportReason.SetBounds(
            8, 76, 220, 28);
        _supportDetails.SetBounds(
            240, 76, 288, 28);

        var send =
            OmsiButton(
                "SOLICITAR SUPORTE",
                180,
                (_, _) =>
                {
                    var reason =
                        _supportReason.SelectedItem
                            ?.ToString() ??
                        "Apoio operacional";

                    var detail =
                        _supportDetails.Text.Trim();

                    var text =
                        string.IsNullOrWhiteSpace(detail)
                            ? reason
                            : $"{reason}: {detail}";

                    if (RequestMessage(
                            "ASSISTLINK",
                            "SUPPORT",
                            text,
                            "CONTROLHUB"))
                    {
                        _supportDetails.Clear();
                    }
                });

        send.SetBounds(
            8, 116, 180, 32);
        _supportMessages.SetBounds(
            8, 164, 520, 150);

        root.Controls.Add(_supportReason);
        root.Controls.Add(_supportDetails);
        root.Controls.Add(send);
        root.Controls.Add(_supportMessages);
        _contentHost.Controls.Add(root);
    }

    private void ShowControlHub()
    {
        _contentHost.Controls.Clear();
        var root =
            ModuleRoot(
                "CONTROLHUB",
                "CCO · despacho e gestão de ocorrências");

        _controlMessages.SetBounds(
            8, 72, 520, 190);
        _controlInput.SetBounds(
            8, 276, 350, 28);

        var dispatch =
            OmsiButton(
                "ENVIAR DESPACHO",
                160,
                (_, _) =>
                {
                    var text =
                        _controlInput.Text.Trim();

                    if (RequestMessage(
                            "CONTROLHUB",
                            "DISPATCH",
                            text,
                            string.Empty))
                    {
                        _controlInput.Clear();
                    }
                });

        dispatch.Enabled =
            HasControlHubPermission();
        dispatch.SetBounds(
            368, 274, 160, 32);

        if (!dispatch.Enabled)
        {
            _status.Text =
                "CONTROLHUB: seu DriverPass não possui função de CCO/gestão.";
            _status.ForeColor =
                Color.FromArgb(110, 70, 30);
        }

        root.Controls.Add(_controlMessages);
        root.Controls.Add(_controlInput);
        root.Controls.Add(dispatch);
        _contentHost.Controls.Add(root);
    }

    private void ShowRouteCore()
    {
        _contentHost.Controls.Clear();
        var root =
            ModuleRoot(
                "ROUTECORE",
                "Terminal operacional · linha, rota e destino");

        AddInlineLabel(
            root,
            "Linha",
            8,
            78);
        _routeLine.SetBounds(
            82, 74, 100, 28);

        AddInlineLabel(
            root,
            "Rota",
            200,
            78);
        _routeCode.SetBounds(
            255, 74, 100, 28);

        AddInlineLabel(
            root,
            "Destino",
            8,
            118);
        _routeDestination.SetBounds(
            82, 114, 273, 28);

        var publish =
            OmsiButton(
                "PUBLICAR OPERAÇÃO",
                166,
                (_, _) =>
                {
                    var line =
                        _routeLine.Text.Trim();
                    var route =
                        _routeCode.Text.Trim();
                    var destination =
                        _routeDestination.Text.Trim();

                    var text =
                        $"Linha {line} | Rota {route} | Destino {destination}";

                    RequestMessage(
                        "ROUTECORE",
                        "ROUTE",
                        text,
                        string.Empty);
                });

        publish.SetBounds(
            362, 112, 166, 32);
        _routeMessages.SetBounds(
            8, 164, 520, 150);

        root.Controls.Add(_routeLine);
        root.Controls.Add(_routeCode);
        root.Controls.Add(_routeDestination);
        root.Controls.Add(publish);
        root.Controls.Add(_routeMessages);
        _contentHost.Controls.Add(root);
    }

    private void ShowFleetCare()
    {
        _contentHost.Controls.Clear();

        var root =
            ModuleRoot(
                "FLEETCARE",
                "Saúde operacional do ônibus · somente dados expostos pelos scripts OMSI");

        _fleetCareState.SetBounds(
            8,
            74,
            520,
            150);

        var support =
            OmsiButton(
                "ENVIAR AO ASSISTLINK",
                190,
                (_, _) =>
                {
                    var summary =
                        _lastFleetCareState.FaultActive
                            ? _lastFleetCareState.FaultState
                            : "Solicitação preventiva de verificação do veículo.";

                    RequestMessage(
                        "ASSISTLINK",
                        "FLEETCARE",
                        summary,
                        "CONTROLHUB");
                });

        support.SetBounds(
            8,
            238,
            190,
            32);

        var note =
            new Label
            {
                AutoSize = false,
                Text =
                    "O FleetCare não altera física, desgaste ou scripts. Ele apenas lê telemetria real que o veículo disponibiliza.",
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f),
                TextAlign =
                    ContentAlignment.MiddleLeft
            };

        note.SetBounds(
            8,
            282,
            520,
            48);

        root.Controls.Add(
            _fleetCareState);
        root.Controls.Add(
            support);
        root.Controls.Add(
            note);

        _contentHost.Controls.Add(
            root);
    }

    private void ShowPassengerFlow()
    {
        _contentHost.Controls.Clear();

        var root =
            ModuleRoot(
                "PASSENGERFLOW",
                "Passageiros e acessibilidade · leitura dos estados OMSI do veículo");

        _passengerFlowState.SetBounds(
            8,
            74,
            520,
            150);

        var note =
            new Label
            {
                AutoSize = false,
                Text =
                    "Quando o ônibus não expõe contagem, rampa, kneeling ou cadeirante em suas variáveis de script, o valor permanece N/D.",
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f),
                TextAlign =
                    ContentAlignment.MiddleLeft
            };

        note.SetBounds(
            8,
            238,
            520,
            54);

        root.Controls.Add(
            _passengerFlowState);
        root.Controls.Add(
            note);

        _contentHost.Controls.Add(
            root);
    }

    private void ShowShiftFlow()
    {
        _contentHost.Controls.Clear();

        var root =
            ModuleRoot(
                "SHIFTFLOW",
                "Jornada do motorista · início, operação e encerramento");

        _shiftState.SetBounds(
            8,
            76,
            520,
            28);

        _shiftToggleButton.SetBounds(
            8,
            112,
            160,
            32);

        _shiftEvents.SetBounds(
            8,
            158,
            520,
            158);

        root.Controls.Add(
            _shiftState);
        root.Controls.Add(
            _shiftToggleButton);
        root.Controls.Add(
            _shiftEvents);

        _contentHost.Controls.Add(
            root);

        RefreshShiftState();
    }

    private void ToggleShift()
    {
        if (!_shiftActive)
        {
            if (!_badgeInserted &&
                _profile.RequireForStart)
            {
                _status.Text =
                    "SHIFTFLOW: insira o DriverPass antes de iniciar o turno.";
                _status.ForeColor =
                    Color.FromArgb(135, 35, 35);
                ShowDriverPass();
                return;
            }

            _shiftActive =
                true;
            _shiftStartedAt =
                DateTimeOffset.Now;

            AppendShiftEvent(
                $"Turno iniciado · {_profile.CompanyName} · {_profile.EmployeeNumber} · {_profile.DriverName}");

            RequestMessage(
                "FLEETLINK",
                "SHIFT",
                "Motorista iniciou o turno.",
                string.Empty);
        }
        else
        {
            var elapsed =
                _shiftStartedAt.HasValue
                    ? DateTimeOffset.Now -
                      _shiftStartedAt.Value
                    : TimeSpan.Zero;

            AppendShiftEvent(
                $"Turno encerrado · duração {elapsed:hh\\:mm\\:ss}");

            RequestMessage(
                "FLEETLINK",
                "SHIFT",
                $"Motorista encerrou o turno após {elapsed:hh\\:mm\\:ss}.",
                string.Empty);

            _shiftActive =
                false;
            _shiftStartedAt =
                null;
        }

        RefreshShiftState();
    }

    private void RefreshShiftState()
    {
        _shiftToggleButton.Text =
            _shiftActive
                ? "ENCERRAR TURNO"
                : "INICIAR TURNO";

        if (_shiftActive &&
            _shiftStartedAt is
                { } started)
        {
            var elapsed =
                DateTimeOffset.Now -
                started;

            _shiftState.Text =
                $"TURNO ATIVO · início {started:HH:mm} · duração {elapsed:hh\\:mm\\:ss}";
            _shiftState.ForeColor =
                Color.FromArgb(30, 105, 45);
        }
        else
        {
            _shiftState.Text =
                "TURNO ENCERRADO";
            _shiftState.ForeColor =
                Color.FromArgb(110, 45, 45);
        }
    }

    private void AppendShiftEvent(
        string text)
    {
        if (!_shiftActive &&
            !_shiftStartedAt.HasValue)
        {
            return;
        }

        AppendMessage(
            _shiftEvents,
            $"[{DateTime.Now:HH:mm:ss}] {text}");
    }

    private void ShowIncidentLog()
    {
        _contentHost.Controls.Clear();

        var root =
            ModuleRoot(
                "INCIDENTLOG",
                "Registro operacional · colisões, suporte e eventos do CCO");

        _incidentMessages.SetBounds(
            8,
            72,
            520,
            232);

        var clear =
            OmsiButton(
                "LIMPAR TELA",
                132,
                (_, _) =>
                    _incidentMessages.Items.Clear());

        clear.SetBounds(
            8,
            314,
            132,
            32);

        root.Controls.Add(
            _incidentMessages);
        root.Controls.Add(
            clear);

        _contentHost.Controls.Add(
            root);
    }

    private bool RequestMessage(
        string module,
        string kind,
        string text,
        string target)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _status.Text =
                $"{module}: informe uma mensagem.";
            _status.ForeColor =
                Color.FromArgb(110, 70, 30);
            return false;
        }

        var driver =
            _driverName.Text.Trim();
        var company =
            _companyName.Text.Trim();
        var employee =
            _employeeNumber.Text.Trim();

        if (string.IsNullOrWhiteSpace(driver) ||
            string.IsNullOrWhiteSpace(company) ||
            string.IsNullOrWhiteSpace(employee))
        {
            _status.Text =
                "DRIVERPASS: cadastre motorista, empresa e matrícula antes de usar a rede operacional.";
            _status.ForeColor =
                Color.FromArgb(135, 35, 35);
            ShowDriverPass();
            return false;
        }

        var request =
            new RuntimeDriveOpsMessageRequest(
                driver,
                company,
                employee,
                module,
                kind,
                text,
                target);

        AppendLocalMessage(
            request);

        if (request.Module.Equals(
                "ASSISTLINK",
                StringComparison.OrdinalIgnoreCase) &&
            request.Kind.Equals(
                "SUPPORT",
                StringComparison.OrdinalIgnoreCase))
        {
            RecordIncident(
                "SUPORTE",
                request.Text);
        }

        MessageRequested?.Invoke(
            request);

        return true;
    }

    private void AppendLocalMessage(
        RuntimeDriveOpsMessageRequest request)
    {
        var line =
            $"[{DateTime.Now:HH:mm}] Você: {request.Text}";

        switch (request.Module)
        {
            case "FLEETLINK":
            case "DRIVERPASS":
                AppendMessage(
                    _fleetMessages,
                    line);
                break;
            case "COMMSLINK":
                AppendMessage(
                    _commsMessages,
                    line);
                break;

            case "ASSISTLINK":
                AppendMessage(
                    _supportMessages,
                    line);
                break;
            case "CONTROLHUB":
                AppendMessage(
                    _controlMessages,
                    line);
                break;
            case "ROUTECORE":
                AppendMessage(
                    _routeMessages,
                    line);
                break;
        }
    }

    private static void AppendMessage(
        ListBox list,
        string text)
    {
        list.Items.Add(text);

        while (list.Items.Count > 100)
        {
            list.Items.RemoveAt(0);
        }

        if (list.Items.Count > 0)
        {
            list.TopIndex =
                list.Items.Count - 1;
        }
    }

    private bool HasControlHubPermission()
    {
        var role =
            _role.Text.Trim();

        return role.Contains(
                   "cco",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "dispatcher",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "supervisor",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "gerente",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "gestor",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "diretor",
                   StringComparison.OrdinalIgnoreCase) ||
               role.Contains(
                   "presidente",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void SaveBadge()
    {
        _profile =
            new DriverPassProfile
            {
                DriverName =
                    _driverName.Text.Trim(),
                CompanyName =
                    _companyName.Text.Trim(),
                EmployeeNumber =
                    _employeeNumber.Text.Trim(),
                Role =
                    string.IsNullOrWhiteSpace(
                        _role.Text)
                        ? "Motorista"
                        : _role.Text.Trim(),
                RequireForStart =
                    _requireForStart.Checked
            };

        SaveProfile(_profile);

        if (!_profile.RequireForStart)
        {
            _badgeInserted = true;
        }
        else if (!ProfileIsValid(_profile))
        {
            _badgeInserted = false;
        }

        RefreshDriverPassStatus();
    }

    private void ToggleBadge()
    {
        SaveBadge();

        if (!_profile.RequireForStart)
        {
            _badgeInserted = true;
            RefreshDriverPassStatus();
            return;
        }

        if (!ProfileIsValid(_profile))
        {
            _badgeInserted = false;
            _status.Text =
                "DRIVERPASS: preencha motorista, empresa e matrícula antes de inserir.";
            _status.ForeColor =
                Color.FromArgb(135, 35, 35);
            return;
        }

        _badgeInserted =
            !_badgeInserted;
        RefreshDriverPassStatus();

        AppendShiftEvent(
            _badgeInserted
                ? "DriverPass inserido."
                : "DriverPass retirado.");

        RequestMessage(
            "DRIVERPASS",
            "PRESENCE",
            _badgeInserted
                ? "Crachá inserido · motorista em operação."
                : "Crachá retirado · motorista fora de operação.",
            string.Empty);
    }

    private void RefreshDriverPassStatus()
    {
        _insertBadgeButton.Text =
            _badgeInserted
                ? "RETIRAR CRACHÁ"
                : "INSERIR CRACHÁ";

        if (!_profile.RequireForStart)
        {
            _status.Text =
                "DRIVERPASS: operação livre; autorização de partida não é exigida.";
            _status.ForeColor = OmsiText;
            return;
        }

        if (_badgeInserted)
        {
            _status.Text =
                $"DRIVERPASS: LIBERADO | {_profile.CompanyName} | {_profile.EmployeeNumber} | {_profile.DriverName}";
            _status.ForeColor =
                Color.FromArgb(30, 105, 45);
        }
        else
        {
            _status.Text =
                "DRIVERPASS: BLOQUEADO — insira o crachá para liberar elétrica/partida.";
            _status.ForeColor =
                Color.FromArgb(135, 35, 35);
        }
    }

    private static bool ProfileIsValid(
        DriverPassProfile profile) =>
        !string.IsNullOrWhiteSpace(
            profile.DriverName) &&
        !string.IsNullOrWhiteSpace(
            profile.CompanyName) &&
        !string.IsNullOrWhiteSpace(
            profile.EmployeeNumber);

    private static void AddHeading(
        TableLayoutPanel root,
        string title,
        string subtitle)
    {
        var panel =
            new Panel
            {
                AutoSize = false,
                Height = 52,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12)
            };

        panel.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = title,
                Font =
                    new Font(
                        "Segoe UI",
                        15.0f,
                        FontStyle.Bold),
                ForeColor = OmsiDark,
                Location = new Point(0, 0)
            });

        panel.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = subtitle,
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f),
                ForeColor = OmsiBlue,
                Location = new Point(0, 28)
            });

        root.Controls.Add(panel, 0, 0);
        root.SetColumnSpan(panel, 2);
    }

    private static void AddField(
        TableLayoutPanel root,
        int row,
        string name,
        Control control)
    {
        root.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = name,
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 7, 8, 7)
            },
            0,
            row);

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        root.Controls.Add(control, 1, row);
    }

    private static void AddInlineLabel(
        Control root,
        string text,
        int x,
        int y)
    {
        root.Controls.Add(
            new Label
            {
                AutoSize = true,
                Text = text,
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                Location = new Point(x, y)
            });
    }

    private static TextBox OmsiTextBox(
        string text) =>
        new()
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = OmsiText,
            Font = new Font("Segoe UI", 9.0f)
        };

    private static ListBox OmsiListBox() =>
        new()
        {
            BackColor =
                Color.FromArgb(238, 241, 244),
            ForeColor = OmsiText,
            BorderStyle = BorderStyle.FixedSingle,
            Font =
                new Font(
                    "Consolas",
                    8.5f)
        };

    private static Label OmsiTelemetryLabel(
        string text) =>
        new()
        {
            AutoSize = false,
            Text = text,
            BackColor =
                Color.FromArgb(
                    238,
                    241,
                    244),
            ForeColor = OmsiText,
            BorderStyle =
                BorderStyle.FixedSingle,
            Font =
                new Font(
                    "Consolas",
                    9.0f,
                    FontStyle.Bold),
            Padding =
                new Padding(
                    10),
            TextAlign =
                ContentAlignment.TopLeft
        };

    private static ComboBox OmsiComboBox() =>
        new()
        {
            DropDownStyle =
                ComboBoxStyle.DropDownList,
            BackColor = Color.White,
            ForeColor = OmsiText,
            Font = new Font("Segoe UI", 9.0f)
        };

    private static Button OmsiButton(
        string text,
        int width,
        EventHandler handler)
    {
        var button =
            new Button
            {
                Text = text,
                Width = width,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(238, 241, 244),
                ForeColor = OmsiText,
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                Cursor = Cursors.Hand
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(140, 148, 157);
        button.Click += handler;
        return button;
    }

    private static DriverPassProfile LoadProfile()
    {
        try
        {
            var path = ProfilePath();

            if (!File.Exists(path))
            {
                return new DriverPassProfile();
            }

            return JsonSerializer.Deserialize<
                       DriverPassProfile>(
                       File.ReadAllText(path)) ??
                   new DriverPassProfile();
        }
        catch
        {
            return new DriverPassProfile();
        }
    }

    private static void SaveProfile(
        DriverPassProfile profile)
    {
        try
        {
            var path = ProfilePath();

            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    profile,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
        }
        catch
        {
        }
    }

    private void LoadIncidentHistory()
    {
        try
        {
            var path =
                IncidentLogPath();

            if (!File.Exists(path))
            {
                return;
            }

            foreach (var line in
                     File.ReadLines(path)
                         .TakeLast(100))
            {
                AppendMessage(
                    _incidentMessages,
                    line);
            }
        }
        catch
        {
        }
    }

    private static string IncidentLogPath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI Compatible Runtime",
            "incidentlog.log");

    private static string ProfilePath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OMSI Compatible Runtime",
            "driverpass.json");
}
