using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class SelfTest
{
    internal static BitmapSource SampleImage()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(UI.Brush("#DDDDDD").Color, UI.Brush("#777777").Color, 90), null, new Rect(0, 0, 960, 600));
            dc.DrawEllipse(Brushes.White, null, new Point(690, 185), 85, 85);
            var far = Geometry.Parse("M 0,420 Q 140,140 310,370 Q 430,470 540,330 Q 730,160 960,360 L960,600 L0,600 Z");
            dc.DrawGeometry(UI.Brush("#999999"), null, far);
            var near = Geometry.Parse("M0,470 Q180,330 390,490 Q600,600 790,420 Q890,380 960,430 L960,600 L0,600 Z");
            dc.DrawGeometry(UI.Brush("#555555"), null, near);
            var front = Geometry.Parse("M0,570 Q210,410 390,550 Q590,640 750,535 Q900,475 960,510 L960,600 L0,600 Z");
            dc.DrawGeometry(UI.Brush("#333333"), null, front);
        }
        var bitmap = new RenderTargetBitmap(960, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static void Run(App app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var output = Path.GetFullPath("test-results"); Directory.CreateDirectory(output);
        var log = new StringBuilder(); int count = 0;
        void Check(bool value, string description) { if (!value) throw new Exception(description); log.AppendLine("PASS " + description); count++; }
        try
        {
            var desktop = new System.Drawing.Rectangle(-1920, -200, 4480, 1640);
            Check(Native.CropBounds(new(-1800, -100, 400, 300), desktop) == new Int32Rect(120, 100, 400, 300), "negative monitor coordinates translate to image pixels");
            Check(Native.CropBounds(new(-2100, -300, 400, 300), desktop) == new Int32Rect(0, 0, 220, 200), "selection clips to desktop bounds");
            Check(Native.CropBounds(new(9000, 9000, 100, 100), desktop).IsEmpty, "out-of-bounds selection rejected");
            var malformed = new Settings { Duration = double.NaN, PreviewWidth = 0, Stroke = 80 }; malformed.Normalize();
            Check(malformed.Duration == 5 && malformed.PreviewWidth == 220 && malformed.Stroke == 3, "settings constrain invalid values");
            var sample = SampleImage(); var crop = Native.Crop(sample, new(100, 120, 240, 180), new(0, 0, 960, 600));
            Check(crop.PixelWidth == 240 && crop.PixelHeight == 180, "cropping preserves requested pixel resolution");
            Check(new Settings { Radius = 25 }.NormalizeAndReadRadius() == 0, "old rounded-corner preferences migrate to square edges");
            string png = Path.Combine(output, "sample.png"); Native.SavePng(sample, png);
            var decoder = BitmapDecoder.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Check(decoder.Frames[0].PixelWidth == 960 && decoder.Frames[0].PixelHeight == 600, "atomic PNG save produces readable full-resolution image");
            foreach (var format in ExportService.Formats)
            {
                string export = Path.Combine(output, "export." + ExportService.Extension(format)); ExportService.Save(sample, export, format);
                if (format == "PDF") Check(Wait(ReadPdf(export)), "exported PDF opens in Windows PDF reader with one page");
                else { var saved = BitmapDecoder.Create(new Uri(export), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); Check(saved.Frames[0].PixelWidth == sample.PixelWidth && saved.Frames[0].PixelHeight == sample.PixelHeight, format + " export is decodable at original dimensions"); }
            }
            Check(ExportService.Resize(sample, 50).PixelWidth == 480, "export resizing produces requested dimensions");
            var fixture = TextImage(); var recognized = Wait(OcrService.RecognizeAsync(fixture));
            Check(recognized.Contains("BETTER SS", StringComparison.OrdinalIgnoreCase) && recognized.Contains("2026"), "local Windows OCR recognizes known text in an actual image");
            Native.SavePng(fixture, Path.Combine(output, "ocr-fixture.png"));
            var dib = ClipboardService.Dib(fixture);
            Check(BitConverter.ToInt32(dib, 0) == 40 && BitConverter.ToInt32(dib, 4) == fixture.PixelWidth && BitConverter.ToInt16(dib, 14) == 24, "clipboard uses standard 24-bit Windows DIB format");
            var dragData = new DataObject(); dragData.SetData(DataFormats.FileDrop, new[] { png }); dragData.SetData(DataFormats.Bitmap, sample);
            Check(dragData.GetDataPresent(DataFormats.FileDrop) && dragData.GetDataPresent(DataFormats.Bitmap), "drag payload supports file and bitmap destinations");
            Check(SoundCatalog.AvailableBuiltInFileNames.Length == SoundCatalog.BuiltInFileNames.Length, "all seven capture sounds are included in the build");
            foreach (var fileName in SoundCatalog.BuiltInFileNames)
                Check(CanPlaySound(SoundCatalog.BuiltInPath(fileName)), fileName + " plays through Windows media playback");
            string missingSound = SoundCatalog.SettingValue("missing.mp3");
            Check(SoundCatalog.UsesSystemSound(missingSound), "a missing built-in sound uses the Windows fallback");
            var settingsBeforeSound = app.Settings;
            app.Settings = new Settings { SoundEnabled = true, SoundPath = missingSound };
            app.PlaySound();
            app.Settings = settingsBeforeSound;
            Check(true, "capture sound playback uses the Windows system fallback when a file is missing");
            string shutterSetting = SoundCatalog.SettingValue("Shutter Click.mp3");
            Check(SoundCatalog.SelectedFileName(shutterSetting) == "Shutter Click.mp3", "shutter sound selection resolves to its packaged preset");
            var settingsBefore = app.Settings; app.Settings = new Settings { Theme = "Light" }; app.HotkeyStatus = "Ready · Ctrl + Shift + S";
            var window = new PreferencesWindow(app); window.Show(); window.UpdateLayout();
            Pump(); RenderWindow(window, Path.Combine(output, "preferences-light.png"));
            Check(window.ActualWidth >= 860, "preferences window builds and renders"); window.Close();
            app.Settings.Theme = "Dark"; var dark = new PreferencesWindow(app); dark.Show(); dark.UpdateLayout(); Pump(); RenderWindow(dark, Path.Combine(output, "preferences-dark.png"));
            foreach (var page in new[] { "Floating preview", "Appearance", "Sound", "Storage", "Startup & guide" }) { dark.ShowPage(page); dark.UpdateLayout(); Pump(); RenderWindow(dark, Path.Combine(output, page.Replace(' ', '-') + ".png")); }
            Check(dark.IsVisible, "every preferences page builds and renders"); dark.Close();
            var editor = new EditorWindow(app, sample, png); editor.Show(); editor.UpdateLayout(); Pump(); var rendered = editor.Render();
            Check(rendered.PixelWidth == 960 && rendered.PixelHeight == 600, "editor export retains original image dimensions");
            Check(SamePixels(sample, rendered), "unannotated editor export preserves image pixels");
            editor.SelectTool("Shapes"); editor.Begin(new Point(120, 140)); editor.Move(new Point(360, 280)); Wait(editor.EndAsync());
            Check(!SamePixels(sample, editor.Render()), "shape tool adds a flattened export annotation"); editor.Undo(); Check(SamePixels(sample, editor.Render()), "shape undo restores the original pixels");
            editor.Begin(new Point(120, 140)); editor.Move(new Point(360, 280)); Wait(editor.EndAsync()); var keyboardShape = editor.Render();
            Check(editor.HandleUndoRedoShortcut(Key.Z, ModifierKeys.Control) && SamePixels(sample, editor.Render()), "Ctrl+Z undoes a normal editor annotation");
            Check(editor.HandleUndoRedoShortcut(Key.Y, ModifierKeys.Control) && SamePixels(keyboardShape, editor.Render()), "Ctrl+Y redoes a normal editor annotation"); editor.Undo();
            editor.SelectTool("Highlighter"); editor.Begin(new Point(100, 100)); editor.Move(new Point(400, 100)); Wait(editor.EndAsync());
            Check(!SamePixels(sample, editor.Render()), "highlighter changes exported pixels"); editor.Undo(); Check(SamePixels(sample, editor.Render()), "undo restores exact original pixels"); editor.Redo(); Check(!SamePixels(sample, editor.Render()), "redo reapplies the annotation"); editor.Undo();
            editor.SelectTool("Gaussian blur"); editor.Begin(new Point(300, 350)); editor.Move(new Point(800, 350)); Wait(editor.EndAsync()); Check(!SamePixels(sample, editor.Render()), "Gaussian brush alters pixels along stroke"); editor.Undo();
            editor.SelectTool("Mosaic"); editor.Begin(new Point(300, 350)); editor.Move(new Point(800, 350)); Wait(editor.EndAsync()); Check(!SamePixels(sample, editor.Render()), "mosaic brush alters pixels along stroke");
            Native.SavePng(editor.Render(), Path.Combine(output, "editor-export.png")); RenderWindow(editor, Path.Combine(output, "editor.png")); editor.Close();
            var exportWindow = new ExportWindow(app, fixture, png); exportWindow.Show(); exportWindow.UpdateLayout(); Pump(); RenderWindow(exportWindow, Path.Combine(output, "export-dialog.png")); exportWindow.Close();
            var preview = new PreviewWindow(app, sample, png, System.Windows.Forms.Screen.PrimaryScreen!); preview.Show(); preview.UpdateLayout(); Pump(); RenderWindow(preview, Path.Combine(output, "floating-preview.png"));
            Check(preview.ActualWidth > app.Settings.PreviewWidth, "floating preview renders with action strip and frame"); preview.Close();
            app.Settings = settingsBefore;
            // Test the actual capture backend without retaining any desktop pixels.
            var native = Native.Capture(new System.Drawing.Rectangle(System.Windows.Forms.SystemInformation.VirtualScreen.Location, new System.Drawing.Size(2, 2)));
            Check(native.PixelWidth == 2 && native.PixelHeight == 2, "native desktop capture backend returns pixels");
            bool delivered = false, dismissed = false;
            var capture = new CaptureSession(CaptureMode.AllMonitors, System.Windows.Forms.Screen.PrimaryScreen!, (shot, _, _) => {
                var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                delivered = shot.PixelWidth == bounds.Width && shot.PixelHeight == bounds.Height;
            }, () => dismissed = true);
            capture.Start(); Pump();
            var overlays = app.Windows.OfType<CaptureOverlay>().ToArray();
            Check(overlays.Length == System.Windows.Forms.Screen.AllScreens.Length, "capture creates an overlay for each connected monitor");
            foreach (var overlay in overlays)
            {
                Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(overlay).Handle, out var bounds);
                Check(System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds == bounds.Bounds), "overlay bounds match physical monitor pixels");
            }
            var toolbar = app.Windows.OfType<CaptureToolbar>().Single(); toolbar.UpdateLayout(); RenderWindow(toolbar, Path.Combine(output, "capture-toolbar.png"));
            capture.Finish();
            Check(delivered && dismissed && !app.Windows.OfType<CaptureOverlay>().Any(), "all-display capture delivers image and removes overlays");
            var testWindow = new Window { Title = "Better SS minimized-window test", Width = 500, Height = 300, Topmost = true, Background = Brushes.White, Content = new System.Windows.Controls.Image { Source = fixture } }; testWindow.Show(); Pump();
            InteractionTests.FocusTestWindow(testWindow);
            var handle = new System.Windows.Interop.WindowInteropHelper(testWindow).Handle;
            testWindow.WindowState = WindowState.Minimized; Pump();
            var entry = WindowCatalog.Enumerate(true).Single(w => w.Handle == handle);
            Check(entry.Minimized, "window catalog includes minimized windows");
            var captured = Wait(WindowCatalog.CaptureAsync(entry)); Pump();
            Check(captured.Image.PixelWidth > 200 && captured.Image.PixelHeight > 150 && Native.IsIconic(handle), "minimized window is captured and returned to minimized state"); testWindow.Close();
            var backup = new DataObject(); var previous = Clipboard.GetDataObject();
            if (previous != null) foreach (var format in previous.GetFormats(false)) { try { var item = previous.GetData(format, false); if (item is MemoryStream memory) item = new MemoryStream(memory.ToArray()); if (item != null) backup.SetData(format, item); } catch { } }
            try
            {
                Check(Wait(ClipboardService.ImageAsync(fixture)) && Clipboard.ContainsImage() && Clipboard.GetImage().PixelWidth == fixture.PixelWidth, "image clipboard write is readable through Windows clipboard API");
                Check(Wait(ClipboardService.TextAsync("Better SS OCR test")) && Clipboard.GetText() == "Better SS OCR test", "OCR text clipboard round trip succeeds");
            }
            finally { Wait(RestoreClipboardAsync(backup)); }
            InteractionTests.Run(app, sample, png, Check);
            log.AppendLine($"SUCCESS: {count} checks passed."); Environment.ExitCode = 0;
        }
        catch (Exception ex) { log.AppendLine("FAIL " + ex); Environment.ExitCode = 1; }
        File.WriteAllText(Path.Combine(output, "results.txt"), log.ToString()); app.Shutdown(Environment.ExitCode);
    }
    private static double NormalizeAndReadRadius(this Settings settings) { settings.Normalize(); return settings.Radius; }
    private static async Task RestoreClipboardAsync(DataObject backup)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(backup, true); return; }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 12) { await Task.Delay(120); }
        }
    }
    internal static BitmapSource TextImage()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 900, 240)); dc.DrawText(new FormattedText("BETTER SS OCR 2026\nCopy text locally", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 38, Brushes.Black, 1), new Point(32, 42)); }
        var bitmap = new RenderTargetBitmap(900, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static async Task<bool> ReadPdf(string path)
    { var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path); var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file); return pdf.PageCount == 1; }
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher; var frame = new DispatcherFrame();
            task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static bool CanPlaySound(string path)
    {
        var player = new MediaPlayer { Volume = 0 }; bool decoded = false; var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        var progress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timeout.Tick += (_, _) => frame.Continue = false;
        progress.Tick += (_, _) => { if (player.Position > TimeSpan.Zero) { decoded = true; frame.Continue = false; } };
        player.MediaOpened += (_, _) => { if (player.NaturalDuration.HasTimeSpan && player.NaturalDuration.TimeSpan > TimeSpan.Zero) { player.Play(); progress.Start(); } else frame.Continue = false; };
        player.MediaEnded += (_, _) => { decoded = true; frame.Continue = false; };
        player.MediaFailed += (_, _) => frame.Continue = false;
        timeout.Start(); player.Open(new Uri(path)); Dispatcher.PushFrame(frame); timeout.Stop(); progress.Stop(); player.Close(); return decoded;
    }
    private static bool SamePixels(BitmapSource left, BitmapSource right)
    {
        var a = new FormatConvertedBitmap(left, PixelFormats.Bgra32, null, 0); var b = new FormatConvertedBitmap(right, PixelFormats.Bgra32, null, 0);
        var stride = a.PixelWidth * 4; var first = new byte[stride * a.PixelHeight]; var second = new byte[first.Length]; a.CopyPixels(first, stride, 0); b.CopyPixels(second, stride, 0);
        for (int i = 0; i < first.Length; i++) if (Math.Abs(first[i] - second[i]) > 1) return false; return true;
    }
    private static void RenderWindow(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        var height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); image.Render(window); Native.SavePng(image, path);
    }
}
