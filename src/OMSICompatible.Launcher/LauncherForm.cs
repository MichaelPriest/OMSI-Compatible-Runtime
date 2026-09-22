using System.Diagnostics;
using OmsiCompat.Core;
using OmsiCompat.Map;

namespace OMSICompatible.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly MapHeroPanel _hero =
        new();

    private readonly TextBox _contentPathBox =
        new();

    private readonly ComboBox _mapBox =
        new();

    private Button _playButton =
        new();

    private readonly Label _mapCountValue =
        new();

    private readonly Label _runtimeValue =
        new();

    private readonly Label _statusValue =
        new();

    private readonly RichTextBox _logBox =
        new();

    private readonly RuntimeProcessHost _runtime =
        new();

    private readonly LauncherSettings _settings;

    private IReadOnlyList<OmsiMapInfo> _maps =
        Array.Empty<OmsiMapInfo>();

    public LauncherForm(
        string? explicitContentPath)
    {
        _settings =
            LauncherSettings.Load();

        Text =
            "OMSI Compatible Runtime x64";

        StartPosition =
            FormStartPosition.CenterScreen;

        MinimumSize =
            new Size(
                1050,
                760);

        ClientSize =
            new Size(
                1220,
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

        BuildInterface();

        _runtime.OutputReceived +=
            line =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(
                        () => AppendRuntimeLog(line));
                }
            };

        _runtime.Exited +=
            exitCode =>
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(
                        () => RuntimeExited(exitCode));
                }
            };

        var initialPath =
            explicitContentPath ??
            _settings.ContentPath;

        if (!string.IsNullOrWhiteSpace(
                initialPath))
        {
            _contentPathBox.Text =
                initialPath;

            RefreshMaps(
                _settings.MapName);
        }

        FormClosed +=
            (_, _) =>
            {
                SaveSettings();
                _runtime.Dispose();
            };
    }

    private void BuildInterface()
    {
        var root =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding =
                    new Padding(
                        28,
                        24,
                        28,
                        24)
            };

        root.RowStyles.Add(
            new RowStyle(
                SizeType.Absolute,
                300));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));

        root.Controls.Add(
            _hero,
            0,
            0);

        var controlsPanel =
            BuildControlsPanel();

        root.Controls.Add(
            controlsPanel,
            0,
            1);

        var statusCards =
            BuildStatusCards();

        root.Controls.Add(
            statusCards,
            0,
            2);

        var diagnostics =
            BuildDiagnostics();

        root.Controls.Add(
            diagnostics,
            0,
            3);

        Controls.Add(root);

        _hero.SetPresentation(null);
    }

    private Control BuildControlsPanel()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Padding =
                    new Padding(
                        0,
                        18,
                        0,
                        12)
            };

        panel.Controls.Add(
            CreateFieldLabel(
                "PASTA DO CONTEÚDO"),
            0,
            0);

        var contentRow =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true
            };

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                116));

        contentRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                104));

        _contentPathBox.Dock =
            DockStyle.Fill;

        StyleTextBox(
            _contentPathBox);

        var browse =
            CreateButton(
                "Procurar",
                false);

        browse.Click +=
            (_, _) =>
                BrowseForContent();

        var refresh =
            CreateButton(
                "Atualizar",
                false);

        refresh.Click +=
            (_, _) =>
                RefreshMaps(
                    _mapBox.SelectedItem
                        ?.ToString());

        contentRow.Controls.Add(
            _contentPathBox,
            0,
            0);

        contentRow.Controls.Add(
            browse,
            1,
            0);

        contentRow.Controls.Add(
            refresh,
            2,
            0);

        panel.Controls.Add(
            contentRow,
            0,
            1);

        panel.Controls.Add(
            CreateFieldLabel("MAPA"),
            0,
            2);

        var mapPlayRow =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true
            };

        mapPlayRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                100));

        mapPlayRow.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Absolute,
                220));

        _mapBox.Dock =
            DockStyle.Fill;

        _mapBox.DropDownStyle =
            ComboBoxStyle.DropDownList;

        _mapBox.FlatStyle =
            FlatStyle.Flat;

        _mapBox.BackColor =
            Color.FromArgb(
                31,
                36,
                46);

        _mapBox.ForeColor =
            Color.White;

        _mapBox.Font =
            new Font(
                "Segoe UI",
                11.0f);

        _mapBox.SelectedIndexChanged +=
            (_, _) =>
                MapSelectionChanged();

        _playButton =
            CreateButton(
                "▶  JOGAR",
                true);

        _playButton.Enabled = false;

        _playButton.Click +=
            (_, _) =>
                StartRuntime();

        mapPlayRow.Controls.Add(
            _mapBox,
            0,
            0);

        mapPlayRow.Controls.Add(
            _playButton,
            1,
            0);

        panel.Controls.Add(
            mapPlayRow,
            0,
            3);

        var shortcuts =
            new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = true,
                Padding =
                    new Padding(
                        0,
                        12,
                        0,
                        0)
            };

        AddShortcut(
            shortcuts,
            "Pasta OMSI",
            () =>
                OpenFolder(
                    _contentPathBox.Text));

        AddShortcut(
            shortcuts,
            "Mapas",
            () =>
                OpenFolder(
                    Path.Combine(
                        _contentPathBox.Text,
                        "maps")));

        AddShortcut(
            shortcuts,
            "Veículos",
            () =>
                OpenFolder(
                    Path.Combine(
                        _contentPathBox.Text,
                        "Vehicles")));

        AddShortcut(
            shortcuts,
            "Logs",
            () =>
                OpenFolder(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Logs")));

        AddShortcut(
            shortcuts,
            "Criar atalho no Desktop",
            CreateDesktopShortcut);

        panel.Controls.Add(
            shortcuts,
            0,
            4);

        return panel;
    }

    private Control BuildStatusCards()
    {
        var row =
            new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Padding =
                    new Padding(
                        0,
                        4,
                        0,
                        14)
            };

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.33f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.33f));

        row.ColumnStyles.Add(
            new ColumnStyle(
                SizeType.Percent,
                33.34f));

        row.Controls.Add(
            CreateStatusCard(
                "MAPAS",
                _mapCountValue),
            0,
            0);

        row.Controls.Add(
            CreateStatusCard(
                "RUNTIME",
                _runtimeValue),
            1,
            0);

        row.Controls.Add(
            CreateStatusCard(
                "STATUS",
                _statusValue),
            2,
            0);

        _mapCountValue.Text = "—";
        _runtimeValue.Text = "x64 · D3D11";
        _statusValue.Text = "Aguardando";

        return row;
    }

    private Control BuildDiagnostics()
    {
        var panel =
            new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };

        panel.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        panel.RowStyles.Add(
            new RowStyle(
                SizeType.Percent,
                100));

        panel.Controls.Add(
            CreateFieldLabel(
                "STATUS / DIAGNÓSTICO"),
            0,
            0);

        _logBox.Dock =
            DockStyle.Fill;

        _logBox.ReadOnly = true;

        _logBox.BorderStyle =
            BorderStyle.None;

        _logBox.BackColor =
            Color.FromArgb(
                8,
                11,
                15);

        _logBox.ForeColor =
            Color.FromArgb(
                190,
                203,
                219);

        _logBox.Font =
            new Font(
                "Cascadia Mono",
                9.0f);

        panel.Controls.Add(
            _logBox,
            0,
            1);

        return panel;
    }

    private static Control CreateStatusCard(
        string caption,
        Label value)
    {
        var panel =
            new Panel
            {
                Dock = DockStyle.Fill,
                Height = 72,
                BackColor =
                    Color.FromArgb(
                        24,
                        29,
                        37),
                Margin =
                    new Padding(
                        0,
                        0,
                        10,
                        0)
            };

        var captionLabel =
            new Label
            {
                Text = caption,
                AutoSize = true,
                ForeColor =
                    Color.FromArgb(
                        112,
                        132,
                        154),
                Location =
                    new Point(
                        14,
                        11),
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        8.0f)
            };

        value.AutoSize = true;
        value.ForeColor = Color.White;
        value.Location =
            new Point(
                14,
                34);
        value.Font =
            new Font(
                "Segoe UI Semibold",
                12.0f);

        panel.Controls.Add(
            captionLabel);

        panel.Controls.Add(
            value);

        return panel;
    }

    private static Label CreateFieldLabel(
        string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            ForeColor =
                Color.FromArgb(
                    110,
                    133,
                    159),
            Font =
                new Font(
                    "Segoe UI Semibold",
                    8.5f),
            Margin =
                new Padding(
                    0,
                    8,
                    0,
                    6)
        };

    private static void StyleTextBox(
        TextBox box)
    {
        box.BorderStyle =
            BorderStyle.FixedSingle;

        box.BackColor =
            Color.FromArgb(
                31,
                36,
                46);

        box.ForeColor =
            Color.White;

        box.Font =
            new Font(
                "Segoe UI",
                10.5f);

        box.MinimumSize =
            new Size(
                100,
                38);
    }

    private static Button CreateButton(
        string text,
        bool primary)
    {
        var button =
            new Button
            {
                Text = text,
                Height = primary
                    ? 48
                    : 38,
                Dock = DockStyle.Fill,
                FlatStyle =
                    FlatStyle.Flat,
                ForeColor =
                    Color.White,
                BackColor =
                    primary
                        ? Color.FromArgb(
                            0,
                            126,
                            214)
                        : Color.FromArgb(
                            39,
                            46,
                            58),
                Cursor =
                    Cursors.Hand,
                Font =
                    new Font(
                        "Segoe UI Semibold",
                        primary
                            ? 12.0f
                            : 9.5f),
                Margin =
                    new Padding(
                        8,
                        0,
                        0,
                        0)
            };

        button.FlatAppearance.BorderSize =
            0;

        return button;
    }

    private void AddShortcut(
        Control parent,
        string text,
        Action action)
    {
        var button =
            CreateButton(
                text,
                false);

        button.AutoSize = true;

        button.Padding =
            new Padding(
                10,
                2,
                10,
                2);

        button.Margin =
            new Padding(
                0,
                0,
                8,
                8);

        button.Click +=
            (_, _) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ShowError(ex.Message);
                }
            };

        parent.Controls.Add(button);
    }

    private void BrowseForContent()
    {
        using var dialog =
            new FolderBrowserDialog
            {
                Description =
                    "Selecione a pasta raiz do conteúdo OMSI",
                ShowNewFolderButton = false
            };

        if (Directory.Exists(
                _contentPathBox.Text))
        {
            dialog.SelectedPath =
                _contentPathBox.Text;
        }

        if (dialog.ShowDialog(this) !=
            DialogResult.OK)
        {
            return;
        }

        _contentPathBox.Text =
            dialog.SelectedPath;

        RefreshMaps(null);
    }

    private void RefreshMaps(
        string? selectMap)
    {
        _mapBox.Items.Clear();
        _maps =
            Array.Empty<OmsiMapInfo>();

        if (!OmsiContentRoot.TryCreate(
                _contentPathBox.Text,
                out var contentRoot,
                out var error) ||
            contentRoot is null)
        {
            _hero.SetPresentation(null);
            _mapCountValue.Text = "0";
            _statusValue.Text =
                "Pasta inválida";
            _playButton.Enabled = false;

            AppendRuntimeLog(
                error);

            return;
        }

        _maps =
            MapDiscovery.Discover(
                contentRoot);

        foreach (var map in _maps)
        {
            _mapBox.Items.Add(
                map.FolderName);
        }

        _mapCountValue.Text =
            _maps.Count.ToString("N0");

        _statusValue.Text =
            _maps.Count > 0
                ? "Pronto"
                : "Sem mapas";

        if (_maps.Count == 0)
        {
            _hero.SetPresentation(null);
            _playButton.Enabled = false;
            return;
        }

        var target =
            _maps.FirstOrDefault(
                map =>
                    string.Equals(
                        map.FolderName,
                        selectMap,
                        StringComparison.OrdinalIgnoreCase));

        _mapBox.SelectedItem =
            target?.FolderName ??
            _maps[0].FolderName;

        AppendRuntimeLog(
            $"{_maps.Count:N0} mapa(s) encontrado(s).");
    }

    private void MapSelectionChanged()
    {
        var map =
            SelectedMap();

        _playButton.Enabled =
            map is not null &&
            !_runtime.IsRunning;

        _hero.SetPresentation(
            map is null
                ? null
                : MapPresentationReader.Read(
                    map));

        SaveSettings();
    }

    private OmsiMapInfo? SelectedMap()
    {
        var name =
            _mapBox.SelectedItem
                ?.ToString();

        return _maps.FirstOrDefault(
            map =>
                string.Equals(
                    map.FolderName,
                    name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void StartRuntime()
    {
        if (_runtime.IsRunning)
        {
            return;
        }

        var map =
            SelectedMap();

        if (map is null)
        {
            ShowError(
                "Selecione um mapa.");
            return;
        }

        if (!Directory.Exists(
                _contentPathBox.Text))
        {
            ShowError(
                "A pasta de conteúdo não existe.");
            return;
        }

        try
        {
            SaveSettings();

            _statusValue.Text =
                "Iniciando...";

            _playButton.Enabled = false;

            AppendRuntimeLog(
                $"Iniciando {map.FolderName}...");

            if (!_runtime.Start(
                    _contentPathBox.Text,
                    map.FolderName))
            {
                throw new InvalidOperationException(
                    "O runtime não pôde ser iniciado.");
            }

            _statusValue.Text =
                "Em execução";
        }
        catch (Exception ex)
        {
            _statusValue.Text =
                "Erro";
            _playButton.Enabled = true;
            ShowError(ex.Message);
        }
    }

    private void RuntimeExited(
        int exitCode)
    {
        _statusValue.Text =
            exitCode == 0
                ? "Encerrado"
                : $"Erro {exitCode}";

        _playButton.Enabled =
            SelectedMap() is not null;

        AppendRuntimeLog(
            $"Runtime encerrado com código {exitCode}.");
    }

    private void CreateDesktopShortcut()
    {
        var path =
            DesktopShortcutCreator.Create();

        AppendRuntimeLog(
            $"Atalho criado: {path}");

        MessageBox.Show(
            this,
            "Atalho criado na Área de Trabalho.",
            "OMSI Compatible Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenFolder(
        string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Pasta não encontrada: {path}");
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private void AppendRuntimeLog(
        string line)
    {
        if (_logBox.TextLength > 0)
        {
            _logBox.AppendText(
                Environment.NewLine);
        }

        _logBox.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {line}");

        _logBox.SelectionStart =
            _logBox.TextLength;

        _logBox.ScrollToCaret();
    }

    private void SaveSettings()
    {
        new LauncherSettings(
            _contentPathBox.Text,
            _mapBox.SelectedItem
                ?.ToString())
            .Save();
    }

    private void ShowError(
        string message)
    {
        MessageBox.Show(
            this,
            message,
            "OMSI Compatible Runtime",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
