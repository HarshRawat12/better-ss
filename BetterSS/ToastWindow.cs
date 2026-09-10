using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed class ToastWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    internal ToastWindow(string message, Forms.Screen screen, bool dark)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; Opacity = 0; IsHitTestVisible = false;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var check = UI.Text("✓", 13, UI.Brush("#37A866"), FontWeights.SemiBold);
        check.Width = 18; check.TextAlignment = TextAlignment.Center; check.Margin = new Thickness(0, 0, 6, 0); content.Children.Add(check);
        content.Children.Add(UI.Text(message, 12, UI.Ink(dark), FontWeights.SemiBold));
        Content = new Border { Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 9, 16, 9), Child = content };
        SourceInitialized += (_, _) => { Native.Exclude(this); Native.NoActivate(this); };
        Loaded += (_, _) => { Native.MoveToMonitor(this, screen); var dpi = VisualTreeHelper.GetDpi(this); int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY); Native.Place(this, new System.Drawing.Rectangle(screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2, screen.WorkingArea.Bottom - height - 28, width, height)); BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))); timer.Start(); };
        timer.Tick += (_, _) => { timer.Stop(); var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)); fade.Completed += (_, _) => Close(); BeginAnimation(OpacityProperty, fade); };
        Closed += (_, _) => timer.Stop();
    }
}
