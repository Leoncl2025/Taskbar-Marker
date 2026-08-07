using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarMarker;

/// <summary>
/// Edits one rule. The button list is read live from the taskbar so a target can be picked
/// instead of typed, and the right matching field is chosen automatically.
/// </summary>
internal sealed class RuleEditDialog : Form
{
    private static readonly string[] Palette =
    {
        "#E53935", "#D81B60", "#8E24AA", "#5E35B1", "#3949AB", "#1E88E5",
        "#00ACC1", "#00897B", "#43A047", "#FDD835", "#FB8C00", "#6D4C41",
    };

    private readonly ListView _buttonList;
    private readonly TextBox _matchBox;
    private readonly TextBox _matchAppIdBox;
    private readonly TextBox _labelBox;
    private readonly Panel _colorPreview;
    private readonly Label _hint;
    private readonly Button _okButton;

    private Color _color;

    public RuleDto Rule { get; }

    public RuleEditDialog(RuleDto rule, string title)
    {
        Rule = rule.Clone();
        _color = Settings.ParseColor(Rule.Color);

        Text = title;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        ClientSize = new Size(840, 640);
        MinimumSize = new Size(720, 600);
        SizeGripStyle = SizeGripStyle.Show;

        var pickLabel = new Label
        {
            Text = "Pick a taskbar button (or leave it and type a pattern below):",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        _buttonList = new ListView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
        };
        _buttonList.Columns.Add("Button", 310);
        _buttonList.Columns.Add("App id", 470);
        _buttonList.SelectedIndexChanged += OnButtonSelected;
        _buttonList.SizeChanged += (_, _) => ResizeButtonColumns();

        var refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            Margin = new Padding(0, 0, 8, 0),
        };
        refreshButton.Click += async (_, _) => await LoadButtonsAsync();

        _hint = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var matchLabel = new Label
        {
            Text = "Match name (regex)",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _matchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Text = Rule.Match ?? "",
        };
        _matchBox.TextChanged += (_, _) => UpdateOkState();

        var appIdLabel = new Label
        {
            Text = "Match app id (regex)",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _matchAppIdBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            Text = Rule.MatchAppId ?? "",
        };
        _matchAppIdBox.TextChanged += (_, _) => UpdateOkState();

        var labelLabel = new Label
        {
            Text = "Label (optional)",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _labelBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = Rule.Label ?? "",
        };

        var colorLabel = new Label
        {
            Text = "Color",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        _colorPreview = new Panel
        {
            Size = new Size(40, 23),
            MinimumSize = new Size(40, 23),
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            BackColor = _color,
            BorderStyle = BorderStyle.FixedSingle,
        };

        var swatches = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 0, 8, 0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
        };
        foreach (string hex in Palette)
        {
            Color swatchColor = Settings.ParseColor(hex);
            var swatch = new Panel
            {
                Size = new Size(14, 22),
                Margin = new Padding(1),
                BackColor = swatchColor,
                Cursor = Cursors.Hand,
            };
            swatch.Click += (_, _) => SetColor(swatchColor);
            swatches.Controls.Add(swatch);
        }

        var customColorButton = new Button
        {
            Text = "Custom...",
            AutoSize = true,
            MinimumSize = new Size(100, 32),
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        customColorButton.Click += OnPickCustomColor;

        _okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(85, 32),
            Margin = new Padding(0, 0, 8, 0),
        };
        _okButton.Click += OnOk;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(85, 32),
            Margin = Padding.Empty,
        };

        var pickerFooter = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        pickerFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pickerFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pickerFooter.Controls.Add(refreshButton, 0, 0);
        pickerFooter.Controls.Add(_hint, 1, 0);

        var detailsLayout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
        };
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsLayout.Controls.Add(labelLabel, 0, 0);
        detailsLayout.Controls.Add(_labelBox, 0, 1);
        detailsLayout.Controls.Add(colorLabel, 2, 0);
        detailsLayout.Controls.Add(_colorPreview, 2, 1);
        detailsLayout.Controls.Add(swatches, 3, 1);
        detailsLayout.Controls.Add(customColorButton, 4, 1);

        var buttonLayout = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonLayout.Controls.Add(_okButton, 1, 0);
        buttonLayout.Controls.Add(cancelButton, 2, 0);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 9,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(pickLabel, 0, 0);
        layout.Controls.Add(_buttonList, 0, 1);
        layout.Controls.Add(pickerFooter, 0, 2);
        layout.Controls.Add(matchLabel, 0, 3);
        layout.Controls.Add(_matchBox, 0, 4);
        layout.Controls.Add(appIdLabel, 0, 5);
        layout.Controls.Add(_matchAppIdBox, 0, 6);
        layout.Controls.Add(detailsLayout, 0, 7);
        layout.Controls.Add(buttonLayout, 0, 8);
        Controls.Add(layout);

        FitButtonToFont(refreshButton, 90);
        FitButtonToFont(customColorButton, 100);
        FitButtonToFont(_okButton, 85);
        FitButtonToFont(cancelButton, 85);

        AcceptButton = _okButton;
        CancelButton = cancelButton;
        ResizeButtonColumns();
        UpdateOkState();
    }

    private static void FitButtonToFont(Button button, int minimumWidth)
    {
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(minimumWidth, 32);
        button.Padding = new Padding(10, 3, 10, 3);
    }

    private void ResizeButtonColumns()
    {
        if (_buttonList.Columns.Count < 2)
            return;

        int available = Math.Max(320,
            _buttonList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
        int nameWidth = Math.Clamp((int)(available * 0.4), 240, 520);
        _buttonList.Columns[0].Width = nameWidth;
        _buttonList.Columns[1].Width = Math.Max(100, available - nameWidth);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadButtonsAsync();
    }

    private async Task LoadButtonsAsync()
    {
        _hint.Text = "Reading the taskbar...";
        _buttonList.Enabled = false;

        List<TaskbarButton> buttons = await Task.Run(static () =>
        {
            var all = new List<TaskbarButton>();
            foreach (IntPtr hwnd in Native.FindTaskbars())
                all.AddRange(TaskbarScanner.Scan(hwnd));

            // The same window gets a button on every monitor's taskbar; show it once.
            return all
                .GroupBy(b => (b.Name, b.AppId))
                .Select(g => g.First())
                .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });

        _buttonList.BeginUpdate();
        _buttonList.Items.Clear();
        foreach (TaskbarButton button in buttons)
        {
            var item = new ListViewItem(button.Name.Replace("\r", " ").Replace("\n", " "))
            {
                Tag = button,
                ToolTipText = $"{button.Name}\n{button.AppId}",
            };
            item.SubItems.Add(button.AppId);
            _buttonList.Items.Add(item);
        }
        _buttonList.EndUpdate();
        _buttonList.Enabled = true;

        _hint.Text = buttons.Count == 0
            ? "No running-app buttons found."
            : $"{buttons.Count} button(s). Selecting one fills in the pattern below.";
    }

    private void OnButtonSelected(object? sender, EventArgs e)
    {
        if (_buttonList.SelectedItems.Count == 0 || _buttonList.SelectedItems[0].Tag is not TaskbarButton picked)
            return;

        // If several buttons share this name, the app id is the only thing that tells them apart,
        // so match on that instead of the name.
        int sameName = _buttonList.Items.Cast<ListViewItem>()
            .Count(i => i.Tag is TaskbarButton b &&
                        string.Equals(b.Name, picked.Name, StringComparison.OrdinalIgnoreCase));

        if (sameName > 1 && !string.IsNullOrEmpty(picked.AppId))
        {
            _matchBox.Text = "";
            _matchAppIdBox.Text = EscapeRegex(picked.AppId);
            _hint.Text = $"{sameName} buttons share this name - matching on app id instead.";
        }
        else
        {
            _matchBox.Text = EscapeRegex(picked.Name);
            _matchAppIdBox.Text = "";
            _hint.Text = "Matching on the button name.";
        }

        if (_labelBox.Text.Length == 0)
            _labelBox.Text = SuggestLabel(picked.Name);
    }

    private static string EscapeRegex(string value) =>
        System.Text.RegularExpressions.Regex.Escape(value);

    private static string SuggestLabel(string name)
    {
        // "Remote Desktop - 1 running window" -> "Remote Desktop"
        int dash = name.IndexOf(" - ", StringComparison.Ordinal);
        string trimmed = dash > 0 ? name[..dash] : name;
        return trimmed.Length > 20 ? trimmed[..20] : trimmed;
    }

    private void SetColor(Color color)
    {
        _color = color;
        _colorPreview.BackColor = color;
    }

    private void OnPickCustomColor(object? sender, EventArgs e)
    {
        using var dialog = new ColorDialog { Color = _color, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            SetColor(dialog.Color);
    }

    private void UpdateOkState() =>
        _okButton.Enabled = _matchBox.Text.Trim().Length > 0 || _matchAppIdBox.Text.Trim().Length > 0;

    private void OnOk(object? sender, EventArgs e)
    {
        string match = _matchBox.Text.Trim();
        string appId = _matchAppIdBox.Text.Trim();

        foreach ((string pattern, string field) in new[] { (match, "name"), (appId, "app id") })
        {
            if (pattern.Length == 0)
                continue;
            try
            {
                _ = new System.Text.RegularExpressions.Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, $"The {field} pattern is not a valid regex:\n{ex.Message}",
                    "Taskbar Marker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
        }

        Rule.Match = match.Length == 0 ? null : match;
        Rule.MatchAppId = appId.Length == 0 ? null : appId;
        Rule.Label = _labelBox.Text.Trim() is { Length: > 0 } label ? label : null;
        Rule.Color = Settings.ToHex(_color);
    }
}
