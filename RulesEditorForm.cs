using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarMarker;

/// <summary>
/// Lists the configured rules and lets them be added, edited, reordered and removed.
/// Changes are written straight to rules.json, which the running app picks up.
/// </summary>
internal sealed class RulesEditorForm : Form
{
    private readonly ListView _list;
    private readonly Button _editButton;
    private readonly Button _removeButton;
    private readonly Button _upButton;
    private readonly Button _downButton;
    private readonly Config _config;

    /// <summary>Raised whenever the rules were written to disk, so the overlay can refresh.</summary>
    public event Action<Config>? Applied;

    public RulesEditorForm(Config config)
    {
        _config = config.Clone();

        Text = "Taskbar Marker - rules";
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(720, 380);
        MinimumSize = new Size(620, 320);
        Font = SystemFonts.MessageBoxFont!;

        _list = new ListView
        {
            Location = new Point(12, 12),
            Size = new Size(576, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            OwnerDraw = false,
        };
        _list.Columns.Add("", 30);
        _list.Columns.Add("Label", 100);
        _list.Columns.Add("Match name", 175);
        _list.Columns.Add("Match app id", 250);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => EditSelected();

        var addButton = MakeButton("Add...", 0);
        addButton.Click += (_, _) => AddRule();

        _editButton = MakeButton("Edit...", 1);
        _editButton.Click += (_, _) => EditSelected();

        _removeButton = MakeButton("Remove", 2);
        _removeButton.Click += (_, _) => RemoveSelected();

        _upButton = MakeButton("Up", 3);
        _upButton.Click += (_, _) => MoveSelected(-1);

        _downButton = MakeButton("Down", 4);
        _downButton.Click += (_, _) => MoveSelected(1);

        var order = new Label
        {
            Text = "Rules are evaluated top to bottom; the first match wins.",
            Location = new Point(12, 340),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
        };

        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(598, 302),
            Size = new Size(110, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK,
        };

        Controls.AddRange(new Control[]
        {
            _list, addButton, _editButton, _removeButton, _upButton, _downButton, order, closeButton,
        });

        AcceptButton = closeButton;
        Reload();
    }

    private Button MakeButton(string text, int row)
    {
        return new Button
        {
            Text = text,
            Location = new Point(598, 12 + row * 34),
            Size = new Size(110, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
    }

    private void Reload()
    {
        int selected = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (RuleDto rule in _config.Rules)
        {
            var item = new ListViewItem("") { UseItemStyleForSubItems = false };
            item.SubItems.Add(rule.Label ?? "");
            item.SubItems.Add(rule.Match ?? "");
            item.SubItems.Add(rule.MatchAppId ?? "");
            // The first, empty column doubles as the color swatch.
            item.BackColor = Settings.ParseColor(rule.Color);
            item.SubItems[1].BackColor = SystemColors.Window;
            item.SubItems[2].BackColor = SystemColors.Window;
            item.SubItems[3].BackColor = SystemColors.Window;
            _list.Items.Add(item);
        }
        _list.EndUpdate();

        if (selected >= 0 && selected < _list.Items.Count)
            _list.Items[selected].Selected = true;

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        int index = _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;
        _editButton.Enabled = index >= 0;
        _removeButton.Enabled = index >= 0;
        _upButton.Enabled = index > 0;
        _downButton.Enabled = index >= 0 && index < _config.Rules.Count - 1;
    }

    private void AddRule()
    {
        using var dialog = new RuleEditDialog(new RuleDto(), "Add rule");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _config.Rules.Add(dialog.Rule);
        Reload();
        _list.Items[^1].Selected = true;
        Apply();
    }

    private void EditSelected()
    {
        if (_list.SelectedIndices.Count == 0)
            return;

        int index = _list.SelectedIndices[0];
        using var dialog = new RuleEditDialog(_config.Rules[index], "Edit rule");
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _config.Rules[index] = dialog.Rule;
        Reload();
        Apply();
    }

    private void RemoveSelected()
    {
        if (_list.SelectedIndices.Count == 0)
            return;

        _config.Rules.RemoveAt(_list.SelectedIndices[0]);
        Reload();
        Apply();
    }

    private void MoveSelected(int delta)
    {
        if (_list.SelectedIndices.Count == 0)
            return;

        int index = _list.SelectedIndices[0];
        int target = index + delta;
        if (target < 0 || target >= _config.Rules.Count)
            return;

        (_config.Rules[index], _config.Rules[target]) = (_config.Rules[target], _config.Rules[index]);
        Reload();
        _list.Items[target].Selected = true;
        Apply();
    }

    private void Apply() => Applied?.Invoke(_config);
}
