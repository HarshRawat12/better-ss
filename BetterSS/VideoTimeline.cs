using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal sealed record VideoThumbnail(double Time, BitmapSource Image);

// The filmstrip uses source timestamps; edits never rewrite the source video.
internal sealed class VideoTimeline : Grid
{
    private readonly bool dark;
    private readonly Grid clips = new();
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Border playhead;
    private readonly TimelineRuler ruler;
    private readonly List<Button> buttons = new();
    private IReadOnlyList<VideoThumbnail> thumbnails = Array.Empty<VideoThumbnail>();
    private VideoCutDocument? document;
    private string filename = "";
    private int selected;
    private double position;
    internal event Action<double>? SeekRequested;
    internal event Action<int>? ClipSelected;
    internal event Action? TrimStarted;
    internal event Action<int, double, double>? TrimPreviewChanged;
    internal event Action<int, double, double>? TrimRequested;
    internal int ThumbnailCount => thumbnails.Count;
    internal VideoTimeline(bool dark)
    {
        this.dark = dark; Background = UI.Background(dark); Height = 156; ClipToBounds = true;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); ColumnDefinitions.Add(new ColumnDefinition());
        var labels = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        var trackTitle = UI.Text("TRACK", 10, UI.Muted(dark)); trackTitle.Height = 30; labels.Children.Add(trackTitle);
        var trackName = UI.Text("●  V1", 12, UI.Accent(dark), FontWeights.SemiBold); trackName.Margin = new Thickness(0, 38, 0, 0); labels.Children.Add(trackName);
        var source = UI.Text("VIDEO", 9, UI.Muted(dark)); source.Margin = new Thickness(0, 7, 0, 0); labels.Children.Add(source); Children.Add(labels);
        var workspace = new Grid { Margin = new Thickness(0, 0, 12, 0), Background = Brushes.Transparent, Focusable = true };
        workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) }); workspace.RowDefinitions.Add(new RowDefinition());
        ruler = new TimelineRuler(dark); workspace.Children.Add(ruler);
        clips.Margin = new Thickness(0, 7, 0, 5); Grid.SetRow(clips, 1); workspace.Children.Add(clips);
        playhead = new Border { Width = 2, Height = 146, Background = UI.Brush("#DF3348"), Child = new Border { Width = 12, Height = 11, Background = UI.Brush("#DF3348"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-5, 0, -5, 0), Child = UI.Text("•", 10, Brushes.White) } };
        overlay.Children.Add(playhead); Grid.SetRowSpan(overlay, 3); workspace.Children.Add(overlay); Grid.SetColumn(workspace, 1); Children.Add(workspace);
        workspace.PreviewMouseLeftButtonDown += (_, e) =>
        {
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node != null && node != workspace) { if (node is Thumb) return; node = VisualTreeHelper.GetParent(node); }
            if (document?.Clips.Count is not > 0) return;
            SeekRequested?.Invoke(Math.Clamp(e.GetPosition(workspace).X / Math.Max(1, workspace.ActualWidth) * document.Duration, 0, document.Duration));
            workspace.Focus(); workspace.CaptureMouse(); e.Handled = true;
        };
        workspace.MouseMove += (_, e) => { if (workspace.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && document != null) SeekRequested?.Invoke(Math.Clamp(e.GetPosition(workspace).X / Math.Max(1, workspace.ActualWidth) * document.Duration, 0, document.Duration)); };
        workspace.MouseLeftButtonUp += (_, e) => { if (workspace.IsMouseCaptured) { workspace.ReleaseMouseCapture(); e.Handled = true; } };
        workspace.SizeChanged += (_, _) => { RedrawFilmstrips(); Position(position); };
    }
    internal void SetThumbnails(IReadOnlyList<VideoThumbnail> value) { thumbnails = value; RedrawFilmstrips(); }
    internal void Update(VideoCutDocument value, int selection, string name)
    {
        document = value; selected = selection; filename = name; clips.Children.Clear(); clips.ColumnDefinitions.Clear(); buttons.Clear(); ruler.Duration = value.Duration;
        for (int i = 0; i < value.Clips.Count; i++)
        {
            int index = i; var clip = value.Clips[i]; clips.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clip.Duration, GridUnitType.Star) });
            var button = UI.Button("CLIP " + (i + 1), () => ClipSelected?.Invoke(index), false, dark);
            button.Margin = new Thickness(0, 0, i == value.Clips.Count - 1 ? 0 : 2, 0); button.Padding = new Thickness(0); button.BorderThickness = new Thickness(1.5); button.BorderBrush = i == selected ? UI.Brush("#0071E3") : UI.Line(dark);
            button.Background = i == selected ? UI.Brush("#0071E3") : UI.Brush("#536777");
            button.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="4" ClipToBounds="True">
                <Grid><Border Background="{Binding}"/><Border Background="{TemplateBinding Background}" Height="19" VerticalAlignment="Top" Padding="5,1"><TextBlock Text="{TemplateBinding Tag}" Foreground="White" FontSize="10" TextTrimming="CharacterEllipsis"/></Border>
                <Border BorderBrush="#65FFFFFF" BorderThickness="1" Visibility="Collapsed" Name="Focus"/></Grid>
              </Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Focus" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>
            """);
            button.Tag = $"{filename}  [{VideoEditorWindow.Timecode(clip.Start)} — {VideoEditorWindow.Timecode(clip.End)}]";
            button.ToolTip = $"Clip {i + 1} · source {clip.Start:0.000}s → {clip.End:0.000}s. Drag a yellow edge to trim.";
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
            var shell = new Grid(); shell.Children.Add(button); Grid.SetColumn(shell, i); clips.Children.Add(shell); buttons.Add(button);
            if (i == selected) { AddHandle(shell, index, true, clip); AddHandle(shell, index, false, clip); }
        }
        if (value.Clips.Count == 0) clips.Children.Add(UI.Text("No clips left. Undo to restore your video.", 12, UI.Muted(dark)));
        RedrawFilmstrips(); Position(position);
    }
    internal void Select(int selection) { if (document != null && selected != selection) Update(document, selection, filename); }
    internal void Position(double seconds)
    { position = seconds; Canvas.SetLeft(playhead, document?.Duration > 0 ? Math.Clamp(seconds / document.Duration * Math.Max(0, overlay.ActualWidth - 2), 0, Math.Max(0, overlay.ActualWidth - 2)) : 0); }
    private void RedrawFilmstrips()
    {
        if (document == null) return;
        for (int i = 0; i < buttons.Count; i++)
        {
            var clip = document.Clips[i]; double width = Math.Max(24, clips.ActualWidth * clip.Duration / Math.Max(.001, document.Duration)), height = 88;
            var drawing = new DrawingGroup(); using (var dc = drawing.Open())
            {
                dc.DrawRectangle(UI.Brush("#303435"), null, new Rect(0, 0, width, height));
                int count = Math.Max(1, (int)Math.Ceiling(width / 100)); double tile = width / count;
                for (int n = 0; n < count && thumbnails.Count > 0; n++)
                {
                    double at = clip.Start + (n + .5) / count * clip.Duration;
                    var frame = thumbnails.MinBy(t => Math.Abs(t.Time - at))!;
                    dc.DrawRectangle(new ImageBrush(frame.Image) { Stretch = Stretch.UniformToFill }, null, new Rect(n * tile, 19, tile + .5, height - 19));
                }
                if (thumbnails.Count == 0) dc.DrawText(new FormattedText("Loading thumbnails…", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.White, 1), new Point(10, 43));
            }
            drawing.Freeze(); var brush = new DrawingBrush(drawing) { Stretch = Stretch.Fill }; brush.Freeze(); buttons[i].DataContext = brush;
        }
    }
    private void AddHandle(Grid shell, int index, bool left, VideoClip clip)
    {
        var thumb = new Thumb { Width = 7, Height = 34, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 19, 2, 0), Cursor = Cursors.SizeWE, ToolTip = left ? "Drag source start" : "Drag source end", Background = UI.Brush("#FFD24A"), Template = (ControlTemplate)XamlReader.Parse("""<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb"><Border CornerRadius="2" Background="{TemplateBinding Background}" BorderBrush="#9D7410" BorderThickness=".5"/></ControlTemplate>""") };
        System.Windows.Automation.AutomationProperties.SetName(thumb, left ? "Trim clip start" : "Trim clip end"); shell.Children.Add(thumb);
        double delta = 0, start = clip.Start, end = clip.End; var transform = new TranslateTransform(); thumb.RenderTransform = transform;
        thumb.DragStarted += (_, _) => { delta = 0; start = clip.Start; end = clip.End; TrimStarted?.Invoke(); };
        thumb.DragDelta += (_, e) =>
        {
            delta += e.HorizontalChange; double seconds = delta * clip.Duration / Math.Max(1, shell.ActualWidth);
            if (left) start = Math.Clamp(clip.Start + seconds, 0, clip.End - .04); else end = Math.Clamp(clip.End + seconds, clip.Start + .04, document?.SourceDuration ?? clip.End);
            transform.X = (left ? start - clip.Start : end - clip.End) / clip.Duration * shell.ActualWidth;
            thumb.ToolTip = (left ? "In: " : "Out: ") + VideoEditorWindow.Timecode(left ? start : end);
            TrimPreviewChanged?.Invoke(index, start, end);
        };
        thumb.DragCompleted += (_, e) => { transform.X = 0; if (e.Canceled) TrimPreviewChanged?.Invoke(index, clip.Start, clip.End); else if (Math.Abs(start - clip.Start) > .0001 || Math.Abs(end - clip.End) > .0001) TrimRequested?.Invoke(index, start, end); };
    }
}

internal sealed class TimelineRuler(bool dark) : FrameworkElement
{
    private double duration;
    internal double Duration { set { duration = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (duration <= 0 || ActualWidth <= 0) return;
        double desired = duration / Math.Max(2, Math.Floor(ActualWidth / 115));
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(.001, desired))));
        double step = new[] { 1d, 2, 5, 10 }.Select(n => n * magnitude).First(n => n >= desired);
        var pen = new Pen(UI.Line(dark), 1);
        for (double at = 0; at <= duration + .00001; at += step / 5)
        {
            double x = at / duration * ActualWidth; bool major = Math.Abs(at / step - Math.Round(at / step)) < .0001;
            dc.DrawLine(pen, new Point(x, major ? 18 : 23), new Point(x, 30));
            if (major) { string text = TimeSpan.FromSeconds(at).ToString(duration >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss"); var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, UI.Muted(dark), VisualTreeHelper.GetDpi(this).PixelsPerDip); if (x + label.Width < ActualWidth) dc.DrawText(label, new Point(x + 3, 3)); }
        }
        dc.DrawLine(pen, new Point(0, 29.5), new Point(ActualWidth, 29.5));
    }
}
