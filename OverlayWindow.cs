using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskbarMarker;

/// <summary>
/// A per-pixel-alpha, click-through, always-on-top window that we paint the marks into.
/// WS_EX_TRANSPARENT makes mouse input fall straight through to the taskbar underneath,
/// and WS_EX_NOACTIVATE keeps it from ever stealing focus.
/// </summary>
internal sealed class OverlayWindow : Form
{
    private Rectangle _currentBounds = Rectangle.Empty;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "Taskbar Marker overlay";
        // Park off-screen until the first render so nothing flashes at 0,0.
        Bounds = new Rectangle(-32000, -32000, 1, 1);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED
                        | Native.WS_EX_TRANSPARENT
                        | Native.WS_EX_TOOLWINDOW
                        | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Pushes a freshly rendered ARGB bitmap onto the window at the given screen rect.</summary>
    public void UpdateSurface(Rectangle screenRect, Bitmap bitmap)
    {
        if (!Visible)
            Show();

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = Native.SelectObject(memDc, hBitmap);

            var size = new Native.SIZE(bitmap.Width, bitmap.Height);
            var source = new Native.POINT(0, 0);
            var destination = new Native.POINT(screenRect.X, screenRect.Y);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                memDc, ref source, 0, ref blend, Native.ULW_ALPHA);

            _currentBounds = screenRect;
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                Native.SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero)
                Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // The taskbar is topmost too, so re-assert our position above it on every frame.
        EnsureTopMost();
    }

    public void HideOverlay()
    {
        if (Visible)
            Hide();
        _currentBounds = Rectangle.Empty;
    }

    /// <summary>Re-asserts z-order above the taskbar, which is topmost as well.</summary>
    public void EnsureTopMost() =>
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    public Rectangle CurrentBounds => _currentBounds;
}
