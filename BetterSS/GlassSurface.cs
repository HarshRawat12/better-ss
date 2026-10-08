using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BetterSS;

// Material layers stay independent of foreground controls and captured image pixels.
internal sealed class GlassSurface : Border
{
    internal const byte BackdropTintAlpha = 230, FixedTintAlpha = 247, CaptureTintAlpha = 180;
    internal const double BlurRadius = 12, ShadowRadius = 24;
    internal static bool? TransparencyOverride { get; set; }
    internal static bool Transparency => TransparencyOverride ?? (!UI.HighContrast && (RenderCapability.Tier >> 16) >= 2 && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is int enabled && enabled != 0);
    private readonly Border backgroundLayer, tintLayer, edgeLayer, ornaments, shadowLayer;
    private readonly RadialGradientBrush light;
    private readonly bool dark, liquid;
    private readonly double radius;
    internal bool UsesBackdrop { get; private set; }
    internal bool UsesEdgeRefraction { get; private set; }
    internal bool UsesFullRefraction { get; private set; }
    internal GlassSurface(UIElement content, bool dark, Thickness padding, double radius = 14, BitmapSource? backdrop = null, bool captureMaterial = false, bool liquidGlass = false)
    {
        this.dark = dark; this.radius = radius; liquid = captureMaterial || liquidGlass;
        CornerRadius = new CornerRadius(radius); BorderThickness = new Thickness(.75);
        var layers = new Grid(); var shell = new Grid();
        shadowLayer = new Border { CornerRadius = new CornerRadius(radius), IsHitTestVisible = false }; shell.Children.Add(shadowLayer); shell.Children.Add(layers);
        backgroundLayer = new Border { IsHitTestVisible = false }; layers.Children.Add(backgroundLayer);
        tintLayer = new Border { IsHitTestVisible = false }; layers.Children.Add(tintLayer);
        edgeLayer = new Border { IsHitTestVisible = false }; layers.Children.Add(edgeLayer);
        var lighting = new Grid();
        lighting.Children.Add(new Border { Background = new LinearGradientBrush(UI.Brush(dark ? "#16FFFFFF" : "#55FFFFFF").Color, Colors.Transparent, 100) });
        lighting.Children.Add(new Border { CornerRadius = new CornerRadius(Math.Max(0, radius - 1)), BorderThickness = new Thickness(1, 1, .5, .5), BorderBrush = UI.Brush(dark ? "#65FFFFFF" : "#E0FFFFFF"), Margin = new Thickness(.75) });
        light = new RadialGradientBrush { Center = new Point(.25, 0), GradientOrigin = new Point(.25, 0), RadiusX = .7, RadiusY = 1.4, GradientStops = { new GradientStop(UI.Brush("#24FFFFFF").Color, 0), new GradientStop(Colors.Transparent, 1) } };
        lighting.Children.Add(new Border { Background = light });
        ornaments = new Border { Child = lighting, IsHitTestVisible = false }; layers.Children.Add(ornaments);
        layers.Children.Add(new Border { Padding = padding, Child = content }); Child = shell;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SizeChanged += (_, _) => layers.Clip = new RectangleGeometry(new Rect(0, 0, layers.ActualWidth, layers.ActualHeight), Math.Max(0, radius - 1), Math.Max(0, radius - 1));
        MouseMove += (_, e) =>
        {
            if (!liquid || !Transparency || Motion.Reduced || ActualWidth <= 0 || ActualHeight <= 0) return;
            var point = e.GetPosition(this); light.Center = light.GradientOrigin = new Point(point.X / ActualWidth, point.Y / ActualHeight);
        };
        ApplyMaterial(false);
        if (backdrop != null) SetBackdrop(backdrop);
    }
    private void ApplyMaterial(bool hasBackdrop, bool solid = false)
    {
        bool glass = Transparency && !solid;
        BorderBrush = UI.HighContrast ? SystemColors.WindowTextBrush : UI.Brush(glass && liquid ? dark ? "#70FFFFFF" : "#BFFFFFFF" : dark ? "#48484F" : "#DCDCE2");
        Color tint = UI.Brush(dark ? "#29292E" : "#FFFFFF").Color;
        tint.A = !glass ? (byte)255 : !hasBackdrop ? FixedTintAlpha : liquid ? dark ? CaptureTintAlpha : (byte)195 : BackdropTintAlpha;
        tintLayer.Background = UI.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(tint);
        ornaments.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        // Shadow only the backing shape. Never send foreground text/image through an effect.
        shadowLayer.Background = glass ? UI.Brush(dark ? "#29292E" : "#FFFFFF") : null;
        shadowLayer.Effect = glass ? new DropShadowEffect { BlurRadius = ShadowRadius, ShadowDepth = 5, Opacity = dark ? .24 : .15, RenderingBias = RenderingBias.Performance } : null;
    }
    // This method only reads immutable fields; live sampling can prepare frames off-thread.
    internal LiquidLens.Frame PrepareBackdrop(BitmapSource source, int width, int height)
        => liquid ? LiquidLens.Render(source, width, height, radius) : new(source, null);
    internal void SetBackdrop(BitmapSource source)
    {
        if (!Transparency) { UseSolidFallback(); return; }
        int width = ActualWidth > 0 ? (int)Math.Ceiling(ActualWidth) : source.PixelWidth;
        int height = ActualHeight > 0 ? (int)Math.Ceiling(ActualHeight) : source.PixelHeight;
        SetPreparedBackdrop(PrepareBackdrop(source, width, height));
    }
    internal void SetPreparedBackdrop(LiquidLens.Frame frame)
    {
        if (!Transparency) { UseSolidFallback(); return; }
        backgroundLayer.Background = new ImageBrush(frame.Body) { Stretch = Stretch.Fill };
        backgroundLayer.Effect = new BlurEffect { Radius = liquid ? 7 : BlurRadius, RenderingBias = RenderingBias.Performance };
        edgeLayer.Background = frame.Rim == null ? null : new ImageBrush(frame.Rim) { Stretch = Stretch.Fill };
        UsesBackdrop = true; UsesEdgeRefraction = UsesFullRefraction = frame.Rim != null;
        ApplyMaterial(true);
    }
    internal void UseSolidFallback()
    {
        backgroundLayer.Background = edgeLayer.Background = null; backgroundLayer.Effect = null;
        UsesBackdrop = UsesEdgeRefraction = UsesFullRefraction = false;
        ApplyMaterial(false, solid: true);
    }
}
