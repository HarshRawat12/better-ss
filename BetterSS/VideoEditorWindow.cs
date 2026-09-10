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
using Microsoft.Win32;

namespace BetterSS;

internal sealed class VideoEditorWindow : Window
{
    private readonly string sourcePath;
    private readonly bool dark;
    private readonly VideoExportSettings? recordingSettings;
    private readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, Stretch = Stretch.Uniform, ScrubbingEnabled = true, Volume = .8 };
    private readonly Slider timeline = new() { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true };
    private readonly Grid track = new();
    private readonly List<Button> clipButtons = new();
    private readonly StackPanel editControls = new();
    private readonly TextBlock status, time, details, marks;
    private readonly TextBox trimStart, trimEnd;
    private readonly Button play, mute, export, undo, redo, split, delete, applyTrim, keepRange;
    private readonly Border playheadLine;
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
    internal bool PreviewReady => ready;
    internal bool LoadFinished { get; private set; }

    internal VideoEditorWindow(App app, string path, VideoExportSettings? settings = null)
    {
        sourcePath = Path.GetFullPath(path); recordingSettings = settings; dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Better SS Split", 1100, 800, dark); MinWidth = 850; MinHeight = 650;
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var headerActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        headerActions.Children.Add(UI.Button("Keyboard", ShowShortcuts, false, dark)); export = UI.Button("Export video…", Export, true, dark); export.IsEnabled = false; export.ToolTip = "Export video · Ctrl+M"; headerActions.Children.Add(export); DockPanel.SetDock(headerActions, Dock.Right); header.Children.Add(headerActions);
        var heading = new StackPanel(); heading.Children.Add(UI.Text("Better SS Split", 25, UI.Ink(dark), FontWeights.SemiBold));
        details = UI.Text(Path.GetFileName(sourcePath), 11, UI.Muted(dark)); details.Margin = new Thickness(0, 6, 0, 0); heading.Children.Add(details); header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        status = UI.Text("Reading video…", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 14, 0, 0); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var transport = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        transport.Children.Add(UI.Button("J  ◀", () => ShuttleBack(), false, dark)); transport.Children.Add(UI.Button("K  ■", Pause, false, dark));
        play = UI.Button("L  ▶", ShuttleForward, false, dark); play.IsEnabled = false; play.ToolTip = "Play/Pause · Space or L"; transport.Children.Add(play);
        transport.Children.Add(UI.Button("◀ Frame", () => Step(-1), false, dark)); transport.Children.Add(UI.Button("Frame ▶", () => Step(1), false, dark));
        mute = UI.Button("Mute audio", ToggleMute, false, dark); mute.IsEnabled = false; transport.Children.Add(mute);
        time = UI.Text("00:00.000 / 00:00.000", 12, UI.Muted(dark)); time.Margin = new Thickness(10, 0, 0, 0); transport.Children.Add(time); bottom.Children.Add(transport);
        UI.StyleSlider(timeline, dark); timeline.IsEnabled = false; timeline.ValueChanged += (_, _) => { if (!updating) Seek(timeline.Value); }; bottom.Children.Add(timeline);
        marks = UI.Text("IN  —   OUT  —", 11, UI.Muted(dark)); marks.Margin = new Thickness(0, 2, 0, 6); bottom.Children.Add(marks);
        var timelineHeader = new Grid(); timelineHeader.ColumnDefinitions.Add(new ColumnDefinition()); timelineHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); timelineHeader.Children.Add(UI.Text("TIMELINE  ·  V1", 11, UI.Muted(dark), FontWeights.SemiBold)); var timelineHint = UI.Text("Selection follows the playhead", 10, UI.Muted(dark)); Grid.SetColumn(timelineHint, 1); timelineHeader.Children.Add(timelineHint); bottom.Children.Add(timelineHeader);
        playheadLine = new Border { Width = 2, Background = UI.Brush("#EF4444"), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false }; Panel.SetZIndex(playheadLine, 20);
        var trackScroll = new ScrollViewer { Content = track, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 10), Height = 78, Background = UI.Brush(dark ? "#111111" : "#E8E8E8") };
        var trackShell = new Grid(); trackShell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) }); trackShell.ColumnDefinitions.Add(new ColumnDefinition());
        var trackName = new Border { Background = UI.Brush(dark ? "#292929" : "#DEDEDE"), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = UI.Text("V1\nVIDEO", 10, UI.Ink(dark), FontWeights.SemiBold) }; trackShell.Children.Add(trackName); Grid.SetColumn(trackScroll, 1); trackShell.Children.Add(trackScroll); bottom.Children.Add(trackShell); bottom.Children.Add(editControls); editControls.IsEnabled = false;
        var actions = new WrapPanel();
        split = UI.Button("Add Edit", SplitAtPlayhead, false, dark); split.ToolTip = "Split at playhead · Ctrl+K or C";
        delete = UI.Button("Ripple Delete", DeleteSelected, false, dark); delete.ToolTip = "Remove the selected clip and close the gap · Delete, Backspace, or Shift+Delete";
        undo = UI.Button("Undo", () => Edit(() => document!.Undo()), false, dark); redo = UI.Button("Redo", () => Edit(() => document!.Redo()), false, dark);
        actions.Children.Add(split); actions.Children.Add(delete); actions.Children.Add(undo); actions.Children.Add(redo);
        actions.Children.Add(UI.Button("Mark In", () => SetMark(true), false, dark)); actions.Children.Add(UI.Button("Mark Out", () => SetMark(false), false, dark));
        keepRange = UI.Button("Keep In–Out", KeepMarkedRange, false, dark); keepRange.IsEnabled = false; keepRange.ToolTip = "Ripple trim everything outside the marked range"; actions.Children.Add(keepRange); editControls.Children.Add(actions);
        var trim = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        trim.Children.Add(UI.Text("Selected clip · source start (s)", 11, UI.Muted(dark)));
        trimStart = UI.TextBox(dark); trimStart.Width = 100; trimStart.Margin = new Thickness(8, 0, 14, 0); trim.Children.Add(trimStart);
        trim.Children.Add(UI.Text("End (s)", 11, UI.Muted(dark))); trimEnd = UI.TextBox(dark); trimEnd.Width = 100; trimEnd.Margin = new Thickness(8, 0, 14, 0); trim.Children.Add(trimEnd);
        applyTrim = UI.Button("Apply trim", ApplyTrim, false, dark); trim.Children.Add(applyTrim); editControls.Children.Add(trim);
        root.Children.Add(new Border { Background = Brushes.Black, BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Child = player, MinHeight = 140 }); Content = root;
        player.MediaOpened += (_, _) => { ready = true; play.IsEnabled = document?.Clips.Count > 0; player.Pause(); Seek(editPosition); status.Text = "Ready. The original stays intact. Split, delete or trim clips, then export."; };
        player.MediaEnded += (_, _) => { Pause(); if (document != null) UpdatePosition(document.Duration); };
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
        else if ((modifiers == ModifierKeys.Control && key == Key.K) || (key == Key.C && modifiers == ModifierKeys.None)) SplitAtPlayhead();
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
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) { status.Text = "Couldn't prepare video: " + ex.Message; export.IsEnabled = document != null; } }
        finally { LoadFinished = true; if (closed) CleanupPreview(); }
    }
    private void CleanupPreview()
    {
        if (previewFolder == null) return;
        try { string file = Path.Combine(previewFolder, "preview.mp4"); if (File.Exists(file)) File.Delete(file); if (Directory.Exists(previewFolder)) Directory.Delete(previewFolder); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void Pause() { playing = false; timer.Stop(); player.Pause(); play.Content = "L  ▶"; }
    private void TogglePlay()
    {
        if (!ready || document?.Clips.Count is not > 0) return;
        if (playing) { Pause(); return; }
        if (editPosition >= document.Duration - .015) Seek(0);
        playing = true; player.Play(); timer.Start(); play.Content = "K  ■";
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
            "Space / L    Play\nK             Pause\nJ             Jump back 1 second\n← / →         One frame\nShift + ←/→   Five frames\n↑ / ↓         Previous / next edit\nCtrl+K or C   Add edit\nDelete        Ripple delete clip at playhead\nI / O         Mark In / Out\nHome / End    Timeline start / end\nCtrl+Z / Ctrl+Y   Undo / redo\nCtrl+M        Export",
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
            if (playbackIndex == document.Clips.Count - 1) { Pause(); UpdatePosition(document.Duration); return; }
            playbackIndex++; selected = playbackIndex; RefreshClipSelection(); player.Position = TimeSpan.FromSeconds(document.Clips[playbackIndex].Start);
            UpdatePosition(document.Offset(playbackIndex));
        }
        else UpdatePosition(document.Offset(playbackIndex) + Math.Clamp(position - clip.Start, 0, clip.Duration));
    }
    private void UpdatePosition(double position)
    {
        editPosition = Math.Clamp(position, 0, document?.Duration ?? 0); updating = true; timeline.Value = editPosition; updating = false;
        time.Text = $"{Clock(editPosition)} / {Clock(document?.Duration ?? 0)}";
        if (document != null && document.Duration > 0 && track.ActualWidth > 0) playheadLine.RenderTransform = new TranslateTransform(Math.Clamp(editPosition / document.Duration * Math.Max(0, track.ActualWidth - 2), 0, Math.Max(0, track.ActualWidth - 2)), 0);
    }
    private static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.fff");
    private void Edit(Action action)
    {
        if (document == null) return; Pause();
        try { action(); selected = Math.Clamp(selected, 0, Math.Max(0, document.Clips.Count - 1)); Refresh(); Seek(Math.Min(editPosition, document.Duration)); }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private void ApplyTrim()
    {
        if (!double.TryParse(trimStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double start) || !double.TryParse(trimEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double end)) { status.Text = "Enter cut times in seconds, for example 2.5 and 8.75."; return; }
        Edit(() => document!.Trim(selected, start, end));
    }
    private void Refresh()
    {
        if (document == null) return;
        track.Children.Clear(); track.ColumnDefinitions.Clear(); clipButtons.Clear();
        for (int i = 0; i < document.Clips.Count; i++)
        {
            int index = i; var clip = document.Clips[i];
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clip.Duration, GridUnitType.Star), MinWidth = 112 });
            var button = UI.Button($"CLIP {i + 1}\n{clip.Duration:0.00}s", () => SelectClip(index), i == selected, dark);
            button.ToolTip = $"Source {Clock(clip.Start)} → {Clock(clip.End)} · Click to select"; button.Margin = new Thickness(0, 0, 3, 0); Grid.SetColumn(button, i); track.Children.Add(button); clipButtons.Add(button);
        }
        bool any = document.Clips.Count > 0;
        if (any) { Grid.SetColumnSpan(playheadLine, document.Clips.Count); track.Children.Add(playheadLine); }
        else track.Children.Add(UI.Text("No clips left. Undo to restore your video.", 13, UI.Muted(dark)));
        trimStart.IsEnabled = trimEnd.IsEnabled = applyTrim.IsEnabled = split.IsEnabled = delete.IsEnabled = any;
        if (any) { trimStart.Text = VideoCutService.Number(document.Clips[selected].Start); trimEnd.Text = VideoCutService.Number(document.Clips[selected].End); }
        else { trimStart.Text = trimEnd.Text = ""; }
        undo.IsEnabled = document.CanUndo; redo.IsEnabled = document.CanRedo; play.IsEnabled = any && ready;
        export.IsEnabled = any; timeline.IsEnabled = any; updating = true; timeline.Maximum = Math.Max(.001, document.Duration); updating = false;
        player.IsMuted = document.Muted; mute.Content = info?.HasAudio == true ? document.Muted ? "Unmute audio" : "Mute audio" : "No audio";
        status.Text = $"{document.Clips.Count} clip(s) · {Clock(document.Duration)} after cuts. Original: {Clock(document.SourceDuration)}.";
        RefreshMarks(); UpdatePosition(Math.Min(editPosition, document.Duration));
    }
    private void SelectClip(int index) { if (document == null) return; Pause(); selected = index; RefreshClipSelection(); Seek(document.Offset(index)); }
    private void RefreshClipSelection()
    {
        if (document == null || document.Clips.Count == 0) return;
        selected = Math.Clamp(selected, 0, document.Clips.Count - 1);
        for (int i = 0; i < clipButtons.Count; i++) UI.Select(clipButtons[i], i == selected, dark);
        trimStart.Text = VideoCutService.Number(document.Clips[selected].Start); trimEnd.Text = VideoCutService.Number(document.Clips[selected].End);
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
        fields.Children.Add(UI.Text("Export video", 25, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text($"{clips.Length} clip(s) · {clips.Sum(c => c.Duration):0.00}s · {(info.HasAudio ? muted ? "Audio muted" : "Original audio included" : "No source audio")}", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 8, 0, 14); fields.Children.Add(note);
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
