using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class PresentationTests
{
    internal static void Run(App app, Action<bool, string> check, string output)
    {
        var reduced = Motion.ReducedOverride; var transparency = GlassSurface.TransparencyOverride; var contrast = UI.HighContrastOverride;
        Window? window = null;
        try
        {
            Motion.ReducedOverride = false;
            var scale = new ScaleTransform(.98, .98);
            window = new Window { Title = "Better SS motion review", Width = 260, Height = 140, Content = new Border { Background = Brushes.White, RenderTransform = scale } }; window.Show(); Pump(100);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Standard, true); Pump(45);
            double mid = scale.ScaleX;
            check(mid > .98 && mid < 1, $"spring release advances without bounce (value {mid:0.000000})");
            Motion.To(scale, ScaleTransform.ScaleXProperty, .985, Motion.Standard, true);
            check(Math.Abs(scale.ScaleX - mid) < .00001, "spring interruption preserves current presentation position");
            Pump(30); double second = scale.ScaleX; Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Standard, true);
            check(Math.Abs(scale.ScaleX - second) < .00001, "rapid spring reversal starts from its live position");
            Pump(250);
            check(scale.ScaleX == 1 && !Motion.HasClock(scale, ScaleTransform.ScaleXProperty) && !scale.HasAnimatedProperties, "completed motion removes its animation clock");
            var physical = Motion.Spring(.98, 1, 0, 40, .05);
            var interrupted = Motion.Spring(physical.Position, .985, physical.Velocity, 40, 0);
            check(Math.Abs(interrupted.Position - physical.Position) < 1e-12 && Math.Abs(interrupted.Velocity - physical.Velocity) < 1e-12, "spring retarget carries velocity continuously");
            var panel = (FrameworkElement)window.Content; Motion.Enter(panel); Pump(45); double panelOpacity = panel.Opacity; var panelScale = panel.RenderTransform;
            Motion.Enter(panel);
            check(ReferenceEquals(panelScale, panel.RenderTransform) && Math.Abs(panel.Opacity - panelOpacity) < .00001, "rapid panel re-entry retargets without resetting its scale or opacity");
            Pump(280);
            Motion.ReducedOverride = true; Motion.To(scale, ScaleTransform.ScaleXProperty, .98, Motion.Panel, true);
            check(scale.ScaleX == .98 && !scale.HasAnimatedProperties, "reduced motion applies feedback immediately without a spatial clock");
            Motion.ReducedOverride = false; GlassSurface.TransparencyOverride = true;
            check(Contrast(((SolidColorBrush)UI.Accent(true)).Color, ((SolidColorBrush)UI.Selected(true)).Color) >= 4.5, "dark selected tool labels meet 4.5 text contrast");
            check(Contrast(((SolidColorBrush)UI.Accent(false)).Color, ((SolidColorBrush)UI.Selected(false)).Color) >= 4.5, "light selected tool labels meet 4.5 text contrast");
            var root = new StackPanel { Margin = new Thickness(24) };
            root.Children.Add(UI.Text("Floating materials · light and dark", 20, UI.Ink(false), FontWeights.SemiBold));
            var subtitle = UI.Text("Readability over white, black and busy content. Foreground remains crisp.", 12, UI.Muted(false)); subtitle.Margin = new Thickness(0, 6, 0, 16); root.Children.Add(subtitle);
            var cases = new Grid(); cases.ColumnDefinitions.Add(new ColumnDefinition()); cases.ColumnDefinitions.Add(new ColumnDefinition());
            for (int row = 0; row < 3; row++)
            {
                cases.RowDefinitions.Add(new RowDefinition { Height = new GridLength(132) });
                for (int column = 0; column < 2; column++)
                {
                    bool dark = column == 1;
                    var background = row == 0 ? Solid(Colors.White) : row == 1 ? Solid(Colors.Black) : SelfTest.TextImage();
                    var scene = new Grid { Background = new ImageBrush(background), Margin = new Thickness(0, 0, 12, 12) };
                    var tools = new StackPanel { Orientation = Orientation.Horizontal };
                    var pen = UI.Button("Pen", () => { }, false, dark); UI.Icon(pen, "\uED63"); UI.Select(pen, true, dark); tools.Children.Add(pen);
                    var shape = UI.Button("Shapes", () => { }, false, dark); UI.Icon(shape, "\uE7C1"); tools.Children.Add(shape);
                    var export = UI.Button("Export", () => { }, true, dark); tools.Children.Add(export);
                    var material = new GlassSurface(tools, dark, new Thickness(12), backdrop: background) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    scene.Children.Add(material); Grid.SetRow(scene, row); Grid.SetColumn(scene, column); cases.Children.Add(scene);
                    check(material.UsesBackdrop, (dark ? "dark" : "light") + " glass samples only its existing image background case " + row);
                    // Worst-case tint composition, including a white background under dark glass.
                    Color tint = UI.Brush(dark ? "#29292E" : "#FFFFFF").Color;
                    var worst = Blend(tint, dark ? Colors.White : Colors.Black, 230 / 255.0);
                    check(Contrast(((SolidColorBrush)UI.Ink(dark)).Color, worst) >= 4.5, (dark ? "dark" : "light") + " glass text meets 4.5 contrast over arbitrary backgrounds case " + row);
                }
            }
            root.Children.Add(cases);
            var note = UI.Text("Desktop floating windows use the same rim and tint with a solid fallback when transparency is disabled.", 12, UI.Muted(false)); root.Children.Add(note);
            window.Close(); window = new Window { Title = "Better SS material review", Width = 1050, Height = 590, Background = UI.Background(false), Content = root, WindowStartupLocation = WindowStartupLocation.CenterScreen, FontFamily = new FontFamily("Segoe UI") };
            window.Show(); Pump(320); Save(window, Path.Combine(output, "materials-review.png"));
            var testButton = UI.Button("Keyboard focus", () => { }); root.Children.Add(testButton); window.UpdateLayout(); testButton.Focus(); Pump(60);
            check(testButton.IsKeyboardFocused && testButton.Template.FindName("Focus", testButton) is Border { Visibility: Visibility.Visible }, "keyboard focus has a visible outline");
            UI.Select(testButton, true, false); Pump(160);
            check(testButton.Template.FindName("SelectionMark", testButton) is Border { Visibility: Visibility.Visible }, "selected controls have a structural indicator in addition to color");
            UI.Select(testButton, false, false); UI.Select(testButton, true, false); Pump(160);
            check(testButton.Template.FindName("SelectionMark", testButton) is Border { Visibility: Visibility.Visible, Opacity: 1 }, "interrupted selection fade keeps the latest selected state");
            var menu = UI.Menu(false); menu.PlacementTarget = testButton; menu.Items.Add(new MenuItem { Header = "Export screenshot" }); menu.IsOpen = true; Pump(50);
            check(menu.IsOpen && menu.Template.FindName("MenuSurface", menu) is FrameworkElement { RenderTransform: ScaleTransform }, "menu opens from a stable trigger origin with immediate input availability");
            menu.IsOpen = false; check(!menu.IsOpen, "menu dismissal never waits for decorative motion");
            GlassSurface.TransparencyOverride = false;
            var fallback = UI.Floating(UI.Text("Solid fallback"), false, new Thickness(12));
            check(!fallback.UsesBackdrop && fallback.Effect == null, "transparency-disabled fallback has no blur or shadow effect");
            UI.HighContrastOverride = true; GlassSurface.TransparencyOverride = null; Motion.ReducedOverride = null;
            var accessible = UI.Floating(UI.Text("Accessible material"), true, new Thickness(12));
            check(!GlassSurface.Transparency && Motion.Reduced && accessible.Effect == null && UI.Ink(true) == SystemColors.WindowTextBrush, "high contrast uses OS text colors, solid materials and reduced motion");
            UI.HighContrastOverride = contrast; GlassSurface.TransparencyOverride = false; Motion.ReducedOverride = false;
            Pump(400);
            var process = Process.GetCurrentProcess(); TimeSpan before = process.TotalProcessorTime; Pump(1000); process.Refresh();
            File.WriteAllText(Path.Combine(output, "presentation-performance.txt"), $"WPF rendering tier: {RenderCapability.Tier >> 16}\nIdle review window CPU over 1000ms: {(process.TotalProcessorTime - before).TotalMilliseconds:0.0}ms\nNo permanent motion rendering subscription, no periodic desktop sampling. Blur radius12 on six small review surfaces. Native capture toolbar uses one small frozen crop.\nThis CPU sample is not a GPU frame-time benchmark.\n");
            window.Close(); window = null;
            GlassSurface.TransparencyOverride = true;
            CaptureGlassReview(check, output);
            GlassSurface.TransparencyOverride = false;
            string theme = app.Settings.Theme;
            try
            {
                foreach (string mode in new[] { "Light", "Dark" })
                {
                    app.Settings.Theme = mode; window = new EditorWindow(app, SelfTest.SampleImage(), Path.Combine(output, "sample.png")); window.Show(); Pump(300);
                    Save(window, Path.Combine(output, "editor-polished-" + mode.ToLowerInvariant() + ".png"));
                    window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout(); Pump(150);
                    check(Find<Button>(window).Where(b => b.IsVisible).All(b => { var p = b.TranslatePoint(new Point(), window); return p.X >= -1 && p.X + b.ActualWidth <= window.ActualWidth + 1; }), mode + " editor controls fit its minimum window width");
                    Save(window, Path.Combine(output, "editor-minimum-" + mode.ToLowerInvariant() + ".png")); window.Close(); window = null;
                    window = new PreferencesWindow(app); window.Show(); Pump(320); Save(window, Path.Combine(output, "preferences-polished-" + mode.ToLowerInvariant() + ".png")); window.Close(); window = null;
                }
            }
            finally { app.Settings.Theme = theme; }
        }
        finally { window?.Close(); Motion.ReducedOverride = reduced; GlassSurface.TransparencyOverride = transparency; UI.HighContrastOverride = contrast; }
    }
    private static BitmapSource Solid(Color color)
    { var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 320, 100)); var bitmap = new RenderTargetBitmap(320, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap; }
    private static void CaptureGlassReview(Action<bool, string> check, string output)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(UI.Brush("#176681").Color, UI.Brush("#D49A64").Color, 0), null, new Rect(0, 0, 960, 300));
            dc.DrawEllipse(UI.Brush("#647FB5"), null, new Point(350, 170), 100, 140);
        }
        var backdrop = new RenderTargetBitmap(960, 300, 96, 96, PixelFormats.Pbgra32); backdrop.Render(visual); backdrop.Freeze();
        var commands = new StackPanel(); var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string label in new[] { "Select area", "Window", "Display", "All displays", "Video", "Cancel", "Capture" })
        {
            var button = UI.Button(label, () => { }, label == "Capture", true);
            if (label == "Select area") UI.Select(button, true, true);
            else if (label != "Capture") { button.Background = Brushes.Transparent; button.BorderBrush = UI.Brush("#30FFFFFF"); }
            row.Children.Add(button);
        }
        commands.Children.Add(row);
        var material = new GlassSurface(commands, true, new Thickness(12), 16, backdrop, true);
        material.HorizontalAlignment = HorizontalAlignment.Center; material.VerticalAlignment = VerticalAlignment.Center;
        check(material.UsesBackdrop && material.UsesEdgeRefraction, "capture glass includes backdrop blur and a cached refractive rim");
        var scene = new Grid { Background = new ImageBrush(backdrop) }; scene.Children.Add(material);
        var review = new Window { Width = 960, Height = 300, WindowStyle = WindowStyle.None, Content = scene, FontFamily = new FontFamily("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
        try
        {
            review.Show(); Pump(100); Save(review, Path.Combine(output, "capture-glass.png"));
            check(material.Clip == null && commands.Opacity == 1, "capture glass panel is shown directly without an entrance animation");
            check(!commands.Children.OfType<TextBlock>().Any(text => text.Text.Contains("Drag an area", StringComparison.OrdinalIgnoreCase)), "capture panel omits the drag instruction text");
            // Include the brightest rim highlight in the worst-case white composition.
            var body = Blend(UI.Brush("#29292E").Color, Colors.White, GlassSurface.CaptureTintAlpha / 255.0);
            var highlighted = Blend(Colors.White, body, 37 / 255.0);
            check(Contrast(UI.Brush("#F5F5F7").Color, highlighted) >= 4.5, "more transparent capture glass retains text contrast over white backgrounds");
        }
        finally { review.Close(); }
    }
    private static Color Blend(Color a, Color b, double alpha) => Color.FromRgb((byte)(a.R * alpha + b.R * (1 - alpha)), (byte)(a.G * alpha + b.G * (1 - alpha)), (byte)(a.B * alpha + b.B * (1 - alpha)));
    private static double Luminance(Color color)
    { double Channel(byte value) { double c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); } return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B); }
    private static double Contrast(Color a, Color b) { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    private static void Pump(int ms)
    { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static void Save(Window window, string path)
    { window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); Native.SavePng(bitmap, path); }
    private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { if (root is T item) yield return item; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
