using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace BetterSS;

public sealed class App : Application
{
    internal Settings Settings = Settings.Load();
    internal string? SettingsPathOverride;
    internal PreferencesWindow? Preferences;
    private Forms.NotifyIcon? tray;
    internal HotkeyService? Hotkeys;
    internal Action<bool> ConfigureStartup = StartupService.SetEnabled;
    private WelcomeWindow? welcome;
    private CaptureSession? session;
    private bool capturing;
    private readonly MediaPlayer player = new();
    private readonly List<PreviewWindow> previews = new();
    private Mutex? mutex;
    private ToastWindow? toast;
    internal string HotkeyStatus = "";
    public App()
    {
        Resources[SystemColors.HighlightBrushKey] = UI.Brush("#666666");
        Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
        Resources[SystemColors.HotTrackBrushKey] = UI.Brush("#888888");
    }
    [STAThread] public static void Main(string[] args)
    {
        var app = new App();
        if (args.Contains("--live-capture-test")) { LiveCaptureTests.Run(app); return; }
        if (args.Contains("--editor-test")) { EditorRegressionTests.Run(app); return; }
        if (args.Contains("--recording-test")) { VideoRegressionTests.Run(app); return; }
        if (args.Contains("--video-cutter-test")) { VideoCutTests.Run(app); return; }
        if (args.Contains("--self-test")) { SelfTest.Run(app); return; }
        app.mutex = new Mutex(true, "Local\\Better SS.Singleton", out var first);
        if (!first) { MessageBox.Show("Better SS is already running. Open it from the system tray.", "Better SS"); return; }
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.DispatcherUnhandledException += (_, e) => { app.Notify("Something went wrong", e.Exception.Message); e.Handled = true; };
        app.Startup += (_, _) => app.Start(args.Contains("--background")); app.Exit += (_, _) => app.Cleanup(); app.Run();
    }
    private void Start(bool background)
    {
        player.MediaFailed += (_, e) => Notify("Couldn't play capture sound", e.ErrorException.Message);
        tray = new Forms.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Text = "Better SS · ready to capture", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        foreach (var mode in Enum.GetValues<CaptureMode>()) { var chosen = mode; menu.Items.Add(CaptureSession.Label(mode), null, (_, _) => Dispatcher.InvokeAsync(() => BeginCapture(chosen))); }
        menu.Items.Add("Record video…", null, (_, _) => Dispatcher.Invoke(ShowVideoRecording));
        menu.Items.Add("Cut video…", null, (_, _) => Dispatcher.Invoke(ShowVideoEditor));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Preferences…", null, (_, _) => Dispatcher.Invoke(ShowPreferences));
        menu.Items.Add("Quick guide…", null, (_, _) => Dispatcher.Invoke(ShowGuide));
        menu.Items.Add("Open screenshot folder", null, (_, _) => Native.OpenFolder(Settings.SaveFolder));
        menu.Items.Add("Quit Better SS", null, (_, _) => Dispatcher.Invoke(Shutdown));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.InvokeAsync(() => BeginCapture());
        Hotkeys = new HotkeyService(() => BeginCapture()); RegisterHotkey();
        if (!Settings.IntroductionSeen) ShowGuide();
        else if (!background) ShowPreferences();
    }
    internal void RegisterHotkey()
    {
        if (Hotkeys == null) return;
        HotkeyStatus = Hotkeys.TrySet(Settings.Hotkey, out var error) ? ShortcutStatus() : error;
    }
    private string ShortcutStatus() => Settings.Hotkey == "Disabled" ? "Hotkey off · capture from the tray" : "Ready · " + Settings.Hotkey;
    internal bool TrySetHotkey(string value, out string error)
    {
        if (!HotkeyBinding.TryParse(value, out var binding, out error)) return false;
        if (Hotkeys != null && !Hotkeys.TrySet(value, out error)) return false;
        Settings.Hotkey = binding?.Label ?? "Disabled"; HotkeyStatus = ShortcutStatus(); Persist(); return true;
    }
    internal void ResumeHotkey()
    {
        RegisterHotkey();
    }
    internal bool SetStartupEnabled(bool enabled, out string error)
    {
        try { ConfigureStartup(enabled); Settings.StartOnLogin = enabled; Persist(); error = ""; return true; }
        catch (Exception ex) { error = "Couldn't update Windows startup: " + ex.Message; return false; }
    }
    internal void ShowGuide()
    {
        if (welcome == null) { welcome = new WelcomeWindow(this); welcome.Closed += (_, _) => welcome = null; }
        welcome.Show(); welcome.Activate();
    }
    internal void ShowPreferences()
    { if (Preferences == null) { Preferences = new PreferencesWindow(this); Preferences.Closed += (_, _) => Preferences = null; } Preferences.Show(); Preferences.Activate(); }
    internal void ShowVideoRecording()
    { new VideoRecordingWindow(this) { Owner = Preferences?.IsVisible == true ? Preferences : null }.Show(); }
    internal void ShowVideoEditor() => VideoEditorWindow.OpenExisting(this);
    internal async void BeginCapture(CaptureMode mode = CaptureMode.Region)
    {
        if (capturing) return; capturing = true;
        try
        {
            CaptureAnimationWindow.CancelAll(); toast?.Close(); Preferences?.Hide(); foreach (var p in previews.ToArray()) p.Hide();
            if (mode == CaptureMode.Window)
            {
                try
                {
                    var picker = new WindowPicker(this);
                    if (picker.ShowDialog() == true && picker.Selection != null)
                    {
                        if (Settings.Delay > 0) { var delay = new CountdownWindow(Settings.Delay); delay.Show(); if (!await delay.Completion) return; }
                        var result = await WindowCatalog.CaptureAsync(picker.Selection); CompleteCapture(result.Image, result.Screen, result.Bounds);
                    }
                }
                finally { capturing = false; foreach (var p in previews.ToArray()) p.Show(); }
                return;
            }
            if (Settings.Delay > 0) { var countdown = new CountdownWindow(Settings.Delay); countdown.Show(); if (!await countdown.Completion) { capturing = false; foreach (var p in previews.ToArray()) p.Show(); return; } }
            await Task.Delay(180);
            var activeScreen = Forms.Screen.FromPoint(Native.CursorPosition);
            session = new CaptureSession(mode, activeScreen, CompleteCapture, () => { session = null; capturing = false; foreach (var p in previews.ToArray()) p.Show(); }, () => BeginCapture(CaptureMode.Window), ShowVideoRecording);
            session.Start();
        }
        catch (Exception ex) { capturing = false; session?.Cancel(); session = null; foreach (var p in previews.ToArray()) p.Show(); Notify("Capture failed", ex.Message); }
    }
    internal async void CompleteCapture(BitmapSource bitmap, Forms.Screen screen, System.Drawing.Rectangle sourceBounds)
    {
        var folder = Settings.AutoSave ? Settings.SaveFolder : Path.Combine(Settings.DataFolder, "DragCache");
        string path = Path.Combine(folder, "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..5] + ".png");
        var preview = ShowPreview(bitmap, path, screen, deferReveal: true);
        var animation = CaptureAnimationWindow.PlayAsync(bitmap, sourceBounds, preview.ImageScreenBounds, Settings);
        PlaySound();
        var copy = CopyImageAsync(bitmap, screen);
        try
        {
            await Task.Run(() => Native.SavePng(bitmap, path));
        }
        catch (Exception ex)
        {
            Notify("Couldn't save to your chosen folder", ex.Message);
            path = Path.Combine(Settings.DataFolder, "DragCache", "Screenshot " + Guid.NewGuid().ToString("N") + ".png");
            try { await Task.Run(() => Native.SavePng(bitmap, path)); } catch { preview.Close(); await animation; return; }
        }
        await animation; preview.Reveal(path); await copy;
    }
    internal async void Copy(BitmapSource image) => await CopyImageAsync(image);
    internal async Task<bool> CopyImageAsync(BitmapSource image, Forms.Screen? screen = null)
    {
        try
        {
            bool copied = await ClipboardService.ImageAsync(image);
            Toast(copied ? "Screenshot copied to clipboard" : "Clipboard busy · open Edit and try Copy image", screen);
            return copied;
        }
        catch (Exception ex) { Notify("Couldn't copy image", ex.Message); return false; }
    }
    internal void Toast(string message, Forms.Screen? screen = null)
    { toast?.Close(); toast = new ToastWindow(message, screen ?? Forms.Screen.FromPoint(Native.CursorPosition), UI.Dark(Settings)); toast.Show(); }
    internal PreviewWindow ShowPreview(BitmapSource image, string path, Forms.Screen? screen = null, bool deferReveal = false)
    {
        var p = new PreviewWindow(this, image, path, screen ?? Forms.Screen.FromPoint(Native.CursorPosition), deferReveal);
        previews.Add(p); p.Closed += (_, _) => { previews.Remove(p); Reflow(); };
        while (previews.Count > 3) previews[0].Close(); p.Show(); p.UpdateLayout(); Reflow(); return p;
    }
    private void Reflow()
    {
        foreach (var group in previews.GroupBy(p => p.Screen.DeviceName)) { int offset = 0; foreach (var p in group.Reverse()) { p.Position(offset); offset += (int)(p.ActualHeight * p.ScaleY) + 8; } }
    }
    internal void DemoPreview()
    {
        var image = SelfTest.SampleImage(); var path = Path.Combine(Settings.DataFolder, "DragCache", "Better SS preview.png"); Native.SavePng(image, path); ShowPreview(image, path); PlaySound();
    }
    internal void PlaySound()
    {
        if (!Settings.SoundEnabled) return;
        if (SoundCatalog.UsesSystemSound(Settings.SoundPath))
        {
            SoundCatalog.PlaySystemSound();
            return;
        }
        var path = SoundCatalog.ResolvePath(Settings.SoundPath);
        if (!File.Exists(path)) { Notify("Sound file unavailable", "Choose another sound in Preferences."); return; }
        player.Stop(); player.Open(new Uri(path)); player.Volume = Settings.Volume; player.Play();
    }
    internal void Notify(string title, string message)
    { if (tray != null) tray.ShowBalloonTip(4000, title, message, Forms.ToolTipIcon.Info); else MessageBox.Show(message, title); }
    internal void Persist() { try { Settings.Save(SettingsPathOverride); } catch (Exception e) { Notify("Couldn't save preferences", e.Message); } }
    private void Cleanup()
    { Hotkeys?.Dispose(); tray?.Dispose(); player.Close(); mutex?.Dispose(); }
}
