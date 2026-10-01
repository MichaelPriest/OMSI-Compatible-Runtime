using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// OMSI-style in-game control editor. It edits the native OMSI keyboard.cfg
/// rather than introducing a parallel input mapping layer.
/// </summary>
internal sealed class RuntimeVehiclePanel : Panel
{
    private sealed record CommandRow(
        RuntimeOmsiEditableKeyboardEntry Entry,
        string Label)
    {
        public override string ToString() =>
            Label;
    }

    private sealed record SystemCategory(
        string Name,
        string[] Tokens);

    private static readonly SystemCategory[] Categories =
    [
        new(
            "MOTOR / ELÉTRICA",
            ["engine", "motor", "batter", "electric", "starter", "ignition", "enginestart"]),
        new(
            "PORTAS",
            ["door", "tuer", "tür", "frontdoor", "backdoor"]),
        new(
            "LUZES / SETAS",
            ["light", "licht", "blink", "indicator", "hazard", "fernlicht", "fog"]),
        new(
            "LIMPADORES",
            ["wiper", "wipe", "wisch", "washer"]),
        new(
            "FREIOS / CÂMBIO",
            ["brake", "parking", "retarder", "automatic_", "gear", "clutch", "bremse"]),
        new(
            "VOLANTE / BUZINA",
            ["steer", "steering", "horn", "hupe"]),
        new(
            "IBIS / BILHETAGEM",
            ["ibis", "ticket", "fahrschein", "cash", "printer", "ticketing"]),
        new(
            "CÂMERAS / ESPELHOS",
            ["view_", "camera", "mirror", "sicht"]),
        new(
            "SUSPENSÃO / RAMPA",
            ["kneel", "suspension", "ramp", "wheelchair", "lift", "niveau"]),
        new(
            "OUTROS",
            [])
    ];

    private readonly string _contentRoot;
    private readonly string? _inputLanguage;
    private readonly Panel _cockpit;
    private readonly FlowLayoutPanel _systems;
    private readonly ListBox _commands;
    private readonly Label _selected;
    private readonly Label _status;
    private readonly CheckBox _continuous;
    private readonly CheckBox _shift;
    private readonly CheckBox _control;
    private readonly Button _capture;
    private string _activeCategory =
        Categories[0].Name;
    private string? _captureTrigger;

    public RuntimeVehiclePanel(
        string contentRoot,
        string? inputLanguage)
    {
        _contentRoot =
            contentRoot;
        _inputLanguage =
            inputLanguage;

        Visible =
            false;
        Width =
            900;
        Height =
            590;
        BackColor =
            Color.FromArgb(
                220,
                224,
                229);
        BorderStyle =
            BorderStyle.FixedSingle;
        TabStop =
            true;

        var header =
            new Panel
            {
                Dock =
                    DockStyle.Top,
                Height =
                    48,
                BackColor =
                    Color.FromArgb(
                        54,
                        62,
                        72)
            };

        header.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Left =
                    14,
                Top =
                    8,
                Text =
                    "VEHICLEPANEL  |  CONTROLES DO ÔNIBUS",
                ForeColor =
                    Color.White,
                Font =
                    new Font(
                        "Segoe UI",
                        14.0f,
                        FontStyle.Bold)
            });

        var close =
            OmsiButton(
                "X",
                (_, _) =>
                    HidePanel());

        close.Width =
            38;
        close.Height =
            30;
        close.Left =
            Width -
            52;
        close.Top =
            8;
        close.Anchor =
            AnchorStyles.Top |
            AnchorStyles.Right;

        header.Controls.Add(
            close);
        Controls.Add(
            header);

        _cockpit =
            new Panel
            {
                Left =
                    14,
                Top =
                    60,
                Width =
                    872,
                Height =
                    155,
                BackColor =
                    Color.FromArgb(
                        195,
                        201,
                        208)
            };

        _cockpit.Paint +=
            PaintCockpit;

        Controls.Add(
            _cockpit);

        _systems =
            new FlowLayoutPanel
            {
                Left =
                    22,
                Top =
                    70,
                Width =
                    856,
                Height =
                    135,
                BackColor =
                    Color.Transparent,
                FlowDirection =
                    FlowDirection.LeftToRight,
                WrapContents =
                    true
            };

        foreach (var category in
                 Categories)
        {
            var local =
                category;

            var button =
                OmsiButton(
                    local.Name,
                    (_, _) =>
                        SelectCategory(
                            local.Name));

            button.Width =
                158;
            button.Height =
                36;
            button.Margin =
                new Padding(
                    5);

            _systems.Controls.Add(
                button);
        }

        _cockpit.Controls.Add(
            _systems);

        var left =
            new GroupBox
            {
                Left =
                    14,
                Top =
                    225,
                Width =
                    530,
                Height =
                    350,
                Text =
                    "Comandos OMSI",
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold)
            };

        _commands =
            new ListBox
            {
                Left =
                    12,
                Top =
                    24,
                Width =
                    506,
                Height =
                    312,
                IntegralHeight =
                    false,
                Font =
                    new Font(
                        "Consolas",
                        9.0f),
                BackColor =
                    Color.FromArgb(
                        246,
                        247,
                        249)
            };

        _commands.SelectedIndexChanged +=
            (_, _) =>
                UpdateSelection();

        left.Controls.Add(
            _commands);
        Controls.Add(
            left);

        var right =
            new GroupBox
            {
                Left =
                    554,
                Top =
                    225,
                Width =
                    332,
                Height =
                    350,
                Text =
                    "Editar atribuição",
                Font =
                    new Font(
                        "Segoe UI",
                        9.0f,
                        FontStyle.Bold)
            };

        _selected =
            new Label
            {
                Left =
                    12,
                Top =
                    28,
                Width =
                    306,
                Height =
                    58,
                Text =
                    "Selecione um comando.",
                ForeColor =
                    Color.FromArgb(
                        35,
                        39,
                        45)
            };

        _continuous =
            OmsiCheck(
                "Contínuo");
        _continuous.SetBounds(
            12,
            92,
            120,
            24);

        _shift =
            OmsiCheck(
                "Shift");
        _shift.SetBounds(
            12,
            120,
            120,
            24);

        _control =
            OmsiCheck(
                "Ctrl");
        _control.SetBounds(
            12,
            148,
            120,
            24);

        _capture =
            OmsiButton(
                "ATRIBUIR PRÓXIMA TECLA",
                (_, _) =>
                    BeginCapture());

        _capture.SetBounds(
            12,
            184,
            306,
            38);

        var remove =
            OmsiButton(
                "REMOVER TECLA",
                (_, _) =>
                    RemoveBinding());

        remove.SetBounds(
            12,
            228,
            306,
            34);

        _status =
            new Label
            {
                Left =
                    12,
                Top =
                    270,
                Width =
                    306,
                Height =
                    65,
                Text =
                    "O arquivo original recebe backup automático (.vehiclepanel.bak).",
                ForeColor =
                    Color.FromArgb(
                        73,
                        78,
                        86)
            };

        right.Controls.Add(
            _selected);
        right.Controls.Add(
            _continuous);
        right.Controls.Add(
            _shift);
        right.Controls.Add(
            _control);
        right.Controls.Add(
            _capture);
        right.Controls.Add(
            remove);
        right.Controls.Add(
            _status);

        Controls.Add(
            right);

        RefreshBindings();
    }

    public event Action?
        BindingsChanged;

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
        Visible =
            true;
        BringToFront();
        Focus();
        RefreshBindings();
    }

    public void HidePanel()
    {
        _captureTrigger =
            null;
        _capture.Text =
            "ATRIBUIR PRÓXIMA TECLA";
        Visible =
            false;
    }

    public bool TryCaptureKey(
        Keys key,
        bool shift,
        bool control)
    {
        if (!Visible ||
            string.IsNullOrWhiteSpace(
                _captureTrigger))
        {
            return false;
        }

        if (key is
            Keys.ShiftKey or
            Keys.ControlKey or
            Keys.Menu)
        {
            return true;
        }

        var trigger =
            _captureTrigger;

        _captureTrigger =
            null;
        _capture.Text =
            "ATRIBUIR PRÓXIMA TECLA";

        var saved =
            RuntimeOmsiKeyboardBindings.TryAssign(
                _contentRoot,
                _inputLanguage,
                trigger,
                key,
                _continuous.Checked,
                shift,
                control,
                out var message);

        _status.Text =
            message;

        if (saved)
        {
            BindingsChanged?.Invoke();
            RefreshBindings(
                trigger);
        }

        return true;
    }

    private void SelectCategory(
        string name)
    {
        _activeCategory =
            name;
        RefreshBindings();
    }

    private void RefreshBindings(
        string? selectTrigger = null)
    {
        var entries =
            RuntimeOmsiKeyboardBindings.LoadEditable(
                _contentRoot,
                _inputLanguage);

        _commands.BeginUpdate();
        _commands.Items.Clear();

        var category =
            Categories.First(
                item =>
                    item.Name ==
                    _activeCategory);

        foreach (var entry in
                 entries
                     .Where(
                         item =>
                             MatchesCategory(
                                 item.Trigger,
                                 category))
                     .OrderBy(
                         static item =>
                             item.Trigger,
                         StringComparer.OrdinalIgnoreCase))
        {
            var key =
                FormatBinding(
                    entry);

            _commands.Items.Add(
                new CommandRow(
                    entry,
                    $"{FriendlyTrigger(entry.Trigger),-30} {key}"));
        }

        _commands.EndUpdate();

        if (!string.IsNullOrWhiteSpace(
                selectTrigger))
        {
            for (var index = 0;
                 index < _commands.Items.Count;
                 index++)
            {
                if (_commands.Items[index] is
                        CommandRow row &&
                    row.Entry.Trigger.Equals(
                        selectTrigger,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _commands.SelectedIndex =
                        index;
                    return;
                }
            }
        }

        if (_commands.Items.Count >
            0)
        {
            _commands.SelectedIndex =
                0;
        }
        else
        {
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        if (_commands.SelectedItem is
            not CommandRow row)
        {
            _selected.Text =
                "Nenhum comando nesta área.";
            _continuous.Checked =
                false;
            _shift.Checked =
                false;
            _control.Checked =
                false;
            return;
        }

        _selected.Text =
            $"{FriendlyTrigger(row.Entry.Trigger)}\n{row.Entry.Trigger}\nAtual: {FormatBinding(row.Entry)}";

        _continuous.Checked =
            row.Entry.Continuous;
        _shift.Checked =
            row.Entry.Shift;
        _control.Checked =
            row.Entry.Control;
    }

    private void BeginCapture()
    {
        if (_commands.SelectedItem is
            not CommandRow row)
        {
            _status.Text =
                "Selecione um comando antes de atribuir a tecla.";
            return;
        }

        _captureTrigger =
            row.Entry.Trigger;
        _capture.Text =
            "PRESSIONE A TECLA...";
        _status.Text =
            "Aguardando teclado. Shift/Ctrl serão detectados automaticamente.";
        Focus();
    }

    private void RemoveBinding()
    {
        if (_commands.SelectedItem is
            not CommandRow row)
        {
            return;
        }

        if (RuntimeOmsiKeyboardBindings.TryAssign(
                _contentRoot,
                _inputLanguage,
                row.Entry.Trigger,
                Keys.None,
                row.Entry.Continuous,
                shift:
                    false,
                control:
                    false,
                out var message))
        {
            _status.Text =
                message;
            BindingsChanged?.Invoke();
            RefreshBindings(
                row.Entry.Trigger);
        }
        else
        {
            _status.Text =
                message;
        }
    }

    private static bool MatchesCategory(
        string trigger,
        SystemCategory category)
    {
        if (category.Tokens.Length ==
            0)
        {
            return !Categories
                .Where(
                    item =>
                        item.Tokens.Length >
                        0)
                .Any(
                    item =>
                        item.Tokens.Any(
                            token =>
                                trigger.Contains(
                                    token,
                                    StringComparison.OrdinalIgnoreCase)));
        }

        return category.Tokens.Any(
            token =>
                trigger.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string FriendlyTrigger(
        string trigger)
    {
        var value =
            trigger
                .Replace(
                    "kw_",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "cp_",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    '_',
                    ' ')
                .Trim();

        if (value.Length ==
            0)
        {
            return trigger;
        }

        return char.ToUpperInvariant(
                   value[0]) +
               value[1..];
    }

    private static string FormatBinding(
        RuntimeOmsiEditableKeyboardEntry entry)
    {
        if (!entry.Key.HasValue)
        {
            return "<sem tecla>";
        }

        return
            (entry.Control
                ? "Ctrl+"
                : string.Empty) +
            (entry.Shift
                ? "Shift+"
                : string.Empty) +
            entry.Key.Value;
    }

    private void PaintCockpit(
        object? sender,
        PaintEventArgs e)
    {
        var graphics =
            e.Graphics;

        using var dark =
            new Pen(
                Color.FromArgb(
                    78,
                    85,
                    94),
                2.0f);

        using var light =
            new Pen(
                Color.FromArgb(
                    227,
                    230,
                    234),
                1.0f);

        graphics.DrawRectangle(
            dark,
            8,
            8,
            _cockpit.Width - 17,
            _cockpit.Height - 17);

        graphics.DrawArc(
            dark,
            24,
            54,
            120,
            120,
            180,
            180);

        graphics.DrawEllipse(
            dark,
            50,
            74,
            68,
            68);

        graphics.DrawRectangle(
            dark,
            340,
            18,
            190,
            42);

        graphics.DrawLine(
            light,
            340,
            66,
            530,
            66);
    }

    private static Button OmsiButton(
        string text,
        EventHandler handler)
    {
        var button =
            new Button
            {
                Text =
                    text,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        238,
                        240,
                        243),
                ForeColor =
                    Color.FromArgb(
                        31,
                        36,
                        43),
                Font =
                    new Font(
                        "Segoe UI",
                        8.5f,
                        FontStyle.Bold),
                Cursor =
                    Cursors.Hand
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                145,
                151,
                160);
        button.FlatAppearance.BorderSize =
            1;
        button.Click +=
            handler;

        return button;
    }

    private static CheckBox OmsiCheck(
        string text) =>
        new()
        {
            Text =
                text,
            AutoSize =
                false,
            ForeColor =
                Color.FromArgb(
                    31,
                    36,
                    43)
        };
}
