using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TaskbarMarker;

internal readonly record struct Mark(Rectangle ButtonBounds, CompiledRule Rule);

/// <summary>
/// Owns one overlay window (one per taskbar) and paints the colored bars / label chips
/// for every taskbar button that matched a rule.
/// </summary>
internal sealed class TaskbarOverlay : IDisposable
{
    /// <summary>Windows 11 taskbar height at 100% scaling; used to derive a DPI factor.</summary>
    private const float BaselineTaskbarHeight = 48f;

    private readonly OverlayWindow _window = new();
    private readonly Bitmap _measureBitmap = new(1, 1);
    private readonly Graphics _measureGraphics;
    private Bitmap? _surface;
    private Font? _font;
    private float _fontSize = -1f;
    private int _lastSignature;

    public TaskbarOverlay() => _measureGraphics = Graphics.FromImage(_measureBitmap);

    public void Render(Rectangle taskbarRect, Rectangle screenBounds, IReadOnlyList<Mark> marks, Config config)
    {
        if (marks.Count == 0 || taskbarRect.Width <= 0 || taskbarRect.Height <= 0)
        {
            Hide();
            return;
        }

        float scale = Math.Clamp(taskbarRect.Height / BaselineTaskbarHeight, 0.75f, 3f);
        bool taskbarAtTop = taskbarRect.Top <= screenBounds.Top + 2;

        int barHeight = Math.Max(2, (int)Math.Round(config.BarHeight * scale));
        int barInset = (int)Math.Round(config.BarInset * scale);
        int edgeMargin = (int)Math.Round(3 * scale);
        int gap = (int)Math.Round(2 * scale);

        Font font = GetFont(config.LabelFontSize * scale);
        bool wantsLabels = config.ShowLabel && HasAnyLabel(marks);
        int chipPadX = (int)Math.Round(6 * scale);
        int chipPadY = (int)Math.Round(2 * scale);
        int chipHeight = wantsLabels ? (int)Math.Ceiling(font.GetHeight(96f)) + chipPadY * 2 : 0;

        var ordered = new List<Mark>(marks);
        ordered.Sort(static (a, b) => a.ButtonBounds.X.CompareTo(b.ButtonBounds.X));

        // Everything below is laid out in screen coordinates first, so the overlay window can be
        // shrunk to just the painted area instead of spanning the whole taskbar.
        var bars = new List<Shape>(ordered.Count);
        foreach (Mark mark in ordered)
        {
            Rectangle button = mark.ButtonBounds;
            int barWidth = Math.Max(4, button.Width - barInset * 2);
            bars.Add(new Shape(
                new Rectangle(
                    button.X + (button.Width - barWidth) / 2,
                    taskbarAtTop ? button.Top + edgeMargin : button.Bottom - edgeMargin - barHeight,
                    barWidth,
                    barHeight),
                mark.Rule.Color,
                null));
        }

        List<Shape> chips = wantsLabels
            ? LayoutChips(ordered, taskbarRect, taskbarAtTop, font, chipHeight, chipPadX, gap)
            : new List<Shape>();

        Rectangle overlayRect = Rectangle.Empty;
        foreach (Shape shape in bars)
            overlayRect = overlayRect.IsEmpty ? shape.Rect : Rectangle.Union(overlayRect, shape.Rect);
        foreach (Shape shape in chips)
            overlayRect = Rectangle.Union(overlayRect, shape.Rect);
        overlayRect.Inflate(1, 1);

        if (overlayRect.Width <= 0 || overlayRect.Height <= 0)
        {
            Hide();
            return;
        }

        int signature = ComputeSignature(overlayRect, bars, chips);
        if (signature == _lastSignature && _window.Visible)
        {
            _window.EnsureTopMost();
            return;
        }
        _lastSignature = signature;

        Bitmap surface = GetSurface(overlayRect.Size);
        using (var g = Graphics.FromImage(surface))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // GDI+ text (not TextRenderer/GDI) is required here: GDI ignores the alpha channel
            // and would render the label as an invisible or black block on a layered window.
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.TranslateTransform(-overlayRect.X, -overlayRect.Y);

            foreach (Shape bar in bars)
            {
                using var brush = new SolidBrush(bar.Color);
                using GraphicsPath path = RoundedRect(bar.Rect, bar.Rect.Height / 2f);
                g.FillPath(brush, path);
            }

            if (chips.Count > 0)
            {
                using var format = new StringFormat(StringFormatFlags.NoWrap)
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                };

                foreach (Shape chip in chips)
                {
                    using (var chipBrush = new SolidBrush(chip.Color))
                    using (GraphicsPath chipPath = RoundedRect(chip.Rect, chip.Rect.Height / 2f))
                    {
                        g.FillPath(chipBrush, chipPath);
                    }

                    using var textBrush = new SolidBrush(ContrastingText(chip.Color));
                    g.DrawString(chip.Label, font, textBrush, chip.Rect, format);
                }
            }
        }

        _window.UpdateSurface(overlayRect, surface);
    }

    /// <summary>
    /// Chips are usually wider than a 44-66px taskbar button, so they are laid out left to right and
    /// each one is pushed past the previous chip instead of being allowed to overwrite it.
    /// </summary>
    private List<Shape> LayoutChips(List<Mark> ordered, Rectangle taskbarRect, bool taskbarAtTop,
        Font font, int chipHeight, int chipPadX, int gap)
    {
        var chips = new List<Shape>();
        int chipY = taskbarAtTop ? taskbarRect.Bottom + gap : taskbarRect.Top - gap - chipHeight;
        int nextFreeX = taskbarRect.Left;

        foreach (Mark mark in ordered)
        {
            if (string.IsNullOrWhiteSpace(mark.Rule.Label))
                continue;

            string label = mark.Rule.Label!;
            Rectangle button = mark.ButtonBounds;

            int chipWidth = (int)Math.Ceiling(_measureGraphics.MeasureString(label, font).Width) + chipPadX * 2;
            chipWidth = Math.Min(chipWidth, taskbarRect.Width);

            int chipX = button.X + (button.Width - chipWidth) / 2;
            chipX = Math.Max(chipX, nextFreeX);
            chipX = Math.Min(chipX, taskbarRect.Right - chipWidth);
            if (chipX < nextFreeX)
            {
                // Ran out of room on the right; shrink instead of overlapping the previous chip.
                chipWidth = taskbarRect.Right - nextFreeX;
                chipX = nextFreeX;
                if (chipWidth < chipPadX * 2)
                    break;
            }

            var rect = new Rectangle(chipX, chipY, chipWidth, chipHeight);
            chips.Add(new Shape(rect, mark.Rule.Color, label));
            nextFreeX = rect.Right + gap * 2;
        }

        return chips;
    }

    private static int ComputeSignature(Rectangle overlayRect, List<Shape> bars, List<Shape> chips)
    {
        var hash = new HashCode();
        hash.Add(overlayRect);
        foreach (Shape shape in bars)
        {
            hash.Add(shape.Rect);
            hash.Add(shape.Color.ToArgb());
        }
        foreach (Shape shape in chips)
        {
            hash.Add(shape.Rect);
            hash.Add(shape.Color.ToArgb());
            hash.Add(shape.Label);
        }
        return hash.ToHashCode();
    }

    private readonly record struct Shape(Rectangle Rect, Color Color, string? Label);

    public void Hide()
    {
        _lastSignature = 0;
        _window.HideOverlay();
    }

    private static bool HasAnyLabel(IReadOnlyList<Mark> marks)
    {
        foreach (Mark mark in marks)
        {
            if (!string.IsNullOrWhiteSpace(mark.Rule.Label))
                return true;
        }
        return false;
    }

    private Bitmap GetSurface(Size size)
    {
        if (_surface is not null && _surface.Width == size.Width && _surface.Height == size.Height)
            return _surface;

        _surface?.Dispose();
        _surface = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height),
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        return _surface;
    }

    private Font GetFont(float sizeInPoints)
    {
        sizeInPoints = Math.Max(6f, sizeInPoints);
        if (_font is not null && Math.Abs(_fontSize - sizeInPoints) < 0.01f)
            return _font;

        _font?.Dispose();
        _font = new Font("Segoe UI", sizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
        _fontSize = sizeInPoints;
        return _font;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));

        if (diameter <= 1f)
        {
            path.AddRectangle(rect);
            return path;
        }

        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ContrastingText(Color background)
    {
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6 ? Color.FromArgb(255, 20, 20, 20) : Color.White;
    }

    public void Dispose()
    {
        _window.Dispose();
        _surface?.Dispose();
        _font?.Dispose();
        _measureGraphics.Dispose();
        _measureBitmap.Dispose();
    }
}
