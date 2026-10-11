using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BetterSS;

internal enum VideoFeature { Recording, Editing }
internal sealed record VideoToolsProgress(string Message, long Received = 0, long? Total = null);

// Components live outside the app version directory so updates retain an offline copy.
internal sealed class VideoToolsManager
{
    internal const string ArchiveUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private const long MaximumDownload = 512L * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    internal static VideoToolsManager Default { get; set; } = new();
    private readonly string root, bundled;
    private readonly HttpClient client;
    private readonly Func<string, CancellationToken, Task> validate;
    private readonly SemaphoreSlim installGate = new(1, 1);
    private sealed record Receipt(string Directory, string ArchiveSha256, long FfmpegBytes, long FfprobeBytes);

    internal VideoToolsManager(string? root = null, string? bundled = null, HttpClient? client = null, Func<string, CancellationToken, Task>? validate = null)
    {
        this.root = Path.GetFullPath(root ?? Path.Combine(Settings.DataFolder, "Components", "VideoTools"));
        this.bundled = bundled ?? AppContext.BaseDirectory;
        this.client = client ?? SharedClient; this.validate = validate ?? ValidateExecutablesAsync;
    }
    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BetterSS/0.6 optional-video-tools"); return http;
    }
    internal string? AvailableDirectory
    {
        get
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(Path.Combine(root, "current.json")));
                if (receipt != null && receipt.Directory != null && Regex.IsMatch(receipt.Directory, "^tools-[a-f0-9]{32}$"))
                {
                    string folder = Path.Combine(root, receipt.Directory);
                    if (receipt.FfmpegBytes > 0 && receipt.FfprobeBytes > 0 &&
                        HasLength(Path.Combine(folder, "ffmpeg.exe"), receipt.FfmpegBytes) &&
                        HasLength(Path.Combine(folder, "ffprobe.exe"), receipt.FfprobeBytes)) return folder;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            // Existing full installs and developer builds can continue using their tools.
            return File.Exists(Path.Combine(bundled, "ffmpeg.exe")) && File.Exists(Path.Combine(bundled, "ffprobe.exe")) ? bundled : null;
        }
    }
    private static bool HasLength(string path, long length) => File.Exists(path) && new FileInfo(path).Length == length;
    internal bool IsAvailable => AvailableDirectory != null;
    internal string FfmpegPath => Path.Combine(AvailableDirectory ?? root, "ffmpeg.exe");
    internal string FfprobePath => Path.Combine(AvailableDirectory ?? root, "ffprobe.exe");
    internal long InstalledBytes => AvailableDirectory is string folder
        ? new FileInfo(Path.Combine(folder, "ffmpeg.exe")).Length + new FileInfo(Path.Combine(folder, "ffprobe.exe")).Length : 0;

    internal async Task InstallAsync(IProgress<VideoToolsProgress>? progress, CancellationToken token)
    {
        await installGate.WaitAsync(token);
        string? work = null;
        try
        {
            if (IsAvailable) return;
            Directory.CreateDirectory(root);
            work = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            string archivePath = Path.Combine(work, "tools.zip"), staged = Path.Combine(work, "ready");
            progress?.Report(new("Connecting to the video tools download…"));
            using var checksumResponse = await client.GetAsync(ArchiveUrl + ".sha256", token);
            checksumResponse.EnsureSuccessStatusCode();
            string checksumText = await checksumResponse.Content.ReadAsStringAsync(token);
            var match = Regex.Match(checksumText, @"(?i)\b[0-9a-f]{64}\b");
            if (!match.Success) throw new InvalidDataException("The download provider did not return a valid checksum. Please retry later.");
            using var response = await client.GetAsync(ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode(); long? total = response.Content.Headers.ContentLength;
            if (total > MaximumDownload) throw new InvalidDataException("The video tools download is larger than expected.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; long received = 0; var reportClock = Stopwatch.StartNew(); int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += read;
                    if (received > MaximumDownload) throw new InvalidDataException("The video tools download is larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (reportClock.ElapsedMilliseconds >= 100) { progress?.Report(new("Downloading video tools…", received, total)); reportClock.Restart(); }
                }
                if (total.HasValue && received != total.Value) throw new InvalidDataException("The download was interrupted. Please retry.");
                progress?.Report(new("Checking the download…", received, total));
            }
            await using (var file = File.OpenRead(archivePath))
            {
                byte[] actual = await SHA256.HashDataAsync(file, token);
                if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(match.Value)))
                    throw new InvalidDataException("The download failed its integrity check. Nothing was installed. Please retry.");
            }
            progress?.Report(new("Preparing video tools…")); Directory.CreateDirectory(staged);
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                foreach (string filename in new[] { "ffmpeg.exe", "ffprobe.exe" })
                {
                    var entries = zip.Entries.Where(e => e.Name.Equals(filename, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (entries.Length != 1 || entries[0].Length < 2 || entries[0].Length > MaximumDownload)
                        throw new InvalidDataException("The download does not contain the expected video tools.");
                    await using var input = entries[0].Open();
                    await using var output = new FileStream(Path.Combine(staged, filename), FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                    await input.CopyToAsync(output, token);
                }
                var license = zip.Entries.FirstOrDefault(e => e.Name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || e.Name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase));
                if (license != null && license.Length < 1024 * 1024)
                {
                    await using var input = license.Open(); await using var output = File.Create(Path.Combine(staged, "FFmpeg-LICENSE.txt"));
                    await input.CopyToAsync(output, token);
                }
            }
            progress?.Report(new("Checking that the video tools can run…"));
            await validate(staged, token); token.ThrowIfCancellationRequested();
            string name = "tools-" + Guid.NewGuid().ToString("N");
            var receipt = new Receipt(name, match.Value.ToUpperInvariant(), new FileInfo(Path.Combine(staged, "ffmpeg.exe")).Length, new FileInfo(Path.Combine(staged, "ffprobe.exe")).Length);
            string receiptPath = Path.Combine(work, "current.json");
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt), token);
            token.ThrowIfCancellationRequested();
            // Publish only a complete, verified pair. An interrupted attempt never becomes active.
            string installed = Path.Combine(root, name); Directory.Move(staged, installed);
            try { File.Move(receiptPath, Path.Combine(root, "current.json"), true); }
            catch { DeleteWorkingDirectory(installed); throw; }
            progress?.Report(new("Video tools are ready. They will work offline."));
        }
        finally
        {
            if (work != null) DeleteWorkingDirectory(work);
            installGate.Release();
        }
    }
    private void DeleteWorkingDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try { if (Directory.Exists(full)) Directory.Delete(full, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
    }
    private static async Task ValidateExecutablesAsync(string folder, CancellationToken token)
    {
        foreach (string tool in new[] { "ffmpeg", "ffprobe" })
        {
            var start = new ProcessStartInfo(Path.Combine(folder, tool + ".exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-version");
            using var process = Process.Start(start) ?? throw new IOException("The downloaded video tools could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                string output = await stdout; await stderr;
                if (process.ExitCode != 0 || !output.StartsWith(tool + " version", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The downloaded video tools could not be verified.");
            }
            catch { if (!process.HasExited) process.Kill(true); throw; }
        }
    }
}
