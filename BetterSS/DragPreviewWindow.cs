using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

// A visual only: the original preview remains the source of the native OLE file drop.
internal sealed class DragPreviewWindow : Window
{
    private const double PaddingPixels = 28;
    private readonly Vector sizePixels, grabPixels;
    private readonly Border picture;
    private readonly ScaleTransform shrink = new();
    private readonly DispatcherTimer follow = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    internal double PreviewScale => shrink.ScaleX;

    internal DragPreviewWindow(BitmapSource image, Border source, Point grab, Settings settings)
    {
        var dpi = VisualTreeHelper.GetDpi(source);
        sizePixels = new Vector(source.ActualWidth * dpi.DpiScaleX, source.ActualHeight * dpi.DpiScaleY);
        grabPixels = new Vector(grab.X * dpi.DpiScaleX, grab.Y * dpi.DpiScaleY);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        IsHitTestVisible = false; Focusable = false; Opacity = .94;
        Width = source.ActualWidth + PaddingPixels * 2; Height = source.ActualHeight + PaddingPixels * 2;
        bool dark = UI.Dark(settings);
        picture = new Border
        {
            Child = new Image { Source = image, Stretch = Stretch.Uniform },
            Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(settings.Stroke),
            RenderTransform = shrink,
            RenderTransformOrigin = new Point(grab.X / source.ActualWidth, grab.Y / source.ActualHeight)
        };
        UI.RoundImage(picture);
        if (settings.Shadow > 0) picture.Effect = new System.Windows.Media.Effects.DropShadowEffect
        { BlurRadius = settings.Shadow, ShadowDepth = 2, Opacity = .14, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        var canvas = new Canvas(); canvas.Children.Add(picture); Content = canvas;
        SourceInitialized += (_, _) =>
        {
            Native.NoActivate(this); Native.Exclude(this);
            var hwnd = new WindowInteropHelper(this).Handle;
            // Layered + WS_EX_TRANSPARENT passes input to drop targets in other processes too.
            // https://learn.microsoft.com/windows/win32/winmsg/window-features#layered-windows
            Native.SetWindowLongPtr(hwnd, -20, new IntPtr(Native.GetWindowLongPtr(hwnd, -20).ToInt64() | 0x20));
            FollowCursor();
        };
        Loaded += (_, _) =>
        {
            FollowCursor();
            Motion.To(shrink, ScaleTransform.ScaleXProperty, .9, 150);
            Motion.To(shrink, ScaleTransform.ScaleYProperty, .9, 150);
            follow.Start();
        };
        follow.Tick += (_, _) => FollowCursor();
        Closed += (_, _) => follow.Stop();
    }

    internal void FollowCursor()
    {
        var cursor = Native.CursorPosition;
        // Native positions are physical pixels, including negative monitor coordinates.
        // Keep the grab point and visual size stable while WPF changes monitor DPI.
        Native.Place(this, new System.Drawing.Rectangle(
            (int)Math.Round(cursor.X - grabPixels.X - PaddingPixels),
            (int)Math.Round(cursor.Y - grabPixels.Y - PaddingPixels),
            (int)Math.Ceiling(sizePixels.X + 2 * PaddingPixels),
            (int)Math.Ceiling(sizePixels.Y + 2 * PaddingPixels)));
        var dpi = VisualTreeHelper.GetDpi(this);
        picture.Width = sizePixels.X / dpi.DpiScaleX; picture.Height = sizePixels.Y / dpi.DpiScaleY;
        Canvas.SetLeft(picture, PaddingPixels / dpi.DpiScaleX); Canvas.SetTop(picture, PaddingPixels / dpi.DpiScaleY);
    }
}
