using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace BetterSS;

// Opt-in integration test: records actual desktop windows and actual screenshot transitions.
internal static class LiveCaptureTests
{
    internal static void Run(App app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += async (_, _) =>
        {
            string folder = Path.GetFullPath(".validation/live-capture-tests"); Directory.CreateDirectory(folder);
            var log = new List<string>(); var windows = new List<Window>(); ScreenRecorder? recorder = null;
            var previousClipboard = Clipboard.GetDataObject();
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); log.Add("PASS " + label); File.WriteAllLines(Path.Combine(folder, "results.txt"), log); }
            try
            {
                app.Settings = new Settings { Theme = "Dark", SoundEnabled = false, SaveFolder = folder, Duration = 30 };
                app.SettingsPathOverride = Path.Combine(folder, "settings.json");
                var screens = Forms.Screen.AllScreens;
                log.Add("Displays: " + string.Join("; ", screens.Select(s => s.DeviceName + " " + s.Bounds)));
                foreach (var screen in screens)
                {
                    var backdrop = Surface("Better SS live capture · " + screen.DeviceName, "#14263A", "LIVE DESKTOP CAPTURE\n" + screen.DeviceName);
                    windows.Add(backdrop); backdrop.Show(); Native.Place(backdrop, screen.Bounds);
                }
                var target = Surface("Better SS moving application", "#16884A", "APPLICATION ONLY"); windows.Add(target); target.Show();
                var a = screens[0]; var b = screens[^1];
                Native.Place(target, new Drawing.Rectangle(a.WorkingArea.Left + 100, a.WorkingArea.Top + 120, 640, 400));
                var label = (TextBlock)((Grid)target.Content).Children[0];
                int tick = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (_, _) => label.Text = "APPLICATION ONLY\nLive frame " + ++tick; timer.Start();
                await Task.Delay(500);
                string video = Path.Combine(folder, "isolated-window.mp4"); recorder = new ScreenRecorder();
                await recorder.StartAsync(a.Bounds, 640, 400, 10, 4, false, video, new WindowInteropHelper(target).Handle);
                await Task.Delay(1100);
                Native.Place(target, new Drawing.Rectangle(b.WorkingArea.Left + 180, b.WorkingArea.Top + 160, 640, 400));
                await Task.Delay(1200);
                var cover = Surface("Occluding application", "#DD1688", "THIS MUST NOT APPEAR IN THE WINDOW VIDEO"); windows.Add(cover); cover.Show(); Native.Place(cover, b.Bounds);
                await Task.Delay(1200);
                Native.SetWindowPos(new WindowInteropHelper(target).Handle, IntPtr.Zero, b.WorkingArea.Left + 240, b.WorkingArea.Top + 200, 420, 500, 0x4 | 0x10);
                await Task.Delay(1200);
                await recorder.PauseAsync(); Check(recorder.IsPaused && !recorder.IsRunning, "Window capture pauses");
                cover.Close(); windows.Remove(cover);
                Native.Place(target, new Drawing.Rectangle(a.WorkingArea.Left + 240, a.WorkingArea.Top + 160, 800, 320));
                await recorder.ResumeAsync(); await Task.Delay(1200); await recorder.StopAsync(); recorder = null; timer.Stop();
                var info = await VideoCutService.ProbeAsync(video, CancellationToken.None);
                Check(info.Width == 640 && info.Height == 400 && info.Duration > 5, "Window recording keeps a fixed output canvas across moves, resizing and pause/resume");
                for (int i = 1; i <= 5; i++)
                {
                    string frame = Path.Combine(folder, $"window-frame-{i}.png");
                    await VideoCutService.RunAsync(ScreenRecorder.ExecutablePath, new[] { "-v", "error", "-y", "-ss", i.ToString(), "-i", video, "-frames:v", "1", frame }, CancellationToken.None);
                    using var bitmap = new Drawing.Bitmap(frame);
                    int green = 0, unrelated = 0, samples = 0;
                    for (int y = 5; y < bitmap.Height; y += 10) for (int x = 5; x < bitmap.Width; x += 10)
                    {
                        var color = bitmap.GetPixel(x, y); samples++;
                        if (color.G > color.R * 1.6 && color.G > color.B * 1.3) green++;
                        if (color.R > 130 && color.B > 70 && color.G < 80) unrelated++;
                    }
                    Check(green > samples * .35 && unrelated == 0, $"Decoded second {i} contains the selected application without the covering window or desktop");
                }
                target.Close(); windows.Remove(target);
                var desktop = Forms.SystemInformation.VirtualScreen;
                recorder = new ScreenRecorder();
                await recorder.StartAsync(desktop, desktop.Width / 2 * 2, desktop.Height / 2 * 2, 30, 12, false, Path.Combine(folder, "screenshot-animations.mp4"));
                async Task Capture(CaptureMode mode, Forms.Screen screen, Drawing.Rectangle area, string name)
                {
                    foreach (var p in app.Windows.OfType<PreviewWindow>().ToArray()) p.Close();
                    var capture = new CaptureSession(mode, screen, app.CompleteCapture, () => { });
                    capture.Start(); capture.Selection = area; capture.Finish();
                    Check(app.Windows.OfType<CaptureAnimationWindow>().Count() == (app.Settings.CaptureAnimation ? screens.Length : 0), name + " creates only the requested animation surfaces");
                    // Only in this opt-in test, allow the observer recording to see excluded UI.
                    foreach (var w in app.Windows.OfType<CaptureAnimationWindow>()) Native.SetWindowDisplayAffinity(new WindowInteropHelper(w).Handle, 0);
                    var flashFrame = Task.Run(async () => { await Task.Delay(20); Native.SavePng(Native.Capture(desktop), Path.Combine(folder, name + "-flash.png")); });
                    var movingFrame = Task.Run(async () => { await Task.Delay(170); Native.SavePng(Native.Capture(desktop), Path.Combine(folder, name + "-shrinking.png")); });
                    await Task.WhenAll(flashFrame, movingFrame); await Task.Delay(800);
                    var preview = app.Windows.OfType<PreviewWindow>().Single();
                    Check(preview.Opacity == 1 && !app.Windows.OfType<CaptureAnimationWindow>().Any(), name + " animation completes and reveals the preview");
                    Native.SetWindowDisplayAffinity(new WindowInteropHelper(preview).Handle, 0);
                    Native.SavePng(Native.Capture(desktop), Path.Combine(folder, name + "-preview.png"));
                    await Task.Delay(350);
                }
                foreach (var screen in screens) await Capture(CaptureMode.Monitor, screen, screen.Bounds, "display-" + Array.IndexOf(screens, screen));
                await Capture(CaptureMode.Region, b, new Drawing.Rectangle(b.Bounds.Left + 140, b.Bounds.Top + 100, 700, 380), "region");
                await Capture(CaptureMode.AllMonitors, a, desktop, "all-displays");
                app.Settings.AnimationDuration = 700;
                await Capture(CaptureMode.Region, b, new Drawing.Rectangle(b.Bounds.Left + 140, b.Bounds.Top + 100, 700, 380), "custom-duration");
                app.Settings.AnimationDuration = 320;
                app.Settings.CaptureAnimation = false;
                await Capture(CaptureMode.Region, b, new Drawing.Rectangle(b.Bounds.Left + 140, b.Bounds.Top + 100, 700, 380), "disabled");
                app.Settings.CaptureAnimation = true;
                await recorder.StopAsync(); recorder = null;
                app.Settings.AnimationDuration = 700; app.Settings.FlashOpacity = 25; app.Persist();
                var saved = Settings.Load(app.SettingsPathOverride);
                Check(saved.AnimationDuration == 700 && saved.FlashOpacity == 25 && saved.CaptureAnimation, "Custom animation settings persist");
                var preferences = new PreferencesWindow(app); windows.Add(preferences); preferences.Show(); preferences.ShowPage("Floating preview");
                Native.MoveToMonitor(preferences, a); await Task.Delay(200);
                Native.GetWindowRect(new WindowInteropHelper(preferences).Handle, out var rect);
                Native.SavePng(Native.Capture(rect.Bounds), Path.Combine(folder, "animation-preferences.png"));
                Check(true, "Live capture checks completed");
            }
            catch (Exception ex) { log.Add("FAIL " + ex); Environment.ExitCode = 1; }
            finally
            {
                if (recorder != null) { try { await recorder.StopAsync(); } catch { } }
                CaptureAnimationWindow.CancelAll();
                foreach (var w in app.Windows.Cast<Window>().ToArray()) w.Close();
                try { if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true); } catch { }
                File.WriteAllLines(Path.Combine(folder, "results.txt"), log); app.Shutdown(Environment.ExitCode);
            }
        };
        app.Run();
    }
    private static Window Surface(string title, string color, string text)
    {
        var grid = new Grid { Background = UI.Brush(color) };
        grid.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        return new Window { Title = title, Content = grid, Width = 640, Height = 400, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true };
    }
}
