using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class GlassCompatibilityTests
{
    internal static void Run(App app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string output = Path.GetFullPath("test-results"); Directory.CreateDirectory(output);
        app.Startup += (_, _) =>
        {
            var log = new StringBuilder(); int count = 0;
            try
            {
                Run((passed, message) => { if (!passed) throw new Exception(message); count++; log.AppendLine("PASS " + message); }, output);
                log.AppendLine($"SUCCESS: {count} glass compatibility checks passed.");
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); Environment.ExitCode = 1; }
            finally { File.WriteAllText(Path.Combine(output, "glass-compatibility.txt"), log.ToString()); app.Shutdown(); }
        };
        app.Run();
    }

    internal static void Run(Action<bool, string> check, string output)
    {
        var transparency = GlassSurface.TransparencyOverride; var windows = GlassSurface.WindowsTransparencyOverride;
        var contrast = UI.HighContrastOverride; var tier = GlassSurface.RenderingTierOverride;
        var mode = RenderOptions.ProcessRenderMode;
        try
        {
            GlassSurface.TransparencyOverride = null; GlassSurface.WindowsTransparencyOverride = true; UI.HighContrastOverride = false;
            var source = Pattern(); var original = Pixels(source);
            foreach (int level in new[] { 0, 1, 2 })
            {
                GlassSurface.RenderingTierOverride = level;
                RenderOptions.ProcessRenderMode = level < 2 ? RenderMode.SoftwareOnly : RenderMode.Default;
                check(GlassSurface.Transparency, $"tier {level} keeps Windows-enabled transparency without a test transparency override");
                foreach (bool dark in new[] { false, true })
                {
                    var material = UI.Liquid(UI.Text("Liquid glass\nCompatibility review", 17, UI.Ink(dark)), dark, new Thickness(16));
                    material.Width = 320; material.Height = 160;
                    var review = Window(material); review.SizeToContent = SizeToContent.WidthAndHeight;
                    try
                    {
                        review.Show(); material.SetBackdrop(source); material.UpdateLayout();
                        check(material.UsesBackdrop && material.UsesEdgeRefraction, $"tier {level} {(dark ? "dark" : "light")} keeps backdrop and refraction");
                        if (level < 2) check(material.UsesLightweightRendering && !Find(material).Any(e => e.Effect != null), $"tier {level} glass renders without WPF blur or shadow effects");
                        var bitmap = new RenderTargetBitmap(320, 160, 96, 96, PixelFormats.Pbgra32); bitmap.Render(material);
                        var rendered = Pixels(bitmap);
                        check(Enumerable.Range(0, rendered.Length / 4).Any(i => Math.Abs(rendered[i * 4] - rendered[i * 4 + 2]) > 15), $"tier {level} {(dark ? "dark" : "light")} rendered pixels retain background color");
                        Native.SavePng(bitmap, Path.Combine(output, $"glass-tier-{level}-{(dark ? "dark" : "light")}.png"));
                    }
                    finally { review.Close(); }
                }
            }
            var full = LiquidLens.Render(source, 64, 64, 12); var lite = LiquidLens.Render(source, 64, 64, 12, true);
            check(!Pixels(full.Body).SequenceEqual(Pixels(lite.Body)) && Pixels(full.Rim!).SequenceEqual(Pixels(lite.Rim!)), "software frost changes body pixels while preserving sharp refracted rim pixels");
            var bounded = LiquidLens.Render(source, 1000, 100, 20, true);
            check(bounded.Body.PixelWidth <= 350 && bounded.Body.PixelHeight <= 225 && Math.Abs(bounded.Body.PixelWidth / (double)bounded.Body.PixelHeight - 10) < .1, "compatibility buffers are bounded without distorting wide toolbar proportions");
            check(lite.Body.IsFrozen && lite.Rim!.IsFrozen && Pixels(source).SequenceEqual(original), "software rendering freezes output and leaves screenshot source pixels intact");
            var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[] { 31, 63, 127, 255 }, 4); pixel.Freeze();
            check(Pixels(LiquidLens.Render(pixel, 1, 1, 12, true).Body).SequenceEqual(Pixels(pixel)), "software blur handles a one-pixel material without changing its color");

            GlassSurface.RenderingTierOverride = 0; RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            GlassSurface.WindowsTransparencyOverride = false;
            var solid = UI.Liquid(UI.Text("Solid"), false, new Thickness(12)); solid.SetBackdrop(source);
            check(!GlassSurface.Transparency && !solid.UsesBackdrop && GlassSurface.MaterialStatus.Contains("Windows transparency is disabled"), "explicit Windows transparency off stays solid and reports its reason");
            GlassSurface.WindowsTransparencyOverride = true; UI.HighContrastOverride = true;
            check(!GlassSurface.Transparency && GlassSurface.MaterialStatus.Contains("High Contrast"), "High Contrast stays solid even on compatibility rendering");
            UI.HighContrastOverride = false;
            Sampler(check, source);
        }
        finally
        {
            GlassSurface.TransparencyOverride = transparency; GlassSurface.WindowsTransparencyOverride = windows;
            GlassSurface.RenderingTierOverride = tier; UI.HighContrastOverride = contrast; RenderOptions.ProcessRenderMode = mode;
        }
    }

    private static void Sampler(Action<bool, string> check, BitmapSource sample)
    {
        var material = UI.Liquid(UI.Text("Exclusion test"), false, new Thickness(12));
        var window = Window(material); int calls = 0;
        using (var sampler = new LiveGlassBackdrop(window, new[] { material }, _ => false, _ => { calls++; return sample; }))
        {
            try
            {
                window.Show(); PumpUntil(() => window.IsLoaded, 1000);
                check(!sampler.IsSampling && calls == 0 && !material.UsesBackdrop && sampler.Status.Contains("Capture exclusion unavailable"), "failed capture exclusion never samples itself and preserves the error through Loaded");
            }
            finally { window.Close(); }
        }
        material = UI.Liquid(UI.Text("Frozen-frame test"), false, new Thickness(12)); window = Window(material); calls = 0;
        using (var sampler = new LiveGlassBackdrop(window, new[] { material }, _ => true, bounds =>
        {
            if (++calls > 1) throw new InvalidOperationException("simulated capture failure");
            var image = new TransformedBitmap(sample, new ScaleTransform(bounds.Width / (double)sample.PixelWidth, bounds.Height / (double)sample.PixelHeight)); image.Freeze(); return image;
        }))
        {
            try
            {
                window.Show(); PumpUntil(() => sampler.FrameCount > 0, 2500);
                check(sampler.FrameCount == 1 && material.UsesBackdrop && sampler.SampleIntervalMilliseconds == 500, "compatibility preview samples its backdrop at the reduced refresh rate");
                PumpUntil(() => !sampler.IsSampling, 3000);
                check(!sampler.IsSampling && material.UsesBackdrop && sampler.Status.StartsWith("Frozen glass:"), "repeated capture failures retain the last good glass frame and stop sampling");
            }
            finally { window.Close(); }
        }
    }
    private static Window Window(GlassSurface material) => new()
    {
        Width = 250, Height = 140, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
        ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = material
    };
    private static BitmapSource Pattern()
    {
        var pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        { int i = (y * 64 + x) * 4; pixels[i] = (byte)(x * 4); pixels[i + 1] = (byte)(((x / 4 + y / 4) % 2) * 200); pixels[i + 2] = (byte)(y * 4); pixels[i + 3] = 255; }
        var image = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Pbgra32, null, pixels, 64 * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image) { var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels; }
    private static System.Collections.Generic.IEnumerable<UIElement> Find(DependencyObject node)
    {
        if (node is UIElement element) yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Find(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void PumpUntil(Func<bool> condition, int timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeout)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
    }
}
