namespace OMSICompatible.Launcher;

internal sealed class OmsiKeyboardSettingsPanel :
    UserControl
{
    private readonly RichTextBox _editor;
    private readonly DataGridView _grid =
        new();
    private readonly TextBox _search =
        new();
    private readonly TextBox _newEvent =
        new();
    private bool _loading;

    public OmsiKeyboardSettingsPanel(
        RichTextBox editor)
    {
        _editor =
            editor;

        Dock =
            DockStyle.Fill;

        BuildInterface();
        Reload();
    }

    public void Reload()
    {
        _loading =
            true;

        try
        {
            var selectedOrdinal =
                SelectedBinding()
                    ?.Ordinal;

            var filter =
                _search.Text
                    .Trim();

            var bindings =
                OmsiInputConfigEditor
                    .ParseKeyboard(
                        _editor.Text)
                    .Where(
                        binding =>
                            filter.Length == 0 ||
                            binding.EventName
                                .Contains(
                                    filter,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            _grid.Rows.Clear();

            foreach (var binding in
                     bindings)
            {
                var rowIndex =
                    _grid.Rows.Add(
                        binding.EventName,
                        OmsiInputConfigEditor
                            .KeyboardScanCodeName(
                                binding.ScanCode),
                        binding.Control,
                        binding.Shift,
                        binding.Continuous);

                _grid.Rows[
                    rowIndex].Tag =
                    binding;
            }

            if (selectedOrdinal.HasValue)
            {
                foreach (DataGridViewRow row in
                         _grid.Rows)
                {
                    if (row.Tag is
                            OmsiKeyboardBinding binding &&
                        binding.Ordinal ==
                            selectedOrdinal.Value)
                    {
                        row.Selected =
                            true;

                        if (row.Cells.Count >
                            0)
                        {
                            _grid.CurrentCell =
                                row.Cells[0];
                        }

                        break;
                    }
                }
            }
        }
        finally
        {
            _loading =
                false;
        }
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                ColumnCount =
                    1,
                RowCount =
                    3
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.Controls.Add(
            BuildToolbar(),
            0,
            0);

        ConfigureGrid();

        root.Controls.Add(
            _grid,
            0,
            1);

        var help =
            new Label
            {
                Dock =
                    DockStyle.Fill,
                AutoSize =
                    true,
                ForeColor =
                    Color.FromArgb(
                        160,
                        178,
                        198),
                Padding =
                    new Padding(
                        2,
                        8,
                        2,
                        2),
                Text =
                    "Selecione uma ação e use “Atribuir tecla”. Ctrl, Shift e Duração usam as mesmas flags do keyboard.cfg original do OMSI."
            };

        root.Controls.Add(
            help,
            0,
            2);

        Controls.Add(
            root);
    }

    private Control BuildToolbar()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Top,
                AutoSize =
                    true,
                ColumnCount =
                    7,
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            };

        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                68));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                55));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                104));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                45));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                114));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                96));

        panel.Controls.Add(
            CreateLabel(
                "Buscar"),
            0,
            0);

        StyleTextBox(
            _search);

        _search.TextChanged +=
            (_, _) =>
                Reload();

        panel.Controls.Add(
            _search,
            1,
            0);

        var assign =
            CreateButton(
                "Atribuir tecla");

        assign.Click +=
            (_, _) =>
                AssignSelected();

        panel.Controls.Add(
            assign,
            2,
            0);

        var clear =
            CreateButton(
                "Remover");

        clear.Click +=
            (_, _) =>
                ClearSelected();

        panel.Controls.Add(
            clear,
            3,
            0);

        StyleTextBox(
            _newEvent);

        _newEvent.PlaceholderText =
            "Novo evento de veículo...";

        panel.Controls.Add(
            _newEvent,
            4,
            0);

        var add =
            CreateButton(
                "Adicionar");

        add.Click +=
            (_, _) =>
                AddEvent();

        panel.Controls.Add(
            add,
            5,
            0);

        var reload =
            CreateButton(
                "Atualizar");

        reload.Click +=
            (_, _) =>
                Reload();

        panel.Controls.Add(
            reload,
            6,
            0);

        return panel;
    }

    private void ConfigureGrid()
    {
        _grid.Dock =
            DockStyle.Fill;
        _grid.AllowUserToAddRows =
            false;
        _grid.AllowUserToDeleteRows =
            false;
        _grid.RowHeadersVisible =
            false;
        _grid.MultiSelect =
            false;
        _grid.SelectionMode =
            DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns =
            false;
        _grid.BackgroundColor =
            Color.FromArgb(
                17,
                23,
                32);
        _grid.BorderStyle =
            BorderStyle.None;
        _grid.GridColor =
            Color.FromArgb(
                45,
                56,
                70);
        _grid.EnableHeadersVisualStyles =
            false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(
                27,
                34,
                44);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.White;
        _grid.DefaultCellStyle.BackColor =
            Color.FromArgb(
                20,
                27,
                37);
        _grid.DefaultCellStyle.ForeColor =
            Color.White;
        _grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(
                45,
                77,
                105);
        _grid.DefaultCellStyle.SelectionForeColor =
            Color.White;

        _grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Ação / evento OMSI",
                ReadOnly =
                    true,
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth =
                    280
            });

        _grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Tecla",
                ReadOnly =
                    true,
                Width =
                    150
            });

        _grid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Ctrl",
                Width =
                    58
            });

        _grid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Shift",
                Width =
                    58
            });

        _grid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Duração",
                Width =
                    76
            });

        _grid.CellValueChanged +=
            (_, _) =>
            {
                if (!_loading)
                {
                    ApplyRows();
                }
            };

        _grid.CurrentCellDirtyStateChanged +=
            (_, _) =>
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(
                        DataGridViewDataErrorContexts.Commit);
                }
            };
    }

    private void AssignSelected()
    {
        var binding =
            SelectedBinding();

        if (binding is null)
        {
            return;
        }

        using var dialog =
            new KeyCaptureDialog();

        if (dialog.ShowDialog(
                FindForm()) !=
            DialogResult.OK)
        {
            return;
        }

        var row =
            _grid.CurrentRow;

        var continuous =
            row?.Cells[4].Value is
                true;

        _editor.Text =
            OmsiInputConfigEditor
                .UpdateKeyboardBinding(
                    _editor.Text,
                    binding.Ordinal,
                    dialog.ScanCode,
                    continuous,
                    dialog.Shift,
                    dialog.Control);

        Reload();
    }

    private void ClearSelected()
    {
        var binding =
            SelectedBinding();

        if (binding is null)
        {
            return;
        }

        _editor.Text =
            OmsiInputConfigEditor
                .UpdateKeyboardBinding(
                    _editor.Text,
                    binding.Ordinal,
                    0,
                    false,
                    false,
                    false);

        Reload();
    }

    private void AddEvent()
    {
        var name =
            _newEvent.Text
                .Trim();

        if (name.Length == 0)
        {
            return;
        }

        _editor.Text =
            OmsiInputConfigEditor
                .AddKeyboardEvent(
                    _editor.Text,
                    name);

        _newEvent.Clear();
        _search.Clear();
        Reload();
    }

    private void ApplyRows()
    {
        if (_loading)
        {
            return;
        }

        var text =
            _editor.Text;

        foreach (DataGridViewRow row in
                 _grid.Rows)
        {
            if (row.Tag is not
                OmsiKeyboardBinding binding)
            {
                continue;
            }

            text =
                OmsiInputConfigEditor
                    .UpdateKeyboardBinding(
                        text,
                        binding.Ordinal,
                        binding.ScanCode,
                        row.Cells[4].Value is true,
                        row.Cells[3].Value is true,
                        row.Cells[2].Value is true);
        }

        _editor.Text =
            text;
    }

    private OmsiKeyboardBinding?
        SelectedBinding() =>
        _grid.CurrentRow?.Tag as
            OmsiKeyboardBinding;

    private static Label CreateLabel(
        string text) =>
        new()
        {
            Text =
                text,
            AutoSize =
                true,
            ForeColor =
                Color.FromArgb(
                    190,
                    203,
                    218),
            Padding =
                new Padding(
                    0,
                    9,
                    6,
                    0)
        };

    private static void StyleTextBox(
        TextBox box)
    {
        box.Dock =
            DockStyle.Fill;
        box.BorderStyle =
            BorderStyle.FixedSingle;
        box.BackColor =
            Color.FromArgb(
                23,
                29,
                38);
        box.ForeColor =
            Color.White;
    }

    private static Button CreateButton(
        string text)
    {
        var button =
            new Button
            {
                Text =
                    text,
                Dock =
                    DockStyle.Fill,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        31,
                        39,
                        50),
                ForeColor =
                    Color.White,
                Height =
                    36,
                Margin =
                    new Padding(
                        3)
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                57,
                72,
                90);

        return button;
    }

    private sealed class KeyCaptureDialog :
        Form
    {
        private readonly Label _value =
            new();

        public int ScanCode
        {
            get;
            private set;
        }

        public bool Shift
        {
            get;
            private set;
        }

        public bool Control
        {
            get;
            private set;
        }

        public KeyCaptureDialog()
        {
            Text =
                "Atribuir tecla";
            StartPosition =
                FormStartPosition.CenterParent;
            ClientSize =
                new Size(
                    430,
                    150);
            FormBorderStyle =
                FormBorderStyle.FixedDialog;
            MaximizeBox =
                false;
            MinimizeBox =
                false;
            KeyPreview =
                true;
            BackColor =
                Color.FromArgb(
                    17,
                    23,
                    32);
            ForeColor =
                Color.White;

            _value.Dock =
                DockStyle.Fill;
            _value.TextAlign =
                ContentAlignment.MiddleCenter;
            _value.Font =
                new Font(
                    "Segoe UI Semibold",
                    14);
            _value.Text =
                "Pressione a tecla desejada...\nEsc cancela";

            Controls.Add(
                _value);

            KeyDown +=
                CaptureKey;
        }

        private void CaptureKey(
            object? sender,
            KeyEventArgs e)
        {
            if (e.KeyCode ==
                Keys.Escape)
            {
                DialogResult =
                    DialogResult.Cancel;
                Close();
                return;
            }

            if (e.KeyCode is
                Keys.ControlKey or
                Keys.ShiftKey or
                Keys.Menu)
            {
                return;
            }

            ScanCode =
                OmsiInputConfigEditor
                    .VirtualKeyToDirectInputScanCode(
                        e.KeyCode);

            Shift =
                e.Shift;

            Control =
                e.Control;

            if (ScanCode <= 0)
            {
                _value.Text =
                    "Essa tecla não pôde ser convertida para o código DirectInput do OMSI.";
                return;
            }

            DialogResult =
                DialogResult.OK;
            Close();
        }
    }
}

internal sealed class OmsiGameControllerSettingsPanel :
    UserControl
{
    private readonly RichTextBox _editor;
    private readonly ComboBox _controllers =
        new();
    private readonly CheckBox _active =
        new();
    private readonly DataGridView _axes =
        new();
    private readonly DataGridView _buttons =
        new();
    private readonly NumericUpDown _ffAction =
        new();
    private readonly NumericUpDown _ffIntensity =
        new();
    private bool _loading;

    public OmsiGameControllerSettingsPanel(
        RichTextBox editor)
    {
        _editor =
            editor;

        Dock =
            DockStyle.Fill;

        BuildInterface();
        Reload();
    }

    public void Reload(
        string? preferredName = null)
    {
        _loading =
            true;

        try
        {
            preferredName ??=
                SelectedController()
                    ?.Name;

            var controllers =
                OmsiInputConfigEditor
                    .ParseControllers(
                        _editor.Text);

            _controllers.DataSource =
                null;
            _controllers.DisplayMember =
                nameof(
                    OmsiControllerBinding.Name);
            _controllers.DataSource =
                controllers.ToList();

            if (!string.IsNullOrWhiteSpace(
                    preferredName))
            {
                var match =
                    controllers.FirstOrDefault(
                        controller =>
                            string.Equals(
                                controller.Name,
                                preferredName,
                                StringComparison.OrdinalIgnoreCase));

                if (match is not null)
                {
                    _controllers.SelectedItem =
                        match;
                }
            }

            LoadSelectedController();
        }
        finally
        {
            _loading =
                false;
        }
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                ColumnCount =
                    1,
                RowCount =
                    4
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.Controls.Add(
            BuildControllerRow(),
            0,
            0);

        var tabs =
            new TabControl
            {
                Dock =
                    DockStyle.Fill
            };

        var axisPage =
            new TabPage(
                "Eixos")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White
            };

        ConfigureAxisGrid();

        axisPage.Controls.Add(
            _axes);

        var buttonPage =
            new TabPage(
                "Botões")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White
            };

        ConfigureButtonGrid();

        buttonPage.Controls.Add(
            _buttons);

        tabs.TabPages.Add(
            axisPage);
        tabs.TabPages.Add(
            buttonPage);

        root.Controls.Add(
            tabs,
            0,
            1);

        root.Controls.Add(
            BuildForceFeedbackRow(),
            0,
            2);

        var actions =
            new FlowLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                AutoSize =
                    true,
                FlowDirection =
                    FlowDirection.LeftToRight,
                Padding =
                    new Padding(
                        0,
                        8,
                        0,
                        0)
            };

        var apply =
            CreateButton(
                "Aplicar mapeamento");

        apply.Click +=
            (_, _) =>
                Apply();

        var refresh =
            CreateButton(
                "Recarregar gamectrler.cfg");

        refresh.Click +=
            (_, _) =>
                Reload();

        actions.Controls.Add(
            apply);
        actions.Controls.Add(
            refresh);

        root.Controls.Add(
            actions,
            0,
            3);

        Controls.Add(
            root);
    }

    private Control BuildControllerRow()
    {
        var row =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Top,
                AutoSize =
                    true,
                ColumnCount =
                    4,
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            };

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                180));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                100));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                110));

        row.Controls.Add(
            new Label
            {
                Text =
                    "Controlador configurado",
                AutoSize =
                    true,
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        9,
                        0,
                        0)
            },
            0,
            0);

        _controllers.Dock =
            DockStyle.Fill;
        _controllers.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _controllers.BackColor =
            Color.FromArgb(
                23,
                29,
                38);
        _controllers.ForeColor =
            Color.White;

        _controllers.SelectedIndexChanged +=
            (_, _) =>
            {
                if (!_loading)
                {
                    LoadSelectedController();
                }
            };

        row.Controls.Add(
            _controllers,
            1,
            0);

        _active.Text =
            "Ativo";
        _active.AutoSize =
            true;
        _active.ForeColor =
            Color.White;
        _active.Padding =
            new Padding(
                8,
                7,
                0,
                0);

        row.Controls.Add(
            _active,
            2,
            0);

        var status =
            new Label
            {
                Text =
                    "OMSI nativo",
                AutoSize =
                    true,
                ForeColor =
                    Color.FromArgb(
                        112,
                        199,
                        255),
                Padding =
                    new Padding(
                        6,
                        9,
                        0,
                        0)
            };

        row.Controls.Add(
            status,
            3,
            0);

        return row;
    }

    private Control BuildForceFeedbackRow()
    {
        var row =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Top,
                AutoSize =
                    true,
                ColumnCount =
                    5,
                Padding =
                    new Padding(
                        0,
                        10,
                        0,
                        0)
            };

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                160));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                130));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                160));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                130));
        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        row.Controls.Add(
            CreateLabel(
                "Force Feedback ação"),
            0,
            0);

        ConfigureDecimal(
            _ffAction);

        row.Controls.Add(
            _ffAction,
            1,
            0);

        row.Controls.Add(
            CreateLabel(
                "Force Feedback intensidade"),
            2,
            0);

        ConfigureDecimal(
            _ffIntensity);

        row.Controls.Add(
            _ffIntensity,
            3,
            0);

        row.Controls.Add(
            new Label
            {
                Text =
                    "Os dois valores são gravados em [FFScale].",
                Dock =
                    DockStyle.Fill,
                ForeColor =
                    Color.FromArgb(
                        160,
                        178,
                        198),
                Padding =
                    new Padding(
                        10,
                        9,
                        0,
                        0)
            },
            4,
            0);

        return row;
    }

    private void ConfigureAxisGrid()
    {
        ConfigureBaseGrid(
            _axes);

        _axes.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Eixo",
                ReadOnly =
                    true,
                Width =
                    92
            });

        var assignment =
            new DataGridViewComboBoxColumn
            {
                HeaderText =
                    "Função OMSI",
                Width =
                    180,
                FlatStyle =
                    FlatStyle.Flat
            };

        assignment.Items.AddRange(
            "<nenhum>",
            "Direção",
            "Freio",
            "Acelerador",
            "Embreagem",
            "Acelerador + freio");

        _axes.Columns.Add(
            assignment);

        _axes.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Invertido",
                Width =
                    74
            });

        _axes.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Faixa reduzida",
                Width =
                    102
            });

        var characteristic =
            new DataGridViewComboBoxColumn
            {
                HeaderText =
                    "Característica",
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth =
                    150,
                FlatStyle =
                    FlatStyle.Flat
            };

        characteristic.Items.AddRange(
            "Linear",
            "Progressiva",
            "Degressiva",
            "Bi-progressiva",
            "Bi-degressiva");

        _axes.Columns.Add(
            characteristic);
    }

    private void ConfigureButtonGrid()
    {
        ConfigureBaseGrid(
            _buttons);

        _buttons.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Botão",
                ReadOnly =
                    true,
                Width =
                    72
            });

        _buttons.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Ação / evento OMSI",
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth =
                    260
            });

        _buttons.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Duração",
                Width =
                    82
            });
    }

    private static void ConfigureBaseGrid(
        DataGridView grid)
    {
        grid.Dock =
            DockStyle.Fill;
        grid.AllowUserToAddRows =
            false;
        grid.AllowUserToDeleteRows =
            false;
        grid.RowHeadersVisible =
            false;
        grid.MultiSelect =
            false;
        grid.SelectionMode =
            DataGridViewSelectionMode.CellSelect;
        grid.AutoGenerateColumns =
            false;
        grid.BackgroundColor =
            Color.FromArgb(
                17,
                23,
                32);
        grid.BorderStyle =
            BorderStyle.None;
        grid.GridColor =
            Color.FromArgb(
                45,
                56,
                70);
        grid.EnableHeadersVisualStyles =
            false;
        grid.ColumnHeadersDefaultCellStyle.BackColor =
            Color.FromArgb(
                27,
                34,
                44);
        grid.ColumnHeadersDefaultCellStyle.ForeColor =
            Color.White;
        grid.DefaultCellStyle.BackColor =
            Color.FromArgb(
                20,
                27,
                37);
        grid.DefaultCellStyle.ForeColor =
            Color.White;
        grid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(
                45,
                77,
                105);
        grid.DefaultCellStyle.SelectionForeColor =
            Color.White;
    }

    private void LoadSelectedController()
    {
        _loading =
            true;

        try
        {
            var controller =
                SelectedController();

            _axes.Rows.Clear();
            _buttons.Rows.Clear();

            if (controller is null)
            {
                _active.Checked =
                    false;
                _ffAction.Value =
                    0;
                _ffIntensity.Value =
                    0;
                return;
            }

            _active.Checked =
                controller.Active;

            foreach (var axis in
                     controller.Axes)
            {
                _axes.Rows.Add(
                    OmsiInputConfigEditor
                        .AxisName(
                            axis.AxisIndex),
                    OmsiInputConfigEditor
                        .AxisAssignmentName(
                            axis.Assignment),
                    axis.Reversed,
                    axis.Narrowed,
                    OmsiInputConfigEditor
                        .AxisCharacteristicName(
                            axis.Characteristic));
            }

            foreach (var button in
                     controller.Buttons)
            {
                _buttons.Rows.Add(
                    button.ButtonIndex +
                    1,
                    button.EventName,
                    button.Continuous);
            }

            _ffAction.Value =
                ClampDecimal(
                    controller
                        .ForceFeedbackAction);

            _ffIntensity.Value =
                ClampDecimal(
                    controller
                        .ForceFeedbackIntensity);
        }
        finally
        {
            _loading =
                false;
        }
    }

    private void Apply()
    {
        var controller =
            SelectedController();

        if (controller is null)
        {
            return;
        }

        _axes.EndEdit();
        _buttons.EndEdit();

        var axes =
            _axes.Rows
                .Cast<DataGridViewRow>()
                .Select(
                    row =>
                    (
                        Assignment:
                            OmsiInputConfigEditor
                                .AxisAssignmentValue(
                                    row.Cells[1]
                                        .Value?
                                        .ToString()),
                        Reversed:
                            row.Cells[2]
                                .Value is true,
                        Narrowed:
                            row.Cells[3]
                                .Value is true,
                        Characteristic:
                            OmsiInputConfigEditor
                                .AxisCharacteristicValue(
                                    row.Cells[4]
                                        .Value?
                                        .ToString())
                    ))
                .ToArray();

        var buttons =
            _buttons.Rows
                .Cast<DataGridViewRow>()
                .Select(
                    row =>
                    (
                        EventName:
                            row.Cells[1]
                                .Value?
                                .ToString() ??
                            "0",
                        Continuous:
                            row.Cells[2]
                                .Value is true
                    ))
                .ToArray();

        _editor.Text =
            OmsiInputConfigEditor
                .UpdateController(
                    _editor.Text,
                    controller,
                    _active.Checked,
                    axes,
                    buttons,
                    (double)_ffAction.Value,
                    (double)_ffIntensity.Value);

        Reload(
            controller.Name);
    }

    private OmsiControllerBinding?
        SelectedController() =>
        _controllers.SelectedItem as
            OmsiControllerBinding;

    private static void ConfigureDecimal(
        NumericUpDown value)
    {
        value.Dock =
            DockStyle.Fill;
        value.DecimalPlaces =
            3;
        value.Increment =
            0.050M;
        value.Minimum =
            -10;
        value.Maximum =
            10;
        value.BackColor =
            Color.FromArgb(
                23,
                29,
                38);
        value.ForeColor =
            Color.White;
    }

    private static decimal ClampDecimal(
        double value)
    {
        if (!double.IsFinite(
                value))
        {
            return 0;
        }

        return Math.Clamp(
            (decimal)value,
            -10M,
            10M);
    }

    private static Label CreateLabel(
        string text) =>
        new()
        {
            Text =
                text,
            AutoSize =
                true,
            ForeColor =
                Color.FromArgb(
                    190,
                    203,
                    218),
            Padding =
                new Padding(
                    0,
                    9,
                    5,
                    0)
        };

    private static Button CreateButton(
        string text)
    {
        var button =
            new Button
            {
                Text =
                    text,
                AutoSize =
                    true,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        31,
                        39,
                        50),
                ForeColor =
                    Color.White,
                Height =
                    36,
                Padding =
                    new Padding(
                        8,
                        0,
                        8,
                        0)
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                57,
                72,
                90);

        return button;
    }
}
