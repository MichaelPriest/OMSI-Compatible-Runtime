using System.Drawing;
using System.Windows.Forms;

namespace OMSICompatible.Renderer.D3D11;

/// <summary>
/// Player-selectable line, route and destination from real OMSI scheduled
/// routes. The default ESC flow never invents road paths or destinations.
/// </summary>
public sealed record RuntimeQuickRouteOption(
    string Line,
    string RouteCode,
    string Destination)
{
    public string Label =>
        $"{Line}  →  {Destination}    ·    rota {RouteCode}";
}

internal sealed class RuntimeQuickRouteDialog : Form
{
    private readonly RuntimeQuickRouteOption[] _options;
    private readonly TextBox _search;
    private readonly ListBox _routes;
    private readonly Label _status;
    private readonly Button _confirm;

    public RuntimeQuickRouteOption? SelectedRoute { get; private set; }

    public RuntimeQuickRouteDialog(
        IReadOnlyList<RuntimeQuickRouteOption> options)
    {
        _options = options?.ToArray() ??
            throw new ArgumentNullException(nameof(options));

        Text = "OMSI · Seleção rápida de rota";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(590, 460);
        BackColor = Color.FromArgb(23, 31, 45);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        var heading = new Label
        {
            Text = "LINHA E DESTINO",
            ForeColor = Color.FromArgb(142, 218, 255),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            Left = 22,
            Top = 22,
            Width = 540,
            Height = 34
        };

        var description = new Label
        {
            Text = "Escolha uma rota real do mapa. O GPS será atualizado automaticamente.",
            ForeColor = Color.FromArgb(192, 203, 219),
            Left = 22,
            Top = 60,
            Width = 545,
            Height = 25
        };

        _search = new TextBox
        {
            PlaceholderText = "Buscar linha, rota ou destino...",
            Left = 22,
            Top = 96,
            Width = 545,
            Height = 31,
            BackColor = Color.FromArgb(35, 47, 64),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        _routes = new ListBox
        {
            Left = 22,
            Top = 139,
            Width = 545,
            Height = 225,
            IntegralHeight = false,
            BackColor = Color.FromArgb(33, 44, 59),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ItemHeight = 22,
            DisplayMember = nameof(RuntimeQuickRouteOption.Label)
        };

        _status = new Label
        {
            Left = 22,
            Top = 372,
            Width = 545,
            Height = 23,
            ForeColor = Color.FromArgb(187, 200, 218)
        };

        _confirm = new Button
        {
            Text = "USAR ROTA",
            Left = 422,
            Top = 408,
            Width = 145,
            Height = 35,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(28, 124, 176),
            ForeColor = Color.White
        };
        _confirm.FlatAppearance.BorderSize = 0;
        _confirm.Click += (_, _) => Confirm();
        _routes.DoubleClick += (_, _) => Confirm();
        _search.TextChanged += (_, _) => Refilter();
        _routes.SelectedIndexChanged += (_, _) =>
            _confirm.Enabled = _routes.SelectedItem is not null;
        AcceptButton = _confirm;

        var cancel = new Button
        {
            Text = "CANCELAR",
            Left = 295,
            Top = 408,
            Width = 115,
            Height = 35,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(65, 76, 94)
        };
        cancel.FlatAppearance.BorderSize = 0;
        CancelButton = cancel;

        Controls.AddRange(
            [heading, description, _search, _routes, _status, cancel, _confirm]);
        Refilter();
        Shown += (_, _) => _search.Focus();
    }

    private void Refilter()
    {
        var term = _search.Text.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? _options
            : _options.Where(option =>
                option.Line.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                option.RouteCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                option.Destination.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        _routes.BeginUpdate();
        try
        {
            _routes.Items.Clear();
            _routes.Items.AddRange(filtered.Cast<object>().ToArray());
        }
        finally
        {
            _routes.EndUpdate();
        }

        if (_routes.Items.Count > 0)
        {
            _routes.SelectedIndex = 0;
        }
        _status.Text = _options.Length == 0
            ? "Nenhuma rota navegável foi encontrada no timetable deste mapa."
            : $"{filtered.Length} rota(s) encontrada(s) de {_options.Length}.";
        _confirm.Enabled = _routes.SelectedItem is not null;
    }

    private void Confirm()
    {
        if (_routes.SelectedItem is not RuntimeQuickRouteOption chosen)
        {
            return;
        }
        SelectedRoute = chosen;
        DialogResult = DialogResult.OK;
        Close();
    }
}
