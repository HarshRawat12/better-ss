using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal enum CaptureMode { Region, Window, Monitor, AllMonitors }

internal sealed class CaptureSession
{
    private readonly Action<BitmapSource, Forms.Screen, Drawing.Rectangle> complete;
    private readonly Action dismissed;
    private readonly Forms.Screen active;
    private readonly List<CaptureOverlay> overlays = new();
    private readonly Action? chooseWindow;
    private readonly Action? recordVideo;
    private readonly Drawing.Rectangle desktop;
    private readonly BitmapSource frozen;
    private CaptureToolbar? toolbar;
    internal CaptureMode Mode;
    internal Drawing.Rectangle Selection;
    private Drawing.Point? start;
    private bool closed;
    internal CaptureSession(CaptureMode mode, Forms.Screen active, Action<BitmapSource, Forms.Screen, Drawing.Rectangle> complete, Action dismissed, Action? chooseWindow = null, Action? recordVideo = null)
    {
        Mode = mode; this.active = active; this.complete = complete; this.dismissed = dismissed;
        this.chooseWindow = chooseWindow; this.recordVideo = recordVideo; desktop = Forms.SystemInformation.VirtualScreen; frozen = Native.Capture(desktop);
    }
    internal static string Label(CaptureMode mode) => mode switch { CaptureMode.Region => "Select area", CaptureMode.Window => "Window", CaptureMode.Monitor => "Display", _ => "All displays" };
    internal void Start()
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new CaptureOverlay(this, screen, Native.Crop(frozen, screen.Bounds, desktop)); overlays.Add(overlay); overlay.Show(); Native.Place(overlay, screen.Bounds);
        }
        toolbar = new CaptureToolbar(this, active, recordVideo); toolbar.Show(); SetMode(Mode);
    }
    internal void SetMode(CaptureMode mode)
    { if (mode == CaptureMode.Window) { Cancel(); chooseWindow?.Invoke(); return; } Mode = mode; start = null; Selection = Drawing.Rectangle.Empty; UpdatePointer(); toolbar?.Refresh(mode); }
    internal void UpdatePointer()
    {
        var point = Native.CursorPosition;
        if (start is Drawing.Point origin) Selection = Drawing.Rectangle.FromLTRB(Math.Min(origin.X, point.X), Math.Min(origin.Y, point.Y), Math.Max(origin.X, point.X), Math.Max(origin.Y, point.Y));
        else if (Mode == CaptureMode.Monitor) Selection = Forms.Screen.FromPoint(point).Bounds;
        else if (Mode == CaptureMode.AllMonitors) Selection = desktop;
        foreach (var overlay in overlays) overlay.Paint(Selection);
    }
    internal void Down(CaptureOverlay overlay)
    {
        if (Mode == CaptureMode.Region) { start = Native.CursorPosition; overlay.CaptureMouse(); UpdatePointer(); }
    }
    internal void Up(CaptureOverlay overlay)
    {
        if (Mode == CaptureMode.Region && start == null) return;
        UpdatePointer(); start = null; overlay.ReleaseMouseCapture();
        if (Selection.Width >= 3 && Selection.Height >= 3) Finish();
    }
    internal void Finish()
    {
        if (Selection.Width < 1 || Selection.Height < 1) return;
        var result = Native.Crop(frozen, Selection, desktop);
        var screen = Mode == CaptureMode.AllMonitors ? active : Forms.Screen.FromPoint(new Drawing.Point(Selection.Left + Selection.Width / 2, Selection.Top + Selection.Height / 2));
        Cancel(); complete(result, screen, Selection);
    }
    internal void Cancel()
    {
        if (closed) return; closed = true;
        foreach (var overlay in overlays) { overlay.ReleaseMouseCapture(); overlay.Close(); }
        toolbar?.Close(); dismissed();
    }
}

internal sealed class CaptureOverlay : Window
{
    private readonly CaptureSession session;
    private readonly Forms.Screen screen;
    private readonly Canvas canvas = new();
    private readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle outline = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly Border badge;
    private readonly TextBlock dimensions = UI.Text("", 12, Brushes.White);
    internal CaptureOverlay(CaptureSession session, Forms.Screen screen, BitmapSource image)
    {
        this.session = session; this.screen = screen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Left = screen.Bounds.Left; Top = screen.Bounds.Top; Width = screen.Bounds.Width; Height = screen.Bounds.Height;
        var grid = new Grid { Background = Brushes.Black }; grid.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        canvas.Background = Brushes.Transparent; canvas.Children.Add(dim); canvas.Children.Add(outline);
        badge = new Border { Background = UI.Brush("#ED202020"), Padding = new Thickness(10, 6, 10, 6), Child = dimensions, IsHitTestVisible = false, Visibility = Visibility.Hidden };
        canvas.Children.Add(badge); grid.Children.Add(canvas); Content = grid;
        SourceInitialized += (_, _) => Native.Exclude(this);
        SizeChanged += (_, _) => Paint(session.Selection);
        MouseMove += (_, _) => session.UpdatePointer(); MouseLeftButtonDown += (_, _) => session.Down(this); MouseLeftButtonUp += (_, _) => session.Up(this);
        MouseRightButtonUp += (_, _) => session.Cancel(); KeyDown += (_, e) => { if (e.Key == Key.Escape) session.Cancel(); };
    }
    internal void Paint(Drawing.Rectangle selection)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var sx = ActualWidth / screen.Bounds.Width; var sy = ActualHeight / screen.Bounds.Height;
        var local = Drawing.Rectangle.Intersect(selection, screen.Bounds);
        var whole = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (local.Width <= 0 || local.Height <= 0) { dim.Data = whole; outline.Visibility = badge.Visibility = Visibility.Hidden; return; }
        var r = new Rect((local.X - screen.Bounds.X) * sx, (local.Y - screen.Bounds.Y) * sy, local.Width * sx, local.Height * sy);
        dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(r));
        outline.Visibility = Visibility.Visible; Canvas.SetLeft(outline, r.X); Canvas.SetTop(outline, r.Y); outline.Width = r.Width; outline.Height = r.Height;
        dimensions.Text = $"{selection.Width} × {selection.Height}"; badge.Visibility = Visibility.Visible;
        Canvas.SetLeft(badge, Math.Clamp(r.X, 8, Math.Max(8, ActualWidth - 140))); Canvas.SetTop(badge, r.Y >= 38 ? r.Y - 34 : Math.Min(r.Bottom + 8, ActualHeight - 42));
    }
}

internal sealed class CaptureToolbar : Window
{
    private readonly CaptureSession session;
    private readonly Forms.Screen screen;
    private readonly Dictionary<CaptureMode, Button> buttons = new();
    private readonly TextBlock hint = UI.Text("", 12, UI.Brush("#B0B0B0"));
    private readonly Button capture;
    internal CaptureToolbar(CaptureSession session, Forms.Screen screen, Action? recordVideo = null)
    {
        this.session = session; this.screen = screen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        var root = new StackPanel(); var row = new StackPanel { Orientation = Orientation.Horizontal };
        string[] icons = { "▧", "▣", "▭", "▦" }; int index = 0;
        foreach (var mode in Enum.GetValues<CaptureMode>()) { var m = mode; var b = UI.Button(icons[index++] + "  " + CaptureSession.Label(mode), () => session.SetMode(m), false, true); buttons.Add(mode, b); row.Children.Add(b); }
        row.Children.Add(UI.Button("●  Video", () => { session.Cancel(); recordVideo?.Invoke(); }, false, true));
        row.Children.Add(UI.Button("×  Cancel", session.Cancel, false, true));
        capture = UI.Button("Capture", session.Finish, true, true); row.Children.Add(capture);
        root.Children.Add(row); hint.Margin = new Thickness(4, 12, 4, 0); root.Children.Add(hint);
        Content = new Border { Background = UI.Brush("#FA202020"), BorderBrush = UI.Brush("#505050"), BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(16), Child = root };
        Loaded += (_, _) => { Native.MoveToMonitor(this, screen); Position(); };
        SourceInitialized += (_, _) => Native.Exclude(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) session.Cancel(); };
    }
    private void Position()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.Place(this, new Drawing.Rectangle(screen.WorkingArea.Left + (screen.WorkingArea.Width - (int)(ActualWidth * dpi.DpiScaleX)) / 2, screen.WorkingArea.Bottom - (int)(ActualHeight * dpi.DpiScaleY) - 24, (int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY)));
    }
    internal void Refresh(CaptureMode mode)
    {
        foreach (var entry in buttons) UI.Select(entry.Value, entry.Key == mode, true);
        capture.IsEnabled = mode == CaptureMode.AllMonitors;
        hint.Text = mode switch { CaptureMode.Region => "Drag an area and release to capture. Right-click to cancel.", CaptureMode.Window => "Choose an open or minimized application window.", CaptureMode.Monitor => "Click anywhere on the display you want to capture.", _ => "Capture all displays together, arranged as on your desktop." };
    }
}

internal sealed class CountdownWindow : Window
{
    private readonly TaskCompletionSource<bool> completion = new();
    internal Task<bool> Completion => completion.Task;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal CountdownWindow(int seconds)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; SizeToContent = SizeToContent.WidthAndHeight; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel(); var label = UI.Text(seconds.ToString(), 56, Brushes.White, FontWeights.SemiBold); label.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(label); panel.Children.Add(UI.Button("Cancel capture", () => { completion.TrySetResult(false); Close(); }, false, true));
        Content = new Border { Background = UI.Brush("#FA202020"), Padding = new Thickness(30), Child = panel };
        timer.Tick += (_, _) => { if (--seconds <= 0) { completion.TrySetResult(true); Close(); } else label.Text = seconds.ToString(); };
        Closed += (_, _) => { timer.Stop(); completion.TrySetResult(false); }; Loaded += (_, _) => timer.Start();
    }
}
