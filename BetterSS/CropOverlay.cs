using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BetterSS;

// An overlay in image coordinates. Dragging previews a crop; the editor commits only on Apply.
internal sealed class CropOverlay : FrameworkElement
{
    internal Rect Bounds;
    internal bool Editing;
    internal Action<Rect>? Commit;
    private Rect before;
    private Point origin;
    private string? drag;
    internal CropOverlay() { Cursor = Cursors.Cross; Focusable = true; }
    private double Unit => 1 / Math.Max(.05, TransformToAncestor(Window.GetWindow(this) ?? (Visual)this).Transform(new Point(1, 0)).X - TransformToAncestor(Window.GetWindow(this) ?? (Visual)this).Transform(new Point()).X);
    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), new RectangleGeometry(Bounds)));
        if (!Editing) return;
        double u = Unit; dc.DrawRectangle(Brushes.Transparent, new Pen(Brushes.White, u), Bounds);
        for (int i = 1; i < 3; i++) { dc.DrawLine(new Pen(Brushes.White, .4 * u), new Point(Bounds.X + Bounds.Width * i / 3, Bounds.Top), new Point(Bounds.X + Bounds.Width * i / 3, Bounds.Bottom)); dc.DrawLine(new Pen(Brushes.White, .4 * u), new Point(Bounds.Left, Bounds.Y + Bounds.Height * i / 3), new Point(Bounds.Right, Bounds.Y + Bounds.Height * i / 3)); }
        foreach (var p in Handles()) dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, u), new Rect(p.X - 4 * u, p.Y - 4 * u, 8 * u, 8 * u));
    }
    private Point[] Handles() => new[] { Bounds.TopLeft, new Point(Bounds.Left + Bounds.Width / 2, Bounds.Top), Bounds.TopRight, new Point(Bounds.Right, Bounds.Top + Bounds.Height / 2), Bounds.BottomRight, new Point(Bounds.Left + Bounds.Width / 2, Bounds.Bottom), Bounds.BottomLeft, new Point(Bounds.Left, Bounds.Top + Bounds.Height / 2) };
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Editing) return; origin = e.GetPosition(this); before = Bounds;
        var names = new[] { "LT", "T", "RT", "R", "RB", "B", "LB", "L" }; var points = Handles(); drag = Bounds.Contains(origin) ? "Move" : "New";
        for (int i = 0; i < points.Length; i++) if ((points[i] - origin).Length < 12 * Unit) { drag = names[i]; break; }
        CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag == null) return; var p = e.GetPosition(this); p = new Point(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight)); var delta = p - origin;
        if (drag == "Move") Bounds = new Rect(Math.Clamp(before.X + delta.X, 0, ActualWidth - before.Width), Math.Clamp(before.Y + delta.Y, 0, ActualHeight - before.Height), before.Width, before.Height);
        else if (drag == "New") Bounds = new Rect(origin, p);
        else { double l = before.Left, t = before.Top, r = before.Right, b = before.Bottom; if (drag.Contains('L')) l = Math.Min(p.X, r - 3); if (drag.Contains('R')) r = Math.Max(p.X, l + 3); if (drag.Contains('T')) t = Math.Min(p.Y, b - 3); if (drag.Contains('B')) b = Math.Max(p.Y, t + 3); Bounds = new Rect(new Point(l, t), new Point(r, b)); }
        InvalidateVisual(); e.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (drag == null) return; var result = Bounds; Bounds = before; drag = null; ReleaseMouseCapture(); if (result.Width >= 3 && result.Height >= 3) Commit?.Invoke(result); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { if (drag != null) { Bounds = before; drag = null; InvalidateVisual(); } }
}
