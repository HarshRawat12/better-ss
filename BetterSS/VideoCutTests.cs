using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class VideoCutTests
{
    internal static void Run(App app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += async (_, _) =>
        {
            string folder = Path.GetFullPath(".validation/video-cutter-tests"); Directory.CreateDirectory(folder);
            var log = new List<string>();
            void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); log.Add("PASS " + label); }
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
                string source = Path.Combine(folder, "source with audio.mp4");
                await VideoCutService.RunAsync(ScreenRecorder.ExecutablePath, new[] { "-hide_banner", "-v", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "color=c=red:s=320x180:r=24:d=2", "-f", "lavfi", "-i", "color=c=green:s=320x180:r=24:d=2", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=24:d=2", "-f", "lavfi", "-i", "sine=frequency=600:sample_rate=48000:duration=6", "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]", "-map", "[v]", "-map", "3:a", "-c:v", "libx264", "-g", "120", "-pix_fmt", "yuv420p", "-c:a", "aac", source }, CancellationToken.None);
                byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
                VideoInfo info = await VideoCutService.ProbeAsync(source, CancellationToken.None);
                Check(info.HasAudio && Math.Abs(info.Duration - 6) < .1, "Reads source video and original audio");
                var document = new VideoCutDocument(info.Duration);
                document.Split(2); document.Split(4); document.Delete(1);
                Check(document.Clips.Count == 2 && Math.Abs(document.Duration - 4) < .1 && document.Locate(2.1).Source > 4, "Deleting the middle clip closes the gap and preserves source mapping");
                document.Undo(); Check(document.Clips.Count == 3, "Undo restores a deleted clip"); document.Redo();
                document.Trim(0, .5, 1.5); document.Trim(1, 4.25, 5.5);
                var range = new VideoCutDocument(info.Duration); range.Split(2); range.Split(4); range.KeepRange(1, 5);
                Check(Math.Abs(range.Duration - 4) < .01 && range.Clips.Count == 3 && Math.Abs(range.Clips[0].Start - 1) < .01 && Math.Abs(range.Clips[^1].End - 5) < .01, "In/Out range ripples away video outside the marks");
                document.SetMuted(true); document.Undo(); Check(!document.Muted, "Mute can be undone without losing cuts"); document.Redo(); document.SetMuted(false);
                var settings = new VideoExportSettings(640, 360, 30, 2, "MP4 · H.264"); string retained = Path.Combine(folder, "cuts audio.mp4");
                await VideoCutService.ExportAsync(source, retained, document.Clips, false, info, settings, null, CancellationToken.None);
                var output = await VideoCutService.ProbeAsync(retained, CancellationToken.None);
                Check(output.HasAudio && output.Width == 640 && output.Height == 360 && Math.Abs(output.Fps - 30) < .01 && output.Codec == "h264" && Math.Abs(output.Duration - 2.25) < .12, "Export joins precise cuts and applies resolution, FPS, codec and retained audio");
                await CheckColor(retained, .3, true, folder); await CheckColor(retained, 1.6, false, folder);
                Check(true, "Export contains red then blue; deleted green section is absent at the checked cut positions");
                string muted = Path.Combine(folder, "muted cuts.mov");
                await VideoCutService.ExportAsync(source, muted, document.Clips, true, info, settings with { Format = "MOV · H.265", Width = 320, Height = 180, Fps = 24 }, null, CancellationToken.None);
                var mutedInfo = await VideoCutService.ProbeAsync(muted, CancellationToken.None);
                Check(!mutedInfo.HasAudio && mutedInfo.Codec == "hevc" && mutedInfo.Width == 320, "MOV/H.265 export removes audio when muted");
                foreach (var format in new[] { "MOV · H.264", "MP4 · H.265" })
                {
                    var alternate = settings with { Format = format, Width = 320, Height = 180 };
                    string destination = Path.Combine(folder, "alternate-" + (alternate.H265 ? "hevc" : "avc") + alternate.Extension);
                    await VideoCutService.ExportAsync(source, destination, document.Clips, false, info, alternate, null, CancellationToken.None);
                    var alternateInfo = await VideoCutService.ProbeAsync(destination, CancellationToken.None);
                    Check(alternateInfo.HasAudio && alternateInfo.Codec == (alternate.H265 ? "hevc" : "h264"), format + " exports and preserves audio");
                }
                string silentSource = Path.Combine(folder, "silent source.mp4");
                await VideoCutService.RunAsync(ScreenRecorder.ExecutablePath, new[] { "-v", "error", "-y", "-i", source, "-c:v", "copy", "-an", silentSource }, CancellationToken.None);
                var silentInfo = await VideoCutService.ProbeAsync(silentSource, CancellationToken.None);
                await VideoCutService.ExportAsync(silentSource, Path.Combine(folder, "silent cut.mp4"), document.Clips, false, silentInfo, settings, null, CancellationToken.None);
                Check(true, "Video-only recordings export correctly without an audio stream");
                byte[] previousOutput = SHA256.HashData(File.ReadAllBytes(retained));
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await VideoCutService.ExportAsync(source, retained, document.Clips, false, info, settings, null, cancellation.Token); throw new Exception("Cancellation was ignored."); } catch (OperationCanceledException) { }
                Check(previousOutput.SequenceEqual(SHA256.HashData(File.ReadAllBytes(retained))), "Cancelled export preserves an existing destination");
                using var activeCancellation = new CancellationTokenSource();
                try
                {
                    await VideoCutService.ExportAsync(source, retained, document.Clips, false, info, settings,
                        new DirectProgress(_ => activeCancellation.Cancel()), activeCancellation.Token);
                    throw new Exception("Active cancellation was ignored.");
                }
                catch (OperationCanceledException) { }
                Check(previousOutput.SequenceEqual(SHA256.HashData(File.ReadAllBytes(retained))) && !Directory.EnumerateFiles(folder, ".better-ss-export-*").Any(), "Cancelling an active encode keeps the previous file and removes partial output");
                try { await VideoCutService.ExportAsync(source, source, document.Clips, false, info, settings, null, CancellationToken.None); throw new Exception("Source replacement was accepted."); } catch (InvalidOperationException) { }
                Check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Source video remains byte-for-byte intact");

                app.Settings.Theme = "Dark";
                var saved = new RecordingSavedWindow(app, source, settings); saved.Show(); saved.UpdateLayout();
                Check(!app.Windows.OfType<VideoEditorWindow>().Any(), "Recording saved confirmation does not launch the editor");
                Screenshot(saved, Path.Combine(folder, "recording-saved.png")); Click(saved, "Done");
                Check(!app.Windows.OfType<VideoEditorWindow>().Any() && sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))), "Done dismisses the confirmation and keeps the saved recording");
                saved = new RecordingSavedWindow(app, source, settings); saved.Show(); saved.UpdateLayout(); Click(saved, "Edit in Better SS Split");
                var editor = app.Windows.OfType<VideoEditorWindow>().Single();
                for (int i = 0; i < 300 && !editor.PreviewReady; i++) await Task.Delay(50);
                Check(editor.PreviewReady, "Preview opens and decodes in the desktop video cutter");
                var slider = Find<Slider>(editor).Single(); slider.Value = 2;
                Check(editor.HandleShortcut(Key.K, ModifierKeys.Control), "Ctrl+K adds an edit at the playhead"); slider.Value = 4; Check(editor.HandleShortcut(Key.C, ModifierKeys.None), "C adds an edit at the playhead");
                var clips = Find<Button>(editor).Where(b => b.Content is string value && value.StartsWith("CLIP ")).ToArray();
                Check(clips.Length == 3, "Preview playhead creates three selectable clips through actual UI actions");
                clips[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Click(editor, "Ripple Delete");
                Check(Math.Abs(slider.Maximum - 4) < .1, "Ripple Delete button removes the selected middle clip and closes the gap");
                Click(editor, "Undo"); Click(editor, "Redo"); Click(editor, "Undo");
                slider.Value = 3; Check(editor.HandleShortcut(Key.Delete, ModifierKeys.None) && Math.Abs(slider.Maximum - 4) < .1, "Delete key removes the clip under the playhead");
                editor.HandleShortcut(Key.Z, ModifierKeys.Control); slider.Value = 1; editor.HandleShortcut(Key.I, ModifierKeys.None); slider.Value = 5; editor.HandleShortcut(Key.O, ModifierKeys.None); Click(editor, "Keep In–Out");
                Check(Math.Abs(slider.Maximum - 4) < .1, "I/O marks keep a range and ripple its outside edges"); editor.HandleShortcut(Key.Z, ModifierKeys.Control);
                Click(editor, "Mute audio"); Check(Find<Button>(editor).Any(b => b.Content as string == "Unmute audio"), "Mute control can restore the original audio");
                Click(editor, "Unmute audio"); slider.Value = 2.3; editor.HandleShortcut(Key.L, ModifierKeys.None); await Task.Delay(500); editor.HandleShortcut(Key.K, ModifierKeys.None);
                Check(slider.Value > 2.35, "Playback progresses on the edited timeline after the cut");
                editor.UpdateLayout(); Screenshot(editor, Path.Combine(folder, "video-cutter.png"));
                var export = new VideoExportWindow(source, document.Clips.ToArray(), false, info, settings, true); export.Show(); export.UpdateLayout();
                var text = Find<TextBox>(export).Select(t => t.Text).ToArray();
                Check(text.Contains("640") && text.Contains("360") && text.Contains("30") && text.Contains("2"), "Export form retains the supplied recording settings");
                Screenshot(export, Path.Combine(folder, "export-options.png")); export.Close(); editor.Close();
                var hevcEditor = new VideoEditorWindow(app, muted); hevcEditor.Show();
                for (int i = 0; i < 300 && !hevcEditor.PreviewReady; i++) await Task.Delay(50);
                Check(hevcEditor.PreviewReady, "H.265 source plays through a compatible preview copy"); hevcEditor.Close();
                var backup = new DataObject(); var previous = Clipboard.GetDataObject();
                if (previous != null) foreach (string format in previous.GetFormats(false)) { try { var item = previous.GetData(format, false); if (item is MemoryStream memory) item = new MemoryStream(memory.ToArray()); if (item != null) backup.SetData(format, item); } catch { } }
                try
                {
                    var sample = SelfTest.SampleImage(); bool copied = await app.CopyImageAsync(sample, System.Windows.Forms.Screen.PrimaryScreen);
                    await Task.Delay(300);
                    var toast = app.Windows.OfType<ToastWindow>().Single();
                    Check(copied && Clipboard.ContainsImage() && toast.IsVisible && toast.Opacity > .9 && Find<TextBlock>(toast).Any(t => t.Text == "Screenshot copied to clipboard"), "A successful image copy displays a visible clipboard popup");
                    Screenshot(toast, Path.Combine(folder, "clipboard-popup.png")); toast.Close();
                }
                finally
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try { Clipboard.SetDataObject(backup, true); break; }
                        catch (System.Runtime.InteropServices.COMException) when (attempt < 12) { await Task.Delay(120); }
                    }
                }
                log.Add($"Completed {log.Count} checks."); Environment.ExitCode = 0;
            }
            catch (Exception ex) { log.Add("FAIL " + ex); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(folder, "results.txt"), log); app.Shutdown(Environment.ExitCode); }
        };
        app.Run();
    }
    private static async Task CheckColor(string video, double time, bool red, string folder)
    {
        string frame = Path.Combine(folder, red ? "first-cut.png" : "second-cut.png");
        await VideoCutService.RunAsync(ScreenRecorder.ExecutablePath, new[] { "-v", "error", "-y", "-ss", VideoCutService.Number(time), "-i", video, "-frames:v", "1", frame }, CancellationToken.None);
        using var stream = File.OpenRead(frame); var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0); var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        int index = ((image.PixelHeight / 2) * image.PixelWidth + image.PixelWidth / 2) * 4;
        if (red ? pixels[index + 2] < 200 || pixels[index] > 40 : pixels[index] < 200 || pixels[index + 2] > 40) throw new InvalidOperationException("A cut exported the wrong source frames.");
    }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject { if (parent is T match) yield return match; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Find<T>(VisualTreeHelper.GetChild(parent, i))) yield return child; }
    private sealed class DirectProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    private static void Click(Window window, string label) => Find<Button>(window).Single(b => b.Content as string == label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Screenshot(Window window, string path) { var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); Native.SavePng(bitmap, path); }
}
