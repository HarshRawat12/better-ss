using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed class PreviewWindow : Window
{
    private readonly App app;
    private readonly BitmapSource image;
    private string path;
    private bool waitingForCapture;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly Border picture;
    private readonly Border actions;
    private readonly Button pinButton;
    private readonly StackPanel interactionSurface;
    private readonly DispatcherTimer hideActions = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private double remaining;
    private bool pinned, dragging, closing;
    private Point? down;
    private int offset;
    internal Forms.Screen Screen { get; }
    internal double ScaleY => VisualTreeHelper.GetDpi(this).DpiScaleY;
    internal PreviewWindow(App app, BitmapSource image, string path, Forms.Screen screen, bool deferReveal = false)
    {
        this.app = app; this.image = image; this.path = path; Screen = screen; remaining = app.Settings.Duration; waitingForCapture = deferReveal;
        var settings = app.Settings; bool dark = UI.Dark(settings);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight; UseLayoutRounding = true; Opacity = 0;
        // A nonzero alpha connects the picture, gap, and buttons for layered-window hit testing.
        // Fully transparent pixels are passed through to the window underneath by Windows.
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22), Background = UI.Brush("#01202020") };
        interactionSurface = row;
        // The action strip reserves room on the left, so every pill can grow leftward
        // without moving the screenshot itself.
        var controls = new StackPanel { Width = 108 };
        controls.Children.Add(ActionButton("Edit", "✎", () => { new EditorWindow(app, image, path).Show(); Dismiss(); }, dark));
        controls.Children.Add(ActionButton("Text", "T", () => { SetPinned(true); new OcrWindow(app, image).Show(); }, dark));
        controls.Children.Add(ActionButton("Export", "↥", () => { SetPinned(true); new ExportWindow(app, image, path).Show(); }, dark));
        pinButton = ActionButton("Pin", "pin", () => SetPinned(!pinned), dark); controls.Children.Add(pinButton);
        controls.Children.Add(ActionButton("Dismiss", "×", Dismiss, dark));
        actions = new Border { Child = controls, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Opacity = 0, IsHitTestVisible = false };
        row.Children.Add(actions);
        double width = settings.PreviewWidth, height = Math.Clamp(width * image.PixelHeight / image.PixelWidth, 125, 240);
        picture = new Border { Child = new Image { Source = image, Stretch = Stretch.Uniform }, Width = width, Height = height, BorderBrush = UI.Line(dark), BorderThickness = new Thickness(settings.Stroke), Background = UI.Surface(dark), Cursor = Cursors.Hand, ClipToBounds = true };
        if (settings.Shadow > 0) picture.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = settings.Shadow, ShadowDepth = 3, Opacity = .22, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        row.Children.Add(picture); Content = row;
        picture.ToolTip = "Drag into an app or folder. Click to edit.";
        picture.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(picture); picture.CaptureMouse(); };
        picture.MouseMove += (_, e) =>
        {
            if (down is not Point origin || e.LeftButton != MouseButtonState.Pressed || dragging) return;
            Point current = e.GetPosition(picture);
            if (Math.Abs(current.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null; dragging = true; picture.ReleaseMouseCapture();
            DragPreviewWindow? ghost = null;
            GiveFeedbackEventHandler feedback = (_, e) => { ghost?.FollowCursor(); e.UseDefaultCursors = true; e.Handled = true; };
            QueryContinueDragEventHandler cancel = (_, e) =>
            { if ((e.KeyStates & DragDropKeyStates.RightMouseButton) != 0) { e.Action = DragAction.Cancel; e.Handled = true; } };
            try
            {
                var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { path }); data.SetImage(image);
                ghost = new DragPreviewWindow(image, picture, origin, settings); ghost.Show();
                row.Opacity = 0; hideActions.Stop();
                picture.GiveFeedback += feedback; picture.QueryContinueDrag += cancel;
                if (DragDrop.DoDragDrop(picture, data, DragDropEffects.Copy) != DragDropEffects.None) Dismiss();
            }
            catch (Exception ex) { app.Notify("Couldn't drag screenshot", ex.Message); }
            finally
            {
                picture.GiveFeedback -= feedback; picture.QueryContinueDrag -= cancel;
                ghost?.Close();
                // A rejected or cancelled drop returns the original preview, ready to retry.
                if (!closing) row.Opacity = 1;
                actions.Opacity = 0; actions.IsHitTestVisible = false;
                dragging = false; remaining = settings.Duration; elapsed.Restart();
            }
        };
        picture.MouseLeftButtonUp += (_, _) => { if (down == null) return; down = null; picture.ReleaseMouseCapture(); new EditorWindow(app, image, path).Show(); Dismiss(); };
        picture.LostMouseCapture += (_, _) => down = null;
        row.MouseEnter += (_, _) => { hideActions.Stop(); actions.Opacity = 1; actions.IsHitTestVisible = true; };
        row.MouseLeave += (_, _) => { hideActions.Start(); remaining = settings.Duration; elapsed.Restart(); };
        hideActions.Tick += (_, _) =>
        {
            hideActions.Stop();
            if (!interactionSurface.IsMouseOver && !dragging && !IsMouseCaptureWithin)
            { actions.Opacity = 0; actions.IsHitTestVisible = false; }
        };
        SourceInitialized += (_, _) => { Native.Exclude(this); Native.NoActivate(this); };
        Loaded += (_, _) =>
        {
            Native.MoveToMonitor(this, Screen); Position(offset);
            if (!waitingForCapture) { BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))); timer.Start(); }
        };
        IsVisibleChanged += (_, _) => { elapsed.Restart(); if (IsVisible) remaining = settings.Duration; };
        timer.Tick += (_, _) =>
        {
            double delta = elapsed.Elapsed.TotalSeconds; elapsed.Restart();
            if (IsVisible && !IsMouseOver && !pinned && !dragging && down == null && !closing && (remaining -= delta) <= 0) Dismiss();
        };
        Closed += (_, _) => { closing = true; timer.Stop(); hideActions.Stop(); };
    }
    internal Rect ImageScreenBounds
    {
        get
        {
            var visual = (Image)picture.Child; var dpi = VisualTreeHelper.GetDpi(this);
            double scale = Math.Min(visual.ActualWidth / image.PixelWidth, visual.ActualHeight / image.PixelHeight);
            double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
            var point = visual.PointToScreen(new Point((visual.ActualWidth - w) / 2, (visual.ActualHeight - h) / 2));
            return new Rect(point.X, point.Y, w * dpi.DpiScaleX, h * dpi.DpiScaleY);
        }
    }
    internal void Reveal(string savedPath)
    {
        if (closing) return;
        path = savedPath; waitingForCapture = false; BeginAnimation(OpacityProperty, null); Opacity = 1;
        remaining = app.Settings.Duration; elapsed.Restart(); timer.Start();
    }
    private static Button ActionButton(string text, string icon, Action click, bool dark)
    {
        var button = new Button
        {
            Content = UI.Text(text, 10, UI.Ink(dark)), Tag = text, ToolTip = text,
            Width = 108, Height = 30, Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 2, 0, 2), HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Cursor = Cursors.Hand
        };
        button.Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter Property="Opacity" Value="0.9"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.68"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
        button.Click += (_, _) => click(); return button;
    }
    private void SetPinned(bool value)
    {
        pinned = value; string label = value ? "Unpin" : "Pin";
        pinButton.Tag = label; pinButton.ToolTip = label;
        ((TextBlock)pinButton.Content).Text = label;
        remaining = app.Settings.Duration;
    }
    internal void Position(int stackOffset)
    {
        offset = stackOffset;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this); int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        Native.Place(this, new System.Drawing.Rectangle(Screen.WorkingArea.Right - width - 4, Math.Max(Screen.WorkingArea.Top, Screen.WorkingArea.Bottom - height - 4 - offset), width, height));
    }
    private void Dismiss()
    {
        if (closing) return; closing = true; timer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(170)); fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade);
    }
}
