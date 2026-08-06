using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace TaskbarMarker;

/// <summary>A single taskbar button as reported by UI Automation, in physical screen pixels.</summary>
internal readonly record struct TaskbarButton(string Name, Rectangle Bounds, string ClassName, string AppId);

/// <summary>
/// Reads taskbar buttons out of explorer via UI Automation. No injection, no hooking:
/// every Windows 11 taskbar button is a regular UIA Button whose Name is the window title
/// and whose BoundingRectangle gives us the on-screen position to draw over.
/// </summary>
internal static class TaskbarScanner
{
    /// <summary>UIA class name of the running-app buttons; everything else on the taskbar
    /// (Start, Search, Widgets, tray icons) uses a different class.</summary>
    public const string TaskListButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    private const string AppIdPrefix = "Appid: ";

    private static readonly Condition ButtonCondition =
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

    /// <summary>
    /// Filtering on the provider side keeps explorer from marshalling the ~15 shell/tray buttons
    /// back to us on every poll, which is where most of the scan cost sits.
    /// </summary>
    private static readonly Condition AppButtonCondition = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ClassNameProperty, TaskListButtonClass));

    /// <summary>
    /// Must be called from a background thread: UIA cross-process calls block, and doing that
    /// on the UI thread that also pumps messages can deadlock.
    /// </summary>
    public static List<TaskbarButton> Scan(IntPtr taskbarHwnd, bool appButtonsOnly = true)
    {
        var buttons = new List<TaskbarButton>();

        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(taskbarHwnd);
        }
        catch (ElementNotAvailableException)
        {
            return buttons;
        }
        catch (COMException)
        {
            return buttons;
        }

        if (root is null)
            return buttons;

        // Batch Name + BoundingRectangle into a single cross-process round trip. Without this,
        // every property read is its own RPC and a busy taskbar costs tens of milliseconds.
        var cacheRequest = new CacheRequest
        {
            AutomationElementMode = AutomationElementMode.None,
        };
        cacheRequest.Add(AutomationElement.NameProperty);
        cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
        cacheRequest.Add(AutomationElement.ClassNameProperty);
        cacheRequest.Add(AutomationElement.AutomationIdProperty);

        try
        {
            using (cacheRequest.Activate())
            {
                AutomationElementCollection found = root.FindAll(TreeScope.Descendants,
                    appButtonsOnly ? AppButtonCondition : ButtonCondition);
                foreach (AutomationElement element in found)
                {
                    string name;
                    string className;
                    string automationId;
                    System.Windows.Rect rect;
                    try
                    {
                        name = element.Cached.Name;
                        className = element.Cached.ClassName ?? "";
                        automationId = element.Cached.AutomationId ?? "";
                        rect = element.Cached.BoundingRectangle;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (rect.IsEmpty || double.IsInfinity(rect.Width) || rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    if (automationId.StartsWith(AppIdPrefix, StringComparison.Ordinal))
                        automationId = automationId[AppIdPrefix.Length..];

                    buttons.Add(new TaskbarButton(
                        name,
                        new Rectangle(
                            (int)Math.Round(rect.X),
                            (int)Math.Round(rect.Y),
                            (int)Math.Round(rect.Width),
                            (int)Math.Round(rect.Height)),
                        className,
                        automationId));
                }
            }
        }
        catch (ElementNotAvailableException)
        {
            // The taskbar was recreated (explorer restart, DPI change) mid-scan.
        }
        catch (COMException)
        {
        }

        return buttons;
    }
}
