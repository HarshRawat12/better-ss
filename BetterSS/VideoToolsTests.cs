using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class VideoToolsTests
{
    internal static void Run(App app, bool network)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += async (_, _) =>
        {
            string output = Path.GetFullPath(network ? ".validation/video-tools-network" : ".validation/optional-video-tests");
            Directory.CreateDirectory(output); var log = new List<string>();
            var originalManager = VideoToolsManager.Default;
            app.Settings = new Settings { Hotkey = "Disabled", IntroductionSeen = true };
            app.SettingsPathOverride = Path.Combine(output, "test-settings.json");
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); log.Add("PASS " + label); }
            try
            {
                if (network)
                {
                    var manager = new VideoToolsManager(Path.Combine(output, "component"), Path.Combine(output, "empty"));
                    await manager.InstallAsync(new Progress<VideoToolsProgress>(p => File.WriteAllText(Path.Combine(output, "progress.txt"), p.Message + " " + p.Received)), CancellationToken.None);
                    Check(manager.IsAvailable, "real upstream download activates a verified component");
                    Check(File.Exists(manager.FfmpegPath) && File.Exists(manager.FfprobePath), "both actual video executables are installed together");
                    Check(!Directory.EnumerateDirectories(Path.Combine(output, "component"), ".download-*").Any(), "download archive and staging files are cleaned up");
                }
                else
                {
                    string trial = Path.Combine(output, "run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(trial);
                    var source = new FakeSource(); using var client = new HttpClient(source);
                    var manager = new VideoToolsManager(Path.Combine(trial, "component"), Path.Combine(trial, "empty"), client, (_, _) => Task.CompletedTask);
                    Check(!manager.IsAvailable, "small base starts without video tools");
                    source.BadChecksum = true;
                    try { await manager.InstallAsync(null, CancellationToken.None); throw new Exception("Bad checksum was accepted"); }
                    catch (InvalidDataException) { Check(!manager.IsAvailable, "checksum mismatch never activates executables"); }
                    Check(!Directory.EnumerateDirectories(Path.Combine(trial, "component"), ".download-*").Any(), "failed download leaves no staging directory");
                    source.BadChecksum = false; source.Fail = true;
                    try { await manager.InstallAsync(null, CancellationToken.None); throw new Exception("Server failure was accepted"); }
                    catch (HttpRequestException) { Check(!manager.IsAvailable, "server error preserves the usable base app"); }
                    source.Fail = false; source.BlockArchive = true;
                    using (var cancellation = new CancellationTokenSource(60))
                    {
                        try { await manager.InstallAsync(null, cancellation.Token); throw new Exception("Cancellation was ignored"); }
                        catch (OperationCanceledException) { Check(!manager.IsAvailable, "cancelled download never activates a partial component"); }
                    }
                    source.BlockArchive = false; int before = source.Requests;
                    await Task.WhenAll(manager.InstallAsync(null, CancellationToken.None), manager.InstallAsync(null, CancellationToken.None));
                    Check(source.Requests - before == 2, "concurrent feature requests share one archive download");
                    Check(manager.IsAvailable && manager.InstalledBytes == 64, "verified executable pair becomes available together");
                    var reopened = new VideoToolsManager(Path.Combine(trial, "component"), Path.Combine(trial, "empty"), client, (_, _) => Task.CompletedTask);
                    Check(reopened.IsAvailable, "component persists across app launches");
                    before = source.Requests; await reopened.InstallAsync(null, CancellationToken.None);
                    Check(source.Requests == before, "offline reuse does not make another network request");
                    VideoToolsManager.Default = reopened;
                    Check(ScreenRecorder.ExecutablePath == reopened.FfmpegPath, "recording resolves the shared component path");
                    await app.SetVideoFeatureEnabledAsync(VideoFeature.Recording, false);
                    Check(!app.Settings.VideoRecordingEnabled && app.Settings.VideoEditingEnabled, "recording and editing switches are independent");
                    Check(reopened.IsAvailable, "switching a feature off retains the offline component");
                    _ = app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        var prompt = app.Windows.OfType<VideoToolsWindow>().Single();
                        prompt.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    Check(await app.EnsureVideoFeatureAsync(VideoFeature.Recording), "disabled installed feature prompts and can be enabled again");
                    Check(app.Settings.VideoRecordingEnabled && source.Requests == before, "enabling installed tools persists without a second download");
                    File.WriteAllBytes(reopened.FfprobePath, new byte[] { 1 });
                    Check(!reopened.IsAvailable, "damaged cached executable is detected before use");
                    VideoToolsManager.Default = new VideoToolsManager(Path.Combine(trial, "missing"), Path.Combine(trial, "empty"), client, (_, _) => Task.CompletedTask);
                    await app.SetVideoFeatureEnabledAsync(VideoFeature.Editing, false);
                    before = source.Requests;
                    _ = app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => app.Windows.OfType<VideoToolsWindow>().Single().Close()));
                    Check(!await app.EnsureVideoFeatureAsync(VideoFeature.Editing), "Not now cancels first-use download prompt");
                    Check(!app.Settings.VideoEditingEnabled && source.Requests == before, "declining the prompt changes no feature or download state");
                    source.Fail = true;
                    var retryManager = new VideoToolsManager(Path.Combine(trial, "retry-ui"), Path.Combine(trial, "empty"), client, (_, _) => Task.CompletedTask);
                    var retryWindow = new VideoToolsWindow(app, VideoFeature.Editing, retryManager); retryWindow.Show();
                    retryWindow.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
                    Check(retryWindow.IsVisible && retryWindow.DownloadButton.IsEnabled && !retryWindow.Accepted, "failed download keeps a working Retry button in the prompt");
                    source.Fail = false;
                    var retryClosed = new TaskCompletionSource(); retryWindow.Closed += (_, _) => retryClosed.TrySetResult();
                    retryWindow.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await retryClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(retryWindow.Accepted && retryManager.IsAvailable, "retry succeeds from the same download prompt");
                    source.BlockArchive = true;
                    var cancelManager = new VideoToolsManager(Path.Combine(trial, "cancel-ui"), Path.Combine(trial, "empty"), client, (_, _) => Task.CompletedTask);
                    var cancelWindow = new VideoToolsWindow(app, VideoFeature.Recording, cancelManager); cancelWindow.Show();
                    var cancelClosed = new TaskCompletionSource(); cancelWindow.Closed += (_, _) => cancelClosed.TrySetResult();
                    cancelWindow.DownloadButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80); cancelWindow.Close();
                    await cancelClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(!cancelWindow.Accepted && !cancelManager.IsAvailable, "closing an active download prompt cancels without installing partial tools");
                    source.BlockArchive = false;
                    Motion.ReducedOverride = true; app.Settings.Theme = "Light";
                    var preferences = new PreferencesWindow(app); app.Preferences = preferences; preferences.Show(); preferences.ShowPage("Optional features");
                    Render(preferences, Path.Combine(output, "optional-features.png")); preferences.Close(); app.Preferences = null;
                    var promptWindow = new VideoToolsWindow(app, VideoFeature.Recording); promptWindow.Show(); Render(promptWindow, Path.Combine(output, "download-prompt.png")); promptWindow.Close();
                    Check(true, "optional-feature settings and first-use prompt render successfully");
                    app.Settings.Theme = "Dark";
                    preferences = new PreferencesWindow(app); preferences.Show(); preferences.ShowPage("Optional features"); Render(preferences, Path.Combine(output, "optional-features-dark.png")); preferences.Close();
                }
                log.Add($"SUCCESS: {log.Count} checks passed."); File.WriteAllLines(Path.Combine(output, "results.txt"), log); app.Shutdown(0);
            }
            catch (Exception ex) { log.Add("FAIL: " + ex); File.WriteAllLines(Path.Combine(output, "results.txt"), log); app.Shutdown(1); }
            finally { VideoToolsManager.Default = originalManager; Motion.ReducedOverride = null; }
        };
        app.Run();
    }
    private static void Render(Window window, string path)
    {
        var frame = new DispatcherFrame();
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; };
        settle.Start(); Dispatcher.PushFrame(frame);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.ActualWidth, window.ActualHeight));
        content.Arrange(new Rect(0, 0, window.ActualWidth, window.ActualHeight)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class FakeSource : HttpMessageHandler
    {
        private readonly byte[] archive;
        internal bool BadChecksum, Fail, BlockArchive;
        internal int Requests;
        internal FakeSource()
        {
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
                foreach (string filename in new[] { "ffmpeg.exe", "ffprobe.exe" }) { using var file = zip.CreateEntry("ffmpeg-test/bin/" + filename).Open(); file.Write(new byte[32]); }
            archive = data.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BadChecksum ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(archive))) };
            if (BlockArchive) await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
        }
    }
}
