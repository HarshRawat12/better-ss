using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace BetterSS;

internal static class UI
{
    internal static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    internal static bool Dark(Settings settings) => settings.Theme == "Dark" || (settings.Theme == "System" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int n && n == 0);
    internal static Brush Ink(bool dark) => Brush(dark ? "#EFEFEF" : "#181818");
    internal static Brush Muted(bool dark) => Brush(dark ? "#A0A0A0" : "#696969");
    internal static Brush Background(bool dark) => Brush(dark ? "#151515" : "#F6F6F6");
    internal static Brush Surface(bool dark) => Brush(dark ? "#202020" : "#FFFFFF");
    internal static Brush Line(bool dark) => Brush(dark ? "#383838" : "#DCDCDC");
    internal static void SetupWindow(Window window, string title, double width, double height, bool dark)
    {
        window.Title = "Better SS · " + title; window.Width = width; window.Height = height;
        window.Background = Background(dark); window.Foreground = Ink(dark); window.FontFamily = new FontFamily("Segoe UI");
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) => Native.SquareCorners(window, dark);
    }
    internal static TextBlock Text(string text, double size = 14, Brush? color = null, FontWeight? weight = null)
        => new() { Text = text, FontSize = size, Foreground = color ?? Ink(false), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    internal static Button Button(string text, Action action, bool primary = false, bool dark = false)
    {
        var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 12, BorderThickness = new Thickness(1), BorderBrush = Line(dark), UseLayoutRounding = true };
        const string xaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter Property="Opacity" Value="0.78"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.6"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """;
        button.Template = (ControlTemplate)XamlReader.Parse(xaml); Select(button, primary, dark);
        button.Click += (_, _) => action(); return button;
    }
    internal static void Select(Button button, bool selected, bool dark)
    { button.Background = selected ? Ink(dark) : Surface(dark); button.Foreground = selected ? Background(dark) : Ink(dark); }
    internal static Border Card(UIElement child, bool dark, Thickness? padding = null)
        => new() { Background = Surface(dark), BorderBrush = Line(dark), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(22), Margin = new Thickness(0, 0, 0, 18), Child = child };
    internal static TextBox TextBox(bool dark) => new() { Background = Surface(dark), Foreground = Ink(dark), BorderBrush = Line(dark), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 13, CaretBrush = Ink(dark), SelectionBrush = Brush(dark ? "#555555" : "#BBBBBB") };
    internal static void StyleSlider(Slider slider, bool dark)
    {
        slider.IsMoveToPointEnabled = true;
        const string xaml = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Grid Height="26" Background="Transparent">
            <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}">
              <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="2" Background="INK" VerticalAlignment="Center"/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Width="10" Height="18"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="INK"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Grid Background="Transparent"><Border Height="2" Background="TRACK" VerticalAlignment="Center"/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
        """;
        slider.Template = (ControlTemplate)XamlReader.Parse(xaml.Replace("INK", dark ? "#DDDDDD" : "#222222").Replace("TRACK", dark ? "#454545" : "#D0D0D0"));
    }
}
