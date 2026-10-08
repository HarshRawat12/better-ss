using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shell;
using Microsoft.Win32;

namespace BetterSS;

internal sealed class VideoEditorWindow : Window
{
    private readonly string sourcePath;
    private readonly bool dark;
    private readonly VideoExportSettings? recordingSettings;
    private readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, ScrubbingEnabled = true, Volume = .8 };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true };
    private readonly VideoTimeline track;
    private readonly StackPanel editControls = new();
    private readonly TextBlock status, time, details, marks;
    private readonly TextBlock frames, viewerInfo, viewerTime, trimDuration, fileStats;
    private readonly TextBox trimStart, trimEnd;
    private readonly Button play, mute, export, undo, redo, split, delete, applyTrim, keepRange;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly CancellationTokenSource lifetime = new();
    private VideoCutDocument? document;
    private VideoInfo? info;
    private VideoExportSettings? exportSettings;
    private string? previewFolder;
    private bool playing, ready, updating, closed;
    private int selected, playbackIndex;
    private double editPosition;
    private double? markIn, markOut;
    private double speed = 1;
    private bool loop;
    internal int TimelineThumbnailCount => track.ThumbnailCount;
    internal bool PreviewReady => ready;
    internal bool LoadFinished { get; private set; }

    internal VideoEditorWindow(App app, string path, VideoExportSettings? settings = null)
    {
        sourcePath = Path.GetFullPath(path); recordingSettings = settings; dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Better SS Split", 1180, 800, dark); MinWidth = 900; MinHeight = 600;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        var root = new DockPanel();
        var header = new DockPanel { Height = 44, Margin = new Thickness(9, 0, 5, 0) };
        var caption = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(caption, Dock.Right); header.Children.Add(caption);
        caption.Children.Add(Command("Minimize", "\uE921", () => WindowState = WindowState.Minimized, iconOnly: true));
        caption.Children.Add(Command("Maximize or restore", "\uE922", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, iconOnly: true));
        var close = Command("Close", "\uE8BB", Close, iconOnly: true); close.MouseEnter += (_, _) => { close.Background = UI.Brush("#C42B1C"); close.Foreground = Brushes.White; }; close.MouseLeave += (_, _) => UI.Quiet(close, dark); caption.Children.Add(close);
        export = Command("Export video…", "\uE74E", Export, primary: true); export.ToolTip = "Export video · Ctrl+M"; export.IsEnabled = false; DockPanel.SetDock(export, Dock.Right); header.Children.Add(export);
        var history = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 12, 0) };
        undo = Command("Undo", "\uE7A7", () => Edit(() => document!.Undo()), iconOnly: true); redo = Command("Redo", "\uE7A6", () => Edit(() => document!.Redo()), iconOnly: true); history.Children.Add(undo); history.Children.Add(redo); history.Children.Add(Command("Shortcuts", "\uE765", ShowShortcuts)); DockPanel.SetDock(history, Dock.Right); header.Children.Add(history);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) }; var logo = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = UI.Brush("#0071E3"), Child = new TextBlock { Text = "B", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; brand.Children.Add(logo); var name = UI.Text("Better SS Split  /", 13, UI.Ink(dark), FontWeights.SemiBold); name.Margin = new Thickness(8, 0, 0, 0); brand.Children.Add(name); DockPanel.SetDock(brand, Dock.Left); header.Children.Add(brand);
        details = UI.Text(Path.GetFileName(sourcePath), 11, UI.Ink(dark)); details.TextWrapping = TextWrapping.NoWrap; details.TextTrimming = TextTrimming.CharacterEllipsis; details.ToolTip = sourcePath;
        header.Children.Add(new Border { Background = UI.Background(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, Child = details });
        var headerBorder = Strip(header, UI.Surface(dark)); DockPanel.SetDock(headerBorder, Dock.Top); root.Children.Add(headerBorder);
        var footer = new DockPanel { Height = 26, Margin = new Thickness(10, 0, 10, 0) };
        var shortcuts = UI.Text("Space: Play  ·  S: Split  ·  I/O: In/Out", 10, UI.Muted(dark)); DockPanel.SetDock(shortcuts, Dock.Right); footer.Children.Add(shortcuts);
        fileStats = UI.Text("", 10, UI.Muted(dark)); fileStats.Margin = new Thickness(12, 0, 18, 0); DockPanel.SetDock(fileStats, Dock.Right); footer.Children.Add(fileStats);
        status = UI.Text("Reading video…", 10, UI.Muted(dark)); status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status); var footerBorder = Strip(footer, UI.Surface(dark)); DockPanel.SetDock(footerBorder, Dock.Bottom); root.Children.Add(footerBorder);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var transport = new Grid { Height = 42, Margin = new Thickness(10, 0, 10, 0) }; transport.ColumnDefinitions.Add(new ColumnDefinition()); transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); transport.ColumnDefinitions.Add(new ColumnDefinition());
        var counters = new StackPanel { Orientation = Orientation.Horizontal }; time = UI.Text("00:00.000 / 00:00.000", 11, UI.Accent(dark), FontWeights.SemiBold); time.FontFamily = new FontFamily("Consolas"); counters.Children.Add(time); frames = UI.Text("Frames: 0", 10, UI.Muted(dark)); frames.Margin = new Thickness(12, 0, 0, 0); counters.Children.Add(frames); transport.Children.Add(counters);
        var playback = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        playback.Children.Add(Command("Previous edit", "\uE892", () => GoToCut(false), iconOnly: true)); playback.Children.Add(Command("Previous frame", "\uE892", () => Step(-1), iconOnly: true));
        play = Command("Play", "\uE768", TogglePlay, primary: true, iconOnly: true); play.IsEnabled = false; play.ToolTip = "Play/Pause · Space"; playback.Children.Add(play);
        playback.Children.Add(Command("Next frame", "\uE893", () => Step(1), iconOnly: true)); playback.Children.Add(Command("Next edit", "\uE893", () => GoToCut(true), iconOnly: true)); Grid.SetColumn(playback, 1); transport.Children.Add(playback);
        var audio = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        mute = Command("Mute audio", "\uE767", ToggleMute, iconOnly: true); mute.IsEnabled = false; audio.Children.Add(mute);
        var speedMenu = UI.Menu(dark); Button? speedButton = null; speedButton = Command("1.0× ▾", "", () => speedMenu.IsOpen = true); speedButton.ToolTip = "Preview playback speed"; speedMenu.PlacementTarget = speedButton;
        foreach (double rate in new[] { .5, 1, 1.5, 2 }) { double value = rate; var item = new MenuItem { Header = rate.ToString("0.0", CultureInfo.InvariantCulture) + "×" }; item.Click += (_, _) => { speed = value; player.SpeedRatio = speed; speedButton.Content = speed.ToString("0.0", CultureInfo.InvariantCulture) + "× ▾"; }; speedMenu.Items.Add(item); } audio.Children.Add(speedButton);
        Button? loopButton = null; loopButton = Command("Loop playback", "\uE8EE", () => { loop = !loop; UI.Select(loopButton!, loop, dark); }, iconOnly: true); audio.Children.Add(loopButton); Grid.SetColumn(audio, 2); transport.Children.Add(audio); bottom.Children.Add(Strip(transport, UI.Surface(dark)));
        var edit = new DockPanel { Margin = new Thickness(10, 5, 10, 5) };
        var trim = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        trim.Children.Add(UI.Text("In:", 10, UI.Muted(dark))); trimStart = TrimField(); trim.Children.Add(trimStart); trim.Children.Add(UI.Text("Out:", 10, UI.Muted(dark))); trimEnd = TrimField(); trim.Children.Add(trimEnd); trimDuration = UI.Text("Dur: —", 10, UI.Muted(dark)); trimDuration.Width = 102; trimDuration.Margin = new Thickness(8, 0, 3, 0); trim.Children.Add(trimDuration); applyTrim = Command("Apply trim", "", ApplyTrim); trim.Children.Add(applyTrim); DockPanel.SetDock(trim, Dock.Right); edit.Children.Add(trim);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; split = Command("Split", "\uE8C6", SplitAtPlayhead); split.ToolTip = "Split at playhead · S / Ctrl+K / C"; delete = Command("Ripple Delete", "\uE74D", DeleteSelected); actions.Children.Add(split); actions.Children.Add(delete); actions.Children.Add(Command("Mark In", "", () => SetMark(true))); actions.Children.Add(Command("Mark Out", "", () => SetMark(false))); keepRange = Command("Keep In–Out", "", KeepMarkedRange); keepRange.IsEnabled = false; keepRange.ToolTip = "Keep the marked range and remove its outside edges"; actions.Children.Add(keepRange); edit.Children.Add(actions); editControls.Children.Add(edit); editControls.IsEnabled = false; bottom.Children.Add(Strip(editControls, UI.Background(dark)));
        track = new VideoTimeline(dark); track.SeekRequested += value => { Pause(); Seek(value); }; track.ClipSelected += SelectClip; track.TrimStarted += Pause; track.TrimRequested += (index, start, end) => { selected = index; Edit(() => document!.Trim(index, start, end)); }; bottom.Children.Add(track);
        track.TrimPreviewChanged += (_, start, end) => { trimStart.Text = Timecode(start); trimEnd.Text = Timecode(end); trimDuration.Text = "Dur: " + Timecode(end - start); };
        marks = UI.Text("", 10, UI.Muted(dark)); marks.Margin = new Thickness(90, 0, 12, 3); bottom.Children.Add(marks);
        // Retain the programmatic playhead control; the filmstrip owns the visible scrub target.
        timeline.Visibility = Visibility.Collapsed; timeline.ValueChanged += (_, _) => { if (!updating) Seek(timeline.Value); }; bottom.Children.Add(timeline);
        var viewer = new Grid { Background = UI.HighContrast ? SystemColors.WindowBrush : UI.Brush("#303334") };
        var viewport = new Grid { Background = Brushes.Black, MaxWidth = 720, MaxHeight = 406, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; viewport.Children.Add(player); player.MinHeight = 100;
        viewerInfo = UI.Text("Reading source…", 10, Brushes.White); viewerTime = UI.Text("TC  00:00.000", 10, Brushes.White, FontWeights.SemiBold); viewerTime.FontFamily = new FontFamily("Consolas");
        viewport.Children.Add(Hud(viewerInfo, HorizontalAlignment.Left, VerticalAlignment.Top)); viewport.Children.Add(Hud(viewerTime, HorizontalAlignment.Left, VerticalAlignment.Bottom)); viewport.Children.Add(Hud(UI.Text("Fit", 10, Brushes.White), HorizontalAlignment.Right, VerticalAlignment.Top)); viewer.Children.Add(viewport); root.Children.Add(viewer); Content = root;
        player.MediaOpened += (_, _) => { ready = true; play.IsEnabled = document?.Clips.Count > 0; player.Pause(); Seek(editPosition); status.Text = "Ready. The original stays intact. Split, delete or trim clips, then export."; };
        player.MediaEnded += (_, _) => { if (loop && document?.Clips.Count > 0) { Seek(0); player.Play(); } else { Pause(); if (document != null) UpdatePosition(document.Duration); } };
        player.MediaFailed += (_, _) => { ready = false; Pause(); play.IsEnabled = false; status.Text = "Windows couldn't play the preview. You can still cut by time and export the video."; };
        timer.Tick += (_, _) => PlaybackTick();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox || document == null || !IsEnabled) return;
            e.Handled = HandleShortcut(e.Key, Keyboard.Modifiers);
        };
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => { closed = true; lifetime.Cancel(); timer.Stop(); player.Close(); CleanupPreview(); };
    }

    private Button Command(string label, string glyph, Action action, bool primary = false, bool iconOnly = false)
    {
        var button = UI.Button(label, action, primary, dark); button.FontSize = 11; button.Height = 28; button.Padding = new Thickness(8, 3, 8, 3); button.Margin = new Thickness(0, 0, 5, 0); button.VerticalAlignment = VerticalAlignment.Center;
        if (glyph.Length > 0) UI.Icon(button, glyph, iconOnly);
        if (iconOnly) { button.Width = 29; if (!primary) UI.Quiet(button, dark); }
        WindowChrome.SetIsHitTestVisibleInChrome(button, true); return button;
    }
    private TextBox TrimField()
    { var field = UI.TextBox(dark); field.Width = 85; field.Height = 28; field.FontSize = 11; field.FontFamily = new FontFamily("Consolas"); field.Padding = new Thickness(6, 4, 6, 4); field.Margin = new Thickness(5, 0, 8, 0); field.ToolTip = "Source time as mm:ss.mmm, hh:mm:ss.mmm, or seconds"; return field; }
    private Border Strip(UIElement content, Brush background)
        => new() { Child = content, Background = background, BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 0, 0, .5) };
    private static Border Hud(UIElement content, HorizontalAlignment horizontal, VerticalAlignment vertical)
        => new() { Child = content, Background = UI.Brush("#C01A1C1D"), BorderBrush = UI.Brush("#30FFFFFF"), BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(12), HorizontalAlignment = horizontal, VerticalAlignment = vertical, IsHitTestVisible = false };

    internal static void OpenExisting(App app)
    {
        var dialog = new OpenFileDialog { Title = "Open a video to cut", Filter = "Video|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm", Multiselect = false };
        if (dialog.ShowDialog() == true) new VideoEditorWindow(app, dialog.FileName).Show();
    }

    internal bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (document == null || !IsEnabled) return false;
        if (modifiers == ModifierKeys.Control && key == Key.Z) Edit(() => document.Undo());
        else if ((modifiers == ModifierKeys.Control && key == Key.Y) || (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z)) Edit(() => document.Redo());
        else if ((modifiers == ModifierKeys.Control && key == Key.K) || (key is Key.C or Key.S && modifiers == ModifierKeys.None)) SplitAtPlayhead();
        else if (modifiers == ModifierKeys.Control && key == Key.M) Export();
        else if (key is Key.Delete or Key.Back && modifiers is ModifierKeys.None or ModifierKeys.Shift) DeleteSelected();
        else if (key == Key.Space && modifiers == ModifierKeys.None) TogglePlay();
        else if (key == Key.J && modifiers == ModifierKeys.None) ShuttleBack();
        else if (key == Key.K && modifiers == ModifierKeys.None) Pause();
        else if (key == Key.L && modifiers == ModifierKeys.None) ShuttleForward();
        else if (key == Key.I && modifiers == ModifierKeys.None) SetMark(true);
        else if (key == Key.O && modifiers == ModifierKeys.None) SetMark(false);
        else if (key == Key.Left) Step(modifiers.HasFlag(ModifierKeys.Shift) ? -5 : -1);
        else if (key == Key.Right) Step(modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1);
        else if (key == Key.Up && modifiers == ModifierKeys.None) GoToCut(false);
        else if (key == Key.Down && modifiers == ModifierKeys.None) GoToCut(true);
        else if (key == Key.Home && modifiers == ModifierKeys.None) { Pause(); Seek(0); }
        else if (key == Key.End && modifiers == ModifierKeys.None) { Pause(); Seek(document.Duration); }
        else return false;
        return true;
    }

    private async Task LoadAsync()
    {
        try
        {
            info = await VideoCutService.ProbeAsync(sourcePath, lifetime.Token);
            document = new VideoCutDocument(info.Duration); exportSettings = recordingSettings ?? info.Defaults(sourcePath);
            details.Text = $"{Path.GetFileName(sourcePath)}  ·  {info.Width} × {info.Height}  ·  {VideoCutService.Number(info.Fps)} FPS";
            string codec = info.Codec switch { "h264" => "H.264", "hevc" => "H.265", _ => info.Codec.ToUpperInvariant() };
            viewerInfo.Text = $"●  {info.Width} × {info.Height}  ·  {info.Fps:0.##} fps  ·  {codec}";
            mute.IsEnabled = info.HasAudio; mute.ToolTip = info.HasAudio ? "Mute or restore the video's original audio in preview and export" : "This video contains no audio. Screen recordings currently capture video only.";
            ToolTipService.SetShowOnDisabled(mute, true);
            editControls.IsEnabled = true; timeline.IsEnabled = true; Refresh();
            previewFolder = Path.Combine(Path.GetTempPath(), "Better SS-video-preview-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(previewFolder);
            string preview = Path.Combine(previewFolder, "preview.mp4");
            status.Text = "Preparing preview…";
            await VideoCutService.PreparePreviewAsync(sourcePath, preview, info.Duration,
                new Progress<double>(value => { if (!closed) status.Text = $"Preparing preview… {value:P0}"; }), lifetime.Token);
            if (closed) return;
            player.Source = new Uri(preview); player.Play(); player.Pause();
            export.IsEnabled = true;
            try { track.SetThumbnails(await VideoCutService.ThumbnailsAsync(preview, previewFolder, info.Duration, lifetime.Token)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { if (!closed) status.Text = "Ready · Thumbnail preview unavailable. Editing and export remain available."; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) { status.Text = "Couldn't prepare video: " + ex.Message; export.IsEnabled = document != null; } }
        finally { LoadFinished = true; if (closed) CleanupPreview(); }
    }
    private void CleanupPreview()
    {
        if (previewFolder == null) return;
        try
        {
            string folder = Path.GetFullPath(previewFolder), temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (!folder.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("Better SS-video-preview-", StringComparison.Ordinal)) return;
            string file = Path.Combine(folder, "preview.mp4"); if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(folder)) { foreach (string thumbnail in Directory.GetFiles(folder, "thumbnail-*.jpg")) File.Delete(thumbnail); Directory.Delete(folder); }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void Pause() { bool wasPlaying = playing; playing = false; timer.Stop(); player.Pause(); if (wasPlaying) { play.Content = "Play"; UI.Icon(play, "\uE768", true); } }
    private void TogglePlay()
    {
        if (!ready || document?.Clips.Count is not > 0) return;
        if (playing) { Pause(); return; }
        if (editPosition >= document.Duration - .015) Seek(0);
        playing = true; player.SpeedRatio = speed; player.Play(); timer.Start(); play.Content = "Pause"; UI.Icon(play, "\uE769", true);
    }
    private void ShuttleForward() { if (!playing) TogglePlay(); }
    private void ShuttleBack() { Pause(); Step(-(int)Math.Max(1, Math.Round(info?.Fps ?? 30))); }
    private void Step(int frames) { if (document == null) return; Pause(); double rate = Math.Max(1, info?.Fps ?? 30); Seek(Math.Clamp(editPosition + frames / rate, 0, document.Duration)); }
    private void GoToCut(bool next)
    {
        if (document == null || document.Clips.Count == 0) return; Pause();
        var cuts = Enumerable.Range(0, document.Clips.Count + 1).Select(i => i == document.Clips.Count ? document.Duration : document.Offset(i)).ToArray();
        double target = next ? cuts.FirstOrDefault(c => c > editPosition + .002, document.Duration) : cuts.LastOrDefault(c => c < editPosition - .002, 0);
        Seek(target);
    }
    private void SplitAtPlayhead() => Edit(() => document!.Split(editPosition));
    private void DeleteSelected()
    {
        if (document?.Clips.Count is not > 0) return;
        int target = document.Locate(Math.Min(editPosition, Math.Max(0, document.Duration - .001))).Index;
        selected = target; Edit(() => document.Delete(target));
    }
    private void SetMark(bool inside)
    {
        if (document == null) return;
        if (inside) markIn = editPosition; else markOut = editPosition;
        RefreshMarks();
    }
    private void KeepMarkedRange()
    {
        if (document == null || markIn == null || markOut == null) return;
        double start = Math.Min(markIn.Value, markOut.Value), end = Math.Max(markIn.Value, markOut.Value);
        Edit(() => document.KeepRange(start, end)); markIn = markOut = null; RefreshMarks(); Seek(0);
    }
    private void RefreshMarks()
    {
        marks.Text = $"IN  {(markIn == null ? "—" : Clock(markIn.Value))}   OUT  {(markOut == null ? "—" : Clock(markOut.Value))}";
        keepRange.IsEnabled = document != null && markIn != null && markOut != null && Math.Abs(markOut.Value - markIn.Value) >= .04;
    }
    private void ShowShortcuts()
    {
        MessageBox.Show(this,
            "Space       Play / pause\nL             Play\nK             Pause\nJ             Jump back 1 second\n← / →         One frame\nShift + ←/→   Five frames\n↑ / ↓         Previous / next edit\nS / C / Ctrl+K   Split\nDelete        Ripple delete clip at playhead\nI / O         Mark In / Out\nHome / End    Timeline start / end\nCtrl+Z / Ctrl+Y   Undo / redo\nCtrl+M        Export",
            "Better SS Split keyboard", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ToggleMute() { if (document == null || info?.HasAudio != true) return; document.SetMuted(!document.Muted); Refresh(); }
    private void Seek(double editedTime)
    {
        if (document?.Clips.Count is not > 0) return;
        var location = document.Locate(editedTime); playbackIndex = location.Index;
        if (selected != location.Index) { selected = location.Index; RefreshClipSelection(); }
        if (ready) player.Position = TimeSpan.FromSeconds(location.Source);
        UpdatePosition(editedTime);
    }
    private void PlaybackTick()
    {
        if (!playing || document?.Clips.Count is not > 0) return;
        var clip = document.Clips[playbackIndex]; double position = player.Position.TotalSeconds;
        if (position >= clip.End - .008)
        {
            if (playbackIndex == document.Clips.Count - 1) { if (loop) { Seek(0); player.Play(); } else { Pause(); UpdatePosition(document.Duration); } return; }
            playbackIndex++; selected = playbackIndex; RefreshClipSelection(); player.Position = TimeSpan.FromSeconds(document.Clips[playbackIndex].Start);
            UpdatePosition(document.Offset(playbackIndex));
        }
        else UpdatePosition(document.Offset(playbackIndex) + Math.Clamp(position - clip.Start, 0, clip.Duration));
    }
    private void UpdatePosition(double position)
    {
        editPosition = Math.Clamp(position, 0, document?.Duration ?? 0); updating = true; timeline.Value = editPosition; updating = false;
        time.Text = $"{Timecode(editPosition)} / {Timecode(document?.Duration ?? 0)}";
        track.Position(editPosition); viewerTime.Text = "TC  " + Timecode(editPosition); frames.Text = "Frames: " + Math.Round(editPosition * (info?.Fps ?? 30)).ToString(CultureInfo.InvariantCulture);
    }
    private static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff");
    internal static string Timecode(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"hh\:mm\:ss\.fff" : @"mm\:ss\.fff");
    private static bool ReadTime(string value, out double seconds)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return double.IsFinite(seconds);
        if (TimeSpan.TryParseExact(value.Trim(), new[] { @"mm\:ss\.fff", @"hh\:mm\:ss\.fff", @"mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var timeValue)) { seconds = timeValue.TotalSeconds; return true; }
        return false;
    }
    private void Edit(Action action)
    {
        if (document == null) return; Pause();
        try { action(); selected = Math.Clamp(selected, 0, Math.Max(0, document.Clips.Count - 1)); Refresh(); Seek(Math.Min(editPosition, document.Duration)); }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void ApplyTrim()
    {
        if (!ReadTime(trimStart.Text, out double start) || !ReadTime(trimEnd.Text, out double end)) { status.Text = "Enter source times as mm:ss.mmm or seconds, for example 00:02.500 or 2.5."; return; }
        Edit(() => document!.Trim(selected, start, end));
    }
    private void Refresh()
    {
        if (document == null) return;
        track.Update(document, selected, Path.GetFileName(sourcePath));
        bool any = document.Clips.Count > 0;
        trimStart.IsEnabled = trimEnd.IsEnabled = applyTrim.IsEnabled = split.IsEnabled = delete.IsEnabled = any;
        if (any) { trimStart.Text = Timecode(document.Clips[selected].Start); trimEnd.Text = Timecode(document.Clips[selected].End); trimDuration.Text = "Dur: " + Timecode(document.Clips[selected].Duration); }
        else { trimStart.Text = trimEnd.Text = ""; }
        undo.IsEnabled = document.CanUndo; redo.IsEnabled = document.CanRedo; play.IsEnabled = any && ready;
        export.IsEnabled = any; timeline.IsEnabled = any; updating = true; timeline.Maximum = Math.Max(.001, document.Duration); updating = false;
        player.IsMuted = document.Muted; mute.Content = info?.HasAudio == true ? document.Muted ? "Unmute audio" : "Mute audio" : "No audio";
        UI.Icon(mute, document.Muted ? "\uE74F" : "\uE767", true); mute.ToolTip = mute.Content;
        fileStats.Text = $"Est. size: {document.Duration * (exportSettings?.Mbps ?? info?.Mbps ?? 0) / 8:0.0} MB";
        status.Text = $"{document.Clips.Count} clip(s) · {Clock(document.Duration)} after cuts. Original: {Clock(document.SourceDuration)}.";
        RefreshMarks(); UpdatePosition(Math.Min(editPosition, document.Duration));
    }
    private void SelectClip(int index) { if (document == null) return; Pause(); selected = index; RefreshClipSelection(); Seek(document.Offset(index)); }
    private void RefreshClipSelection()
    {
        if (document == null || document.Clips.Count == 0) return;
        selected = Math.Clamp(selected, 0, document.Clips.Count - 1);
        track.Select(selected);
        trimStart.Text = Timecode(document.Clips[selected].Start); trimEnd.Text = Timecode(document.Clips[selected].End); trimDuration.Text = "Dur: " + Timecode(document.Clips[selected].Duration);
    }
    private void Export()
    {
        if (document?.Clips.Count is not > 0 || info == null || exportSettings == null) return;
        Pause(); var dialog = new VideoExportWindow(sourcePath, document.Clips.ToArray(), document.Muted, info, exportSettings, dark) { Owner = this };
        dialog.ShowDialog(); if (dialog.ExportedSettings != null) exportSettings = dialog.ExportedSettings;
    }
}

internal sealed class VideoExportWindow : Window
{
    private readonly string input;
    private readonly VideoClip[] clips;
    private readonly bool muted;
    private readonly VideoInfo info;
    private readonly TextBox width, height, fps, bitrate, filename, folder;
    private readonly ChoiceField<string> format;
    private readonly Button save, cancel;
    private readonly StackPanel fields;
    private readonly TextBlock status;
    private CancellationTokenSource? exportCancellation;
    private bool busy;
    internal VideoExportSettings? ExportedSettings { get; private set; }

    internal VideoExportWindow(string source, VideoClip[] clips, bool muted, VideoInfo info, VideoExportSettings defaults, bool dark)
    {
        input = source; this.clips = clips; this.muted = muted; this.info = info;
        UI.SetupWindow(this, "Export video", 620, 690, dark); ResizeMode = ResizeMode.NoResize;
        var root = new DockPanel { Margin = new Thickness(26) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        cancel = UI.Button("Cancel", () => { if (busy) exportCancellation?.Cancel(); else Close(); }, false, dark);
        save = UI.Button("Export video", StartExport, true, dark); actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        status = UI.Text("The original video is kept. Changes here affect only this export.", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 16, 0, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        fields = new StackPanel(); root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        fields.Children.Add(UI.PageHeader("Export video", $"{clips.Length} clip(s) · {clips.Sum(c => c.Duration):0.00}s · {(info.HasAudio ? muted ? "Audio muted" : "Original audio included" : "No source audio")}", dark));
        TextBox Field(string label, string value, Panel? parent = null)
        {
            parent ??= fields; parent.Children.Add(UI.Text(label, 11, UI.Muted(dark))); var text = UI.TextBox(dark); text.Text = value; text.Margin = new Thickness(0, 5, 0, 12); parent.Children.Add(text); return text;
        }
        var quality = new Grid(); quality.ColumnDefinitions.Add(new ColumnDefinition()); quality.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(right, 1); quality.Children.Add(left); quality.Children.Add(right); fields.Children.Add(quality);
        width = Field("Width (pixels)", defaults.Width.ToString(CultureInfo.InvariantCulture), left); height = Field("Height (pixels)", defaults.Height.ToString(CultureInfo.InvariantCulture), right);
        fps = Field("Frames per second", VideoCutService.Number(defaults.Fps), left); bitrate = Field("Bitrate (Mbps)", VideoCutService.Number(defaults.Mbps), right);
        fields.Children.Add(UI.Text("Format", 11, UI.Muted(dark))); format = new ChoiceField<string>(VideoExportSettings.Formats, dark); format.Select(defaults.Format, false); format.Button.Margin = new Thickness(0, 5, 0, 12); fields.Children.Add(format.Button);
        filename = Field("File name (extension is added automatically)", Path.GetFileNameWithoutExtension(source) + " edited"); folder = Field("Save location", Path.GetDirectoryName(source)!);
        fields.Children.Add(UI.Button("Browse folder…", () => { var dialog = new OpenFolderDialog { Title = "Choose export folder", InitialDirectory = Directory.Exists(folder.Text) ? folder.Text : Path.GetDirectoryName(input)! }; if (dialog.ShowDialog(this) == true) folder.Text = dialog.FolderName; }, false, dark));
        Content = root;
        Closing += (_, e) => { if (busy) { e.Cancel = true; exportCancellation?.Cancel(); status.Text = "Cancelling export…"; } };
    }
    private async void StartExport()
    {
        try
        {
            if (!int.TryParse(width.Text, out int w) || !int.TryParse(height.Text, out int h) || !double.TryParse(fps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || !double.TryParse(bitrate.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mbps)) throw new InvalidOperationException("Enter numeric resolution, FPS and bitrate values.");
            var settings = new VideoExportSettings(w, h, rate, mbps, format.Value); settings.Validate();
            string name = filename.Text.Trim();
            if (name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ')) throw new InvalidOperationException("Enter a valid file name without a folder path.");
            if (!Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing save folder.");
            string output = Path.GetFullPath(Path.Combine(folder.Text, name + settings.Extension));
            if (output.Equals(input, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a different name so the original video is preserved.");
            if (File.Exists(output) && MessageBox.Show(this, "Replace the existing file?\n" + output, "Export video", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            busy = true; fields.IsEnabled = save.IsEnabled = false; cancel.Content = "Cancel export"; exportCancellation = new CancellationTokenSource(); status.Text = "Exporting… 0%";
            await VideoCutService.ExportAsync(input, output, clips, muted, info, settings, new Progress<double>(value => { if (busy) status.Text = $"Exporting… {value:P0}"; }), exportCancellation.Token);
            ExportedSettings = settings; status.Text = "Saved to " + output; cancel.Content = "Close";
        }
        catch (OperationCanceledException) { status.Text = "Export cancelled. No output file was replaced."; }
        catch (Exception ex) { status.Text = "Couldn't export: " + ex.Message; }
        finally { busy = false; fields.IsEnabled = save.IsEnabled = true; cancel.Content = ExportedSettings == null ? "Cancel" : "Close"; exportCancellation?.Dispose(); exportCancellation = null; }
    }
}
