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
    private readonly GlassSurface previewMaterial;
    private readonly TextBlock clipboardStatus;
    private readonly LiveGlassBackdrop liveBackdrop;
    internal GlassSurface PreviewMaterial => previewMaterial;
    internal LiveGlassBackdrop LiveBackdrop => liveBackdrop;
    private readonly Button pinButton;
    private readonly StackPanel interactionSurface;
    private readonly DispatcherTimer hideActions = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private double remaining;
    private bool pinned, dragging, closing;
    private bool tutorialPaused;
    private bool tutorialActions;
    internal bool TutorialPaused => tutorialPaused;
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
        var actionMaterial = UI.Liquid(controls, dark, new Thickness(5), 17);
        actions = new Border { Child = actionMaterial, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Opacity = 0, IsHitTestVisible = false };
        row.Children.Add(actions);
        double width = settings.PreviewWidth, height = Math.Clamp(width * image.PixelHeight / image.PixelWidth, 125, 240);
        picture = new Border { Child = new Image { Source = image, Stretch = Stretch.Uniform }, Width = width, Height = height, BorderBrush = UI.Line(dark), BorderThickness = new Thickness(settings.Stroke), Background = UI.Surface(dark), Cursor = Cursors.Hand, ClipToBounds = true };
        UI.RoundImage(picture);
        if (settings.Shadow > 0) picture.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = settings.Shadow, ShadowDepth = 2, Opacity = .14, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        var previewBody = new StackPanel(); previewBody.Children.Add(picture);
        var footer = new Grid { Margin = new Thickness(5, 9, 5, 3) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        clipboardStatus = UI.Text("Screenshot ready", 11, UI.Ink(dark), FontWeights.SemiBold); clipboardStatus.TextWrapping = TextWrapping.NoWrap; clipboardStatus.TextTrimming = TextTrimming.CharacterEllipsis; clipboardStatus.Margin = new Thickness(0, 0, 8, 0); footer.Children.Add(clipboardStatus);
        var resolution = UI.Text($"{image.PixelWidth} × {image.PixelHeight}", 10, UI.Ink(dark)); resolution.Opacity = .75; Grid.SetColumn(resolution, 1); footer.Children.Add(resolution);
        previewBody.Children.Add(footer); previewMaterial = UI.Liquid(previewBody, dark, new Thickness(8), 18); previewMaterial.Width = width + 17.5;
        row.Children.Add(previewMaterial); Content = row;
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
                Motion.Snap(actions, OpacityProperty, tutorialActions ? 1 : 0); actions.IsHitTestVisible = tutorialActions;
                dragging = false; remaining = settings.Duration; elapsed.Restart();
            }
        };
        picture.MouseLeftButtonUp += (_, _) => { if (down == null) return; down = null; picture.ReleaseMouseCapture(); new EditorWindow(app, image, path).Show(); Dismiss(); };
        picture.LostMouseCapture += (_, _) => down = null;
        row.MouseEnter += (_, _) => { hideActions.Stop(); Motion.Snap(actions, OpacityProperty, 1); actions.IsHitTestVisible = true; };
        row.MouseLeave += (_, _) => { hideActions.Start(); remaining = settings.Duration; elapsed.Restart(); };
        hideActions.Tick += (_, _) =>
        {
            hideActions.Stop();
            if (!tutorialActions && !interactionSurface.IsMouseOver && !dragging && !IsMouseCaptureWithin)
            { actions.IsHitTestVisible = false; Motion.To(actions, OpacityProperty, 0, Motion.Exit); }
        };
        SourceInitialized += (_, _) => { Native.Exclude(this); Native.NoActivate(this); };
        Loaded += (_, _) =>
        {
            Native.MoveToMonitor(this, Screen); Position(offset);
            if (!waitingForCapture) { Motion.To(this, OpacityProperty, 1, Motion.Standard); timer.Start(); }
        };
        IsVisibleChanged += (_, _) => { elapsed.Restart(); if (IsVisible) remaining = settings.Duration; };
        timer.Tick += (_, _) =>
        {
            double delta = elapsed.Elapsed.TotalSeconds; elapsed.Restart();
            if (IsVisible && !IsMouseOver && !pinned && !tutorialPaused && !dragging && down == null && !closing && (remaining -= delta) <= 0) Dismiss();
        };
        Closed += (_, _) => { closing = true; timer.Stop(); hideActions.Stop(); };
        liveBackdrop = new LiveGlassBackdrop(this, previewMaterial, actionMaterial);
    }
    internal void SetClipboardState(bool copied) => clipboardStatus.Text = copied ? "✓  Screenshot copied" : "Screenshot ready";
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
        path = savedPath; waitingForCapture = false; Motion.Snap(this, OpacityProperty, 1);
        remaining = app.Settings.Duration; elapsed.Restart(); timer.Start();
    }
    internal void PauseForTutorial(bool value)
    {
        tutorialPaused = value; remaining = app.Settings.Duration; elapsed.Restart();
    }
    internal void ShowTutorialActions(bool value)
    {
        tutorialActions = value; hideActions.Stop();
        Motion.Snap(actions, OpacityProperty, value || IsMouseOver ? 1 : 0); actions.IsHitTestVisible = value || IsMouseOver;
    }
    internal void RefreshPreviewSize()
    {
        if (closing) return;
        picture.Width = app.Settings.PreviewWidth;
        previewMaterial.Width = picture.Width + 17.5;
        picture.Height = Math.Clamp(picture.Width * image.PixelHeight / image.PixelWidth, 125, 240);
        UpdateLayout(); Position(offset);
    }
    private static Button ActionButton(string text, string icon, Action click, bool dark)
    {
        var button = UI.Button(text, click, false, dark);
        UI.Icon(button, text switch { "Edit" => "\uE70F", "Text" => "\uE8D2", "Export" => "\uE74E", "Pin" => "\uE718", _ => "\uE711" });
        button.Content = UI.Text(text, 11, UI.Ink(dark)); button.Tag = text; button.ToolTip = text;
        button.Width = 108; button.Height = 30; button.Padding = new Thickness(8, 0, 8, 0);
        button.Margin = new Thickness(0, 2, 0, 2); button.HorizontalAlignment = HorizontalAlignment.Right;
        UI.GlassButton(button, dark);
        return button;
    }
    private void SetPinned(bool value)
    {
        pinned = value; string label = value ? "Unpin" : "Pin";
        pinButton.Tag = label; pinButton.ToolTip = label;
        ((TextBlock)pinButton.Content).Text = label;
        UI.GlassButton(pinButton, UI.Dark(app.Settings), value);
        System.Windows.Automation.AutomationProperties.SetName(pinButton, label);
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
        Motion.To(this, OpacityProperty, 0, Motion.Exit, completed: Close);
    }
}
