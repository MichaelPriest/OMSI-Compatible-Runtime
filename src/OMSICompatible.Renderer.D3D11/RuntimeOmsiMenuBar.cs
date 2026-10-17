using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeOmsiMenuCommand
{
    Close,
    StartMenu,
    SaveSituation,
    NewBus,
    RemoveBus,
    FindBus,
    RepositionBus,
    RouteDestination,
    Personnel,
    Schedule,
    TimeDate,
    Weather,
    Options,
    Repair,
    Wash,
    Refuel,
    DirectionSigns,
    LiveBoard,
    MiniMap,
    FullMap,
    GroundArrows,
    PerformanceCenter,
    ReplayOps,
    Pause,
    DriverView,
    PassengerView,
    ExteriorView,
    FreeMapCamera,
    MouseSteering,
    GameController,
    VehiclePanel,
    ResetVehicle
}

internal sealed class RuntimeOmsiMenuBar : Panel
{
    private readonly ToolTip _toolTip =
        new();

    private readonly Dictionary<
        RuntimeOmsiMenuCommand,
        Button> _buttons =
            [];

    public RuntimeOmsiMenuBar()
    {
        Visible =
            false;
        AutoSize =
            true;
        AutoSizeMode =
            AutoSizeMode.GrowAndShrink;
        BackColor =
            Color.FromArgb(
                22,
                27,
                34);
        Padding =
            new Padding(
                5);
        Margin =
            Padding.Empty;
        TabStop =
            false;

        var flow =
            new FlowLayoutPanel
            {
                AutoSize =
                    true,
                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,
                FlowDirection =
                    FlowDirection.LeftToRight,
                WrapContents =
                    true,
                BackColor =
                    Color.Transparent,
                Margin =
                    Padding.Empty,
                Padding =
                    Padding.Empty,
                MaximumSize =
                    new Size(
                        780,
                        0),
                MinimumSize =
                    new Size(
                        760,
                        0)
            };

        var header =
            new Panel
            {
                Width = 760,
                Height = 54,
                Margin = new Padding(0, 0, 0, 6),
                BackColor = Color.FromArgb(17, 24, 33)
            };

        var accent =
            new Panel
            {
                Dock = DockStyle.Left,
                Width = 4,
                BackColor = Color.FromArgb(56, 189, 248)
            };
        header.Controls.Add(accent);

        header.Controls.Add(
            new Label
            {
                AutoSize = true,
                Left = 14,
                Top = 6,
                Text = "CENTRAL DE JOGO",
                ForeColor = Color.FromArgb(241, 245, 249),
                Font = new Font("Segoe UI Semibold", 12.0f, FontStyle.Bold)
            });

        header.Controls.Add(
            new Label
            {
                AutoSize = true,
                Left = 15,
                Top = 29,
                Text = "CONTROLES RÁPIDOS  •  SIMULAÇÃO  •  NAVEGAÇÃO",
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Regular)
            });

        var closeMenu =
            new Button
            {
                Text = "×",
                Width = 34,
                Height = 32,
                Left = 716,
                Top = 10,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 54),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Segoe UI", 15.0f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
        closeMenu.FlatAppearance.BorderSize = 0;
        closeMenu.FlatAppearance.MouseOverBackColor = Color.FromArgb(127, 29, 29);
        closeMenu.Click += (_, _) => HideMenu();
        _toolTip.SetToolTip(closeMenu, "Fechar painel (Esc)");
        header.Controls.Add(closeMenu);

        flow.Controls.Add(header);
        Controls.Add(flow);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Close,
            "\uE70E",
            "Fechar menu do OMSI",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.StartMenu,
            "\uE700",
            "Menu principal / voltar ao launcher",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.SaveSituation,
            "\uE74E",
            "Salvar situação",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.NewBus,
            "\uE710",
            "Iniciar outro ônibus",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.RemoveBus,
            "\uE74D",
            "Remover ônibus atual do mapa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.FindBus,
            "\uE721",
            "Localizar / assumir ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.RepositionBus,
            "\uE707",
            "Reposicionar veículo",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.RouteDestination,
            "\uE8F1",
            "Selecionar rapidamente linha, rota e destino do mapa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Personnel,
            "\uE77B",
            "DriveOps — DriverPass, FleetLink, AssistLink, ControlHub e RouteCore",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Schedule,
            "\uE787",
            "Horário / escala",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.TimeDate,
            "\uE823",
            "Data e hora",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Weather,
            "\uE706",
            "Clima",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Options,
            "\uE713",
            "Telas e CCO — ativar módulos e ajustar transparência",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Repair,
            "\uE90F",
            "Reparar ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Wash,
            "\uE790",
            "Lavar ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Refuel,
            "\uE7A5",
            "Abastecer",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.DirectionSigns,
            "\uE72A",
            "NavPulse — GPS/minimapa e FuelTrack",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.LiveBoard,
            "\uE9D2",
            "LiveBoard — HUD opcional (ativar/desativar)",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.MiniMap,
            "\uE80F",
            "Mini mapa opcional — ruas, rota e tráfego",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.FullMap,
            "\uE774",
            "Mapa ampliado — abrir/fechar via ESC",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.GroundArrows,
            "\uE72A",
            "Setas opcionais sobre a rota nas ruas — estilo Forza",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.PerformanceCenter,
            "\uE9D9",
            "Performance Center — FPS, CPU, GPU, draw calls e streaming",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.ReplayOps,
            "\uE81C",
            "ReplayOps — rever os últimos minutos e ocorrências",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Pause,
            "\uE769",
            "Pausar / continuar simulação",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.DriverView,
            "\uE890",
            "Visão do motorista",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.PassengerView,
            "\uE8B0",
            "Visão de passageiro",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.ExteriorView,
            "\uE714",
            "Visão externa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.FreeMapCamera,
            "\uE8B7",
            "Câmera livre / mapa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.MouseSteering,
            "\uE962",
            "Direção pelo mouse (O)",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.GameController,
            "\uE7FC",
            "Volante / game controller (K)",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.VehiclePanel,
            "\uE713",
            "VehiclePanel — configurar teclado e comandos do ônibus",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.ResetVehicle,
            "\uE777",
            "Reposicionar veículo no ponto atual de spawn",
            true);
    }

    public event Action<RuntimeOmsiMenuCommand>?
        CommandInvoked;

    public void ToggleMenu()
    {
        Visible =
            !Visible;

        if (Visible)
        {
            BringToFront();
        }
    }

    public void ShowMenu()
    {
        Visible =
            true;
        BringToFront();
    }

    public void HideMenu()
    {
        Visible =
            false;
    }

    public void SetPaused(
        bool paused)
    {
        SetButtonActive(
            RuntimeOmsiMenuCommand.Pause,
            paused);
    }

    public void SetMouseSteeringActive(
        bool active)
    {
        SetButtonActive(
            RuntimeOmsiMenuCommand.MouseSteering,
            active);
    }

    public void SetControllerActive(
        bool active)
    {
        SetButtonActive(
            RuntimeOmsiMenuCommand.GameController,
            active);
    }

    public void SetNavigationOverlayState(
        bool miniMapEnabled,
        bool fullMapOpen,
        bool liveBoardEnabled,
        bool groundArrowsEnabled)
    {
        SetButtonActive(RuntimeOmsiMenuCommand.MiniMap, miniMapEnabled);
        SetButtonActive(RuntimeOmsiMenuCommand.FullMap, fullMapOpen);
        SetButtonActive(RuntimeOmsiMenuCommand.LiveBoard, liveBoardEnabled);
        SetButtonActive(RuntimeOmsiMenuCommand.GroundArrows, groundArrowsEnabled);
    }

    private void AddButton(
        Control parent,
        RuntimeOmsiMenuCommand command,
        string text,
        string toolTip,
        bool implemented)
    {
        var tile =
            new Panel
            {
                Width = 70,
                Height = 68,
                Margin = new Padding(2),
                Padding = new Padding(3),
                BackColor = Color.FromArgb(28, 35, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = implemented ? Cursors.Hand : Cursors.Default
            };

        var button =
            new Button
            {
                Text =
                    text,
                Dock =
                    DockStyle.Top,
                Height =
                    40,
                Margin =
                    Padding.Empty,
                Padding =
                    Padding.Empty,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    implemented
                        ? Color.FromArgb(
                            35,
                            43,
                            54)
                        : Color.FromArgb(
                            27,
                            32,
                            40),
                ForeColor =
                    implemented
                        ? Color.FromArgb(
                            230,
                            237,
                            243)
                        : Color.FromArgb(
                            111,
                            122,
                            136),
                Font =
                    new Font(
                        "Segoe MDL2 Assets",
                        14.0f,
                        FontStyle.Regular),
                Cursor =
                    implemented
                        ? Cursors.Hand
                        : Cursors.Default,
                TabStop =
                    false,
                Enabled =
                    implemented
            };

        button.FlatAppearance.BorderSize =
            1;
        button.FlatAppearance.BorderColor =
            implemented
                ? Color.FromArgb(
                    75,
                    88,
                    104)
                : Color.FromArgb(
                    55,
                    62,
                    73);
        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(
                52,
                65,
                82);
        button.FlatAppearance.BorderSize = 0;

        var caption =
            new Label
            {
                Dock = DockStyle.Bottom,
                Height = 19,
                Text = ShortLabel(command),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                ForeColor = implemented
                    ? Color.FromArgb(203, 213, 225)
                    : Color.FromArgb(100, 116, 139),
                Font = new Font("Segoe UI", 7.0f, FontStyle.Regular),
                BackColor = Color.Transparent,
                Cursor = implemented ? Cursors.Hand : Cursors.Default
            };

        void Highlight(bool hovered)
        {
            tile.BackColor = hovered
                ? Color.FromArgb(40, 55, 70)
                : Color.FromArgb(28, 35, 45);
            tile.BorderStyle = hovered
                ? BorderStyle.Fixed3D
                : BorderStyle.FixedSingle;
        }

        tile.MouseEnter += (_, _) => Highlight(true);
        tile.MouseLeave += (_, _) => Highlight(false);
        caption.MouseEnter += (_, _) => Highlight(true);
        caption.MouseLeave += (_, _) => Highlight(false);
        caption.Click += (_, _) =>
        {
            if (implemented)
            {
                CommandInvoked?.Invoke(command);
            }
        };

        button.Click +=
            (_, _) =>
            {
                CommandInvoked?.Invoke(
                    command);
            };

        _toolTip.SetToolTip(
            button,
            implemented
                ? toolTip
                : toolTip +
                  " — em desenvolvimento");

        _buttons[
            command] =
            button;

        button.Dock = DockStyle.Top;
        tile.Controls.Add(caption);
        tile.Controls.Add(button);
        parent.Controls.Add(tile);
    }

    private static string ShortLabel(RuntimeOmsiMenuCommand command) =>
        command switch
        {
            RuntimeOmsiMenuCommand.Close => "Fechar",
            RuntimeOmsiMenuCommand.StartMenu => "Início",
            RuntimeOmsiMenuCommand.SaveSituation => "Salvar",
            RuntimeOmsiMenuCommand.NewBus => "Novo ônibus",
            RuntimeOmsiMenuCommand.RemoveBus => "Remover",
            RuntimeOmsiMenuCommand.FindBus => "Localizar",
            RuntimeOmsiMenuCommand.RepositionBus => "Reposicionar",
            RuntimeOmsiMenuCommand.RouteDestination => "Linha / rota",
            RuntimeOmsiMenuCommand.Personnel => "DriveOps",
            RuntimeOmsiMenuCommand.Schedule => "Escala",
            RuntimeOmsiMenuCommand.TimeDate => "Data / hora",
            RuntimeOmsiMenuCommand.Weather => "Clima",
            RuntimeOmsiMenuCommand.Options => "Opções",
            RuntimeOmsiMenuCommand.Repair => "Reparar",
            RuntimeOmsiMenuCommand.Wash => "Lavar",
            RuntimeOmsiMenuCommand.Refuel => "Abastecer",
            RuntimeOmsiMenuCommand.DirectionSigns => "Navegação",
            RuntimeOmsiMenuCommand.LiveBoard => "HUD",
            RuntimeOmsiMenuCommand.MiniMap => "Minimapa",
            RuntimeOmsiMenuCommand.FullMap => "Mapa",
            RuntimeOmsiMenuCommand.GroundArrows => "Setas",
            RuntimeOmsiMenuCommand.PerformanceCenter => "Desempenho",
            RuntimeOmsiMenuCommand.ReplayOps => "Replay",
            RuntimeOmsiMenuCommand.Pause => "Pausar",
            RuntimeOmsiMenuCommand.DriverView => "Motorista",
            RuntimeOmsiMenuCommand.PassengerView => "Passageiro",
            RuntimeOmsiMenuCommand.ExteriorView => "Externa",
            RuntimeOmsiMenuCommand.FreeMapCamera => "Câmera livre",
            RuntimeOmsiMenuCommand.MouseSteering => "Mouse",
            RuntimeOmsiMenuCommand.GameController => "Controle",
            RuntimeOmsiMenuCommand.VehiclePanel => "Comandos",
            RuntimeOmsiMenuCommand.ResetVehicle => "Reset ônibus",
            _ => command.ToString()
        };

    private void SetButtonActive(
        RuntimeOmsiMenuCommand command,
        bool active)
    {
        if (!_buttons.TryGetValue(
                command,
                out var button))
        {
            return;
        }

        button.BackColor =
            active
                ? Color.FromArgb(
                    22,
                    59,
                    82)
                : Color.FromArgb(
                    35,
                    43,
                    54);

        button.FlatAppearance.BorderColor =
            active
                ? Color.FromArgb(
                    56,
                    189,
                    248)
                : Color.FromArgb(
                    75,
                    88,
                    104);
    }
}
