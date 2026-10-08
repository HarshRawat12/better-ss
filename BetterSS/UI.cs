using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace BetterSS;

// Shared presentation tokens and templates for the desktop utility.
internal static class UI
{
    internal static bool? HighContrastOverride { get; set; }
    internal static bool HighContrast => HighContrastOverride ?? SystemParameters.HighContrast;
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    internal static bool Dark(Settings settings) => settings.Theme == "Dark" || (settings.Theme == "System" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int n && n == 0);
    internal static Brush Ink(bool dark) => HighContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#F5F5F7" : "#1D1D1F");
    internal static Brush Muted(bool dark) => HighContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#AEAEB5" : "#68686F");
    internal static Brush Background(bool dark) => HighContrast ? SystemColors.WindowBrush : Brush(dark ? "#1C1C1E" : "#F5F5F7");
    internal static Brush Surface(bool dark) => HighContrast ? SystemColors.WindowBrush : Brush(dark ? "#2C2C2E" : "#FFFFFF");
    internal static Brush Sidebar(bool dark) => HighContrast ? SystemColors.WindowBrush : Brush(dark ? "#232326" : "#EEEEF2");
    internal static Brush Line(bool dark) => HighContrast ? SystemColors.WindowTextBrush : Brush(dark ? "#404045" : "#E0E0E6");
    internal static Brush Accent(bool dark) => HighContrast ? SystemColors.HighlightBrush : Brush(dark ? "#64B5FF" : "#0066CC");
    internal static Brush Selected(bool dark) => HighContrast ? SystemColors.HighlightBrush : Brush(dark ? "#173C62" : "#E5F0FC");
    internal static Brush FloatingSurface(bool dark) => GlassSurface.Transparency ? Brush(dark ? "#F72A2A2D" : "#F7FFFFFF") : Surface(dark);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    internal static void WindowChrome(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        int rounded = HighContrast ? 0 : 2, mode = dark && !HighContrast ? 1 : 0;
        DwmSetWindowAttribute(handle, 33, ref rounded, 4);
        DwmSetWindowAttribute(handle, 20, ref mode, 4);
        int caption = dark ? 0x1E1C1C : 0xF7F5F5, text = dark ? 0xF7F5F5 : 0x1F1D1D;
        if (HighContrast) caption = text = -1; // Restore the OS caption palette.
        DwmSetWindowAttribute(handle, 35, ref caption, 4);
        DwmSetWindowAttribute(handle, 36, ref text, 4);
    }
    internal static void SetupWindow(Window window, string title, double width, double height, bool dark)
    {
        window.Title = "Better SS · " + title; window.Width = width; window.Height = height;
        window.FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); window.FontSize = 13;
        ApplyTheme(window, dark);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) => WindowChrome(window, dark);
        window.Loaded += (_, _) => { if (title is not "Editor" and not "Better SS Split" and not "Recording controls" && window.Content is FrameworkElement content) Motion.Enter(content); };
    }
    internal static void ApplyTheme(Window window, bool dark)
    {
        window.Background = Background(dark); window.Foreground = Ink(dark);
        window.Resources[typeof(CheckBox)] = CheckBoxStyle(dark);
        window.Resources[SystemColors.HighlightBrushKey] = Selected(dark);
        window.Resources[SystemColors.HighlightTextBrushKey] = Ink(dark);
        window.Resources[typeof(ToolTip)] = new Style(typeof(ToolTip))
        {
            Setters = { new Setter(Control.BackgroundProperty, Surface(dark)), new Setter(Control.ForegroundProperty, Ink(dark)), new Setter(Control.BorderBrushProperty, Line(dark)), new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)), new Setter(Control.FontSizeProperty, 12.0) }
        };
        if (window.IsLoaded) WindowChrome(window, dark);
    }
    internal static TextBlock Text(string text, double size = 14, Brush? color = null, FontWeight? weight = null)
        => new() { Text = text, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), FontSize = size, Foreground = color ?? Ink(false), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    private static ControlTemplate ButtonTemplate(string glyph = "", bool iconOnly = false, string? swatch = null)
    {
        const string xaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Grid Background="Transparent"><Grid x:Name="MotionRoot" RenderTransformOrigin="0.5,0.5">
            <Grid.RenderTransform><ScaleTransform x:Name="PressScale"/></Grid.RenderTransform>
            <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" Padding="{TemplateBinding Padding}">
              <Grid HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                ICONCONTENT
                <ContentPresenter Grid.Column="1" Visibility="LABELVISIBLE" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
            </Border>
            <Border x:Name="Hover" CornerRadius="9" Background="#207F7F7F" IsHitTestVisible="False" Opacity="0"/>
            <Border x:Name="SelectionMark" Width="3" Height="12" HorizontalAlignment="Left" Margin="3,0,0,0" CornerRadius="1.5" Background="{TemplateBinding Foreground}" IsHitTestVisible="False" Visibility="Collapsed"/>
            <Border x:Name="Focus" CornerRadius="9" Margin="-2" BorderBrush="#0071E3" BorderThickness="2" IsHitTestVisible="False" Visibility="Collapsed"/>
          </Grid></Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Focus" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """;
        string icon = swatch != null
            ? $"<Border Width=\"16\" Height=\"16\" CornerRadius=\"8\" Background=\"{swatch}\" BorderBrush=\"#929299\" BorderThickness=\"0.5\" Margin=\"{(iconOnly ? "0" : "0,0,6,0")}\" VerticalAlignment=\"Center\"/>"
            : $"<TextBlock Text=\"{glyph}\" FontFamily=\"Segoe MDL2 Assets\" FontSize=\"14\" Margin=\"{(iconOnly ? "0" : "0,0,8,0")}\" VerticalAlignment=\"Center\" Visibility=\"{(glyph.Length == 0 ? "Collapsed" : "Visible")}\"/>";
        return (ControlTemplate)XamlReader.Parse(xaml.Replace("ICONCONTENT", icon).Replace("LABELVISIBLE", iconOnly ? "Collapsed" : "Visible"));
    }
    internal static Button Button(string text, Action action, bool primary = false, bool dark = false)
    {
        var button = new Button { Content = text, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"), HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 12, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, BorderThickness = new Thickness(.75), BorderBrush = primary ? Brushes.Transparent : Line(dark), Background = primary ? Brush("#0071E3") : Surface(dark), Foreground = primary ? Brushes.White : Ink(dark), UseLayoutRounding = true, FocusVisualStyle = null, Template = ButtonTemplate() };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        AttachFeedback(button);
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(button, 450);
        System.Windows.Controls.ToolTipService.SetBetweenShowDelay(button, 80);
        if (HighContrast) { button.Background = primary ? SystemColors.HighlightBrush : SystemColors.WindowBrush; button.Foreground = primary ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush; }
        button.Click += (_, _) => action(); return button;
    }
    internal static void Icon(Button button, string glyph, bool iconOnly = false)
    {
        button.Template = ButtonTemplate(glyph, iconOnly);
        button.ToolTip ??= button.Content?.ToString();
    }
    internal static void Swatch(Button button, Color color)
        => button.Template = ButtonTemplate(iconOnly: true, swatch: $"#{color.R:X2}{color.G:X2}{color.B:X2}");
    internal static void Select(Button button, bool selected, bool dark)
    {
        var state = selection.GetOrCreateValue(button); state.Selected = selected;
        button.Background = selected ? Selected(dark) : Surface(dark);
        button.Foreground = selected && HighContrast ? SystemColors.HighlightTextBrush : selected ? Accent(dark) : Ink(dark);
        button.BorderBrush = selected ? Accent(dark) : Line(dark);
        if (!HighContrast && state.Role == "navigation") { button.Background = selected ? Brush("#0071E3") : Brushes.Transparent; button.Foreground = selected ? Brushes.White : Ink(dark); button.BorderBrush = Brushes.Transparent; }
        if (!HighContrast && state.Role == "segment") { button.Background = selected ? Surface(dark) : Brushes.Transparent; button.Foreground = selected ? Ink(dark) : Muted(dark); button.BorderBrush = selected ? Line(dark) : Brushes.Transparent; }
        if (!HighContrast && state.Role == "glass") { button.Background = selected ? Brush(dark ? "#38FFFFFF" : "#A0FFFFFF") : Brushes.Transparent; button.Foreground = Ink(dark); button.BorderBrush = selected ? Brush("#60FFFFFF") : Brushes.Transparent; }
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        if (button.Template.FindName("SelectionMark", button) is Border mark)
        {
            mark.Visibility = Visibility.Visible;
            Motion.To(mark, UIElement.OpacityProperty, selected ? 1 : 0, Motion.Fast, completed: () => { if (!selection.GetOrCreateValue(button).Selected) mark.Visibility = Visibility.Collapsed; });
        }
    }
    private sealed class Selection { internal bool Selected; internal string Role = ""; }
    private static readonly ConditionalWeakTable<Button, Selection> selection = new();
    private static void AttachFeedback(Button button)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, typeof(Button));
        void Update(object? sender, EventArgs args)
        {
            if (button.Template.FindName("PressScale", button) is not ScaleTransform scale) return;
            double value = button.IsPressed && !Motion.Reduced ? .98 : 1;
            if (button.IsPressed || Motion.Reduced) { Motion.Snap(scale, ScaleTransform.ScaleXProperty, value); Motion.Snap(scale, ScaleTransform.ScaleYProperty, value); }
            else { Motion.To(scale, ScaleTransform.ScaleXProperty, value, Motion.Standard, true); Motion.To(scale, ScaleTransform.ScaleYProperty, value, Motion.Standard, true); }
            if (button.Template.FindName("Hover", button) is Border hover)
                Motion.To(hover, UIElement.OpacityProperty, button.IsPressed ? 1 : button.IsMouseOver ? .5 : 0, button.IsPressed || button.IsMouseOver ? 0 : Motion.Fast);
        }
        button.Loaded += (_, _) =>
        {
            descriptor.AddValueChanged(button, Update); Update(button, EventArgs.Empty);
            if (HighContrast && button.Template.FindName("Focus", button) is Border focus) focus.BorderBrush = SystemColors.HighlightBrush;
            if (selection.TryGetValue(button, out var state) && button.Template.FindName("SelectionMark", button) is Border mark) mark.Visibility = state.Selected ? Visibility.Visible : Visibility.Collapsed;
        };
        button.Unloaded += (_, _) => descriptor.RemoveValueChanged(button, Update);
        button.MouseEnter += Update; button.MouseLeave += Update;
    }
    internal static GlassSurface Floating(UIElement content, bool dark, Thickness padding, double radius = 12)
        => new(content, dark, padding, radius);
    internal static GlassSurface Liquid(UIElement content, bool dark, Thickness padding, double radius = 18)
        => new(content, dark, padding, radius, liquidGlass: true);
    internal static void Navigation(Button button, bool dark)
    { selection.GetOrCreateValue(button).Role = "navigation"; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(12, 9, 12, 9); Select(button, false, dark); }
    internal static void Segment(Button button, bool dark)
    { selection.GetOrCreateValue(button).Role = "segment"; button.Margin = new Thickness(2); button.Padding = new Thickness(10, 6, 10, 6); Select(button, false, dark); }
    internal static void Quiet(Button button, bool dark)
    { if (!HighContrast) { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.Foreground = Ink(dark); } }
    internal static void GlassButton(Button button, bool dark, bool selected = false)
    { selection.GetOrCreateValue(button).Role = "glass"; Select(button, selected, dark); }
    internal static Border Segmented(UIElement content, bool dark)
        => new() { Child = content, Background = Sidebar(dark), CornerRadius = new CornerRadius(11), Padding = new Thickness(2), BorderBrush = Line(dark), BorderThickness = new Thickness(.5) };
    internal static Border Mark(double size = 36)
    {
        UIElement image;
        try { using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); var source = Imaging.CreateBitmapSourceFromHIcon(icon!.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight((int)size, (int)size)); source.Freeze(); image = new Image { Source = source }; }
        catch { image = Text("▣", size * .6, Brushes.White, FontWeights.SemiBold); }
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * .22), Child = image };
    }
    internal static StackPanel PageHeader(string title, string description, bool dark, string glyph = "")
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        if (glyph.Length > 0) panel.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(13), Background = Selected(dark), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16), Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Foreground = Accent(dark), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        panel.Children.Add(Text(title, 22, Ink(dark), FontWeights.SemiBold));
        if (description.Length > 0) { var subtitle = Text(description, 12, Muted(dark)); subtitle.LineHeight = 19; subtitle.Margin = new Thickness(0, 6, 0, 0); panel.Children.Add(subtitle); }
        return panel;
    }
    internal static CheckBox Switch(string label, bool value, bool dark)
    {
        var toggle = new CheckBox { Content = label, IsChecked = value, Foreground = Ink(dark), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch, FocusVisualStyle = null };
        System.Windows.Automation.AutomationProperties.SetName(toggle, label);
        if (HighContrast) return toggle;
        toggle.Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
          <Grid Background="Transparent" MinHeight="30"><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <ContentPresenter VerticalAlignment="Center" Margin="0,0,20,0" RecognizesAccessKey="True"/>
            <Border x:Name="Track" Grid.Column="1" Width="36" Height="22" CornerRadius="11" Background="TRACK" BorderBrush="LINE" BorderThickness=".75" VerticalAlignment="Center">
              <Border x:Name="Knob" Width="17" Height="17" CornerRadius="8.5" Background="White" HorizontalAlignment="Left" Margin="2"/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True"><Setter TargetName="Track" Property="Background" Value="#0071E3"/><Setter TargetName="Track" Property="BorderBrush" Value="#0071E3"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Track" Property="BorderBrush" Value="#409CFF"/><Setter TargetName="Track" Property="BorderThickness" Value="2"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".42"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """.Replace("TRACK", dark ? "#62626A" : "#D5D5DB").Replace("LINE", dark ? "#707078" : "#C9C9D0"));
        return toggle;
    }
    internal static Border Card(UIElement child, bool dark, Thickness? padding = null)
        => new() { Background = Surface(dark), BorderBrush = Line(dark), BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(14), Padding = padding ?? new Thickness(22), Margin = new Thickness(0, 0, 0, 18), Child = child };
    internal static void RoundImage(Border picture)
    {
        picture.CornerRadius = new CornerRadius(8);
        if (picture.Child is FrameworkElement visual)
        {
            void Clip() => visual.Clip = new RectangleGeometry(new Rect(0, 0, visual.ActualWidth, visual.ActualHeight), 7, 7);
            visual.SizeChanged += (_, _) => Clip(); visual.Loaded += (_, _) => Clip();
        }
    }
    internal static TextBox TextBox(bool dark)
    {
        var field = new TextBox { Background = Surface(dark), Foreground = Ink(dark), BorderBrush = Line(dark), BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), FontSize = 13, CaretBrush = Ink(dark), SelectionBrush = Selected(dark), SelectionOpacity = 1, FocusVisualStyle = null };
        field.Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
          <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
            <ScrollViewer x:Name="PART_ContentHost"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Chrome" Property="BorderBrush" Value="#929299"/></Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Chrome" Property="BorderBrush" Value="#0071E3"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
        if (HighContrast) field.Template = null;
        return field;
    }
    private static Style CheckBoxStyle(bool dark)
    {
        if (HighContrast) return new Style(typeof(CheckBox)) { Setters = { new Setter(Control.ForegroundProperty, SystemColors.WindowTextBrush), new Setter(Control.FontSizeProperty, 13.0) } };
        var template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
          <Grid Background="Transparent">
            <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <Border x:Name="Box" Width="16" Height="16" CornerRadius="4" Background="SURFACE" BorderBrush="LINE" BorderThickness="1" VerticalAlignment="Center">
              <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="10" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
            </Border>
            <ContentPresenter Grid.Column="1" Margin="8,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#0071E3"/></Trigger>
            <Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="#0071E3"/><Setter TargetName="Box" Property="BorderBrush" Value="#0071E3"/><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsChecked" Value="{x:Null}"><Setter TargetName="Box" Property="Background" Value="#0071E3"/><Setter TargetName="Check" Property="Text" Value="−"/><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="#409CFF"/><Setter TargetName="Box" Property="BorderThickness" Value="2"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """.Replace("SURFACE", dark ? "#2A2A2D" : "#FFFFFF").Replace("LINE", dark ? "#777780" : "#929299"));
        return new Style(typeof(CheckBox)) { Setters = { new Setter(Control.TemplateProperty, template), new Setter(Control.ForegroundProperty, Ink(dark)), new Setter(Control.FontSizeProperty, 13.0), new Setter(Control.FocusVisualStyleProperty, null) } };
    }
    internal static ContextMenu Menu(bool dark)
    {
        var menu = new ContextMenu { Background = Surface(dark), Foreground = Ink(dark), BorderBrush = Line(dark), BorderThickness = new Thickness(1), FontSize = 12, Padding = new Thickness(4) };
        menu.Resources[SystemColors.HighlightBrushKey] = Selected(dark);
        menu.Resources[SystemColors.HighlightTextBrushKey] = Ink(dark);
        menu.Template = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ContextMenu">
          <Border x:Name="MenuSurface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8" Padding="{TemplateBinding Padding}"><StackPanel IsItemsHost="True"/></Border>
        </ControlTemplate>
        """);
        menu.Opened += (_, _) =>
        {
            if (menu.Template.FindName("MenuSurface", menu) is not FrameworkElement surface) return;
            var origin = new Point(0, 0);
            if (menu.PlacementTarget is FrameworkElement trigger && trigger.IsLoaded && surface.ActualWidth > 0)
            {
                var anchor = trigger.PointToScreen(new Point(trigger.ActualWidth / 2, trigger.ActualHeight / 2)); var position = surface.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(surface);
                origin = new Point(Math.Clamp((anchor.X - position.X) / (surface.ActualWidth * dpi.DpiScaleX), 0, 1), position.Y < anchor.Y ? 1 : 0);
            }
            Motion.Enter(surface, Motion.Standard, origin);
        };
        return menu;
    }
    internal static void StyleSlider(Slider slider, bool dark)
    {
        slider.IsMoveToPointEnabled = true;
        if (HighContrast) return;
        const string xaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Grid Height="26" Background="Transparent">
            <Border x:Name="Focus" CornerRadius="6" BorderBrush="#0071E3" BorderThickness="1" Visibility="Collapsed" IsHitTestVisible="False"/>
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="3" CornerRadius="1.5" Background="#0071E3" VerticalAlignment="Center"/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="16" Height="16"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border x:Name="Knob" CornerRadius="8" Background="THUMB" BorderBrush="LINE" BorderThickness="1"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Knob" Property="BorderBrush" Value="#0071E3"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="Knob" Property="Background" Value="#C9E2FC"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="3" CornerRadius="1.5" Background="TRACK" VerticalAlignment="Center"/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="Focus" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.42"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """;
        slider.FocusVisualStyle = null;
        slider.Template = (ControlTemplate)XamlReader.Parse(xaml.Replace("THUMB", dark ? "#E5E5EA" : "#FFFFFF").Replace("LINE", dark ? "#8C8C93" : "#B8B8C0").Replace("TRACK", dark ? "#515158" : "#D7D7DE"));
    }
}

