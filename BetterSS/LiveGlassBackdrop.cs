using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace BetterSS;

// One transient desktop sample per visible preview, shared by its two materials.
internal sealed class LiveGlassBackdrop : IDisposable
{
    private readonly Window owner;
    private readonly GlassSurface[] surfaces;
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(180) };
    private bool excluded, busy, disposed;
    private int failures;
    internal int FrameCount { get; private set; }
    internal bool IsSampling => timer.IsEnabled && !disposed;
    internal string Status { get; private set; } = "Waiting for window";
    internal LiveGlassBackdrop(Window owner, params GlassSurface[] surfaces)
    {
        this.owner = owner; this.surfaces = surfaces;
        owner.SourceInitialized += SourceInitialized; owner.Loaded += Loaded; owner.Closed += Closed; owner.IsVisibleChanged += VisibilityChanged;
        timer.Tick += Tick;
    }
    private void SourceInitialized(object? sender, EventArgs e)
    {
        excluded = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && Native.SetWindowDisplayAffinity(new WindowInteropHelper(owner).Handle, 0x11);
        Status = excluded ? "Sampling" : "Capture exclusion unavailable: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (excluded && !disposed) { timer.Start(); owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Tick(null, EventArgs.Empty))); }
    }
    private void Loaded(object sender, RoutedEventArgs e) { Status = "Window loaded"; if (excluded) { timer.Start(); Tick(null, EventArgs.Empty); } else foreach (var surface in surfaces) surface.UseSolidFallback(); }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!owner.IsVisible) timer.Stop(); else if (excluded && !disposed) timer.Start(); }
    private void Closed(object? sender, EventArgs e) => Dispose();
    private static bool Visible(FrameworkElement element)
    {
        DependencyObject? node = element;
        while (node is Visual)
        { if (node is UIElement visual && (!visual.IsVisible || visual.Opacity < .01)) return false; node = VisualTreeHelper.GetParent(node); }
        return true;
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (disposed || busy || !excluded || !owner.IsVisible) return;
        if (!GlassSurface.Transparency) { foreach (var surface in surfaces) surface.UseSolidFallback(); return; }
        var targets = new List<(GlassSurface Surface, Drawing.Rectangle Bounds, int Width, int Height)>();
        var desktop = Forms.SystemInformation.VirtualScreen;
        foreach (var surface in surfaces.Where(Visible))
        {
            if (surface.ActualWidth < 1 || surface.ActualHeight < 1) continue;
            var point = surface.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(surface);
            var bounds = new Drawing.Rectangle((int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY));
            if (!desktop.Contains(bounds)) continue;
            targets.Add((surface, bounds, (int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight)));
        }
        if (targets.Count == 0) { Status = "No visible material within desktop bounds"; return; }
        var union = targets.Select(t => t.Bounds).Aggregate(Drawing.Rectangle.Union);
        if ((long)union.Width * union.Height > 1800000) return;
        busy = true;
        Status = "Preparing background";
        try
        {
            var frames = await Task.Run(() =>
            {
                var sample = Native.Capture(union);
                return targets.Select(t =>
                {
                    var crop = new CroppedBitmap(sample, new Int32Rect(t.Bounds.X - union.X, t.Bounds.Y - union.Y, t.Bounds.Width, t.Bounds.Height)); crop.Freeze();
                    return t.Surface.PrepareBackdrop(crop, t.Width, t.Height);
                }).ToArray();
            });
            if (disposed || !owner.IsVisible) return;
            for (int i = 0; i < targets.Count; i++) targets[i].Surface.SetPreparedBackdrop(frames[i]);
            FrameCount++;
            Status = "Sampling";
            failures = 0;
        }
        catch (Exception ex) { Status = ex.Message; if (++failures >= 3) { timer.Stop(); foreach (var surface in surfaces) surface.UseSolidFallback(); } }
        finally { busy = false; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop(); timer.Tick -= Tick;
        owner.SourceInitialized -= SourceInitialized; owner.Loaded -= Loaded; owner.Closed -= Closed; owner.IsVisibleChanged -= VisibilityChanged;
    }
}
