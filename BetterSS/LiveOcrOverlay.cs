using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace BetterSS;

// A small, non-modal result surface anchored to the selection. OCR text is
// copied directly to the clipboard; this surface is only for immediate review.
internal sealed class LiveOcrOverlay : Window
{
    private readonly App app;
    private readonly Forms.Screen screen;
    private readonly DrawingRectangle selection;
    private readonly bool dark;
    private readonly TextBox recognizedText;
    private readonly TextBlock status;
    private readonly Button copy;

    internal LiveOcrOverlay(App app, Forms.Screen screen, DrawingRectangle selection, bool dark)
    {
        this.app = app; this.screen = screen; this.selection = selection; this.dark = dark;
        Title = "Better SS · Live OCR"; Width = 420; Height = 330; MinWidth = 340; MinHeight = 270;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = UI.Selected(dark), Child = new TextBlock { Text = "\uE8D2", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = UI.Accent(dark), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        label.Children.Add(mark);
        var title = UI.Text("Live OCR", 14, UI.Ink(dark), FontWeights.SemiBold); title.Margin = new Thickness(9, 0, 0, 0); label.Children.Add(title);
        heading.Children.Add(label);
        var close = UI.Button("", Close, false, dark); UI.Icon(close, "\uE711", true); close.Width = 30; close.Height = 30; close.Margin = new Thickness(0); close.ToolTip = "Close"; System.Windows.Automation.AutomationProperties.SetName(close, "Close live OCR");
        Grid.SetColumn(close, 1); heading.Children.Add(close);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);

        status = UI.Text("Reading selected text on this PC…", 11, UI.Muted(dark)); status.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        copy = UI.Button("Copy text", CopyAgain, true, dark); copy.Margin = new Thickness(0); copy.IsEnabled = false; footer.Children.Add(copy);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        recognizedText = UI.TextBox(dark); recognizedText.IsReadOnly = true; recognizedText.AcceptsReturn = true;
        recognizedText.TextWrapping = TextWrapping.Wrap; recognizedText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        recognizedText.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; recognizedText.Padding = new Thickness(12);
        recognizedText.Text = "Recognized text will appear here."; root.Children.Add(recognizedText);

        Content = UI.Liquid(root, dark, new Thickness(14), 16);
        SourceInitialized += (_, _) => Native.Exclude(this);
        Loaded += (_, _) => PositionNearSelection();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    internal async void ReadAsync(BitmapSource image)
    {
        try
        {
            string text = await OcrService.RecognizeAsync(image);
            recognizedText.Text = text;
            if (string.IsNullOrWhiteSpace(text))
            {
                status.Text = "No readable text found. Try a larger or sharper area.";
                return;
            }
            copy.IsEnabled = true;
            bool copied = await ClipboardService.TextAsync(text);
            status.Text = copied ? "Copied automatically · select text here or copy it again." : "Clipboard busy · select the text here and try Copy text.";
        }
        catch (Exception ex)
        {
            recognizedText.Text = ""; status.Text = ex.Message;
        }
    }

    private async void CopyAgain()
    {
        bool copied = await ClipboardService.TextAsync(recognizedText.Text);
        status.Text = copied ? "Text copied to clipboard." : "Clipboard is busy. Try Copy text again.";
        if (copied) app.Toast("Text copied to clipboard", screen);
    }

    private void PositionNearSelection()
    {
        Native.MoveToMonitor(this, screen); UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        int left = selection.Left;
        int top = selection.Bottom + 12;
        if (top + height > screen.WorkingArea.Bottom) top = selection.Top - height - 12;
        left = Math.Clamp(left, screen.WorkingArea.Left + 8, Math.Max(screen.WorkingArea.Left + 8, screen.WorkingArea.Right - width - 8));
        top = Math.Clamp(top, screen.WorkingArea.Top + 8, Math.Max(screen.WorkingArea.Top + 8, screen.WorkingArea.Bottom - height - 8));
        Native.Place(this, new DrawingRectangle(left, top, width, height));
    }
}
