using System.Diagnostics;
using OmsiCompat.Core;
using OmsiCompat.Scripting;
using OmsiCompat.Vehicles;

namespace OMSICompatible.Launcher;

internal sealed class SettingsForm : Form
{
    private sealed record OptionDescriptor(
        string Label,
        string Description,
        Type ValueType,
        Func<object?> Read,
        Action<object?> Write,
        IReadOnlyList<string>? Choices = null);

    private readonly string _contentRoot;
    private OmsiRuntimeOptions _options;
    private readonly TabControl _tabs = new();
    private readonly List<DataGridView> _optionGrids = [];
    private readonly ComboBox _presetBox = new();
    private readonly RichTextBox _keyboardEditor = new();
    private readonly RichTextBox _controllerEditor = new();
    private readonly DataGridView _keyboardGrid = new();
    private readonly DataGridView _controllerAxisGrid = new();
    private readonly DataGridView _controllerButtonGrid = new();
    private readonly ComboBox _controllerBox = new();
    private readonly CheckBox _controllerActive = new();
    private readonly NumericUpDown _controllerFfCenter = new();
    private readonly NumericUpDown _controllerFfEffects = new();
    private readonly Label _status = new();
    private IReadOnlyList<OmsiKeyboardKeyDefinition> _keyboardKeys =
        Array.Empty<OmsiKeyboardKeyDefinition>();
    private OmsiControllerBinding? _currentController;
    private int _keyboardCaptureRow = -1;
    private bool _updatingControllerUi;

    public SettingsForm(
        string contentRoot)
    {
        _contentRoot =
            contentRoot;

        _options =
            OmsiRuntimeOptions.Load();

        Text =
            "Configurações — OMSI Compatible Runtime";

        StartPosition =
            FormStartPosition.CenterParent;

        MinimumSize =
            new Size(
                980,
                720);

        ClientSize =
            new Size(
                1180,
                820);

        BackColor =
            Color.FromArgb(
                13,
                16,
                21);

        ForeColor =
            Color.White;

        Font =
            new Font(
                "Segoe UI",
                10.0f);

        KeyPreview =
            true;

        KeyDown +=
            OnSettingsKeyDown;

        BuildInterface();
        LoadPresetList();
        LoadInputFiles();
        RebuildOptionTabs();
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding =
                    new Padding(
                        18)
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
            BuildHeader(),
            0,
            0);

        _tabs.Dock =
            DockStyle.Fill;
        _tabs.Padding =
            new Point(
                14,
                6);

        root.Controls.Add(
            _tabs,
            0,
            1);

        root.Controls.Add(
            BuildFooter(),
            0,
            2);

        Controls.Add(
            root);
    }

    private Control BuildHeader()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 6,
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        12)
            };

        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                175));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                150));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                160));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));

        var title =
            new Label
            {
                AutoSize = true,
                Text =
                    "CONFIGURAÇÕES",
                ForeColor =
                    Color.FromArgb(
                        112,
                        199,
                        255),
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        11.0f),
                Padding =
                    new Padding(
                        0,
                        8,
                        0,
                        0)
            };

        _presetBox.Dock =
            DockStyle.Fill;
        _presetBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _presetBox.BackColor =
            Color.FromArgb(
                31,
                36,
                46);
        _presetBox.ForeColor =
            Color.White;

        var importCurrent =
            CreateButton(
                "Importar options.cfg");

        importCurrent.Click +=
            (_, _) =>
                ImportCurrentOmsiOptions();

        var loadPreset =
            CreateButton(
                "Carregar perfil");

        loadPreset.Click +=
            (_, _) =>
                ImportSelectedPreset();

        var openOmsi =
            CreateButton(
                "Abrir OMSI");

        openOmsi.Click +=
            (_, _) =>
                OpenOmsiFolder();

        var defaults =
            CreateButton(
                "Padrões");

        defaults.Click +=
            (_, _) =>
            {
                _options =
                    new OmsiRuntimeOptions();

                RebuildOptionTabs();

                SetStatus(
                    "Valores padrão do runtime carregados.");
            };

        panel.Controls.Add(
            title,
            0,
            0);
        panel.Controls.Add(
            _presetBox,
            1,
            0);
        panel.Controls.Add(
            importCurrent,
            2,
            0);
        panel.Controls.Add(
            loadPreset,
            3,
            0);
        panel.Controls.Add(
            openOmsi,
            4,
            0);
        panel.Controls.Add(
            defaults,
            5,
            0);

        return panel;
    }

    private Control BuildFooter()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 4,
                Padding =
                    new Padding(
                        0,
                        12,
                        0,
                        0)
            };

        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                150));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));
        panel.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                120));

        _status.Dock =
            DockStyle.Fill;
        _status.AutoEllipsis =
            true;
        _status.ForeColor =
            Color.FromArgb(
                152,
                169,
                189);
        _status.Padding =
            new Padding(
                0,
                9,
                0,
                0);
        _status.Text =
            "As configurações OMSI são importadas sem alterar a instalação original.";

        var saveInputs =
            CreateButton(
                "Salvar Inputs");

        saveInputs.Click +=
            (_, _) =>
                SaveInputFiles();

        var save =
            CreateButton(
                "Salvar");

        save.Click +=
            (_, _) =>
            {
                if (!CommitGridValues())
                {
                    return;
                }

                _options.Save();

                SetStatus(
                    $"Configurações salvas em {OmsiRuntimeOptions.SettingsPath}");

                DialogResult =
                    DialogResult.OK;

                Close();
            };

        var cancel =
            CreateButton(
                "Cancelar");

        cancel.Click +=
            (_, _) =>
            {
                DialogResult =
                    DialogResult.Cancel;

                Close();
            };

        panel.Controls.Add(
            _status,
            0,
            0);
        panel.Controls.Add(
            saveInputs,
            1,
            0);
        panel.Controls.Add(
            save,
            2,
            0);
        panel.Controls.Add(
            cancel,
            3,
            0);

        return panel;
    }

    private void RebuildOptionTabs()
    {
        var selected =
            _tabs.SelectedIndex;

        _tabs.TabPages.Clear();
        _optionGrids.Clear();

        AddOptionsTab(
            "Geral",
            GeneralOptions());

        AddOptionsTab(
            "Avançado",
            AdvancedOptions());

        AddOptionsTab(
            "Gráficos",
            GraphicsOptions());

        AddOptionsTab(
            "Gráficos avançados",
            AdvancedGraphicsOptions());

        AddOptionsTab(
            "Som",
            SoundOptions());

        AddOptionsTab(
            "Tráfego IA",
            TrafficOptions());

        AddKeyboardTab();

        AddGameControllerTab();

        AddOptionsTab(
            "Runtime x64",
            RuntimeOptions());

        if (_tabs.TabPages.Count > 0)
        {
            _tabs.SelectedIndex =
                Math.Clamp(
                    selected,
                    0,
                    _tabs.TabPages.Count - 1);
        }
    }

    private void AddOptionsTab(
        string title,
        IReadOnlyList<OptionDescriptor> options)
    {
        var tab =
            new TabPage(
                title)
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        10)
            };

        var grid =
            CreateOptionsGrid();

        foreach (var option in
                 options)
        {
            var row =
                new DataGridViewRow
                {
                    Tag =
                        option
                };

            row.CreateCells(
                grid);

            row.Cells[0].Value =
                option.Label;

            DataGridViewCell valueCell;

            if (option.ValueType ==
                typeof(bool))
            {
                valueCell =
                    new DataGridViewCheckBoxCell
                    {
                        Value =
                            Convert.ToBoolean(
                                option.Read())
                    };
            }
            else if (option.Choices is
                     { Count: > 0 })
            {
                var combo =
                    new DataGridViewComboBoxCell
                    {
                        FlatStyle =
                            FlatStyle.Flat
                    };

                combo.Items.AddRange(
                    option.Choices
                        .Cast<object>()
                        .ToArray());

                combo.Value =
                    option.Read()
                        ?.ToString() ??
                    option.Choices[0];

                valueCell =
                    combo;
            }
            else
            {
                valueCell =
                    new DataGridViewTextBoxCell
                    {
                        Value =
                            FormatValue(
                                option.Read())
                    };
            }

            row.Cells[1] =
                valueCell;

            row.Cells[2].Value =
                option.Description;

            grid.Rows.Add(
                row);
        }

        _optionGrids.Add(
            grid);

        tab.Controls.Add(
            grid);

        _tabs.TabPages.Add(
            tab);
    }

    private void AddKeyboardTab()
    {
        var tab =
            CreateDarkTab(
                "Teclado");

        var modes =
            new TabControl
            {
                Dock =
                    DockStyle.Fill
            };

        var visual =
            new TabPage(
                "Visual")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White
            };

        var visualLayout =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                ColumnCount =
                    1,
                RowCount =
                    3,
                Padding =
                    new Padding(
                        10)
            };

        visualLayout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        visualLayout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        visualLayout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        visualLayout.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Text =
                    "Comandos reais de Inputs\\keyboard.cfg. A tecla vem do arquivo .kyb do OMSI; Contínuo=1, Shift=2 e Ctrl=4.",
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            },
            0,
            0);

        ConfigureKeyboardGrid();

        visualLayout.Controls.Add(
            _keyboardGrid,
            0,
            1);

        var toolbar =
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

        var capture =
            CreateButton(
                "Capturar tecla");

        capture.AutoSize =
            true;

        capture.Click +=
            (_, _) =>
                BeginKeyboardCapture();

        var apply =
            CreateButton(
                "Aplicar ao arquivo");

        apply.AutoSize =
            true;

        apply.Click +=
            (_, _) =>
            {
                ApplyKeyboardVisualEdits();
                SetStatus(
                    "Alterações visuais aplicadas ao keyboard.cfg em memória.");
            };

        var addVehicleEvent =
            CreateButton(
                "Adicionar evento de veículo...");

        addVehicleEvent.AutoSize =
            true;

        addVehicleEvent.Click +=
            (_, _) =>
                AddVehicleKeyboardEvent();

        var restore =
            CreateButton(
                "Restaurar padrão");

        restore.AutoSize =
            true;

        restore.Click +=
            (_, _) =>
            {
                LoadEditor(
                    _keyboardEditor,
                    Path.Combine(
                        _contentRoot,
                        "Inputs",
                        "keyboard_reset.cfg"));

                RefreshKeyboardVisual();
            };

        var reload =
            CreateButton(
                "Recarregar");

        reload.AutoSize =
            true;

        reload.Click +=
            (_, _) =>
            {
                LoadEditor(
                    _keyboardEditor,
                    Path.Combine(
                        _contentRoot,
                        "Inputs",
                        "keyboard.cfg"));

                RefreshKeyboardVisual();
            };

        toolbar.Controls.Add(
            capture);
        toolbar.Controls.Add(
            apply);
        toolbar.Controls.Add(
            addVehicleEvent);
        toolbar.Controls.Add(
            restore);
        toolbar.Controls.Add(
            reload);

        visualLayout.Controls.Add(
            toolbar,
            0,
            2);

        visual.Controls.Add(
            visualLayout);

        var advanced =
            BuildRawInputPage(
                "Teclado — modo avançado",
                _keyboardEditor,
                Path.Combine(
                    _contentRoot,
                    "Inputs",
                    "keyboard.cfg"),
                includeResetButton:
                    true,
                afterReload:
                    RefreshKeyboardVisual);

        modes.TabPages.Add(
            visual);
        modes.TabPages.Add(
            advanced);

        tab.Controls.Add(
            modes);
        _tabs.TabPages.Add(
            tab);

        RefreshKeyboardVisual();
    }

    private void ConfigureKeyboardGrid()
    {
        ConfigureInputGrid(
            _keyboardGrid);

        _keyboardGrid.Columns.Clear();

        _keyboardGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Comando / trigger",
                ReadOnly =
                    true,
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill,
                FillWeight =
                    48
            });

        _keyboardGrid.Columns.Add(
            new DataGridViewComboBoxColumn
            {
                HeaderText =
                    "Tecla",
                Width =
                    230,
                FlatStyle =
                    FlatStyle.Flat
            });

        _keyboardGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Contínuo",
                Width =
                    78
            });

        _keyboardGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Shift",
                Width =
                    62
            });

        _keyboardGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Ctrl",
                Width =
                    62
            });
    }

    private void RefreshKeyboardVisual()
    {
        _keyboardCaptureRow =
            -1;

        var entries =
            OmsiInputConfiguration.ParseKeyboard(
                _keyboardEditor.Text);

        var keys =
            OmsiInputConfiguration.ReadKeyboardKeys(
                _contentRoot,
                _options.Language)
                .ToList();

        foreach (var entry in
                 entries)
        {
            if (keys.All(
                    key =>
                        key.Index !=
                        entry.KeyIndex))
            {
                keys.Add(
                    new OmsiKeyboardKeyDefinition(
                        entry.KeyIndex,
                        $"Tecla OMSI {entry.KeyIndex}"));
            }
        }

        _keyboardKeys =
            keys
                .OrderBy(
                    static key =>
                        key.Index)
                .ToArray();

        _keyboardGrid.Rows.Clear();

        foreach (var entry in
                 entries)
        {
            var row =
                _keyboardGrid.Rows[
                    _keyboardGrid.Rows.Add()];

            row.Tag =
                entry;

            row.Cells[0].Value =
                entry.Trigger;

            var combo =
                (DataGridViewComboBoxCell)
                    row.Cells[1];

            combo.DisplayMember =
                nameof(
                    OmsiKeyboardKeyDefinition.Name);
            combo.ValueMember =
                nameof(
                    OmsiKeyboardKeyDefinition.Index);
            combo.DataSource =
                _keyboardKeys
                    .ToList();
            combo.Value =
                entry.KeyIndex;

            row.Cells[2].Value =
                (entry.Flags &
                 1) !=
                0;
            row.Cells[3].Value =
                (entry.Flags &
                 2) !=
                0;
            row.Cells[4].Value =
                (entry.Flags &
                 4) !=
                0;
        }

        SetStatus(
            $"{entries.Count:N0} comando(s) de teclado carregado(s).");
    }

    private async void AddVehicleKeyboardEvent()
    {
        if (!OmsiContentRoot.TryCreate(
                _contentRoot,
                out var contentRoot,
                out var error) ||
            contentRoot is null)
        {
            SetStatus(
                error ??
                "Pasta OMSI inválida.");
            return;
        }

        SetStatus(
            "Carregando triggers dos veículos instalados...");

        Cursor =
            Cursors.WaitCursor;

        IReadOnlyList<string> triggers;

        try
        {
            triggers =
                await Task.Run(
                    () =>
                    {
                        var result =
                            new HashSet<string>(
                                StringComparer.OrdinalIgnoreCase);

                        foreach (var bus in
                                 BusDiscovery.Discover(
                                     contentRoot))
                        {
                            try
                            {
                                var catalog =
                                    OmsiScriptCatalogLoader.Load(
                                        contentRoot,
                                        bus.ScriptManifest);

                                foreach (var trigger in
                                         catalog.Program.Triggers.Keys)
                                {
                                    if (!string.IsNullOrWhiteSpace(
                                            trigger))
                                    {
                                        result.Add(
                                            trigger);
                                    }
                                }
                            }
                            catch
                            {
                                // One broken third-party vehicle must not
                                // prevent events from the remaining buses.
                            }
                        }

                        return
                            (IReadOnlyList<string>)
                            result
                                .OrderBy(
                                    static trigger =>
                                        trigger,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToArray();
                    });
        }
        finally
        {
            Cursor =
                Cursors.Default;
        }

        if (triggers.Count == 0)
        {
            SetStatus(
                "Nenhum trigger de veículo foi encontrado.");
            return;
        }

        using var dialog =
            new VehicleEventSelectionDialog(
                triggers);

        if (dialog.ShowDialog(
                this) !=
            DialogResult.OK ||
            string.IsNullOrWhiteSpace(
                dialog.SelectedTrigger))
        {
            SetStatus(
                $"{triggers.Count:N0} evento(s) de veículo disponível(is).");
            return;
        }

        var before =
            _keyboardEditor.Text;

        _keyboardEditor.Text =
            OmsiInputConfiguration
                .AppendKeyboardEntry(
                    before,
                    dialog.SelectedTrigger);

        RefreshKeyboardVisual();

        SetStatus(
            string.Equals(
                before,
                _keyboardEditor.Text,
                StringComparison.Ordinal)
                ? $"O evento {dialog.SelectedTrigger} já existia no keyboard.cfg."
                : $"Evento adicionado: {dialog.SelectedTrigger}. Agora atribua a tecla desejada.");
    }

    private void BeginKeyboardCapture()
    {
        if (_keyboardGrid.CurrentCell is null)
        {
            SetStatus(
                "Selecione primeiro um comando do teclado.");
            return;
        }

        _keyboardCaptureRow =
            _keyboardGrid.CurrentCell
                .RowIndex;

        SetStatus(
            "Pressione agora a tecla desejada. Shift e Ctrl também serão capturados.");

        Focus();
    }

    private void OnSettingsKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (_keyboardCaptureRow < 0 ||
            _keyboardCaptureRow >=
                _keyboardGrid.Rows.Count)
        {
            return;
        }

        var key =
            FindOmsiKey(
                e.KeyCode);

        if (key is null)
        {
            SetStatus(
                $"A tecla {e.KeyCode} não foi encontrada no arquivo .kyb selecionado.");
            e.SuppressKeyPress =
                true;
            return;
        }

        var row =
            _keyboardGrid.Rows[
                _keyboardCaptureRow];

        row.Cells[1].Value =
            key.Index;
        row.Cells[3].Value =
            e.Shift;
        row.Cells[4].Value =
            e.Control;

        _keyboardCaptureRow =
            -1;

        SetStatus(
            $"Tecla capturada: {key.Name}");

        e.SuppressKeyPress =
            true;
        e.Handled =
            true;
    }

    private OmsiKeyboardKeyDefinition? FindOmsiKey(
        Keys keyCode)
    {
        var aliases =
            KeyAliases(
                keyCode)
                .Select(
                    NormalizeKeyLabel)
                .Where(
                    static value =>
                        value.Length > 0)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var key in
                 _keyboardKeys)
        {
            var normalized =
                NormalizeKeyLabel(
                    key.Name);

            if (aliases.Any(
                    alias =>
                        string.Equals(
                            normalized,
                            alias,
                            StringComparison.OrdinalIgnoreCase)))
            {
                return key;
            }
        }

        foreach (var key in
                 _keyboardKeys)
        {
            var normalized =
                NormalizeKeyLabel(
                    key.Name);

            if (aliases.Any(
                    alias =>
                        normalized.Contains(
                            alias,
                            StringComparison.OrdinalIgnoreCase) ||
                        alias.Contains(
                            normalized,
                            StringComparison.OrdinalIgnoreCase)))
            {
                return key;
            }
        }

        return null;
    }

    private static IEnumerable<string> KeyAliases(
        Keys key)
    {
        if (key is >= Keys.A and <= Keys.Z)
        {
            yield return
                key.ToString();
            yield break;
        }

        if (key is >= Keys.D0 and <= Keys.D9)
        {
            yield return
                ((int)key -
                 (int)Keys.D0)
                .ToString();
        }

        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
        {
            var number =
                ((int)key -
                 (int)Keys.NumPad0)
                .ToString();

            yield return
                "Num " +
                number;
            yield return
                "Numpad " +
                number;
            yield return
                "NumPad" +
                number;
        }

        var aliases =
            key switch
            {
                Keys.Add =>
                    ["Num +", "Numpad +", "+"],
                Keys.Subtract =>
                    ["Num -", "Numpad -"],
                Keys.Multiply =>
                    ["Num *", "Numpad *"],
                Keys.Divide =>
                    ["Num /", "Numpad /"],
                Keys.Decimal =>
                    ["Num .", "Numpad .", "Decimal"],
                Keys.Enter =>
                    ["Enter", "Return"],
                Keys.Space =>
                    ["Space", "Spacebar"],
                Keys.Escape =>
                    ["Esc", "Escape"],
                Keys.Left =>
                    ["Left", "Arrow Left"],
                Keys.Right =>
                    ["Right", "Arrow Right"],
                Keys.Up =>
                    ["Up", "Arrow Up"],
                Keys.Down =>
                    ["Down", "Arrow Down"],
                Keys.PageUp =>
                    ["Page Up", "PgUp"],
                Keys.PageDown =>
                    ["Page Down", "PgDn"],
                _ =>
                    [key.ToString()]
            };

        foreach (var alias in
                 aliases)
        {
            yield return alias;
        }
    }

    private static string NormalizeKeyLabel(
        string value) =>
        new string(
            value
                .Where(
                    static character =>
                        char.IsLetterOrDigit(
                            character) ||
                        character is
                            '+' or '-' or
                            '*' or '/' or '.')
                .Select(
                    char.ToLowerInvariant)
                .ToArray());

    private void ApplyKeyboardVisualEdits()
    {
        if (_keyboardGrid.Rows.Count == 0)
        {
            return;
        }

        _keyboardGrid.EndEdit();

        var changes =
            new Dictionary<int, string>();

        foreach (DataGridViewRow row in
                 _keyboardGrid.Rows)
        {
            if (row.Tag is not
                OmsiKeyboardEntryBinding entry)
            {
                continue;
            }

            var keyIndex =
                row.Cells[1].Value is
                    int parsedKey
                    ? parsedKey
                    : int.TryParse(
                        row.Cells[1].Value
                            ?.ToString(),
                        out var converted)
                        ? converted
                        : entry.KeyIndex;

            var flags =
                (Convert.ToBoolean(
                     row.Cells[2].Value ??
                     false)
                    ? 1
                    : 0) +
                (Convert.ToBoolean(
                     row.Cells[3].Value ??
                     false)
                    ? 2
                    : 0) +
                (Convert.ToBoolean(
                     row.Cells[4].Value ??
                     false)
                    ? 4
                    : 0);

            changes[
                entry.KeyLine] =
                keyIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);

            changes[
                entry.FlagsLine] =
                flags.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        _keyboardEditor.Text =
            OmsiInputConfiguration.ApplyLineChanges(
                _keyboardEditor.Text,
                changes);
    }

    private void AddGameControllerTab()
    {
        var tab =
            CreateDarkTab(
                "Game Controller");

        var modes =
            new TabControl
            {
                Dock =
                    DockStyle.Fill
            };

        var visual =
            new TabPage(
                "Visual")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White
            };

        var layout =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                ColumnCount =
                    1,
                RowCount =
                    4,
                Padding =
                    new Padding(
                        10)
            };

        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        layout.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Text =
                    "Configuração real de Inputs\\gamectrler.cfg: controlador, eixos, botões, curva, inversão, faixa e Force Feedback.",
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            },
            0,
            0);

        var header =
            new FlowLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                AutoSize =
                    true,
                FlowDirection =
                    FlowDirection.LeftToRight
            };

        _controllerBox.DropDownStyle =
            ComboBoxStyle.DropDownList;
        _controllerBox.Width =
            330;

        _controllerBox.SelectedIndexChanged +=
            (_, _) =>
            {
                if (_updatingControllerUi)
                {
                    return;
                }

                ApplyControllerVisualEdits();

                PopulateControllerVisual(
                    _controllerBox.SelectedItem
                        ?.ToString());
            };

        _controllerActive.Text =
            "Ativo";
        _controllerActive.AutoSize =
            true;
        _controllerActive.ForeColor =
            Color.White;
        _controllerActive.Padding =
            new Padding(
                8,
                6,
                8,
                0);

        ConfigureFfNumeric(
            _controllerFfCenter);
        ConfigureFfNumeric(
            _controllerFfEffects);

        header.Controls.Add(
            new Label
            {
                Text =
                    "Controlador:",
                AutoSize =
                    true,
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        0,
                        7,
                        4,
                        0)
            });
        header.Controls.Add(
            _controllerBox);
        header.Controls.Add(
            _controllerActive);
        header.Controls.Add(
            new Label
            {
                Text =
                    "FF retorno:",
                AutoSize =
                    true,
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        12,
                        7,
                        4,
                        0)
            });
        header.Controls.Add(
            _controllerFfCenter);
        header.Controls.Add(
            new Label
            {
                Text =
                    "FF efeitos:",
                AutoSize =
                    true,
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        12,
                        7,
                        4,
                        0)
            });
        header.Controls.Add(
            _controllerFfEffects);

        layout.Controls.Add(
            header,
            0,
            1);

        var controllerTabs =
            new TabControl
            {
                Dock =
                    DockStyle.Fill
            };

        ConfigureControllerAxisGrid();
        ConfigureControllerButtonGrid();

        var axesPage =
            new TabPage(
                "Eixos")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32)
            };

        axesPage.Controls.Add(
            _controllerAxisGrid);

        var buttonsPage =
            new TabPage(
                "Botões")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32)
            };

        buttonsPage.Controls.Add(
            _controllerButtonGrid);

        controllerTabs.TabPages.Add(
            axesPage);
        controllerTabs.TabPages.Add(
            buttonsPage);

        layout.Controls.Add(
            controllerTabs,
            0,
            2);

        var toolbar =
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
                "Aplicar ao arquivo");

        apply.AutoSize =
            true;
        apply.Click +=
            (_, _) =>
            {
                ApplyControllerVisualEdits();
                SetStatus(
                    "Alterações do Game Controller aplicadas ao arquivo em memória.");
            };

        var reload =
            CreateButton(
                "Recarregar");

        reload.AutoSize =
            true;
        reload.Click +=
            (_, _) =>
            {
                LoadEditor(
                    _controllerEditor,
                    Path.Combine(
                        _contentRoot,
                        "Inputs",
                        "gamectrler.cfg"));

                RefreshControllerCatalog();
            };

        toolbar.Controls.Add(
            apply);
        toolbar.Controls.Add(
            reload);

        layout.Controls.Add(
            toolbar,
            0,
            3);

        visual.Controls.Add(
            layout);

        var advanced =
            BuildRawInputPage(
                "Game Controller — modo avançado",
                _controllerEditor,
                Path.Combine(
                    _contentRoot,
                    "Inputs",
                    "gamectrler.cfg"),
                includeResetButton:
                    false,
                afterReload:
                    RefreshControllerCatalog);

        modes.TabPages.Add(
            visual);
        modes.TabPages.Add(
            advanced);

        tab.Controls.Add(
            modes);
        _tabs.TabPages.Add(
            tab);

        RefreshControllerCatalog();
    }

    private static void ConfigureFfNumeric(
        NumericUpDown control)
    {
        control.DecimalPlaces =
            3;
        control.Increment =
            0.050m;
        control.Minimum =
            0.000m;
        control.Maximum =
            5.000m;
        control.Width =
            85;
    }

    private void ConfigureControllerAxisGrid()
    {
        ConfigureInputGrid(
            _controllerAxisGrid);

        _controllerAxisGrid.Columns.Clear();

        _controllerAxisGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Eixo",
                ReadOnly =
                    true,
                Width =
                    100
            });

        var function =
            new DataGridViewComboBoxColumn
            {
                HeaderText =
                    "Função",
                Width =
                    180,
                FlatStyle =
                    FlatStyle.Flat
            };

        function.Items.AddRange(
            "<nenhuma>",
            "Direção",
            "Freio",
            "Acelerador",
            "Embreagem",
            "Acelerador + freio");

        _controllerAxisGrid.Columns.Add(
            function);

        _controllerAxisGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Inverter",
                Width =
                    70
            });

        _controllerAxisGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Faixa estreita",
                Width =
                    100
            });

        var curve =
            new DataGridViewComboBoxColumn
            {
                HeaderText =
                    "Curva",
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill,
                FlatStyle =
                    FlatStyle.Flat
            };

        curve.Items.AddRange(
            "Linear",
            "Degressiva",
            "Progressiva",
            "Bi-degressiva",
            "Bi-progressiva");

        _controllerAxisGrid.Columns.Add(
            curve);
    }

    private void ConfigureControllerButtonGrid()
    {
        ConfigureInputGrid(
            _controllerButtonGrid);

        _controllerButtonGrid.Columns.Clear();

        _controllerButtonGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Botão",
                ReadOnly =
                    true,
                Width =
                    80
            });

        _controllerButtonGrid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Comando / trigger",
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill
            });

        _controllerButtonGrid.Columns.Add(
            new DataGridViewCheckBoxColumn
            {
                HeaderText =
                    "Contínuo",
                Width =
                    90
            });
    }

    private void RefreshControllerCatalog()
    {
        _updatingControllerUi =
            true;

        try
        {
            var controllers =
                OmsiInputConfiguration.ParseControllers(
                    _controllerEditor.Text);

            var previousName =
                _currentController?.Name ??
                _controllerBox.SelectedItem
                    ?.ToString();

            _controllerBox.Items.Clear();

            foreach (var controller in
                     controllers)
            {
                _controllerBox.Items.Add(
                    controller.Name);
            }

            var target =
                controllers.FirstOrDefault(
                    controller =>
                        string.Equals(
                            controller.Name,
                            previousName,
                            StringComparison.OrdinalIgnoreCase))
                ?? controllers.FirstOrDefault();

            _controllerBox.SelectedItem =
                target?.Name;

            PopulateControllerVisual(
                target?.Name,
                controllers);
        }
        finally
        {
            _updatingControllerUi =
                false;
        }
    }

    private void PopulateControllerVisual(
        string? controllerName,
        IReadOnlyList<OmsiControllerBinding>? parsed = null)
    {
        parsed ??=
            OmsiInputConfiguration.ParseControllers(
                _controllerEditor.Text);

        _currentController =
            parsed.FirstOrDefault(
                controller =>
                    string.Equals(
                        controller.Name,
                        controllerName,
                        StringComparison.OrdinalIgnoreCase));

        _controllerAxisGrid.Rows.Clear();
        _controllerButtonGrid.Rows.Clear();

        if (_currentController is null)
        {
            _controllerActive.Checked =
                false;
            _controllerFfCenter.Value =
                0;
            _controllerFfEffects.Value =
                0;
            return;
        }

        _controllerActive.Checked =
            _currentController.Active;

        _controllerFfCenter.Value =
            ClampDecimal(
                _currentController
                    .ForceFeedbackCentering);

        _controllerFfEffects.Value =
            ClampDecimal(
                _currentController
                    .ForceFeedbackEffects);

        string[] axisNames =
        [
            "X",
            "Y",
            "Z",
            "Rx",
            "Ry",
            "Rz",
            "Slider 1",
            "Slider 2"
        ];

        foreach (var axis in
                 _currentController.Axes)
        {
            var row =
                _controllerAxisGrid.Rows[
                    _controllerAxisGrid.Rows.Add()];

            row.Tag =
                axis;

            row.Cells[0].Value =
                axis.AxisIndex <
                axisNames.Length
                    ? axisNames[
                        axis.AxisIndex]
                    : $"Axis {axis.AxisIndex}";

            row.Cells[1].Value =
                AxisFunctionLabel(
                    axis.Function);

            row.Cells[2].Value =
                (axis.Flags &
                 1) !=
                0;

            row.Cells[3].Value =
                (axis.Flags &
                 2) !=
                0;

            row.Cells[4].Value =
                AxisCurveLabel(
                    axis.Flags);
        }

        foreach (var button in
                 _currentController.Buttons)
        {
            var row =
                _controllerButtonGrid.Rows[
                    _controllerButtonGrid.Rows.Add()];

            row.Tag =
                button;

            row.Cells[0].Value =
                button.ButtonIndex;

            row.Cells[1].Value =
                button.Trigger;

            row.Cells[2].Value =
                button.Continuous;
        }

        SetStatus(
            $"{_currentController.Name}: {_currentController.Axes.Count} eixo(s), {_currentController.Buttons.Count} botão(ões).");
    }

    private void ApplyControllerVisualEdits()
    {
        var controller =
            _currentController;

        if (controller is null)
        {
            return;
        }

        _controllerAxisGrid.EndEdit();
        _controllerButtonGrid.EndEdit();

        var changes =
            new Dictionary<int, string>
            {
                [controller.ActiveLine] =
                    _controllerActive.Checked
                        ? "1"
                        : "0"
            };

        if (controller.ForceFeedbackCenteringLine
            is int centerLine)
        {
            changes[
                centerLine] =
                _controllerFfCenter.Value
                    .ToString(
                        "0.000",
                        System.Globalization.CultureInfo.InvariantCulture);
        }

        if (controller.ForceFeedbackEffectsLine
            is int effectsLine)
        {
            changes[
                effectsLine] =
                _controllerFfEffects.Value
                    .ToString(
                        "0.000",
                        System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (DataGridViewRow row in
                 _controllerAxisGrid.Rows)
        {
            if (row.Tag is not
                OmsiControllerAxisBinding axis)
            {
                continue;
            }

            changes[
                axis.FunctionLine] =
                AxisFunctionValue(
                    row.Cells[1].Value
                        ?.ToString())
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

            var flags =
                (Convert.ToBoolean(
                     row.Cells[2].Value ??
                     false)
                    ? 1
                    : 0) +
                (Convert.ToBoolean(
                     row.Cells[3].Value ??
                     false)
                    ? 2
                    : 0) +
                AxisCurveValue(
                    row.Cells[4].Value
                        ?.ToString());

            changes[
                axis.FlagsLine] =
                flags.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (DataGridViewRow row in
                 _controllerButtonGrid.Rows)
        {
            if (row.Tag is not
                OmsiControllerButtonBinding button)
            {
                continue;
            }

            changes[
                button.TriggerLine] =
                row.Cells[1].Value
                    ?.ToString() ??
                button.Trigger;

            changes[
                button.ContinuousLine] =
                Convert.ToBoolean(
                    row.Cells[2].Value ??
                    false)
                    ? "1"
                    : "0";
        }

        _controllerEditor.Text =
            OmsiInputConfiguration.ApplyLineChanges(
                _controllerEditor.Text,
                changes);
    }

    private static decimal ClampDecimal(
        double value) =>
        Math.Clamp(
            (decimal)(
                double.IsFinite(
                    value)
                    ? value
                    : 0.0),
            0.000m,
            5.000m);

    private static string AxisFunctionLabel(
        int value) =>
        value switch
        {
            -1 =>
                "<nenhuma>",
            0 =>
                "Direção",
            1 =>
                "Freio",
            2 =>
                "Acelerador",
            3 =>
                "Embreagem",
            4 =>
                "Acelerador + freio",
            _ =>
                "<nenhuma>"
        };

    private static int AxisFunctionValue(
        string? value) =>
        value switch
        {
            "Direção" =>
                0,
            "Freio" =>
                1,
            "Acelerador" =>
                2,
            "Embreagem" =>
                3,
            "Acelerador + freio" =>
                4,
            _ =>
                -1
        };

    private static string AxisCurveLabel(
        int flags) =>
        (flags &
         ~3) switch
        {
            4 =>
                "Degressiva",
            8 =>
                "Progressiva",
            20 =>
                "Bi-degressiva",
            24 =>
                "Bi-progressiva",
            _ =>
                "Linear"
        };

    private static int AxisCurveValue(
        string? value) =>
        value switch
        {
            "Degressiva" =>
                4,
            "Progressiva" =>
                8,
            "Bi-degressiva" =>
                20,
            "Bi-progressiva" =>
                24,
            _ =>
                0
        };

    private static void ConfigureInputGrid(
        DataGridView grid)
    {
        grid.Dock =
            DockStyle.Fill;
        grid.AllowUserToAddRows =
            false;
        grid.AllowUserToDeleteRows =
            false;
        grid.AllowUserToResizeRows =
            false;
        grid.RowHeadersVisible =
            false;
        grid.AutoGenerateColumns =
            false;
        grid.SelectionMode =
            DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect =
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

    private TabPage CreateDarkTab(
        string title) =>
        new(
            title)
        {
            BackColor =
                Color.FromArgb(
                    17,
                    23,
                    32),
            ForeColor =
                Color.White,
            Padding =
                new Padding(
                    10)
        };

    private TabPage BuildRawInputPage(
        string title,
        RichTextBox editor,
        string path,
        bool includeResetButton,
        Action afterReload)
    {
        var page =
            new TabPage(
                "Avançado")
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        10)
            };

        var layout =
            new TableLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                ColumnCount =
                    1,
                RowCount =
                    3
            };

        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        layout.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Text =
                    title +
                    " — edição direta preservada para add-ons e formatos não reconhecidos.",
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            },
            0,
            0);

        editor.Dock =
            DockStyle.Fill;
        editor.BorderStyle =
            BorderStyle.FixedSingle;
        editor.BackColor =
            Color.FromArgb(
                10,
                14,
                20);
        editor.ForeColor =
            Color.FromArgb(
                224,
                231,
                239);
        editor.Font =
            new Font(
                "Cascadia Mono",
                9.5f);
        editor.WordWrap =
            false;
        editor.AcceptsTab =
            true;

        layout.Controls.Add(
            editor,
            0,
            1);

        var buttons =
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

        var reload =
            CreateButton(
                "Recarregar arquivo");

        reload.AutoSize =
            true;

        reload.Click +=
            (_, _) =>
            {
                LoadEditor(
                    editor,
                    path);
                afterReload();
            };

        buttons.Controls.Add(
            reload);

        if (includeResetButton)
        {
            var reset =
                CreateButton(
                    "Restaurar keyboard_reset.cfg");

            reset.AutoSize =
                true;

            reset.Click +=
                (_, _) =>
                {
                    LoadEditor(
                        editor,
                        Path.Combine(
                            _contentRoot,
                            "Inputs",
                            "keyboard_reset.cfg"));
                    afterReload();
                };

            buttons.Controls.Add(
                reset);
        }

        var openInputs =
            CreateButton(
                "Abrir pasta Inputs");

        openInputs.AutoSize =
            true;

        openInputs.Click +=
            (_, _) =>
                OpenPath(
                    Path.Combine(
                        _contentRoot,
                        "Inputs"));

        buttons.Controls.Add(
            openInputs);

        layout.Controls.Add(
            buttons,
            0,
            2);

        page.Controls.Add(
            layout);

        return page;
    }

    private void AddTextEditorTab(
        string title,
        RichTextBox editor,
        string path,
        bool includeResetButton)
    {
        var tab =
            new TabPage(
                title)
            {
                BackColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                ForeColor =
                    Color.White,
                Padding =
                    new Padding(
                        10)
            };

        var layout =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };

        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));
        layout.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        var info =
            new Label
            {
                AutoSize = true,
                MaximumSize =
                    new Size(
                        1050,
                        0),
                Text =
                    title == "Teclado"
                        ? "Mapeamento compatível com Inputs\\keyboard.cfg. Cada [entry] representa um evento do OMSI/veículo e sua tecla."
                        : "Mapeamento compatível com Inputs\\gamectrler.cfg, incluindo controladores, eixos, botões e force feedback.",
                ForeColor =
                    Color.FromArgb(
                        190,
                        203,
                        218),
                Padding =
                    new Padding(
                        0,
                        0,
                        0,
                        8)
            };

        editor.Dock =
            DockStyle.Fill;
        editor.BorderStyle =
            BorderStyle.FixedSingle;
        editor.BackColor =
            Color.FromArgb(
                10,
                14,
                20);
        editor.ForeColor =
            Color.FromArgb(
                224,
                231,
                239);
        editor.Font =
            new Font(
                "Cascadia Mono",
                9.5f);
        editor.WordWrap =
            false;
        editor.AcceptsTab =
            true;

        var buttons =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection =
                    FlowDirection.LeftToRight,
                Padding =
                    new Padding(
                        0,
                        8,
                        0,
                        0)
            };

        var reload =
            CreateButton(
                "Recarregar arquivo");

        reload.Click +=
            (_, _) =>
                LoadEditor(
                    editor,
                    path);

        buttons.Controls.Add(
            reload);

        if (includeResetButton)
        {
            var reset =
                CreateButton(
                    "Restaurar keyboard_reset.cfg");

            reset.Click +=
                (_, _) =>
                    LoadEditor(
                        editor,
                        Path.Combine(
                            _contentRoot,
                            "Inputs",
                            "keyboard_reset.cfg"));

            buttons.Controls.Add(
                reset);
        }

        var openInputs =
            CreateButton(
                "Abrir pasta Inputs");

        openInputs.Click +=
            (_, _) =>
                OpenPath(
                    Path.Combine(
                        _contentRoot,
                        "Inputs"));

        buttons.Controls.Add(
            openInputs);

        layout.Controls.Add(
            info,
            0,
            0);
        layout.Controls.Add(
            editor,
            0,
            1);
        layout.Controls.Add(
            buttons,
            0,
            2);

        tab.Controls.Add(
            layout);

        _tabs.TabPages.Add(
            tab);
    }

    private DataGridView CreateOptionsGrid()
    {
        var grid =
            new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                SelectionMode =
                    DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                BackgroundColor =
                    Color.FromArgb(
                        17,
                        23,
                        32),
                BorderStyle =
                    BorderStyle.None,
                GridColor =
                    Color.FromArgb(
                        45,
                        56,
                        70),
                EnableHeadersVisualStyles =
                    false
            };

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

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Configuração",
                ReadOnly = true,
                Width = 285
            });

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Valor",
                Width = 210
            });

        grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText =
                    "Descrição",
                ReadOnly = true,
                AutoSizeMode =
                    DataGridViewAutoSizeColumnMode.Fill
            });

        return grid;
    }

    private bool CommitGridValues()
    {
        foreach (var grid in
                 _optionGrids)
        {
            grid.EndEdit();

            foreach (DataGridViewRow row in
                     grid.Rows)
            {
                if (row.Tag is not
                    OptionDescriptor option)
                {
                    continue;
                }

                try
                {
                    var raw =
                        row.Cells[1].Value;

                    object? value;

                    if (option.ValueType ==
                        typeof(bool))
                    {
                        value =
                            Convert.ToBoolean(
                                raw ?? false);
                    }
                    else if (option.ValueType ==
                             typeof(int))
                    {
                        value =
                            int.Parse(
                                raw?.ToString() ??
                                "0",
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (option.ValueType ==
                             typeof(double))
                    {
                        value =
                            double.Parse(
                                (raw?.ToString() ??
                                 "0")
                                    .Replace(
                                        ',',
                                        '.'),
                                System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        value =
                            raw?.ToString() ??
                            "";
                    }

                    option.Write(
                        value);
                }
                catch
                {
                    MessageBox.Show(
                        this,
                        $"Valor inválido em: {option.Label}",
                        "Configurações",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return false;
                }
            }
        }

        return true;
    }

    private IReadOnlyList<OptionDescriptor> GeneralOptions() =>
    [
        Choice("Idioma", "Idioma dos diálogos, avisos e interface.", () => _options.Language, v => _options.Language = v, DiscoverLanguages()),
        Choice("Venda de passagens", "0=desativada, 1=simples, 2=avançada.", () => _options.TicketSalesMode.ToString(), v => _options.TicketSalesMode = int.Parse(v), ["0", "1", "2"]),
        StringOption("Rádio via internet", "URL usada por veículos equipados com rádio.", () => _options.InternetRadioUrl, v => _options.InternetRadioUrl = v),
        Bool("Transição suave da câmera", "Transições suaves entre câmeras de motorista/passageiro.", () => _options.SmoothDriverViewTransitions, v => _options.SmoothDriverViewTransitions = v),
        Bool("Movimento da cabeça do motorista", "Movimento relativo da cabeça durante aceleração e frenagem.", () => _options.DriverHeadMovement, v => _options.DriverHeadMovement = v),
        Bool("Colisão veículo/paisagem", "Ativa a detecção de colisão com o cenário.", () => _options.VehicleLandscapeCollisions, v => _options.VehicleLandscapeCollisions = v),
        Bool("Colisão com terreno", "Ativa colisão física com o terreno.", () => _options.TerrainCollisions, v => _options.TerrainCollisions = v),
        Bool("Colisão veículo/veículo", "Ativa colisão entre veículos.", () => _options.VehicleToVehicleCollisions, v => _options.VehicleToVehicleCollisions = v),
        Bool("Colisão veículo/pedestres", "Ativa colisão do veículo do jogador com pedestres.", () => _options.UserVehiclePedestrianCollisions, v => _options.UserVehiclePedestrianCollisions = v),
        Bool("Centralização automática da direção", "Compatibilidade com a opção autoCenter do OMSI.", () => _options.AutomaticSteeringCenter, v => _options.AutomaticSteeringCenter = v),
        Bool("Motorista visível no próprio ônibus", "Mostra o motorista em vistas externas.", () => _options.SeeOwnDriver, v => _options.SeeOwnDriver = v),
        Bool("Informações de passageiros/passagens", "Mostra diálogos e avisos de passageiros.", () => _options.ShowTicketPassengerInfo, v => _options.ShowTicketPassengerInfo = v),
        Bool("Usar hora atual", "Usa a hora atual do sistema ao iniciar.", () => _options.UseCurrentTime, v => _options.UseCurrentTime = v),
        Bool("Usar data atual", "Usa a data atual do sistema ao iniciar.", () => _options.UseCurrentDate, v => _options.UseCurrentDate = v),
        Bool("Usar ano atual", "Usa o ano atual do sistema ao iniciar.", () => _options.UseCurrentYear, v => _options.UseCurrentYear = v),
        Integer("Vida útil/desgaste", "Nível de desgaste/lifespan compatível com options.cfg.", () => _options.WearLifespan, v => _options.WearLifespan = v),
        Bool("Embreagem automática", "Permite gerenciamento automático da embreagem.", () => _options.AutomaticClutch, v => _options.AutomaticClutch = v),
        Bool("Análise automática de horário", "Exibe análise de horário conforme o comportamento OMSI.", () => _options.ScheduleAnalysisPopup, v => _options.ScheduleAnalysisPopup = v),
        Bool("Vista alternativa", "Compatibilidade com a opção altView.", () => _options.AlternativeView, v => _options.AlternativeView = v),
        Bool("Velocidade de direção reduzida", "Compatibilidade com redSteerSpd.", () => _options.ReducedSteeringSpeed, v => _options.ReducedSteeringSpeed = v),
        Bool("Prévia de veículo", "Mostra a prévia do veículo quando disponível.", () => _options.ShowVehiclePreview, v => _options.ShowVehiclePreview = v),
        StringOption("Fonte typewriter", "Fonte usada por elementos do OMSI que dependem de font_typewriter.", () => _options.TypewriterFont, v => _options.TypewriterFont = v),
        Bool("Game Controller ativo", "Ativa o uso dos controladores configurados em Inputs\\gamectrler.cfg.", () => _options.GameControllerEnabled, v => _options.GameControllerEnabled = v)
    ];

    private IReadOnlyList<OptionDescriptor> AdvancedOptions() =>
    [
        Bool("Multithreading reduzido", "Compatibilidade com o modo de multithreading reduzido do OMSI 2.", () => _options.ReducedMultithreading, v => _options.ReducedMultithreading = v),
        Bool("Carregar mapa inteiro ao iniciar", "Equivale à opção loadAllTiles.", () => _options.LoadWholeMapAtStart, v => _options.LoadWholeMapAtStart = v),
        Bool("Autosave", "Salva automaticamente o estado quando suportado.", () => _options.AutoSave, v => _options.AutoSave = v),
        Bool("Mostrar mensagens de erro", "Mostra mensagens de diagnóstico durante a execução.", () => _options.ShowErrorMessages, v => _options.ShowErrorMessages = v)
    ];

    private IReadOnlyList<OptionDescriptor> GraphicsOptions() =>
    [
        Integer("FPS alvo", "Limite de atualização/renderização.", () => _options.TargetFps, v => _options.TargetFps = Math.Clamp(v, 10, 240)),
        Integer("Tiles vizinhos", "Quantidade de tiles ativos em cada direção.", () => _options.NeighborTiles, v => _options.NeighborTiles = Math.Clamp(v, 0, 8)),
        Number("Visibilidade máxima (m)", "Distância máxima de objetos.", () => _options.MaximumObjectVisibilityMeters, v => _options.MaximumObjectVisibilityMeters = Math.Max(50, v)),
        Number("Tamanho mínimo do objeto", "Fração mínima do tamanho da tela.", () => _options.MinimumObjectSize, v => _options.MinimumObjectSize = Math.Max(0, v)),
        Number("Tamanho mínimo em reflexos", "Fração mínima para objetos renderizados nos reflexos.", () => _options.MinimumObjectSizeReflections, v => _options.MinimumObjectSizeReflections = Math.Max(0, v)),
        Choice("Reflexos em tempo real", "none, economy ou full.", () => _options.RealTimeReflections, v => _options.RealTimeReflections = v, ["none", "economy", "full"]),
        Bool("Sistemas de partículas", "Chuva, neve, fumaça e partículas do veículo.", () => _options.ParticleSystems, v => _options.ParticleSystems = v),
        Integer("Máx. partículas por emissor", "Limite de partículas por sistema.", () => _options.MaximumParticlesPerEmitter, v => _options.MaximumParticlesPerEmitter = Math.Max(0, v)),
        Bool("Partículas somente no veículo do jogador", "Reduz partículas dos demais veículos.", () => _options.ParticleOnlyOwnVehicle, v => _options.ParticleOnlyOwnVehicle = v),
        Bool("Partículas em reflexos", "Renderiza partículas nos espelhos/reflexos.", () => _options.ParticleSystemsInReflections, v => _options.ParticleSystemsInReflections = v),
        Bool("Efeito de brilho do sol", "Equivale ao Sun Glow do OMSI.", () => _options.SunGlow, v => _options.SunGlow = v),
        Integer("Complexidade máxima de objetos", "Nível de detalhe/complexidade dos objetos.", () => _options.MaximumObjectComplexity, v => _options.MaximumObjectComplexity = Math.Clamp(v, 0, 3)),
        Integer("Complexidade máxima do mapa", "Nível de detalhe do mapa.", () => _options.MaximumMapComplexity, v => _options.MaximumMapComplexity = Math.Clamp(v, 0, 3)),
        Bool("Stencil Buffer Effects", "Base para sombras e efeitos de chuva/água no OMSI.", () => _options.StencilBufferEffects, v => _options.StencilBufferEffects = v),
        Bool("Sombras", "Ativa sombras.", () => _options.Shadows, v => _options.Shadows = v),
        Bool("Reflexos de chuva", "Ativa reflexos em superfícies molhadas.", () => _options.RainReflections, v => _options.RainReflections = v),
        Bool("Pessoas nos reflexos de chuva", "Renderiza pessoas nas superfícies refletivas molhadas.", () => _options.HumansInRainReflections, v => _options.HumansInRainReflections = v)
    ];

    private IReadOnlyList<OptionDescriptor> AdvancedGraphicsOptions() =>
    [
        Number("Reflexo econômico abaixo de FPS", "Troca reflexos Full para Economy abaixo deste FPS.", () => _options.ReflectionEconomyBelowFps, v => _options.ReflectionEconomyBelowFps = v),
        Number("Reflexo Full acima de FPS", "Volta a reflexos Full acima deste FPS.", () => _options.ReflectionFullAboveFps, v => _options.ReflectionFullAboveFps = v),
        Number("Reduzir tiles abaixo de FPS", "Reduz tiles ativos abaixo deste FPS.", () => _options.ReduceTilesBelowFps, v => _options.ReduceTilesBelowFps = v),
        Number("Aumentar tiles acima de FPS", "Restaura tiles acima deste FPS.", () => _options.IncreaseTilesAboveFps, v => _options.IncreaseTilesAboveFps = v),
        Bool("Aspect ratio manual", "Usa proporção de tela definida manualmente.", () => _options.ManualAspectRatioEnabled, v => _options.ManualAspectRatioEnabled = v),
        Number("Aspect ratio", "Ex.: 1.778 para 16:9.", () => _options.ManualAspectRatio, v => _options.ManualAspectRatio = Math.Max(0.5, v)),
        Bool("Apenas texturas Low-Res", "Prioriza as versões de baixa resolução.", () => _options.OnlyLowResolutionTextures, v => _options.OnlyLowResolutionTextures = v),
        Bool("Limitar texturas a 256 px", "Exceto o próprio ônibus.", () => _options.LimitTexturesTo256, v => _options.LimitTexturesTo256 = v),
        Bool("Low-Res em objetos distantes", "Usa texturas reduzidas conforme a distância.", () => _options.LowResolutionTexturesAtDistance, v => _options.LowResolutionTexturesAtDistance = v),
        Number("Memória para texturas High-Res (MB)", "Limite para carregamento de texturas de alta resolução.", () => _options.HighResolutionTextureMemoryMb, v => _options.HighResolutionTextureMemoryMb = Math.Max(64, v)),
        Integer("Tamanho textura de reflexo", "Resolução das texturas de espelho/reflexo.", () => _options.RealTimeReflectionTextureSize, v => _options.RealTimeReflectionTextureSize = Math.Clamp(v, 64, 4096)),
        Bool("Canal Night Map", "Canal de material noturno.", () => _options.MaterialNightMap, v => _options.MaterialNightMap = v),
        Bool("Canal Light Map", "Canal de iluminação.", () => _options.MaterialLightMap, v => _options.MaterialLightMap = v),
        Bool("Canal Light Map do terreno", "Canal de iluminação do terreno.", () => _options.MaterialTerrainLightMap, v => _options.MaterialTerrainLightMap = v),
        Bool("Canal Reflection Map", "Canal de mapa de reflexão.", () => _options.MaterialReflectionMap, v => _options.MaterialReflectionMap = v),
        Bool("Canal Bump Map", "Canal de relevo/bump.", () => _options.MaterialBumpMap, v => _options.MaterialBumpMap = v),
        Integer("Filtro de textura", "Valor de compatibilidade texFilter.", () => _options.TextureFilterMode, v => _options.TextureFilterMode = v),
        Integer("Filtragem anisotrópica", "Nível de filtragem anisotrópica.", () => _options.AnisotropicFiltering, v => _options.AnisotropicFiltering = Math.Clamp(v, 1, 16))
    ];

    private IReadOnlyList<OptionDescriptor> SoundOptions() =>
    [
        Integer("Volume geral (%)", "Volume mestre de todos os efeitos sonoros.", () => _options.MasterVolumePercent, v => _options.MasterVolumePercent = Math.Clamp(v, 0, 100)),
        Integer("Efeito estéreo", "Separação estéreo compatível com o OMSI.", () => _options.StereoEffect, v => _options.StereoEffect = Math.Clamp(v, 0, 100)),
        Integer("Máximo de sons", "Quantidade máxima de sons simultâneos.", () => _options.MaximumSoundCount, v => _options.MaximumSoundCount = Math.Max(0, v)),
        Bool("Efeito Doppler", "Ativa alteração de frequência por movimento relativo.", () => _options.DopplerEffect, v => _options.DopplerEffect = v),
        Bool("Sons de veículos IA", "Ativa sons dos demais veículos.", () => _options.AiVehicleSounds, v => _options.AiVehicleSounds = v),
        Bool("Sons do cenário", "Ativa sons ambientais e de objetos do mapa.", () => _options.ScenerySounds, v => _options.ScenerySounds = v),
        Bool("Reverb", "Ativa efeitos de reverberação.", () => _options.ReverbEffects, v => _options.ReverbEffects = v)
    ];

    private IReadOnlyList<OptionDescriptor> TrafficOptions() =>
    [
        Integer("Veículos não programados", "Máximo de carros/caminhões IA sem horário.", () => _options.MaximumUnscheduledTraffic, v => _options.MaximumUnscheduledTraffic = Math.Max(0, v)),
        Integer("Fator de tráfego rodoviário (%)", "Multiplicador de tráfego não programado.", () => _options.RoadTrafficFactorPercent, v => _options.RoadTrafficFactorPercent = Math.Clamp(v, 0, 200)),
        Integer("Carros estacionados (%)", "Percentual de vagas ocupadas.", () => _options.ParkedCarsPercent, v => _options.ParkedCarsPercent = Math.Clamp(v, 0, 100)),
        Integer("Máximo de pessoas", "Limite total de pedestres e passageiros.", () => _options.MaximumPeople, v => _options.MaximumPeople = Math.Max(0, v)),
        Integer("Passageiros nas paradas (%)", "Multiplicador da quantidade de passageiros.", () => _options.PassengerFactorPercent, v => _options.PassengerFactorPercent = Math.Clamp(v, 0, 300)),
        Integer("Veículos programados", "Máximo de ônibus/trens/trams IA com horários.", () => _options.MaximumScheduledTraffic, v => _options.MaximumScheduledTraffic = Math.Max(0, v)),
        Integer("Prioridade do tráfego programado", "1 a 4, como no OMSI.", () => _options.ScheduledTrafficPriority, v => _options.ScheduledTrafficPriority = Math.Clamp(v, 1, 4)),
        Bool("Usar lista IA reduzida", "Limita a variedade de veículos IA.", () => _options.UseReducedAiList, v => _options.UseReducedAiList = v)
    ];

    private IReadOnlyList<OptionDescriptor> RuntimeOptions() =>
    [
        Bool("VSync", "Sincronização vertical do renderer x64.", () => _options.RuntimeVSync, v => _options.RuntimeVSync = v),
        Bool("Tela cheia sem bordas", "Abre o runtime em borderless fullscreen.", () => _options.RuntimeBorderlessFullscreen, v => _options.RuntimeBorderlessFullscreen = v),
        Integer("Raio de streaming", "Raio de tiles carregados pelo runtime x64.", () => _options.RuntimeStreamingRadius, v => _options.RuntimeStreamingRadius = Math.Clamp(v, 0, 8)),
        Bool("Preferir GPU de hardware", "Tenta Direct3D em hardware antes do fallback WARP.", () => _options.RuntimePreferHardwareGpu, v => _options.RuntimePreferHardwareGpu = v),
        Bool("Mostrar FPS", "Mostra contador de FPS na janela do runtime.", () => _options.RuntimeShowFps, v => _options.RuntimeShowFps = v),
        Bool("Diagnóstico detalhado", "Mantém logs detalhados de carregamento e renderização.", () => _options.RuntimeDiagnostics, v => _options.RuntimeDiagnostics = v)
    ];

    private OptionDescriptor Bool(
        string label,
        string description,
        Func<bool> read,
        Action<bool> write) =>
        new(
            label,
            description,
            typeof(bool),
            () => read(),
            value =>
                write(
                    Convert.ToBoolean(
                        value)));

    private OptionDescriptor Integer(
        string label,
        string description,
        Func<int> read,
        Action<int> write) =>
        new(
            label,
            description,
            typeof(int),
            () => read(),
            value =>
                write(
                    Convert.ToInt32(
                        value)));

    private OptionDescriptor Number(
        string label,
        string description,
        Func<double> read,
        Action<double> write) =>
        new(
            label,
            description,
            typeof(double),
            () => read(),
            value =>
                write(
                    Convert.ToDouble(
                        value)));

    private OptionDescriptor StringOption(
        string label,
        string description,
        Func<string> read,
        Action<string> write) =>
        new(
            label,
            description,
            typeof(string),
            () => read(),
            value =>
                write(
                    value?.ToString() ??
                    ""));

    private OptionDescriptor Choice(
        string label,
        string description,
        Func<string> read,
        Action<string> write,
        IReadOnlyList<string> choices) =>
        new(
            label,
            description,
            typeof(string),
            () => read(),
            value =>
                write(
                    value?.ToString() ??
                    ""),
            choices);

    private string[] DiscoverLanguages()
    {
        var inputs =
            Path.Combine(
                _contentRoot,
                "Inputs");

        if (!Directory.Exists(inputs))
        {
            return
            [
                "PTBR",
                "ENG",
                "DEU"
            ];
        }

        var values =
            Directory
                .EnumerateFiles(
                    inputs,
                    "*.kyb",
                    SearchOption.TopDirectoryOnly)
                .Select(
                    Path.GetFileNameWithoutExtension)
                .Where(
                    static value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    static value =>
                        value!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    static value =>
                        value,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (!values.Contains(
                _options.Language,
                StringComparer.OrdinalIgnoreCase))
        {
            values.Insert(
                0,
                _options.Language);
        }

        return values.ToArray();
    }

    private void LoadPresetList()
    {
        _presetBox.Items.Clear();

        var directory =
            Path.Combine(
                _contentRoot,
                "option_presets");

        if (!Directory.Exists(
                directory))
        {
            _presetBox.Items.Add(
                "(nenhum perfil encontrado)");
            _presetBox.SelectedIndex =
                0;
            _presetBox.Enabled =
                false;
            return;
        }

        foreach (var path in
                 Directory
                     .EnumerateFiles(
                         directory,
                         "*.oop",
                         SearchOption.TopDirectoryOnly)
                     .OrderBy(
                         Path.GetFileName,
                         StringComparer.OrdinalIgnoreCase))
        {
            _presetBox.Items.Add(
                new FileInfo(path));
        }

        _presetBox.DisplayMember =
            nameof(
                FileInfo.Name);

        if (_presetBox.Items.Count > 0)
        {
            _presetBox.SelectedIndex =
                0;
        }
    }

    private void ImportCurrentOmsiOptions()
    {
        var path =
            Path.Combine(
                _contentRoot,
                "options.cfg");

        if (!File.Exists(path))
        {
            MessageBox.Show(
                this,
                "options.cfg não foi encontrado na pasta selecionada.",
                "Configurações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _options =
            OmsiRuntimeOptions.ImportFromFile(
                path,
                _options);

        RebuildOptionTabs();

        SetStatus(
            "options.cfg importado para o perfil do Runtime x64.");
    }

    private void ImportSelectedPreset()
    {
        if (_presetBox.SelectedItem is not
            FileInfo file)
        {
            return;
        }

        _options =
            OmsiRuntimeOptions.ImportFromFile(
                file.FullName,
                _options);

        RebuildOptionTabs();

        SetStatus(
            $"Perfil importado: {file.Name}");
    }

    private void LoadInputFiles()
    {
        LoadEditor(
            _keyboardEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "keyboard.cfg"));

        LoadEditor(
            _controllerEditor,
            Path.Combine(
                _contentRoot,
                "Inputs",
                "gamectrler.cfg"));
    }

    private void LoadEditor(
        RichTextBox editor,
        string path)
    {
        try
        {
            editor.Text =
                File.Exists(path)
                    ? File.ReadAllText(path)
                    : "";

            SetStatus(
                File.Exists(path)
                    ? $"Carregado: {path}"
                    : $"Arquivo não encontrado: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message);
        }
    }

    private void SaveInputFiles()
    {
        ApplyKeyboardVisualEdits();
        ApplyControllerVisualEdits();

        var inputs =
            Path.Combine(
                _contentRoot,
                "Inputs");

        if (!Directory.Exists(inputs))
        {
            MessageBox.Show(
                this,
                "A pasta Inputs não foi encontrada.",
                "Configurações",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        var result =
            MessageBox.Show(
                this,
                "Salvar os editores de Teclado e Game Controller diretamente na pasta Inputs do OMSI?\n\nUma cópia .runtime-backup será criada antes da gravação.",
                "Salvar Inputs",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

        if (result !=
            DialogResult.Yes)
        {
            return;
        }

        SaveWithBackup(
            Path.Combine(
                inputs,
                "keyboard.cfg"),
            _keyboardEditor.Text);

        SaveWithBackup(
            Path.Combine(
                inputs,
                "gamectrler.cfg"),
            _controllerEditor.Text);

        SetStatus(
            "Inputs salvos com backup.");
    }

    private static void SaveWithBackup(
        string path,
        string text)
    {
        if (File.Exists(path))
        {
            File.Copy(
                path,
                path + ".runtime-backup",
                overwrite: true);
        }

        File.WriteAllText(
            path,
            text);
    }

    private void OpenOmsiFolder()
    {
        OpenPath(
            _contentRoot);
    }

    private static void OpenPath(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private Button CreateButton(
        string text)
    {
        var button =
            new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle =
                    FlatStyle.Flat,
                BackColor =
                    Color.FromArgb(
                        31,
                        39,
                        50),
                ForeColor =
                    Color.White,
                Height = 38,
                Margin =
                    new Padding(
                        4)
            };

        button.FlatAppearance.BorderColor =
            Color.FromArgb(
                57,
                72,
                90);

        return button;
    }

    private static string FormatValue(
        object? value) =>
        value switch
        {
            double number =>
                number.ToString(
                    "0.###",
                    System.Globalization.CultureInfo.InvariantCulture),
            _ =>
                value?.ToString() ??
                ""
        };

    private void SetStatus(
        string text)
    {
        _status.Text =
            text;
    }
}
