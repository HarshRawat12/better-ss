using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace BetterSS;

// Exports the real native views for design review, using only generated sample content.
// This mode does not register hotkeys, create a tray icon, or save user preferences.
internal static class DesignPreview
{
    internal static void Run(App app, string[] args)
    {
        int option = Array.IndexOf(args, "--output");
        string output = Path.GetFullPath(option >= 0 && option + 1 < args.Length ? args[option + 1] : "native-design-preview");
        Directory.CreateDirectory(output);
        app.Settings = new Settings { IntroductionSeen = true, Duration = 30, PreviewWidth = 320, CaptureAnimation = false };
        app.SettingsPathOverride = Path.Combine(output, "preview-settings.json");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, e) => { File.AppendAllText(Path.Combine(output, "dispatcher-error.txt"), e.Exception + Environment.NewLine); e.Handled = true; };
        app.Startup += async (_, _) =>
        {
            try { if (args.Contains("--video-only")) await ExportVideo(app, output); else await Export(app, output); File.WriteAllText(Path.Combine(output, "complete.txt"), "Native WPF design previews exported. No interaction regression suite was run."); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); }
            finally { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); }
        };
        app.Run();
    }
    private static async Task ExportVideo(App app, string output)
    {
        string image = Path.Combine(output, "sample.png"); Save(Sample(), image);
        string source = Path.Combine(output, "Recording 2026-10-08.mp4");
        await VideoCutService.RunAsync(ScreenRecorder.ExecutablePath, new[] { "-hide_banner", "-v", "error", "-nostdin", "-y", "-loop", "1", "-i", image, "-t", "18.6", "-r", "30", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", source }, System.Threading.CancellationToken.None);
        foreach (string theme in new[] { "Light", "Dark" })
        {
            app.Settings.Theme = theme; var editor = new VideoEditorWindow(app, source) { ShowActivated = false, Width = 1280, Height = 801 }; editor.Show();
            for (int i = 0; i < 100 && !editor.LoadFinished; i++) await Task.Delay(150);
            await Task.Delay(600); Save(editor, Path.Combine(output, "video-editor-" + theme.ToLowerInvariant() + ".png"));
            File.WriteAllText(Path.Combine(output, "video-" + theme.ToLowerInvariant() + ".txt"), $"Load finished: {editor.LoadFinished}\nPreview ready: {editor.PreviewReady}\nTimeline thumbnails: {editor.TimelineThumbnailCount}\n");
            editor.Width = editor.MinWidth; editor.Height = editor.MinHeight; await Task.Delay(250); Save(editor, Path.Combine(output, "video-editor-minimum-" + theme.ToLowerInvariant() + ".png")); editor.Close();
        }
    }
    private static async Task Export(App app, string output)
    {
        var sample = Sample(); string samplePath = Path.Combine(output, "sample.png"); Save(sample, samplePath);
        foreach (string theme in new[] { "Light", "Dark" })
        {
            app.Settings.Theme = theme; bool dark = theme == "Dark"; string suffix = theme.ToLowerInvariant();
            var preferences = new PreferencesWindow(app) { ShowActivated = false }; preferences.Show();
            foreach (string page in new[] { "Capture", "Floating preview", "Appearance", "Sound", "Storage", "Startup & guide" })
            { preferences.ShowPage(page); await Task.Delay(280); Save(preferences, Path.Combine(output, "preferences-" + page.Split(' ')[0].ToLowerInvariant() + "-" + suffix + ".png")); }
            preferences.Close();
            await View(new EditorWindow(app, sample, samplePath), "editor-" + suffix, output);
            await View(new ExportWindow(app, sample, samplePath), "export-" + suffix, output);
            await View(new ColorPickerWindow(Color.FromRgb(52, 120, 246), dark), "color-" + suffix, output);
            await View(new VideoRecordingWindow(app), "recording-" + suffix, output);
            await View(new WelcomeWindow(app), "walkthrough-" + suffix, output);
            await View(new OcrWindow(app, sample), "text-" + suffix, output);
            // The shortcut recorder isn't shown: rendering it must not intercept the user's keyboard.
            var shortcut = new HotkeyWindow(app); LayoutOnly(shortcut, Path.Combine(output, "shortcut-" + suffix + ".png")); shortcut.Close();
            await Glass(app, sample, output, suffix);
        }
        string? video = Directory.Exists(".validation") ? Directory.GetFiles(".validation", "*.mp4", SearchOption.AllDirectories).FirstOrDefault(p => Path.GetFileName(p).Equals("source with audio.mp4", StringComparison.OrdinalIgnoreCase)) : null;
        if (video != null)
        {
            var editor = new VideoEditorWindow(app, video) { ShowActivated = false }; editor.Show();
            for (int i = 0; i < 60 && !editor.LoadFinished; i++) await Task.Delay(200);
            await Task.Delay(400); Save(editor, Path.Combine(output, "video-editor.png")); editor.Close();
        }
    }
    private static async Task View(Window window, string name, string output)
    { window.ShowActivated = false; window.Show(); await Task.Delay(400); Save(window, Path.Combine(output, name + ".png")); window.Close(); }
    private static async Task Glass(App app, BitmapSource sample, string output, string suffix)
    {
        var screen = Forms.Screen.PrimaryScreen!;
        var scene = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Background = new LinearGradientBrush(Color.FromRgb(34, 87, 124), Color.FromRgb(184, 112, 74), 30) };
        var desktop = new Grid();
        desktop.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(63, 117, 131), Color.FromRgb(198, 167, 117), 130), CornerRadius = new CornerRadius(110), Width = 680, Height = 390, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 30, 40) });
        desktop.Children.Add(new TextBlock { Text = "Better SS\nCapture a moment.", FontSize = 54, Foreground = Brushes.White, Margin = new Thickness(80), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        scene.Content = desktop; scene.Show(); Native.Place(scene, screen.Bounds); await Task.Delay(200);
        var frozen = Native.Capture(Forms.SystemInformation.VirtualScreen);
        var session = new CaptureSession(CaptureMode.Region, screen, (_, _, _) => { }, () => { });
        var toolbar = new CaptureToolbar(session, screen, backdrop: frozen) { ShowActivated = false }; toolbar.Show(); toolbar.Refresh(CaptureMode.Region);
        var preview = new PreviewWindow(app, sample, Path.Combine(output, "sample.png"), screen); preview.PauseForTutorial(true); preview.ShowTutorialActions(true); preview.Show();
        await Task.Delay(3500);
        Save(preview, Path.Combine(output, "preview-" + suffix + ".png")); Save(toolbar, Path.Combine(output, "capture-toolbar-" + suffix + ".png"));
        File.WriteAllText(Path.Combine(output, "glass-" + suffix + ".txt"), $"Live sample frames: {preview.LiveBackdrop.FrameCount}\nBackdrop active: {preview.PreviewMaterial.UsesBackdrop}\nFull refraction active: {preview.PreviewMaterial.UsesFullRefraction}\nRendering tier: {RenderCapability.Tier >> 16}\nTransparency enabled: {GlassSurface.Transparency}\nSampler: {preview.LiveBackdrop.Status}\nEnabled: {preview.LiveBackdrop.IsSampling}\nWindow loaded: {preview.IsLoaded}; visible: {preview.IsVisible}; opacity: {preview.Opacity}\n");
        preview.Close(); toolbar.Close(); scene.Close();
    }
    private static void LayoutOnly(Window window, string path)
    { var content = (FrameworkElement)window.Content; content.Measure(new Size(window.Width - 32, double.PositiveInfinity)); content.Arrange(new Rect(new Point(), content.DesiredSize)); content.UpdateLayout(); Save(content, path); }
    private static void Save(Window window, string path) { window.UpdateLayout(); Save((FrameworkElement)window.Content, path, window.Background); }
    private static void Save(FrameworkElement content, string path, Brush? background = null)
    { var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(content.ActualWidth)), Math.Max(1, (int)Math.Ceiling(content.ActualHeight)), 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight); dc.DrawRectangle(background, null, bounds); dc.DrawRectangle(new VisualBrush(content), null, bounds); } bitmap.Render(visual); Save(bitmap, path); }
    private static void Save(BitmapSource bitmap, string path)
    { using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file); }
    private static BitmapSource Sample()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(UI.Brush("#F5F5F7"), null, new Rect(0, 0, 1000, 650));
            dc.DrawRoundedRectangle(Brushes.White, new Pen(UI.Brush("#E0E0E6"), 1), new Rect(40, 40, 920, 570), 20, 20);
            Text(dc, "A clear view of your day.", 64, 70, 32, "#1D1D1F"); Text(dc, "A sample screenshot for Better SS design previews", 66, 125, 16, "#68686F");
            dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(42, 123, 159), Color.FromRgb(116, 196, 181), 120), null, new Rect(64, 180, 870, 245), 16, 16);
            Text(dc, "Make room for the work that matters.", 96, 265, 28, "#FFFFFF");
            for (int i = 0; i < 3; i++) { dc.DrawRoundedRectangle(UI.Brush("#F5F5F7"), null, new Rect(64 + i * 297, 449, 276, 124), 12, 12); Text(dc, new[] { "Capture", "Create", "Share" }[i], 86 + i * 297, 472, 20, "#1D1D1F"); Text(dc, new[] { "Find a moment", "Add your perspective", "Keep things moving" }[i], 86 + i * 297, 512, 14, "#68686F"); }
        }
        var bitmap = new RenderTargetBitmap(1000, 650, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static void Text(DrawingContext dc, string text, double x, double y, double size, string color)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, UI.Brush(color), 1), new Point(x, y));
}
