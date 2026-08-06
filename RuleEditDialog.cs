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
        ClientSize = new Size(840, 640);
        MinimumSize = new Size(720, 600);
        SizeGripStyle = SizeGripStyle.Show;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;

        var pickLabel = new Label
        {
            Text = "Pick a taskbar button (or leave it and type a pattern below):",
            Location = new Point(12, 12),
            AutoSize = true,
        };

        _buttonList = new ListView
        {
            Location = new Point(12, 34),
            Size = new Size(816, 315),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
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
            Location = new Point(12, 357),
            Size = new Size(90, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        refreshButton.Click += async (_, _) => await LoadButtonsAsync();

        _hint = new Label
        {
            Location = new Point(110, 360),
            Size = new Size(718, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
        };

        var matchLabel = new Label
        {
            Text = "Match name (regex)",
            Location = new Point(12, 397),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _matchBox = new TextBox
        {
            Location = new Point(12, 417),
            Size = new Size(816, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = Rule.Match ?? "",
        };
        _matchBox.TextChanged += (_, _) => UpdateOkState();

        var appIdLabel = new Label
        {
            Text = "Match app id (regex)",
            Location = new Point(12, 453),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _matchAppIdBox = new TextBox
        {
            Location = new Point(12, 473),
            Size = new Size(816, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = Rule.MatchAppId ?? "",
        };
        _matchAppIdBox.TextChanged += (_, _) => UpdateOkState();

        var labelLabel = new Label
        {
            Text = "Label (optional)",
            Location = new Point(12, 509),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _labelBox = new TextBox
        {
            Location = new Point(12, 529),
            Size = new Size(260, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = Rule.Label ?? "",
        };

        var colorLabel = new Label
        {
            Text = "Color",
            Location = new Point(292, 509),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _colorPreview = new Panel
        {
            Location = new Point(292, 529),
            Size = new Size(40, 23),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            BackColor = _color,
            BorderStyle = BorderStyle.FixedSingle,
        };

        var swatches = new FlowLayoutPanel
        {
            Location = new Point(340, 527),
            Size = new Size(194, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
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
            Location = new Point(728, 527),
            Size = new Size(100, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        customColorButton.Click += OnPickCustomColor;

        _okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(638, 596),
            Size = new Size(85, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        _okButton.Click += OnOk;

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(743, 596),
            Size = new Size(85, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        Controls.AddRange(new Control[]
        {
            pickLabel, _buttonList, refreshButton, _hint,
            matchLabel, _matchBox, appIdLabel, _matchAppIdBox,
            labelLabel, _labelBox, colorLabel, _colorPreview, swatches, customColorButton,
            _okButton, cancelButton,
        });

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
        int bottom = button.Bottom;
        button.Padding = new Padding(10, 3, 10, 3);
        Size preferred = button.GetPreferredSize(Size.Empty);
        button.Size = new Size(
            Math.Max(minimumWidth, preferred.Width),
            Math.Max(32, preferred.Height));
        button.Top = bottom - button.Height;
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
