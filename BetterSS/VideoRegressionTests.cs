using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal static class VideoRegressionTests
{
    internal static void Run(App app)
    {
        string folder = Path.GetFullPath(".validation/recording-tests");
        Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, "screen-recorder-smoke.mp4");
        string results = Path.Combine(folder, "results.txt");
        try
        {
            app.Settings.Theme = "Dark";
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new VideoRecordingWindow(app); window.Show(); window.UpdateLayout();
            if (Find<ComboBox>(window).Any()) throw new InvalidOperationException("A native ComboBox remains in the recording form.");
            window.SourceField.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var sourceMenu = window.SourceMenu ?? throw new InvalidOperationException("The recording-source menu did not open.");
            if (!sourceMenu.Items.Cast<MenuItem>().Select(item => item.Header as string).SequenceEqual(new[] { "Entire display", "Application window…" })) throw new InvalidOperationException("Display and application recording sources are unavailable.");
            sourceMenu.IsOpen = false;
            window.DisplayField.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            var menu = window.DisplayMenu ?? throw new InvalidOperationException("The display menu did not open.");
            if (menu.Background.ToString() != UI.Surface(true).ToString() || menu.Items.Cast<MenuItem>().Any(item => item.Background.ToString() != UI.Surface(true).ToString())) throw new InvalidOperationException("The recording menu did not use the dark Better SS theme.");
            menu.IsOpen = false; Screenshot(window, Path.Combine(folder, "video-options.png")); window.Close();
            if (File.Exists(output)) File.Delete(output);
            if (!ScreenRecorder.IsAvailable) throw new FileNotFoundException("Bundled FFmpeg is missing.", ScreenRecorder.ExecutablePath);
            var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No display is available.");
            var recorder = new ScreenRecorder();
            Task.Run(async () =>
            {
                await recorder.StartAsync(bounds, 640, 360, 10, 4, false, output);
                await Task.Delay(700);
                await recorder.PauseAsync();
                if (!recorder.IsPaused || recorder.IsRunning) throw new InvalidOperationException("Pause did not finalize the current recording segment.");
                await Task.Delay(250);
                await recorder.ResumeAsync();
                if (recorder.IsPaused || !recorder.IsRunning) throw new InvalidOperationException("Resume did not start a new recording segment.");
                await Task.Delay(700);
                await recorder.StopAsync();
            }).GetAwaiter().GetResult();
            var file = new FileInfo(output);
            if (!file.Exists || file.Length < 1000) throw new InvalidOperationException("The recording file was not created correctly.");
            VerifyDecode(output);
            File.WriteAllText(results, $"PASS Video options use themed menus instead of native white ComboBoxes.{Environment.NewLine}PASS Pause and resume create and join recording segments.{Environment.NewLine}PASS Bundled recorder captured and decoded MP4 ({file.Length} bytes).{Environment.NewLine}");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(results, "FAIL " + ex + Environment.NewLine);
            Environment.ExitCode = 1;
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject { if (parent is T match) yield return match; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(parent, i))) yield return child; }
    private static void Screenshot(Window window, string path) { var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); Native.SavePng(bitmap, path); }

    private static void VerifyDecode(string file)
    {
        var start = new ProcessStartInfo(ScreenRecorder.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-i", file, "-f", "null", "-" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the bundled decoder.");
        string error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("The recorded file could not be decoded: " + error.Trim());
    }
}
