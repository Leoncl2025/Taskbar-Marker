using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarMarker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must run before any window exists so UIA's physical-pixel rectangles line up
        // with the coordinates we pass to SetWindowPos / UpdateLayeredWindow.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
            MessageBox.Show(args.Exception.ToString(), "Taskbar Marker error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        if (args.Length > 0 && args[0].Equals("--list", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1)
                Diagnostics.DumpToFile(args[1]);
            else
                Diagnostics.DumpAndOpen();
            return;
        }

        // Opens just the editor. A background instance picks the saved file up on its own,
        // so this also works as a standalone shortcut.
        if (args.Length > 0 && args[0].Equals("--edit", StringComparison.OrdinalIgnoreCase))
        {
            RunStandaloneEditor();
            return;
        }

        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\TaskbarMarker.SingleInstance", out bool isFirst);
        if (!isFirst)
            return;

        using var context = new TrayAppContext();
        Application.Run(context);

        GC.KeepAlive(singleInstance);
    }

    private static void RunStandaloneEditor()
    {
        string path = Settings.DefaultPath;
        Settings settings = File.Exists(path)
            ? Settings.Load(path, out _)
            : new Settings { Raw = new Config(), Rules = Array.Empty<CompiledRule>() };

        using var form = new RulesEditorForm(settings.Raw);
        form.Applied += config =>
        {
            try
            {
                Settings.Save(path, config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save rules.json:\n{ex.Message}", "Taskbar Marker",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        Application.Run(form);
    }
}

internal sealed class TrayAppContext : ApplicationContext
{
    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "TaskbarMarker";
    private const string LegacyStartupValueName = "TaskbarNote";

    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly OverlayCoordinator _coordinator;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly string _rulesPath = Settings.DefaultPath;
    private FileSystemWatcher? _watcher;
    private RulesEditorForm? _editor;
    private IntPtr _trayIconHandle;
    private bool _ticking;
    private volatile bool _rulesDirty;

    public TrayAppContext()
    {
        MigrateLegacyStartupEntry();

        Settings settings = LoadSettings(out string? loadError);
        _coordinator = new OverlayCoordinator(settings);

        _pauseItem = new ToolStripMenuItem("Pause", null, OnTogglePause) { CheckOnClick = true };
        _startupItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup)
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled(),
        };

        var editItem = new ToolStripMenuItem("Edit rules...", null, OnEditRules)
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(editItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("List taskbar buttons", null, OnListButtons));
        menu.Items.Add(new ToolStripMenuItem("Open rules.json", null, OnOpenRulesFile));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        _trayIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "Taskbar Marker",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += OnEditRules;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Clamp(settings.Raw.PollIntervalMs, 100, 5000),
        };
        _timer.Tick += OnTick;
        _timer.Start();

        StartWatchingRulesFile();

        if (loadError is not null)
            Notify($"rules.json problem: {loadError}", ToolTipIcon.Warning);
    }

    /// <summary>
    /// Picks up edits made outside the app (text editor, another tool). The event fires on a
    /// background thread and editors often write several times, so the timer tick does the
    /// actual reload - that debounces it for free.
    /// </summary>
    private void StartWatchingRulesFile()
    {
        string? directory = Path.GetDirectoryName(_rulesPath);
        if (directory is null || !Directory.Exists(directory))
            return;

        try
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_rulesPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => _rulesDirty = true;
            _watcher.Created += (_, _) => _rulesDirty = true;
            _watcher.Renamed += (_, _) => _rulesDirty = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMarker] rules.json watcher failed: {ex}");
        }
    }

    private Settings LoadSettings(out string? error)
    {
        if (!File.Exists(_rulesPath))
        {
            error = $"{_rulesPath} not found.";
            return new Settings { Raw = new Config(), Rules = Array.Empty<CompiledRule>() };
        }

        return Settings.Load(_rulesPath, out error);
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_ticking)
            return;

        _ticking = true;
        try
        {
            if (_rulesDirty)
            {
                _rulesDirty = false;
                ReloadFromDisk(announce: false);
            }

            await _coordinator.TickAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarMarker] tick failed: {ex}");
        }
        finally
        {
            _ticking = false;
        }
    }

    private void OnTogglePause(object? sender, EventArgs e) => _coordinator.Paused = _pauseItem.Checked;

    private void ReloadFromDisk(bool announce)
    {
        Settings settings = LoadSettings(out string? error);
        _coordinator.Settings = settings;
        _coordinator.HideAll();
        _timer.Interval = Math.Clamp(settings.Raw.PollIntervalMs, 100, 5000);

        if (error is not null)
            Notify($"rules.json problem: {error}", ToolTipIcon.Warning);
        else if (announce)
            Notify($"Reloaded {settings.Rules.Count} rule(s).", ToolTipIcon.Info);
    }

    private void OnEditRules(object? sender, EventArgs e)
    {
        if (_editor is { IsDisposed: false })
        {
            _editor.Activate();
            return;
        }

        _editor = new RulesEditorForm(_coordinator.Settings.Raw);
        _editor.Applied += OnEditorApplied;
        _editor.FormClosed += (_, _) => _editor = null;
        _editor.Show();
    }

    private void OnEditorApplied(Config config)
    {
        try
        {
            // Suppress the watcher so our own write does not trigger a second reload.
            _rulesDirty = false;
            Settings.Save(_rulesPath, config);
            _rulesDirty = false;
        }
        catch (Exception ex)
        {
            Notify($"Could not save rules.json: {ex.Message}", ToolTipIcon.Error);
            return;
        }

        ReloadFromDisk(announce: false);
    }

    private void OnOpenRulesFile(object? sender, EventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_rulesPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notify($"Could not open rules.json: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void OnListButtons(object? sender, EventArgs e) => Diagnostics.DumpAndOpen();

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryKey, writable: true);
            if (_startupItem.Checked)
                key.SetValue(StartupValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = IsStartupEnabled();
            Notify($"Could not update startup entry: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private static bool IsStartupEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: false);
        return key?.GetValue(StartupValueName) is not null;
    }

    private static void MigrateLegacyStartupEntry()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryKey, writable: true);
        if (key.GetValue(StartupValueName) is null && key.GetValue(LegacyStartupValueName) is not null)
            key.SetValue(StartupValueName, $"\"{Environment.ProcessPath}\"");

        key.DeleteValue(LegacyStartupValueName, throwOnMissingValue: false);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _timer.Stop();
        _trayIcon.Visible = false;
        ExitThread();
    }

    private void Notify(string message, ToolTipIcon icon) =>
        _trayIcon.ShowBalloonTip(2500, "Taskbar Marker", message, icon);

    private Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new Pen(Color.FromArgb(200, 220, 220, 220), 2f);
            g.DrawRectangle(frame, 3, 8, 25, 16);
            using var accent = new SolidBrush(Color.FromArgb(0xE5, 0x39, 0x35));
            g.FillRectangle(accent, 6, 19, 19, 3);
        }

        _trayIconHandle = bitmap.GetHicon();
        return Icon.FromHandle(_trayIconHandle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _watcher?.Dispose();
            _editor?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _coordinator.Dispose();

            if (_trayIconHandle != IntPtr.Zero)
            {
                Native.DestroyIcon(_trayIconHandle);
                _trayIconHandle = IntPtr.Zero;
            }
        }

        base.Dispose(disposing);
    }
}
