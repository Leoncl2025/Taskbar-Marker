using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskbarMarker;

internal static class Native
{
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int ULW_ALPHA = 0x02;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx;
        public int Cy;
        public SIZE(int cx, int cy) { Cx = cx; Cy = cy; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private static readonly string[] ShellWindowClasses =
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "WorkerW", "Progman", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>
    /// Returns the monitor covered by the foreground window when it is genuinely full-screen.
    /// Maximized windows are excluded explicitly because their rect overhangs the screen edges by
    /// the resize border. Checked directly instead of via SHQueryUserNotificationState, which also
    /// reports "busy" for Do Not Disturb / focus sessions.
    /// </summary>
    public static Rectangle? GetForegroundFullScreenMonitorBounds()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsZoomed(foreground))
            return null;

        var className = new StringBuilder(64);
        if (GetClassName(foreground, className, className.Capacity) > 0 &&
            Array.IndexOf(ShellWindowClasses, className.ToString()) >= 0)
        {
            return null;
        }

        if (!GetWindowRect(foreground, out RECT raw))
            return null;

        Rectangle rect = raw.ToRectangle();
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        Rectangle monitorBounds = Screen.FromRectangle(rect).Bounds;
        return rect.Contains(monitorBounds) ? monitorBounds : null;
    }

    public static string DescribeForegroundWindow()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return "(none)";

        var className = new StringBuilder(128);
        GetClassName(foreground, className, className.Capacity);
        var title = new StringBuilder(256);
        GetWindowText(foreground, title, title.Capacity);
        GetWindowRect(foreground, out RECT raw);

        return $"hwnd=0x{foreground.ToInt64():X} zoomed={IsZoomed(foreground)} " +
               $"class={className} rect={raw.ToRectangle()} title={title}";
    }

    /// <summary>Returns the primary taskbar plus every secondary-monitor taskbar.</summary>
    public static List<IntPtr> FindTaskbars()
    {
        var taskbars = new List<IntPtr>();

        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
            taskbars.Add(primary);

        var buffer = new StringBuilder(64);
        EnumWindows((hWnd, _) =>
        {
            buffer.Clear();
            if (GetClassName(hWnd, buffer, buffer.Capacity) > 0 &&
                buffer.ToString() == "Shell_SecondaryTrayWnd")
            {
                taskbars.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        return taskbars;
    }
}
