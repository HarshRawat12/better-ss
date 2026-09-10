using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed class VideoRecordingWindow : Window
{
    private readonly App app; private readonly bool dark;
    private readonly ChoiceField<string> source, resolution, fps, bitrate, format;
    private readonly ChoiceField<DisplayChoice> display;
    private readonly TextBlock status, sourceDetail; private readonly Button start;
    private WindowEntry? selectedWindow; private ScreenRecorder? recorder; private RecordingOverlay? overlay;
    private string? recordedFile;
    private VideoExportSettings? recordedSettings;
    private bool recordingBusy;
    private readonly DispatcherTimer captureHealth = new() { Interval = TimeSpan.FromMilliseconds(500) };
    internal Button SourceField => source.Button; internal ContextMenu? SourceMenu => source.Menu; internal Button DisplayField => display.Button; internal ContextMenu? DisplayMenu => display.Menu;

    internal VideoRecordingWindow(App app)
    {
        this.app = app; dark = UI.Dark(app.Settings); UI.SetupWindow(this, "Record video", 640, 700, dark); ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
        var body = new StackPanel { Margin = new Thickness(30) }; body.Children.Add(UI.Text("Record video", 25, UI.Ink(dark), FontWeights.SemiBold));
        var intro = UI.Text("Choose what to record, then set output quality. Starting hides this window and leaves only a small recording control on the desktop.", 12, UI.Muted(dark)); intro.Margin = new Thickness(0, 8, 0, 24); body.Children.Add(intro);
        source = new ChoiceField<string>(new[] { "Entire display", "Application window…" }, dark); source.Changed += SelectSource; Configure("RECORD", source.Button, body);
        sourceDetail = UI.Text("Choose a display below.", 11, UI.Muted(dark)); sourceDetail.Margin = new Thickness(0, -10, 0, 16); body.Children.Add(sourceDetail);
        var displays = new List<(string, DisplayChoice)>(); foreach (var screen in Forms.Screen.AllScreens) { var choice = new DisplayChoice(screen); displays.Add((choice.ToString(), choice)); }
        display = new ChoiceField<DisplayChoice>(displays, dark); Configure("DISPLAY", display.Button, body);
        resolution = new ChoiceField<string>(new[] { "Native", "1920 × 1080", "1280 × 720" }, dark); fps = new ChoiceField<string>(new[] { "24 FPS", "30 FPS", "60 FPS" }, dark, 1); bitrate = new ChoiceField<string>(new[] { "Optimized", "4 Mbps", "8 Mbps", "16 Mbps", "32 Mbps" }, dark); format = new ChoiceField<string>(new[] { "MP4 · H.264", "MP4 · H.265", "MOV · H.264", "MOV · H.265" }, dark);
        Configure("RESOLUTION", resolution.Button, body); Configure("FRAME RATE", fps.Button, body); Configure("BITRATE", bitrate.Button, body); Configure("EXPORT FORMAT", format.Button, body);
        status = UI.Text(ScreenRecorder.IsAvailable ? "Ready to record. The recording engine is included with Better SS." : "The bundled recording engine is missing. Rebuild Better SS to restore it.", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 10, 0, 20); body.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; start = UI.Button("Start recording…", Start, true, dark); start.IsEnabled = ScreenRecorder.IsAvailable; actions.Children.Add(UI.Button("Cancel", Close, false, dark)); actions.Children.Add(start); body.Children.Add(actions); Content = body;
        captureHealth.Tick += async (_, _) =>
        {
            if (recorder == null || recordingBusy || recorder.IsPaused) return;
            if (!recorder.IsRunning || (selectedWindow != null && !WindowCatalog.IsWindow(selectedWindow.Handle))) await StopAsync();
        };
        Closed += async (_, _) => { captureHealth.Stop(); if (recorder != null) { try { await recorder.StopAsync(); } catch (Exception ex) { app.Notify("Couldn't finish recording", ex.Message); } } };
    }

    private void Configure(string label, Button field, Panel parent) { parent.Children.Add(UI.Text(label, 10, UI.Muted(dark), FontWeights.SemiBold)); field.Margin = new Thickness(0, 7, 0, 16); field.Height = 38; field.HorizontalContentAlignment = HorizontalAlignment.Left; parent.Children.Add(field); }
    private void SelectSource(string value)
    {
        if (value == "Entire display") { selectedWindow = null; display.Button.IsEnabled = true; sourceDetail.Text = "Choose a display below."; return; }
        var picker = new WindowPicker(app, recording: true) { Owner = this };
        if (picker.ShowDialog() == true && picker.Selection != null) { selectedWindow = picker.Selection; display.Button.IsEnabled = false; sourceDetail.Text = "Application: " + selectedWindow.Application + " · " + selectedWindow.Title; }
        else { source.Select("Entire display", false); selectedWindow = null; display.Button.IsEnabled = true; sourceDetail.Text = "Choose a display below."; }
    }
    private async void Start()
    {
        string selectedFormat = format.Value; bool h265 = selectedFormat.Contains("H.265"); string extension = selectedFormat.StartsWith("MOV") ? "mov" : "mp4"; System.Drawing.Rectangle bounds; string sourceName;
        try { if (selectedWindow != null) { bounds = WindowCatalog.RecordingBounds(selectedWindow); sourceName = selectedWindow.Application; } else { bounds = display.Value.Screen.Bounds; sourceName = display.Value.Screen.DeviceName; } }
        catch (Exception ex) { status.Text = "Choose another recording source: " + ex.Message; return; }
        var save = new SaveFileDialog { Filter = $"{extension.ToUpperInvariant()} video|*.{extension}", DefaultExt = extension, AddExtension = true, InitialDirectory = app.Settings.SaveFolder, FileName = "Screen recording " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + "." + extension };
        if (save.ShowDialog(this) != true) return;
        var (width, height) = ParseResolution(resolution.Value, bounds.Width, bounds.Height); int rate = int.Parse(fps.Value.Split(' ')[0]); int mbps = bitrate.Value == "Optimized" ? OptimizedBitrate(width, height, rate) : int.Parse(bitrate.Value.Split(' ')[0]); recorder = new ScreenRecorder(); start.IsEnabled = false; status.Text = "Starting capture…";
        try { Hide(); await recorder.StartAsync(bounds, width, height, rate, mbps, h265, save.FileName, selectedWindow?.Handle ?? IntPtr.Zero); recordedFile = save.FileName; recordedSettings = new VideoExportSettings(width, height, rate, mbps, selectedFormat); overlay = new RecordingOverlay(dark); overlay.PauseRequested += TogglePauseAsync; overlay.StopRequested += StopAsync; overlay.Show(); captureHealth.Start(); status.Text = "Recording " + sourceName + " · " + width + " × " + height + " · " + rate + " FPS"; }
        catch (Exception ex) { status.Text = "Couldn't start recording: " + ex.Message; start.IsEnabled = true; recorder = null; Show(); Activate(); }
    }
    private async Task TogglePauseAsync()
    {
        if (recorder == null || overlay == null || recordingBusy) return; recordingBusy = true; overlay.SetBusy(true);
        try { if (recorder.IsPaused) { await recorder.ResumeAsync(); overlay.SetPaused(false); } else { await recorder.PauseAsync(); overlay.SetPaused(true); } }
        catch (Exception ex) { overlay.ShowError(ex.Message); }
        finally { recordingBusy = false; overlay.SetBusy(false); }
    }
    private async Task StopAsync()
    {
        if (recorder == null || recordingBusy) return; recordingBusy = true; captureHealth.Stop(); overlay?.SetBusy(true); bool saved = false;
        try { await recorder.StopAsync(); status.Text = "Recording saved."; saved = true; }
        catch (Exception ex) { status.Text = "Couldn't finish recording: " + ex.Message; }
        finally { recorder = null; overlay?.Close(); overlay = null; start.IsEnabled = true; recordingBusy = false; }
        if (saved && recordedFile != null) { new RecordingSavedWindow(app, recordedFile, recordedSettings).Show(); Close(); }
        else { Show(); Activate(); }
    }
    private static (int width, int height) ParseResolution(string value, int nativeWidth, int nativeHeight) { if (value == "Native") return (Even(nativeWidth), Even(nativeHeight)); var values = value.Split('×'); return (Even(int.Parse(values[0].Trim())), Even(int.Parse(values[1].Trim()))); }
    private static int Even(int value) => Math.Max(2, value / 2 * 2); private static int OptimizedBitrate(int width, int height, int rate) => Math.Clamp((int)Math.Round(width * height * rate * .00008), 4, 24);
    internal sealed class DisplayChoice { internal readonly Forms.Screen Screen; internal DisplayChoice(Forms.Screen screen) => Screen = screen; public override string ToString() => Screen.DeviceName + " · " + Screen.Bounds.Width + " × " + Screen.Bounds.Height + (Screen.Primary ? " · Primary" : ""); }
}

internal sealed class RecordingOverlay : Window
{
    private readonly Button pause, stop; private readonly TextBlock message; private readonly DispatcherTimer idle = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    internal event Func<Task>? PauseRequested, StopRequested;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity); private const uint WdaExcludeFromCapture = 0x00000011;
    internal RecordingOverlay(bool dark)
    {
        UI.SetupWindow(this, "Recording controls", 1, 1, dark); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        var root = new Border { Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 8, 8), Cursor = Cursors.SizeAll };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; var dot = UI.Text("●", 13, UI.Brush("#EF4444")); dot.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(dot); message = UI.Text("REC", 10, UI.Ink(dark), FontWeights.SemiBold); message.VerticalAlignment = VerticalAlignment.Center; message.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(message);
        pause = UI.Button("Pause", Pause, false, dark); pause.Padding = new Thickness(10, 6, 10, 6); stop = UI.Button("Stop", Stop, true, dark); stop.Padding = new Thickness(10, 6, 10, 6); row.Children.Add(pause); row.Children.Add(stop); root.Child = row; Content = root;
        root.MouseLeftButtonDown += (_, e) => { if (!InButton(e.OriginalSource as DependencyObject)) DragMove(); }; MouseEnter += (_, _) => { idle.Stop(); Opacity = 1; }; MouseLeave += (_, _) => { idle.Stop(); idle.Start(); }; idle.Tick += (_, _) => { idle.Stop(); Opacity = .5; };
        SourceInitialized += (_, _) => SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture); Loaded += (_, _) => { var area = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea; Left = area.Right - ActualWidth - 18; Top = area.Bottom - ActualHeight - 18; Opacity = .5; };
    }
    private static bool InButton(DependencyObject? node) { while (node != null) { if (node is Button) return true; node = VisualTreeHelper.GetParent(node); } return false; }
    private async void Pause() { if (PauseRequested != null) await PauseRequested.Invoke(); } private async void Stop() { if (StopRequested != null) await StopRequested.Invoke(); }
    internal void SetPaused(bool pausedState) { pause.Content = pausedState ? "Resume" : "Pause"; message.Text = pausedState ? "PAUSED" : "REC"; } internal void SetBusy(bool busy) { pause.IsEnabled = stop.IsEnabled = !busy; Opacity = 1; } internal void ShowError(string error) { message.Text = "ERROR"; ToolTip = error; Opacity = 1; }
}

internal sealed class ChoiceField<T>
{
    private readonly List<(string Label, T Value)> choices; private readonly bool dark; private int selected;
    internal Button Button { get; } internal ContextMenu? Menu { get; private set; } internal T Value => choices[selected].Value; internal event Action<T>? Changed;
    internal ChoiceField(IEnumerable<string> values, bool dark, int selected = 0) : this(StringChoices(values), dark, selected) { }
    internal ChoiceField(IEnumerable<(string Label, T Value)> values, bool dark, int selected = 0) { choices = new List<(string, T)>(values); if (choices.Count == 0) throw new ArgumentException("At least one choice is required."); this.dark = dark; this.selected = Math.Clamp(selected, 0, choices.Count - 1); Button = UI.Button("", Show, false, dark); Refresh(); }
    private static IEnumerable<(string, T)> StringChoices(IEnumerable<string> values) { foreach (string value in values) yield return (value, (T)(object)value); }
    internal void Select(T value, bool notify = true) { int index = choices.FindIndex(choice => EqualityComparer<T>.Default.Equals(choice.Value, value)); if (index < 0) return; selected = index; Refresh(); if (notify) Changed?.Invoke(Value); }
    private void Refresh() => Button.Content = choices[selected].Label + "    ▾";
    private void Show() { var menu = new ContextMenu { Background = UI.Surface(dark), Foreground = UI.Ink(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), PlacementTarget = Button, Placement = PlacementMode.Bottom, MinWidth = Math.Max(180, Button.ActualWidth), HasDropShadow = true }; Menu = menu; for (int i = 0; i < choices.Count; i++) { int index = i; var item = new MenuItem { Header = choices[i].Label, IsCheckable = true, IsChecked = i == selected, Foreground = UI.Ink(dark), Background = UI.Surface(dark), Padding = new Thickness(12, 9, 12, 9) }; item.Click += (_, _) => { selected = index; Refresh(); menu.IsOpen = false; Changed?.Invoke(Value); }; menu.Items.Add(item); } menu.IsOpen = true; }
}

internal sealed class ScreenRecorder
{
    private IntPtr windowHandle;
    private Process? process; private Task<string>? errorOutput; private readonly List<string> parts = new(); private string? target, temporaryFolder; private System.Drawing.Rectangle bounds; private int width, height, fps, mbps; private bool h265;
    // AppContext.BaseDirectory is the self-extraction folder for the single-file build,
    // while Environment.ProcessPath points at the user's standalone executable.
    internal static string ExecutablePath => Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"); internal static bool IsAvailable => File.Exists(ExecutablePath); internal bool IsRunning => process is { HasExited: false }; internal bool IsPaused { get; private set; }
    internal async Task StartAsync(System.Drawing.Rectangle sourceBounds, int outputWidth, int outputHeight, int frameRate, int megabits, bool useH265, string output, IntPtr sourceWindow = default)
    {
        if (!IsAvailable) throw new FileNotFoundException("The bundled recording engine could not be found. Rebuild Better SS.", ExecutablePath);
        windowHandle = sourceWindow; bounds = sourceBounds; width = outputWidth; height = outputHeight; fps = frameRate; mbps = megabits; h265 = useH265; target = output; temporaryFolder = Path.Combine(Path.GetDirectoryName(output) ?? AppContext.BaseDirectory, ".better-ss-recording-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporaryFolder);
        try { await StartSegmentAsync(); }
        catch { Directory.Delete(temporaryFolder, true); throw; }
    }
    internal async Task PauseAsync() { if (process == null) return; await FinishSegmentAsync(); IsPaused = true; }
    internal async Task ResumeAsync() { if (!IsPaused) return; await StartSegmentAsync(); IsPaused = false; }
    private async Task StartSegmentAsync()
    {
        if (windowHandle != IntPtr.Zero && (!WindowCatalog.IsWindow(windowHandle) || Native.IsIconic(windowHandle)))
            throw new InvalidOperationException("The selected window is closed or minimized. Restore it before recording.");
        string part = Path.Combine(temporaryFolder!, $"part-{parts.Count:D4}{Path.GetExtension(target!)}"); var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true }; foreach (string arg in CaptureArguments(part)) start.ArgumentList.Add(arg);
        process = Process.Start(start) ?? throw new InvalidOperationException("The recording engine could not be started."); errorOutput = process.StandardError.ReadToEndAsync(); await Task.Delay(650).ConfigureAwait(false); if (process.HasExited) { string error = (await errorOutput).Trim(); process.Dispose(); process = null; throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The capture engine exited unexpectedly." : LastLine(error)); } parts.Add(part);
    }
    private IEnumerable<string> CaptureArguments(string output)
    {
        yield return "-hide_banner"; yield return "-loglevel"; yield return "warning"; yield return "-y";
        if (windowHandle != IntPtr.Zero)
        {
            // Windows Graphics Capture binds to this HWND, independently of desktop coordinates
            // and occluding windows. Fix the output canvas across resizing and paused segments.
            yield return "-filter_complex";
            yield return $"gfxcapture=hwnd={windowHandle.ToInt64()}:capture_border=0:width={width}:height={height}:resize_mode=scale_aspect:max_framerate={fps},hwdownload,format=bgra,fps={fps},setsar=1";
        }
        else
        {
            yield return "-f"; yield return "gdigrab"; yield return "-framerate"; yield return fps.ToString();
            yield return "-offset_x"; yield return bounds.X.ToString(); yield return "-offset_y"; yield return bounds.Y.ToString();
            yield return "-video_size"; yield return bounds.Width + "x" + bounds.Height; yield return "-i"; yield return "desktop";
            yield return "-vf"; yield return $"scale={width}:{height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
        }
        yield return "-c:v"; yield return h265 ? "libx265" : "libx264"; yield return "-preset"; yield return "veryfast";
        yield return "-b:v"; yield return mbps + "M"; yield return "-pix_fmt"; yield return "yuv420p";
        if (h265) { yield return "-tag:v"; yield return "hvc1"; }
        yield return output;
    }
    private static string LastLine(string value) { var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); return lines.Length == 0 ? value : lines[^1]; }
    private async Task FinishSegmentAsync()
    {
        if (process == null) return; if (!process.HasExited) { await process.StandardInput.WriteLineAsync("q"); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); } } string error = errorOutput == null ? "" : await errorOutput; int exit = process.ExitCode; process.Dispose(); process = null; if (exit != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The recording engine failed while finalizing." : LastLine(error));
    }
    internal async Task StopAsync()
    {
        try
        {
            if (process != null) await FinishSegmentAsync();
            if (parts.Count == 1) File.Move(parts[0], target!, true); else if (parts.Count > 1) await JoinSegmentsAsync();
            if (temporaryFolder != null && Directory.Exists(temporaryFolder)) Directory.Delete(temporaryFolder, true);
            parts.Clear();
        }
        catch (Exception ex) { throw new InvalidOperationException(ex.Message + " Recording segments were kept in " + temporaryFolder, ex); }
        finally { IsPaused = false; }
    }
    private async Task JoinSegmentsAsync()
    {
        string list = Path.Combine(temporaryFolder!, "segments.ffconcat"); File.WriteAllLines(list, parts.Select(path => "file '" + path.Replace("'", "'\\''") + "'")); var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }; foreach (string arg in new[] { "-hide_banner", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", target! }) start.ArgumentList.Add(arg); using var join = Process.Start(start) ?? throw new InvalidOperationException("The recording engine could not join paused segments."); string error = await join.StandardError.ReadToEndAsync(); await join.WaitForExitAsync(); if (join.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Could not join paused recording segments." : LastLine(error));
    }
}
