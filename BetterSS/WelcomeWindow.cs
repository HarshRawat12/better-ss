using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BetterSS;

internal sealed class WelcomeWindow : Window
{
    private readonly App app;
    private readonly bool dark;
    private readonly StackPanel body = new();
    private readonly Button back, next;
    private readonly TextBlock progress, error;
    private bool startOnLogin;
    internal int Step { get; private set; }
    internal WelcomeWindow(App app)
    {
        this.app = app; dark = UI.Dark(app.Settings); startOnLogin = app.Settings.StartOnLogin;
        UI.SetupWindow(this, "Welcome", 780, 680, dark); MinWidth = 670; MinHeight = 620;
        var root = new DockPanel { Margin = new Thickness(32) };
        progress = UI.Text("", 11, UI.Muted(dark)); progress.Margin = new Thickness(0, 0, 0, 22); DockPanel.SetDock(progress, Dock.Top); root.Children.Add(progress);
        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        var skip = UI.Button("Skip for now", Close, false, dark); DockPanel.SetDock(skip, Dock.Left); footer.Children.Add(skip);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        back = UI.Button("Back", () => ShowStep(Step - 1), false, dark); actions.Children.Add(back);
        next = UI.Button("Next", Advance, true, dark); next.Margin = new Thickness(0); actions.Children.Add(next); footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        error = UI.Text("", 12, UI.Muted(dark)); DockPanel.SetDock(error, Dock.Bottom); root.Children.Add(error);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        ShowStep(0);
        Closed += (_, _) => { app.Settings.IntroductionSeen = true; app.Persist(); };
    }
    internal void ShowStep(int step)
    {
        Step = Math.Clamp(step, 0, 3); body.Children.Clear(); error.Text = "";
        progress.Text = "BETTER SS   /   QUICK GUIDE   /   " + (Step + 1) + " OF 4";
        back.IsEnabled = Step > 0; next.Content = Step == 3 ? "Finish setup" : "Next";
        switch (Step)
        {
            case 0:
                TitleBlock("Capture with a click.", "A screenshot utility that stays out of your way.");
                Tip("01   Open capture", "Double-click Better SS in the system tray, or use your capture shortcut. Change or record a shortcut in Preferences → Capture.");
                Tip("02   Choose a mode", "Drag an area, choose a window from the list (including minimized windows), select a display, or capture all displays in one image.");
                Tip("03   Stay with the mouse", "After opening capture, everything works with clicks and drags. Right-click or use Cancel to leave capture without taking a screenshot.");
                break;
            case 1:
                TitleBlock("Your screenshot, in motion.", "The image is copied automatically. A floating preview appears on its capture display.");
                var sample = new Image { Source = SelfTest.SampleImage(), Height = 125, Stretch = Stretch.Uniform };
                body.Children.Add(UI.Card(sample, dark, new Thickness(12)));
                Tip("Drag, drop, or click", "Drag the thumbnail: it shrinks slightly and follows your cursor. Drop it into an app or folder. Right-click during a drag to cancel. Click the thumbnail to edit.");
                Tip("Hover for actions", "Hover to pause the timer and reveal Edit, Text, Export, Pin, and Dismiss. Dismiss removes only the preview, not a saved capture.");
                body.Children.Add(UI.Button("Try a floating preview", app.DemoPreview, false, dark));
                break;
            case 2:
                TitleBlock("Make it clear. Send it anywhere.", "Edits are flattened when copied or exported. Your original capture stays intact.");
                Tip("Annotate", "Use pen, highlighter, arrows, shapes, blur, or mosaic. Pick a color and brush size. Undo and redo are available with the buttons or Ctrl+Z and Ctrl+Y. Use solid redaction to conceal sensitive pixels.");
                Tip("Copy image or extract text", "Copy image copies your edits. Text opens on-device OCR, where you can review and copy the recognized words.");
                Tip("Choose your export", "Save as PNG, JPEG, PDF, TIFF, BMP, or GIF. Choose size, JPEG quality, and PDF layout. Automatic original saving is optional in Storage.");
                body.Children.Add(UI.Button("Try editing a sample", () =>
                {
                    var image = SelfTest.SampleImage(); var path = Path.Combine(Settings.DataFolder, "DragCache", "Better SS practice.png");
                    Native.SavePng(image, path); new EditorWindow(app, image, path).Show();
                }, false, dark));
                break;
            default:
                TitleBlock("Ready when you are.", "Two small finishing touches. Both stay under your control.");
                var startup = new StackPanel();
                var toggle = new CheckBox { Content = "Start Better SS when I sign in to Windows", IsChecked = startOnLogin, FontSize = 14, Foreground = UI.Ink(dark) };
                toggle.Click += (_, _) => startOnLogin = toggle.IsChecked == true; startup.Children.Add(toggle);
                var note = UI.Text("Optional. Starts quietly in the tray, without opening this guide or Preferences. You can change this in Preferences → Startup & guide.", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 12, 0, 0); startup.Children.Add(note); body.Children.Add(UI.Card(startup, dark));
                Tip("Keep Better SS visible in your tray", "Click the hidden-icons arrow (^) beside the clock and drag Better SS into the visible system tray. Or open Taskbar settings and enable Better SS under the system-tray / notification-area options. Windows controls this choice.");
                body.Children.Add(UI.Button("Open Windows taskbar settings", () =>
                { try { StartupService.OpenTaskbarSettings(); } catch (Exception ex) { error.Text = ex.Message; } }, false, dark));
                var location = UI.Text("Keep the app in its current folder if you enable startup. Closing Preferences keeps Better SS running; choose Quit Better SS in the tray to exit.", 12, UI.Muted(dark)); location.Margin = new Thickness(0, 20, 0, 0); body.Children.Add(location);
                break;
        }
    }
    private void Advance()
    {
        if (Step < 3) { ShowStep(Step + 1); return; }
        if (startOnLogin != app.Settings.StartOnLogin && !app.SetStartupEnabled(startOnLogin, out var problem)) { error.Text = problem; return; }
        Close();
    }
    private void TitleBlock(string title, string description)
    {
        body.Children.Add(UI.Text(title, 28, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text(description, 13, UI.Muted(dark)); note.Margin = new Thickness(0, 12, 0, 24); body.Children.Add(note);
    }
    private void Tip(string title, string description)
    {
        var panel = new StackPanel(); panel.Children.Add(UI.Text(title, 14, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text(description, 12, UI.Muted(dark)); note.Margin = new Thickness(0, 9, 0, 0); panel.Children.Add(note);
        body.Children.Add(UI.Card(panel, dark, new Thickness(18)));
    }
}
