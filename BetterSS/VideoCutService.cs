using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace BetterSS;

internal sealed record VideoClip(double Start, double End)
{
    internal double Duration => End - Start;
}

// Edits refer to immutable source times. Nothing rewrites the input video.
internal sealed class VideoCutDocument
{
    private sealed record Snapshot(VideoClip[] Clips, bool Muted);
    private readonly List<VideoClip> clips;
    private readonly Stack<Snapshot> undo = new(), redo = new();
    internal IReadOnlyList<VideoClip> Clips => clips;
    internal double SourceDuration { get; }
    internal double Duration => clips.Sum(c => c.Duration);
    internal bool Muted { get; private set; }
    internal bool CanUndo => undo.Count > 0;
    internal bool CanRedo => redo.Count > 0;
    internal VideoCutDocument(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentException("This video has no playable duration.");
        SourceDuration = duration; clips = new() { new(0, duration) };
    }
    private Snapshot Current() => new(clips.ToArray(), Muted);
    private void Remember() { undo.Push(Current()); redo.Clear(); }
    private void Restore(Snapshot state) { clips.Clear(); clips.AddRange(state.Clips); Muted = state.Muted; }
    internal void Undo() { if (CanUndo) { redo.Push(Current()); Restore(undo.Pop()); } }
    internal void Redo() { if (CanRedo) { undo.Push(Current()); Restore(redo.Pop()); } }
    internal void SetMuted(bool value) { if (Muted != value) { Remember(); Muted = value; } }
    internal (int Index, double Source) Locate(double editedTime)
    {
        double remaining = Math.Clamp(editedTime, 0, Duration);
        for (int i = 0; i < clips.Count; i++)
        {
            if (remaining < clips[i].Duration || i == clips.Count - 1) return (i, clips[i].Start + remaining);
            remaining -= clips[i].Duration;
        }
        throw new InvalidOperationException("There are no clips left. Undo a deletion to restore one.");
    }
    internal double Offset(int index) => clips.Take(index).Sum(c => c.Duration);
    internal void Split(double editedTime)
    {
        var (index, time) = Locate(editedTime); var clip = clips[index];
        if (time - clip.Start < .04 || clip.End - time < .04) throw new InvalidOperationException("Move the playhead inside a clip to split it.");
        Remember(); clips[index] = new(clip.Start, time); clips.Insert(index + 1, new(time, clip.End));
    }
    internal void Trim(int index, double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end > SourceDuration + .001 || end - start < .04)
            throw new InvalidOperationException("Enter start and end times within the original video, with at least 0.04 seconds between them.");
        end = Math.Min(end, SourceDuration);
        if (clips[index] == new VideoClip(start, end)) return;
        Remember(); clips[index] = new(start, end);
    }
    internal void Delete(int index) { Remember(); clips.RemoveAt(index); }
    internal void KeepRange(double editedStart, double editedEnd)
    {
        if (!double.IsFinite(editedStart) || !double.IsFinite(editedEnd) || editedStart < 0 || editedEnd > Duration + .001 || editedEnd - editedStart < .04)
            throw new InvalidOperationException("Set an In point before the Out point, with at least 0.04 seconds between them.");
        var kept = new List<VideoClip>(); double offset = 0;
        foreach (var clip in clips)
        {
            double overlapStart = Math.Max(editedStart, offset), overlapEnd = Math.Min(editedEnd, offset + clip.Duration);
            if (overlapEnd - overlapStart >= .04)
                kept.Add(new VideoClip(clip.Start + overlapStart - offset, clip.Start + overlapEnd - offset));
            offset += clip.Duration;
        }
        if (kept.Count == 0) throw new InvalidOperationException("The marked range does not contain video.");
        Remember(); clips.Clear(); clips.AddRange(kept);
    }
}

internal sealed record VideoExportSettings(int Width, int Height, double Fps, double Mbps, string Format)
{
    internal bool H265 => Format.Contains("H.265", StringComparison.Ordinal);
    internal string Extension => Format.StartsWith("MOV", StringComparison.Ordinal) ? ".mov" : ".mp4";
    internal void Validate()
    {
        if (Width < 2 || Height < 2 || Width > 7680 || Height > 7680 || Width % 2 != 0 || Height % 2 != 0)
            throw new InvalidOperationException("Width and height must be even numbers between 2 and 7680 pixels.");
        if (!double.IsFinite(Fps) || Fps < 1 || Fps > 120) throw new InvalidOperationException("FPS must be between 1 and 120.");
        if (!double.IsFinite(Mbps) || Mbps < .1 || Mbps > 200) throw new InvalidOperationException("Bitrate must be between 0.1 and 200 Mbps.");
        if (!Formats.Contains(Format)) throw new InvalidOperationException("Choose an available video format.");
    }
    internal static readonly string[] Formats = { "MP4 · H.264", "MP4 · H.265", "MOV · H.264", "MOV · H.265" };
}

internal sealed record VideoInfo(double Duration, int Width, int Height, double Fps, double Mbps, string Codec, bool HasAudio)
{
    internal VideoExportSettings Defaults(string path) => new(Math.Max(2, Width / 2 * 2), Math.Max(2, Height / 2 * 2),
        Math.Clamp(Fps, 1, 120), Math.Clamp(Mbps, .1, 200),
        (Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase) ? "MOV" : "MP4") + (Codec == "hevc" ? " · H.265" : " · H.264"));
}

internal static class VideoCutService
{
    internal static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    internal static async Task<VideoInfo> ProbeAsync(string input, CancellationToken token)
    {
        string probe = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
        string json = await RunAsync(probe, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", input }, token);
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.FirstOrDefault(s => Text(s, "codec_type") == "video" && (!s.TryGetProperty("disposition", out var disposition) || !disposition.TryGetProperty("attached_pic", out var attached) || attached.GetInt32() == 0));
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("This file does not contain a video track.");
        var format = root.GetProperty("format"); double duration = Parse(Text(video, "duration"), Parse(Text(format, "duration"), 0));
        if (duration <= 0 || !double.IsFinite(duration)) throw new InvalidOperationException("This file has no usable video duration.");
        string[] fraction = Text(video, "avg_frame_rate").Split('/'); double fps = fraction.Length == 2 ? Parse(fraction[0], 30) / Math.Max(1, Parse(fraction[1], 1)) : 30;
        int width = video.GetProperty("width").GetInt32(), height = video.GetProperty("height").GetInt32();
        if (video.TryGetProperty("side_data_list", out var sideData) && sideData.EnumerateArray().Any(s => s.TryGetProperty("rotation", out var angle) && Math.Abs(angle.GetDouble() % 180) > 1)) (width, height) = (height, width);
        return new(duration, width, height, fps <= 0 ? 30 : fps, Parse(Text(video, "bit_rate"), Parse(Text(format, "bit_rate"), 4000000)) / 1000000,
            Text(video, "codec_name"), streams.Any(s => Text(s, "codec_type") == "audio"));
    }
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : "";
    private static double Parse(string text, double fallback) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result) ? result : fallback;

    // A small H.264/AAC preview keeps H.265 editing usable without a Windows HEVC extension.
    internal static async Task PreparePreviewAsync(string input, string output, double duration, IProgress<double>? progress, CancellationToken token)
    {
        await RunAsync(ScreenRecorder.ExecutablePath, new[] { "-hide_banner", "-v", "error", "-nostdin", "-y", "-i", input, "-map", "0:v:0", "-map", "0:a:0?",
            "-vf", "scale=960:540:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "26", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", "-progress", "pipe:1", "-nostats", output }, token, progress, duration);
    }

    internal static async Task<IReadOnlyList<VideoThumbnail>> ThumbnailsAsync(string preview, string cacheFolder, double duration, CancellationToken token)
    {
        const int count = 24;
        await RunAsync(ScreenRecorder.ExecutablePath, new[] { "-hide_banner", "-v", "error", "-nostdin", "-y", "-i", preview, "-an", "-vf", $"fps={Number(count / Math.Max(.04, duration))}:start_time=0,scale=160:90:force_original_aspect_ratio=increase,crop=160:90", "-frames:v", count.ToString(CultureInfo.InvariantCulture), "-q:v", "4", Path.Combine(cacheFolder, "thumbnail-%02d.jpg") }, token);
        return await Task.Run(() =>
        {
            var frames = new List<VideoThumbnail>();
            foreach (string file in Directory.GetFiles(cacheFolder, "thumbnail-*.jpg").Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested(); using var stream = File.OpenRead(file); var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                // Detach the pixels from the worker-owned decoder before passing them to WPF drawings.
                var converted = new FormatConvertedBitmap(decoded, PixelFormats.Pbgra32, null, 0); int stride = converted.PixelWidth * 4; var pixels = new byte[stride * converted.PixelHeight]; converted.CopyPixels(pixels, stride, 0);
                var image = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride); image.Freeze(); frames.Add(new(frames.Count * duration / count, image));
            }
            return (IReadOnlyList<VideoThumbnail>)frames;
        }, token);
    }

    internal static async Task ExportAsync(string input, string output, IReadOnlyList<VideoClip> clips, bool muted, VideoInfo info,
        VideoExportSettings settings, IProgress<double>? progress, CancellationToken token)
    {
        settings.Validate();
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a different filename to keep the original video intact.");
        if (clips.Count == 0) throw new InvalidOperationException("Keep at least one clip before exporting.");
        foreach (var clip in clips) if (!double.IsFinite(clip.Start) || !double.IsFinite(clip.End) || clip.Start < 0 || clip.End > info.Duration + .001 || clip.Duration < .04) throw new InvalidOperationException("One of the clips has invalid cut points.");
        string folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        string temporary = Path.Combine(folder, ".better-ss-export-" + Guid.NewGuid().ToString("N") + settings.Extension);
        string script = temporary + ".filters";
        bool audio = info.HasAudio && !muted;
        try
        {
            // Re-encode boundaries so cuts are accurate even between keyframes. Each audio cut uses the same times.
            var filters = new StringBuilder();
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                filters.Append($"[0:v:0]trim=start={Number(clip.Start)}:end={Number(clip.End)},setpts=PTS-STARTPTS[v{i}];");
                if (audio) filters.Append($"[0:a:0]atrim=start={Number(clip.Start)}:end={Number(clip.End)},asetpts=PTS-STARTPTS,apad,atrim=duration={Number(clip.Duration)}[a{i}];");
            }
            for (int i = 0; i < clips.Count; i++) { filters.Append($"[v{i}]"); if (audio) filters.Append($"[a{i}]"); }
            filters.Append($"concat=n={clips.Count}:v=1:a={(audio ? 1 : 0)}[joined]"); if (audio) filters.Append("[audio]");
            filters.Append($";[joined]scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={Number(settings.Fps)}[video]");
            string graph = filters.ToString();
            // Normal cuts use FFmpeg's standard filter option directly. Long edit lists use its
            // supported file-argument syntax to stay below Windows' command-line size limit.
            string filterOption = "-filter_complex", filterArgument = graph;
            if (graph.Length > 16000)
            {
                await File.WriteAllTextAsync(script, graph, token);
                filterOption = "-/filter_complex"; filterArgument = script;
            }
            var args = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-y", "-i", input, filterOption, filterArgument, "-map", "[video]" };
            if (audio) args.AddRange(new[] { "-map", "[audio]", "-c:a", "aac", "-b:a", "192k" }); else args.Add("-an");
            args.AddRange(new[] { "-c:v", settings.H265 ? "libx265" : "libx264", "-preset", "veryfast", "-b:v", Number(settings.Mbps) + "M", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-progress", "pipe:1", "-nostats" });
            if (settings.H265) args.AddRange(new[] { "-tag:v", "hvc1", "-x265-params", "log-level=error" });
            args.Add(temporary);
            await RunAsync(ScreenRecorder.ExecutablePath, args, token, progress, clips.Sum(c => c.Duration));
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output, true); progress?.Report(1);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); if (File.Exists(script)) File.Delete(script); }
    }

    internal static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, IProgress<double>? progress = null, double duration = 0)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(executable)) throw new FileNotFoundException("The video tools are missing. Rebuild Better SS to restore them.", executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the video processor.");
        var errors = new Queue<string>(); var result = new StringBuilder();
        async Task ReadErrors() { while (await process.StandardError.ReadLineAsync() is { } line) { errors.Enqueue(line); if (errors.Count > 20) errors.Dequeue(); } }
        async Task ReadOutput()
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (progress == null) result.AppendLine(line);
                else if (duration > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal) && double.TryParse(line[12..], NumberStyles.Float, CultureInfo.InvariantCulture, out double microseconds)) progress.Report(Math.Clamp(microseconds / 1000000 / duration, 0, .99));
            }
        }
        Task stderr = ReadErrors(), stdout = ReadOutput();
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(); await Task.WhenAll(stderr, stdout); throw;
        }
        await Task.WhenAll(stderr, stdout); token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors).Trim() is { Length: > 0 } error ? error : "The video could not be processed.");
        return result.ToString();
    }
}
