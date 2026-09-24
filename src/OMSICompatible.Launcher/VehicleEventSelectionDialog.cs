namespace OMSICompatible.Launcher;

internal sealed class VehicleEventSelectionDialog :
    Form
{
    private readonly TextBox _search =
        new();
    private readonly ListBox _events =
        new();
    private readonly IReadOnlyList<string> _allEvents;

    public string? SelectedTrigger =>
        _events.SelectedItem
            ?.ToString();

    public VehicleEventSelectionDialog(
        IReadOnlyList<string> triggers)
    {
        _allEvents =
            triggers;

        Text =
            "Adicionar evento de veículo";
        StartPosition =
            FormStartPosition.CenterParent;
        ClientSize =
            new Size(
                720,
                620);
        MinimumSize =
            new Size(
                560,
                440);
        BackColor =
            Color.FromArgb(
                17,
                23,
                32);
        ForeColor =
            Color.White;
        Font =
            new Font(
                "Segoe UI",
                10);

        var root =
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
                        16)
            };

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
        root.RowStyles.Add(
            new RowStyle(
                SizeType.AutoSize));

        root.Controls.Add(
            new Label
            {
                AutoSize =
                    true,
                Text =
                    "Eventos encontrados nos scripts dos veículos instalados",
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

        _search.Dock =
            DockStyle.Fill;
        _search.PlaceholderText =
            "Buscar trigger...";
        _search.BackColor =
            Color.FromArgb(
                23,
                29,
                38);
        _search.ForeColor =
            Color.White;
        _search.TextChanged +=
            (_, _) =>
                RefreshList();

        root.Controls.Add(
            _search,
            0,
            1);

        _events.Dock =
            DockStyle.Fill;
        _events.BackColor =
            Color.FromArgb(
                20,
                27,
                37);
        _events.ForeColor =
            Color.White;
        _events.IntegralHeight =
            false;
        _events.DoubleClick +=
            (_, _) =>
            {
                if (_events.SelectedItem is not null)
                {
                    DialogResult =
                        DialogResult.OK;
                    Close();
                }
            };

        root.Controls.Add(
            _events,
            0,
            2);

        var buttons =
            new FlowLayoutPanel
            {
                Dock =
                    DockStyle.Fill,
                AutoSize =
                    true,
                FlowDirection =
                    FlowDirection.RightToLeft,
                Padding =
                    new Padding(
                        0,
                        10,
                        0,
                        0)
            };

        var add =
            CreateButton(
                "Adicionar");

        add.Click +=
            (_, _) =>
            {
                if (_events.SelectedItem is null)
                {
                    return;
                }

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

        buttons.Controls.Add(
            add);
        buttons.Controls.Add(
            cancel);

        root.Controls.Add(
            buttons,
            0,
            3);

        Controls.Add(
            root);

        RefreshList();
    }

    private void RefreshList()
    {
        var filter =
            _search.Text.Trim();

        var selected =
            SelectedTrigger;

        _events.BeginUpdate();

        try
        {
            _events.Items.Clear();

            foreach (var trigger in
                     _allEvents)
            {
                if (filter.Length > 0 &&
                    !trigger.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _events.Items.Add(
                    trigger);
            }

            if (selected is not null)
            {
                _events.SelectedItem =
                    selected;
            }

            if (_events.SelectedIndex < 0 &&
                _events.Items.Count > 0)
            {
                _events.SelectedIndex =
                    0;
            }
        }
        finally
        {
            _events.EndUpdate();
        }
    }

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
                    38,
                Padding =
                    new Padding(
                        12,
                        0,
                        12,
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
