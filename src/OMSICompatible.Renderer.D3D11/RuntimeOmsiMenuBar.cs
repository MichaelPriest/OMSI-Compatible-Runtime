using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

internal enum RuntimeOmsiMenuCommand
{
    Close,
    StartMenu,
    SaveSituation,
    NewBus,
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
    Pause,
    DriverView,
    PassengerView,
    ExteriorView,
    FreeMapCamera,
    MouseSteering,
    GameController,
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
                226,
                24,
                28,
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
                        0)
            };

        Controls.Add(
            flow);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Close,
            "▼",
            "Fechar menu do OMSI",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.StartMenu,
            "MENU",
            "Menu principal / voltar ao launcher",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.SaveSituation,
            "SAVE",
            "Salvar situação",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.NewBus,
            "BUS+",
            "Adicionar novo ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.FindBus,
            "BUS?",
            "Localizar / assumir ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.RepositionBus,
            "POS",
            "Reposicionar veículo",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.RouteDestination,
            "RTE",
            "Rota e destino",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Personnel,
            "DRV",
            "Arquivo do motorista",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Schedule,
            "TT",
            "Horário / escala",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.TimeDate,
            "TIME",
            "Data e hora",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Weather,
            "WX",
            "Clima",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Options,
            "OPT",
            "Opções",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Repair,
            "FIX",
            "Reparar ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Wash,
            "WASH",
            "Lavar ônibus",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Refuel,
            "FUEL",
            "Abastecer",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.DirectionSigns,
            "→?",
            "Setas / ajuda de rota",
            false);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.Pause,
            "Ⅱ",
            "Pausar / continuar simulação",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.DriverView,
            "F1",
            "Visão do motorista",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.PassengerView,
            "F2",
            "Visão de passageiro",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.ExteriorView,
            "F3",
            "Visão externa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.FreeMapCamera,
            "F4",
            "Câmera livre / mapa",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.MouseSteering,
            "MOUSE",
            "Direção pelo mouse (O)",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.GameController,
            "CTRL",
            "Volante / game controller (K)",
            true);

        AddButton(
            flow,
            RuntimeOmsiMenuCommand.ResetVehicle,
            "RESET",
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
                    text.Length >= 5
                        ? 58
                        : text.Length >= 4
                            ? 50
                            : 42,
                Height =
                    36,
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
                            48,
                            54,
                            64)
                        : Color.FromArgb(
                            42,
                            44,
                            49),
                ForeColor =
                    implemented
                        ? Color.White
                        : Color.FromArgb(
                            125,
                            130,
                            138),
                Font =
                    new Font(
                        "Segoe UI",
                        8.0f,
                        FontStyle.Bold),
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
                    88,
                    96,
                    110)
                : Color.FromArgb(
                    60,
                    63,
                    70);

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
                    35,
                    112,
                    72)
                : Color.FromArgb(
                    48,
                    54,
                    64);

        button.FlatAppearance.BorderColor =
            active
                ? Color.FromArgb(
                    92,
                    190,
                    132)
                : Color.FromArgb(
                    88,
                    96,
                    110);
    }
}
