using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed class WelcomeWindow : Window
{
    private readonly App app;
    private readonly bool dark;
    private readonly StackPanel body = new();
    private readonly Button back, next;
    private readonly TextBlock progress, error;
    private readonly StackPanel steps = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private bool startOnLogin, awaitingCapture, closingCoach, closed;
    private PreviewWindow? tutorialPreview;
    private TutorialCoachWindow? coach;
    internal int Step { get; private set; }
    internal bool AwaitingCapture => awaitingCapture;
    internal WelcomeWindow(App app, bool recordOnOpen = false)
    {
        this.app = app; dark = UI.Dark(app.Settings); startOnLogin = app.Settings.StartOnLogin;
        UI.SetupWindow(this, "Walkthrough", 700, 660, dark); MinWidth = 620; MinHeight = 560;
        var root = new DockPanel { Margin = new Thickness(24) };
        progress = UI.Text("", 11, UI.Muted(dark));
        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 24) }; DockPanel.SetDock(steps, Dock.Right); navigation.Children.Add(steps); var mark = UI.Mark(28); mark.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(mark, Dock.Left); navigation.Children.Add(mark); navigation.Children.Add(progress); DockPanel.SetDock(navigation, Dock.Top); root.Children.Add(navigation);
        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        var skip = UI.Button("Skip for now", Skip, false, dark); UI.Quiet(skip, dark); DockPanel.SetDock(skip, Dock.Left); footer.Children.Add(skip);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        back = UI.Button("Back", () => ShowStep(Step - 1), false, dark); actions.Children.Add(back);
        next = UI.Button("Set shortcut", Advance, true, dark); next.Margin = new Thickness(0); actions.Children.Add(next); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        error = UI.Text("", 12, UI.Muted(dark)); DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        ShowStep(0);
        Loaded += (_, _) => { if (recordOnOpen) Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => { if (!closed && Step == 0) RecordShortcut(); })); };
        Closed += (_, _) => { closed = true; awaitingCapture = false; CloseCoach(); ReleasePreview(); };
    }
    internal void ShowStep(int step)
    {
        CloseCoach(); Step = Math.Clamp(step, 0, 4); body.Children.Clear(); error.Text = "";
        progress.Text = "Walkthrough  ·  Step " + (Step + 1) + " of 5";
        steps.Children.Clear(); for (int i = 0; i < 5; i++) steps.Children.Add(new Border { Width = i == Step ? 22 : 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = i <= Step ? UI.Accent(dark) : UI.Line(dark), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        back.IsEnabled = Step > 0; next.IsEnabled = Step != 1;
        next.Content = Step == 0 ? "Set shortcut" : Step == 4 ? "Finish setup" : "Next";
        tutorialPreview?.ShowTutorialActions(Step == 3);
        if (Step is 2 or 3)
        {
            Hide();
            coach = new TutorialCoachWindow(tutorialPreview, Step, () => ShowStep(Step + 1), Skip, dark);
            coach.Closed += (_, _) => { if (!closingCoach && !closed) { coach = null; ShowStep(4); } };
            coach.Show(); return;
        }
        switch (Step)
        {
            case 0:
                TitleBlock("Make a shortcut your own.", "First, choose the keys you will use every day. We will check that they are available before saving them.");
                Tip("Record your capture shortcut", "Press Ctrl or Alt with a key. Add Shift if you like, release the keys, then choose Use shortcut in the recording dialog.");
                body.Children.Add(UI.Button("Record my shortcut…", RecordShortcut, true, dark));
                var saved = UI.Button("Use saved shortcut: " + app.Settings.Hotkey, () =>
                {
                    if (!app.TrySetHotkey(app.Settings.Hotkey, out var problem)) { error.Text = problem; return; }
                    ShowStep(1);
                }, false, dark);
                saved.IsEnabled = app.Settings.Hotkey != "Disabled"; saved.Margin = new Thickness(0, 14, 0, 22); body.Children.Add(saved);
                Tip("Prefer Windows + Shift + S?", "After setup, turn on Use Windows + Shift + S for Better SS in Preferences → Capture. Your personal shortcut stays saved for when you turn that option off.");
                break;
            case 1:
                TitleBlock("Try your first real capture.", "Press your shortcut now. This walkthrough will disappear before the capture overlay opens.");
                var shortcut = UI.Text(app.EffectiveCaptureShortcut, 22, UI.Ink(dark), FontWeights.SemiBold); shortcut.HorizontalAlignment = HorizontalAlignment.Center;
                body.Children.Add(UI.Card(shortcut, dark, new Thickness(26)));
                Tip("Drag a small area, then release", "Choose anything on your desktop. The mode toolbar disappears while you drag. Release to take the screenshot; right-click or Escape cancels.");
                Tip("We will meet you at the preview", "After capture, a small tutorial box will appear beside your actual screenshot. You can try dragging it without racing its dismissal timer.");
                next.Content = "Waiting for your shortcut…";
                break;
            default:
                TitleBlock("Make the preview fit your day.", "Choose how long future previews stay visible and how large they appear. Hovering pauses their timer; Pin keeps a screenshot until you dismiss it.");
                var options = new StackPanel();
                PreviewSlider(options, "Time on screen", 2, 30, app.Settings.Duration, "seconds", v => app.Settings.Duration = v);
                PreviewSlider(options, "Preview width", 220, 440, app.Settings.PreviewWidth, "pixels", v => { app.Settings.PreviewWidth = v; tutorialPreview?.RefreshPreviewSize(); });
                body.Children.Add(UI.Card(options, dark));
                var startup = UI.Switch("Start Better SS when I sign in to Windows", startOnLogin, dark); startup.Margin = new Thickness(0, 8, 0, 16);
                startup.Click += (_, _) => startOnLogin = startup.IsChecked == true; body.Children.Add(startup);
                Tip("Your tools are one hover away", "Edit adds annotations. Text extracts words locally. Export changes the image format or dimensions. Pin keeps the preview on screen. Duration and preview width stay editable in Preferences → Floating preview.");
                Tip("Keep Better SS within reach", "Drag its icon out of the hidden-icons arrow beside the Windows clock. Closing Preferences keeps Better SS running; Quit Better SS in the tray exits the app.");
                break;
        }
        if (IsLoaded && !IsVisible && !closed) { Show(); Activate(); }
    }
    private void RecordShortcut()
    {
        var dialog = new HotkeyWindow(app) { Owner = this };
        dialog.ShowDialog(); if (dialog.Saved && !closed) ShowStep(1);
    }
    internal void CaptureShortcutPressed()
    {
        if (closed || Step != 1) return;
        awaitingCapture = true; Hide();
    }
    internal void CaptureCancelled()
    {
        if (!awaitingCapture || closed) return;
        awaitingCapture = false; ShowStep(1); Show(); Activate();
        error.Text = "Capture cancelled. Press your shortcut again when you are ready.";
    }
    internal void CaptureReady(PreviewWindow preview)
    {
        if (!awaitingCapture || closed) return;
        awaitingCapture = false; ReleasePreview(); tutorialPreview = preview;
        preview.PauseForTutorial(true); ShowStep(2);
    }
    private void Advance()
    {
        if (Step == 0) { RecordShortcut(); return; }
        if (Step == 1) return;
        if (Step < 4) { ShowStep(Step + 1); return; }
        if (startOnLogin != app.Settings.StartOnLogin && !app.SetStartupEnabled(startOnLogin, out var problem)) { error.Text = problem; return; }
        app.Settings.IntroductionSeen = true; app.Persist(); Close();
    }
    private void Skip() { app.Settings.IntroductionSeen = true; app.Persist(); Close(); }
    private void CloseCoach()
    {
        closingCoach = true; coach?.Close(); coach = null; closingCoach = false;
    }
    private void ReleasePreview()
    {
        tutorialPreview?.PauseForTutorial(false); tutorialPreview?.ShowTutorialActions(false); tutorialPreview = null;
    }
    private void PreviewSlider(StackPanel panel, string title, double min, double max, double value, string unit, Action<double> changed)
    {
        var label = UI.Text(title + " · " + Math.Round(value) + " " + unit, 13, UI.Ink(dark)); panel.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 12, 0, 22) }; UI.StyleSlider(slider, dark);
        slider.ValueChanged += (_, _) => { changed(slider.Value); label.Text = title + " · " + Math.Round(slider.Value) + " " + unit; app.Persist(); };
        panel.Children.Add(slider);
    }
    private void TitleBlock(string title, string description)
    {
        body.Children.Add(UI.PageHeader(title, description, dark));
    }
    private void Tip(string title, string description)
    {
        var panel = new StackPanel(); panel.Children.Add(UI.Text(title, 14, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text(description, 12, UI.Muted(dark)); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
        body.Children.Add(UI.Card(panel, dark, new Thickness(18)));
    }
}

internal sealed class TutorialCoachWindow : Window
{
    internal TutorialCoachWindow(PreviewWindow? preview, int step, Action next, Action skip, bool dark)
    {
        var screen = preview?.Screen ?? Forms.Screen.FromPoint(Native.CursorPosition);
        UI.SetupWindow(this, "Screenshot tutorial", 390, 330, dark);
        ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; SizeToContent = SizeToContent.Height;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(UI.Text("Walkthrough  ·  Step " + (step + 1) + " of 5", 10, UI.Muted(dark)));
        var title = UI.Text(step == 2 ? "Drag your screenshot anywhere." : "Your screenshot is editable.", 20, UI.Ink(dark), FontWeights.SemiBold); title.Margin = new Thickness(0, 14, 0, 12); panel.Children.Add(title);
        var instructions = UI.Text(step == 2 ? "Drag the thumbnail into an app that accepts images, or into a folder. A successful drop dismisses the preview. You can try it now or continue with it here." : "Click the screenshot to edit it. Hover over the preview for Edit, Text and Pin. Text extracts words on this PC; Pin keeps a capture visible. You can explore these tools whenever you need them.", 13, UI.Muted(dark)); panel.Children.Add(instructions);
        var note = UI.Text("The tutorial pauses this preview's dismissal timer.", 11, UI.Muted(dark)); note.Margin = new Thickness(0, 14, 0, 18); panel.Children.Add(note);
        void PreviewClosed(object? sender, EventArgs e)
        {
            instructions.Text = step == 2 ? "You used or dismissed your screenshot. On future captures, drag the thumbnail into an app that accepts images or into a folder." : "For your next capture, click the screenshot to edit it, or hover for Edit, Text and Pin. Text extracts words locally; Pin keeps the preview visible.";
            note.Text = "You can continue the walkthrough without this preview.";
        }
        if (preview?.IsVisible != true) PreviewClosed(null, EventArgs.Empty);
        if (preview != null) { preview.Closed += PreviewClosed; Closed += (_, _) => preview.Closed -= PreviewClosed; }
        var actions = new WrapPanel(); actions.Children.Add(UI.Button(step == 2 ? "See screenshot tools" : "Adjust my preview", next, true, dark)); actions.Children.Add(UI.Button("Skip", skip, false, dark)); panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) =>
        {
            Native.MoveToMonitor(this, screen); UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var area = screen.WorkingArea;
            double previewTop = preview?.IsVisible == true ? preview.ImageScreenBounds.Top : area.Bottom - 260;
            Native.Place(this, new System.Drawing.Rectangle(Math.Max(area.Left + 12, area.Right - width - 26), Math.Max(area.Top + 12, (int)previewTop - height - 24), width, height));
        };
    }
}
