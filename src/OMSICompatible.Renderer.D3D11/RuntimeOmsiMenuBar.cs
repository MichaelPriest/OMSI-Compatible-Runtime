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
                238,
                214,
                218,
                224);
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
                        0)
            };

        Controls.Add(
            flow);

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
        var button =
            new Button
            {
                Text =
                    text,
                Width =
                    44,
                Height =
                    42,
                Margin =
                    new Padding(
                        2),
                Padding =
                    Padding.Empty,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    implemented
                        ? Color.FromArgb(
                            244,
                            246,
                            249)
                        : Color.FromArgb(
                            226,
                            229,
                            234),
                ForeColor =
                    implemented
                        ? Color.FromArgb(
                            30,
                            35,
                            43)
                        : Color.FromArgb(
                            138,
                            143,
                            151),
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
                    165,
                    171,
                    181)
                : Color.FromArgb(
                    198,
                    202,
                    209);

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

        parent.Controls.Add(
            button);
    }

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
                    198,
                    225,
                    246)
                : Color.FromArgb(
                    244,
                    246,
                    249);

        button.FlatAppearance.BorderColor =
            active
                ? Color.FromArgb(
                    65,
                    124,
                    171)
                : Color.FromArgb(
                    165,
                    171,
                    181);
    }
}
