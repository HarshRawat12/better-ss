using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace BetterSS;

// One clipped surface per monitor keeps physical-pixel coordinates correct with mixed DPI.
internal sealed class CaptureAnimationWindow : Window
{
    private static readonly List<CaptureAnimationWindow> active = new();
    private readonly TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static void CancelAll() { foreach (var window in active.ToArray()) window.Close(); }
    internal static Task PlayAsync(BitmapSource image, System.Drawing.Rectangle source, Rect destination, Settings settings)
    {
        if (!settings.CaptureAnimation) return Task.CompletedTask;
        var tasks = new List<Task>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new CaptureAnimationWindow(image, source, destination, screen, settings);
            active.Add(window); tasks.Add(window.finished.Task); window.Show();
        }
        return Task.WhenAll(tasks);
    }
    private CaptureAnimationWindow(BitmapSource image, System.Drawing.Rectangle source, Rect destination, Forms.Screen screen, Settings settings)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        var canvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        var frame = new Grid();
        frame.Children.Add(new Image { Source = image, Stretch = Stretch.Fill });
        var flash = new Border { Background = Brushes.White, Opacity = settings.FlashOpacity / 100 };
        frame.Children.Add(flash); canvas.Children.Add(frame); Content = canvas;
        SourceInitialized += (_, _) =>
        {
            Native.Exclude(this); Native.NoActivate(this);
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowLongPtr(handle, -20, new IntPtr(Native.GetWindowLongPtr(handle, -20).ToInt64() | 0x20));
        };
        Loaded += (_, _) =>
        {
            Native.Place(this, screen.Bounds); UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            double x = (source.X - screen.Bounds.X) / dpi.DpiScaleX, y = (source.Y - screen.Bounds.Y) / dpi.DpiScaleY;
            frame.Width = source.Width / dpi.DpiScaleX; frame.Height = source.Height / dpi.DpiScaleY;
            var scale = new ScaleTransform(1, 1); var translation = new TranslateTransform(x, y);
            frame.RenderTransform = new TransformGroup { Children = { scale, translation } };
            double duration = Math.Clamp(settings.AnimationDuration, 160, 700), flashTime = Math.Min(65, duration * .2);
            flash.BeginAnimation(OpacityProperty, new DoubleAnimation(flash.Opacity, 0, TimeSpan.FromMilliseconds(flashTime)));
            DoubleAnimation Motion(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(duration - flashTime))
            { BeginTime = TimeSpan.FromMilliseconds(flashTime), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            translation.BeginAnimation(TranslateTransform.XProperty, Motion(x, (destination.X - screen.Bounds.X) / dpi.DpiScaleX));
            translation.BeginAnimation(TranslateTransform.YProperty, Motion(y, (destination.Y - screen.Bounds.Y) / dpi.DpiScaleY));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion(1, destination.Width / source.Width));
            var finish = Motion(1, destination.Height / source.Height); finish.Completed += (_, _) => Close();
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, finish);
        };
        Closed += (_, _) => { active.Remove(this); finished.TrySetResult(true); };
    }
}
