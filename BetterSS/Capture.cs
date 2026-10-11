using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal enum CaptureMode { Region, Window, Monitor, AllMonitors }
internal enum CaptureIntent { Screenshot, LiveOcr, ColorPicker }

internal sealed class CaptureSession
{
    private readonly Action<BitmapSource, Forms.Screen, Drawing.Rectangle> complete;
    private readonly Action dismissed;
    private readonly Forms.Screen active;
    private readonly List<CaptureOverlay> overlays = new();
    private readonly Action<CaptureIntent>? chooseWindow;
    private readonly Action? recordVideo;
    private readonly Action<BitmapSource, Forms.Screen, Drawing.Rectangle>? readText;
    private readonly Action<Color, Forms.Screen>? colorPicked;
    private readonly Drawing.Rectangle desktop;
    private readonly BitmapSource frozen;
    private BitmapSource? colorPixels;
    private CaptureToolbar? toolbar;
    internal CaptureMode Mode;
    internal CaptureIntent Intent { get; private set; }
    internal Drawing.Rectangle Selection;
    private Drawing.Point? start;
    private bool closed;
    internal bool Completed { get; private set; }
    internal bool TransferringMode { get; private set; }
    internal CaptureSession(CaptureMode mode, Forms.Screen active, Action<BitmapSource, Forms.Screen, Drawing.Rectangle> complete, Action dismissed, Action<CaptureIntent>? chooseWindow = null, Action? recordVideo = null, Action<BitmapSource, Forms.Screen, Drawing.Rectangle>? readText = null, Action<Color, Forms.Screen>? colorPicked = null, CaptureIntent intent = CaptureIntent.Screenshot)
    {
        Mode = mode; Intent = intent; this.active = active; this.complete = complete; this.dismissed = dismissed;
        this.chooseWindow = chooseWindow; this.recordVideo = recordVideo; this.readText = readText; this.colorPicked = colorPicked;
        desktop = Forms.SystemInformation.VirtualScreen; frozen = Native.Capture(desktop);
    }
    internal static string Label(CaptureMode mode) => mode switch { CaptureMode.Region => "Select area", CaptureMode.Window => "Window", CaptureMode.Monitor => "Display", _ => "All displays" };
    internal void Start()
    {
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var overlay = new CaptureOverlay(this, screen, Native.Crop(frozen, screen.Bounds, desktop)); overlays.Add(overlay); overlay.Show(); Native.Place(overlay, screen.Bounds);
        }
        if (Intent == CaptureIntent.Screenshot)
        {
            toolbar = new CaptureToolbar(this, active, recordVideo, frozen); toolbar.Show(); SetMode(Mode);
        }
        else
        {
            start = null; Selection = Drawing.Rectangle.Empty; UpdatePointer();
        }
    }
    internal void SetMode(CaptureMode mode)
    {
        if (mode == CaptureMode.Window) { TransferringMode = true; var intent = Intent; Cancel(); chooseWindow?.Invoke(intent); return; }
        Mode = mode; start = null; Selection = Drawing.Rectangle.Empty; UpdatePointer(); toolbar?.Refresh(mode, Intent);
        if (Intent == CaptureIntent.Screenshot) toolbar?.Show(); else toolbar?.Hide();
    }
    internal void SetIntent(CaptureIntent intent)
    {
        Intent = intent;
        if (intent == CaptureIntent.LiveOcr) { Mode = CaptureMode.Region; start = null; Selection = Drawing.Rectangle.Empty; }
        colorPixels = null;
        UpdatePointer(); toolbar?.Refresh(Mode, Intent);
        if (Intent == CaptureIntent.Screenshot) toolbar?.Show(); else toolbar?.Hide();
    }
    internal void UpdatePointer()
    {
        var point = Native.CursorPosition;
        if (Intent == CaptureIntent.ColorPicker)
        {
            if (desktop.Contains(point))
            {
                var color = ColorAt(point);
                foreach (var overlay in overlays) overlay.PaintColor(point, color);
            }
            return;
        }
        if (start is Drawing.Point origin) Selection = Drawing.Rectangle.FromLTRB(Math.Min(origin.X, point.X), Math.Min(origin.Y, point.Y), Math.Max(origin.X, point.X), Math.Max(origin.Y, point.Y));
        else if (Mode == CaptureMode.Monitor) Selection = Forms.Screen.FromPoint(point).Bounds;
        else if (Mode == CaptureMode.AllMonitors) Selection = desktop;
        foreach (var overlay in overlays) overlay.Paint(Selection);
    }
    internal void Down(CaptureOverlay overlay)
    {
        if (Intent == CaptureIntent.ColorPicker)
        {
            var point = Native.CursorPosition;
            if (!desktop.Contains(point)) return;
            var color = ColorAt(point); var screen = Forms.Screen.FromPoint(point);
            Completed = true; Cancel(); colorPicked?.Invoke(color, screen); return;
        }
        if (Mode == CaptureMode.Region) { start = Native.CursorPosition; toolbar?.Hide(); overlay.Activate(); overlay.CaptureMouse(); UpdatePointer(); }
    }
    internal void Up(CaptureOverlay overlay)
    {
        if (Mode == CaptureMode.Region && start == null) return;
        UpdatePointer(); start = null; overlay.ReleaseMouseCapture();
        if (Selection.Width >= 3 && Selection.Height >= 3) Finish();
        else if (Intent == CaptureIntent.Screenshot) toolbar?.Show();
    }
    internal void Finish()
    {
        if (Intent == CaptureIntent.ColorPicker || Selection.Width < 1 || Selection.Height < 1) return;
        var result = Native.Crop(frozen, Selection, desktop);
        var screen = Mode == CaptureMode.AllMonitors ? active : Forms.Screen.FromPoint(new Drawing.Point(Selection.Left + Selection.Width / 2, Selection.Top + Selection.Height / 2));
        var intent = Intent; Completed = true; Cancel();
        if (intent == CaptureIntent.LiveOcr) readText?.Invoke(result, screen, Selection);
        else complete(result, screen, Selection);
    }
    private Color ColorAt(Drawing.Point point)
    {
        colorPixels ??= new FormatConvertedBitmap(frozen, PixelFormats.Bgra32, null, 0);
        if (!colorPixels.IsFrozen) colorPixels.Freeze();
        int x = Math.Clamp((point.X - desktop.Left) * colorPixels.PixelWidth / desktop.Width, 0, colorPixels.PixelWidth - 1);
        int y = Math.Clamp((point.Y - desktop.Top) * colorPixels.PixelHeight / desktop.Height, 0, colorPixels.PixelHeight - 1);
        var pixel = new byte[4]; colorPixels.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
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
    private readonly TextBlock dimensions = UI.Text("", 12, UI.Ink(true));
    private readonly Brush badgeForeground = UI.Ink(true);
    internal CaptureOverlay(CaptureSession session, Forms.Screen screen, BitmapSource image)
    {
        this.session = session; this.screen = screen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Cursor = Cursors.Cross;
        Left = screen.Bounds.Left; Top = screen.Bounds.Top; Width = screen.Bounds.Width; Height = screen.Bounds.Height;
        var grid = new Grid { Background = Brushes.Black }; grid.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        canvas.Background = Brushes.Transparent; canvas.Children.Add(dim); canvas.Children.Add(outline);
        badge = UI.Floating(dimensions, true, new Thickness(10, 6, 10, 6), 6); badge.IsHitTestVisible = false; badge.Visibility = Visibility.Hidden;
        badge.Effect = null; // This surface tracks the pointer; avoid an effect pass per move.
        canvas.Children.Add(badge); grid.Children.Add(canvas); Content = grid;
        SourceInitialized += (_, _) => Native.Exclude(this);
        SizeChanged += (_, _) => Paint(session.Selection);
        MouseMove += (_, _) => session.UpdatePointer(); MouseLeftButtonDown += (_, _) => session.Down(this); MouseLeftButtonUp += (_, _) => session.Up(this);
        MouseRightButtonUp += (_, _) => session.Cancel(); KeyDown += (_, e) => { if (e.Key == Key.Escape) session.Cancel(); };
    }
    internal void Paint(Drawing.Rectangle selection)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        badge.Background = null; dimensions.Foreground = badgeForeground; dimensions.Inlines.Clear();
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
    internal void PaintColor(Drawing.Point point, Color color)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var local = new Drawing.Point(point.X - screen.Bounds.X, point.Y - screen.Bounds.Y);
        if (local.X < 0 || local.Y < 0 || local.X >= screen.Bounds.Width || local.Y >= screen.Bounds.Height)
        { dim.Data = null; outline.Visibility = badge.Visibility = Visibility.Hidden; return; }
        dim.Data = null; outline.Visibility = Visibility.Hidden;
        double sx = ActualWidth / screen.Bounds.Width, sy = ActualHeight / screen.Bounds.Height;
        dimensions.Inlines.Clear();
        dimensions.Inlines.Add(new InlineUIContainer(new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Background = new SolidColorBrush(color), BorderBrush = Brushes.White, BorderThickness = new Thickness(.75), Margin = new Thickness(0, 0, 7, -2) }));
        dimensions.Inlines.Add(new Run($"#{color.R:X2}{color.G:X2}{color.B:X2} · click to copy"));
        double luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        dimensions.Foreground = luminance > 150 ? Brushes.Black : Brushes.White;
        badge.Visibility = Visibility.Visible;
        double x = local.X * sx, y = local.Y * sy;
        Canvas.SetLeft(badge, Math.Clamp(x + 18, 8, Math.Max(8, ActualWidth - 175)));
        Canvas.SetTop(badge, Math.Clamp(y + 18, 8, Math.Max(8, ActualHeight - 42)));
    }
}

internal sealed class CaptureToolbar : Window
{
    private readonly CaptureSession session;
    private readonly Forms.Screen screen;
    private readonly Dictionary<CaptureMode, Button> buttons = new();
    private readonly Button capture;
    private readonly Button screenshot;
    private readonly Button liveOcr;
    private readonly Button colorPicker;
    internal CaptureToolbar(CaptureSession session, Forms.Screen screen, Action? recordVideo = null, BitmapSource? backdrop = null)
    {
        this.session = session; this.screen = screen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        var root = new StackPanel(); var row = new StackPanel { Orientation = Orientation.Horizontal };
        string[] icons = {
            "M8 3 H3 V8 M16 3 H21 V8 M21 16 V21 H16 M8 21 H3 V16 M3 11 V13 M21 11 V13 M11 3 H13 M11 21 H13",
            "M3 4 H21 V20 H3 Z M3 9 H21 M6 6.5 H7 M10 6.5 H11",
            "M3 4 H21 V16 H3 Z M12 16 V21 M8 21 H16",
            "M9 3 H22 V13 H18 M9 7 V3 M2 8 H17 V19 H2 Z M9.5 19 V22 M6 22 H13"
        }; int index = 0;
        foreach (var mode in Enum.GetValues<CaptureMode>()) { var m = mode; var b = Action(CaptureSession.Label(mode), icons[index++], () => session.SetMode(m)); buttons.Add(mode, b); row.Children.Add(b); }
        row.Children.Add(Divider());
        screenshot = Action("Screenshot", "M3 7 H7 L9 4 H15 L17 7 H21 V20 H3 Z M16 13.5 A4 4 0 1 1 8 13.5 A4 4 0 1 1 16 13.5", () => session.SetIntent(CaptureIntent.Screenshot)); row.Children.Add(screenshot);
        liveOcr = Action("Live OCR", "M7 3 H3 V7 M17 3 H21 V7 M21 17 V21 H17 M7 21 H3 V17 M7 8 H17 M12 8 V17 M9 17 H15", () => session.SetIntent(CaptureIntent.LiveOcr)); row.Children.Add(liveOcr);
        colorPicker = Action("Pick color", "M15 3 L21 9 M18 2 L22 6 L18 10 L14 6 Z M14 6 L4 16 L3 21 L8 20 L18 10 M6 14 L10 18", () => session.SetIntent(CaptureIntent.ColorPicker)); row.Children.Add(colorPicker);
        row.Children.Add(Divider());
        var video = Action("Video", "M3 6 H15 V18 H3 Z M15 10 L21 6 V18 L15 14", () => { session.Cancel(); recordVideo?.Invoke(); }); row.Children.Add(video);
        var cancel = Action("Cancel", "M6 6 L18 18 M18 6 L6 18", session.Cancel); row.Children.Add(cancel);
        capture = Action("Capture", "M5 12 L10 17 L20 6", session.Finish); row.Children.Add(capture);
        root.Children.Add(row);
        var material = new GlassSurface(root, true, new Thickness(6), 18, captureMaterial: true);
        Content = new Border { Child = material, Padding = new Thickness(8), Background = Brushes.Transparent };
        Loaded += (_, _) =>
        {
            Native.MoveToMonitor(this, screen);
            UpdateLayout(); Position();
            if (backdrop != null && GlassSurface.Transparency)
            {
                // Sample once from the capture session's existing frozen image.
                // This does not capture any new desktop pixels or change export content.
                var dpi = VisualTreeHelper.GetDpi(this); var point = material.PointToScreen(new Point(1, 1));
                var desktop = Forms.SystemInformation.VirtualScreen;
                var bounds = new Drawing.Rectangle((int)point.X, (int)point.Y, (int)(material.ActualWidth * dpi.DpiScaleX), (int)(material.ActualHeight * dpi.DpiScaleY));
                var crop = Native.CropBounds(bounds, desktop);
                if (!crop.IsEmpty) material.SetBackdrop(new CroppedBitmap(backdrop, crop));
            }
        };
        SourceInitialized += (_, _) => Native.Exclude(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) session.Cancel(); };
    }
    private void Position()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Native.Place(this, new Drawing.Rectangle(screen.WorkingArea.Left + (screen.WorkingArea.Width - (int)(ActualWidth * dpi.DpiScaleX)) / 2, screen.WorkingArea.Top + 12, (int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY)));
    }
    private static Border Divider() => new() { Width = .75, Height = 22, Background = UI.Brush("#40FFFFFF"), Margin = new Thickness(6, 0, 6, 0) };
    private static Button Action(string label, string symbol, Action action)
    {
        var button = UI.Button(label, action, false, true); UI.Symbol(button, symbol); UI.GlassButton(button, true);
        button.Width = button.Height = 40; button.Padding = new Thickness(9); button.Margin = new Thickness(1);
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(button, 250);
        return button;
    }
    internal void Refresh(CaptureMode mode) => Refresh(mode, CaptureIntent.Screenshot);
    internal void Refresh(CaptureMode mode, CaptureIntent intent)
    {
        foreach (var entry in buttons)
        {
            UI.GlassButton(entry.Value, true, intent != CaptureIntent.ColorPicker && entry.Key == mode);
            entry.Value.IsEnabled = intent != CaptureIntent.ColorPicker;
        }
        UI.GlassButton(screenshot, true, intent == CaptureIntent.Screenshot);
        UI.GlassButton(liveOcr, true, intent == CaptureIntent.LiveOcr);
        UI.GlassButton(colorPicker, true, intent == CaptureIntent.ColorPicker);
        capture.Content = intent switch { CaptureIntent.LiveOcr => "Read text", CaptureIntent.ColorPicker => "Pick color", _ => "Capture" };
        System.Windows.Automation.AutomationProperties.SetName(capture, capture.Content.ToString()!);
        capture.IsEnabled = intent != CaptureIntent.ColorPicker && mode is CaptureMode.Monitor or CaptureMode.AllMonitors;
        capture.ToolTip = intent switch
        {
            CaptureIntent.LiveOcr when mode == CaptureMode.Region => "Drag over text to copy it without saving a screenshot.",
            CaptureIntent.LiveOcr => "Read and copy text from the selected display.",
            CaptureIntent.ColorPicker => "Move over the screen to preview a color, then click to copy its hex value.",
            _ => "Capture the selected display."
        };
        UpdateLayout(); Position();
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
        var panel = new StackPanel(); var label = UI.Text(seconds.ToString(), 56, UI.Ink(true), FontWeights.SemiBold); label.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(label); panel.Children.Add(UI.Button("Cancel capture", () => { completion.TrySetResult(false); Close(); }, false, true));
        Content = UI.Floating(panel, true, new Thickness(24));
        timer.Tick += (_, _) => { if (--seconds <= 0) { completion.TrySetResult(true); Close(); } else label.Text = seconds.ToString(); };
        Closed += (_, _) => { timer.Stop(); completion.TrySetResult(false); }; Loaded += (_, _) => timer.Start();
    }
}
