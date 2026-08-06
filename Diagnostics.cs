using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TaskbarMarker;

/// <summary>
/// Dumps every taskbar button UI Automation can see, so rules can be written against
/// the exact accessible names instead of guesswork.
/// </summary>
internal static class Diagnostics
{
    public static string DefaultDumpPath =>
        Path.Combine(Path.GetTempPath(), "taskbar-marker-buttons.txt");

    public static void DumpToFile(string path)
    {
        var report = new StringBuilder();
        report.AppendLine("Taskbar Marker - taskbar buttons visible to UI Automation");
        report.AppendLine($"Captured {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();

        report.AppendLine($"Foreground window is full screen (overlays hidden): {Native.IsForegroundWindowFullScreen()}");
        report.AppendLine($"Foreground window: {Native.DescribeForegroundWindow()}");

        Settings settings = File.Exists(Settings.DefaultPath)
            ? Settings.Load(Settings.DefaultPath, out string? ruleError)
            : new Settings { Raw = new Config(), Rules = Array.Empty<CompiledRule>() };
        report.AppendLine($"Rules loaded: {settings.Rules.Count} from {Settings.DefaultPath}");
        report.AppendLine();

        List<IntPtr> taskbars = Native.FindTaskbars();
        if (taskbars.Count == 0)
        {
            report.AppendLine("No taskbar window found.");
        }

        foreach (IntPtr hwnd in taskbars)
        {
            Native.GetWindowRect(hwnd, out Native.RECT raw);
            report.AppendLine($"Taskbar 0x{hwnd.ToInt64():X} at {raw.ToRectangle()}");

            List<TaskbarButton> buttons = Task.Run(() => TaskbarScanner.Scan(hwnd, appButtonsOnly: false))
                .GetAwaiter().GetResult();
            if (buttons.Count == 0)
            {
                report.AppendLine("  (no buttons)");
            }

            foreach (TaskbarButton button in buttons)
            {
                bool isAppButton = string.Equals(button.ClassName, TaskbarScanner.TaskListButtonClass, StringComparison.Ordinal);
                CompiledRule? rule = isAppButton ? settings.Match(button) : null;
                string hit = rule is null ? "" : $"  <== MATCH label={rule.Label} color={rule.Color.Name}";
                report.AppendLine($"  {(isAppButton ? "*" : " ")} {button.Bounds,-28} {Flatten(button.Name)}{hit}");
                if (isAppButton)
                    report.AppendLine($"      appId: {button.AppId}");
            }

            report.AppendLine();
        }

        report.AppendLine("Lines marked with * are running-app buttons; only those are matched by default.");
        report.AppendLine("  \"match\"      -> regex tested against the name shown above");
        report.AppendLine("  \"matchAppId\" -> regex tested against the appId shown above");
        report.AppendLine("Two buttons of the same app share a name but have different appIds, so use");
        report.AppendLine("matchAppId when taskbar grouping is on.");
        File.WriteAllText(path, report.ToString());
    }

    private static string Flatten(string value) =>
        value.Replace("\r", " ").Replace("\n", " ");

    public static void DumpAndOpen()
    {
        string path = DefaultDumpPath;
        try
        {
            DumpToFile(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not write {path}:\n{ex.Message}", "Taskbar Marker",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
