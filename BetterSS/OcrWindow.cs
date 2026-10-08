using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal sealed class OcrWindow : Window
{
    internal OcrWindow(App app, BitmapSource image)
    {
        bool dark = UI.Dark(app.Settings); UI.SetupWindow(this, "Extract text", 650, 600, dark); MinWidth = 420; MinHeight = 360;
        var root = new DockPanel { Margin = new Thickness(24) };
        var title = UI.PageHeader("Extract text", "Recognize words on your PC. Review, edit and copy below.", dark, "\uE8D2"); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var status = UI.Text("Reading text on this PC…", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 0, 0, 18); DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var text = UI.TextBox(dark); text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(UI.Button("Close", Close, false, dark)); var copy = UI.Button("Copy text", () => { }, true, dark); copy.IsEnabled = false; actions.Children.Add(copy); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions); root.Children.Add(text); Content = root;
        copy.Click += async (_, _) => { bool copied = await ClipboardService.TextAsync(text.Text); status.Text = copied ? "Text copied to clipboard." : "Clipboard is busy. Try Copy text again."; if (copied) app.Toast("Text copied to clipboard"); };
        Loaded += async (_, _) =>
        {
            try { text.Text = await OcrService.RecognizeAsync(image); status.Text = text.Text.Length > 0 ? "Review or edit the recognized text, then copy it." : "No readable text found. Try a closer or sharper capture."; copy.IsEnabled = text.Text.Length > 0; }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        text.TextChanged += (_, _) => copy.IsEnabled = !string.IsNullOrWhiteSpace(text.Text);
    }
}
