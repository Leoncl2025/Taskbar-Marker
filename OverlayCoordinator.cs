using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarMarker;

/// <summary>Per-taskbar scan result handed back from the background thread.</summary>
internal sealed class ScanResult
{
    public required IntPtr TaskbarHwnd { get; init; }
    public required Rectangle TaskbarRect { get; init; }
    public required List<TaskbarButton> Buttons { get; init; }
}

/// <summary>
/// Drives the scan -> match -> paint loop and keeps one overlay window alive per taskbar.
/// </summary>
internal sealed class OverlayCoordinator : IDisposable
{
    private readonly Dictionary<IntPtr, TaskbarOverlay> _overlays = new();
    private Settings _settings;
    private bool _paused;

    public OverlayCoordinator(Settings settings) => _settings = settings;

    public Settings Settings
    {
        get => _settings;
        set => _settings = value;
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_paused)
                HideAll();
        }
    }

    public async Task TickAsync()
    {
        if (_paused || _settings.Rules.Count == 0)
        {
            HideAll();
            return;
        }

        List<IntPtr> taskbars = Native.FindTaskbars();
        if (taskbars.Count == 0)
        {
            HideAll();
            return;
        }

        bool appButtonsOnly = !_settings.Raw.IncludeAllButtons;
        List<ScanResult> results = await Task.Run(() => ScanAll(taskbars, appButtonsOnly)).ConfigureAwait(true);
        Rectangle? fullScreenMonitor = Native.GetForegroundFullScreenMonitorBounds();

        var seen = new HashSet<IntPtr>();
        foreach (ScanResult result in results)
        {
            seen.Add(result.TaskbarHwnd);

            Rectangle screenBounds = Screen.FromRectangle(result.TaskbarRect).Bounds;
            if (fullScreenMonitor is Rectangle hiddenScreen && hiddenScreen == screenBounds)
            {
                if (_overlays.TryGetValue(result.TaskbarHwnd, out TaskbarOverlay? existing))
                    existing.Hide();
                continue;
            }

            var marks = new List<Mark>();
            foreach (TaskbarButton button in result.Buttons)
            {
                if (!_settings.Raw.IncludeAllButtons &&
                    !string.Equals(button.ClassName, TaskbarScanner.TaskListButtonClass, StringComparison.Ordinal))
                {
                    continue;
                }

                CompiledRule? rule = _settings.Match(button);
                if (rule is not null)
                    marks.Add(new Mark(button.Bounds, rule));
            }

            TaskbarOverlay overlay = GetOverlay(result.TaskbarHwnd);
            overlay.Render(result.TaskbarRect, screenBounds, marks, _settings.Raw);
        }

        // Drop overlays whose taskbar disappeared (monitor unplugged, explorer restart).
        var stale = new List<IntPtr>();
        foreach (KeyValuePair<IntPtr, TaskbarOverlay> entry in _overlays)
        {
            if (!seen.Contains(entry.Key))
                stale.Add(entry.Key);
        }
        foreach (IntPtr hwnd in stale)
        {
            _overlays[hwnd].Dispose();
            _overlays.Remove(hwnd);
        }
    }

    private static List<ScanResult> ScanAll(List<IntPtr> taskbars, bool appButtonsOnly)
    {
        var results = new List<ScanResult>();

        foreach (IntPtr hwnd in taskbars)
        {
            if (!Native.IsWindowVisible(hwnd) || !Native.GetWindowRect(hwnd, out Native.RECT raw))
                continue;

            Rectangle rect = raw.ToRectangle();
            if (rect.Width <= 0 || rect.Height <= 0 || rect.Height > rect.Width)
                continue; // vertical taskbars are not supported

            // When auto-hide has slid the taskbar off-screen only a sliver remains visible.
            Rectangle screen = Screen.FromRectangle(rect).Bounds;
            Rectangle visible = Rectangle.Intersect(rect, screen);
            if (visible.Height < rect.Height / 2)
                continue;

            results.Add(new ScanResult
            {
                TaskbarHwnd = hwnd,
                TaskbarRect = rect,
                Buttons = TaskbarScanner.Scan(hwnd, appButtonsOnly),
            });
        }

        return results;
    }

    private TaskbarOverlay GetOverlay(IntPtr taskbarHwnd)
    {
        if (!_overlays.TryGetValue(taskbarHwnd, out TaskbarOverlay? overlay))
        {
            overlay = new TaskbarOverlay();
            _overlays[taskbarHwnd] = overlay;
        }
        return overlay;
    }

    public void HideAll()
    {
        foreach (TaskbarOverlay overlay in _overlays.Values)
            overlay.Hide();
    }

    public void Dispose()
    {
        foreach (TaskbarOverlay overlay in _overlays.Values)
            overlay.Dispose();
        _overlays.Clear();
    }
}
