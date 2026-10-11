using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed record ScreenColorValues(string Hex, string Rgb, string Hsl, string Cmyk)
{
    internal static ScreenColorValues FromColor(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min, lightness = (max + min) / 2;
        double hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        double black = 1 - max;
        // Generic RGB -> CMYK, with a dedicated pure-black case to avoid 0/0.
        double cyan = max == 0 ? 0 : (max - r) / max;
        double magenta = max == 0 ? 0 : (max - g) / max;
        double yellow = max == 0 ? 0 : (max - b) / max;
        return new($"#{color.R:X2}{color.G:X2}{color.B:X2}", $"rgb({color.R}, {color.G}, {color.B})",
            FormattableString.Invariant($"hsl({hue:0.#}, {saturation * 100:0.#}%, {lightness * 100:0.#}%)"),
            FormattableString.Invariant($"cmyk({cyan * 100:0.#}%, {magenta * 100:0.#}%, {yellow * 100:0.#}%, {black * 100:0.#}%)"));
    }
}

// Screen sampling results. Kept separate from the annotation-color editor.
internal sealed class ScreenColorWindow : Window
{
    private readonly App app;
    private readonly bool dark;
    private readonly Border sample;
    private readonly TextBlock hexTitle, status;
    private readonly StackPanel history = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, TextBox> fields = new();
    private Forms.Screen screen;
    internal Color SelectedColor { get; private set; }

    internal ScreenColorWindow(App app, Color color, Forms.Screen screen, bool copied)
    {
        this.app = app; this.screen = screen; dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Color picker", 448, 450, dark);
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
        ShowInTaskbar = false; Topmost = true;
        var body = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var pick = UI.Button("Pick new", () => app.BeginCapture(CaptureMode.Region, CaptureIntent.ColorPicker), false, dark);
        UI.Symbol(pick, "M15 3 L21 9 M18 2 L22 6 L18 10 L14 6 Z M14 6 L4 16 L3 21 L8 20 L18 10 M6 14 L10 18", false);
        pick.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(pick, Dock.Right); heading.Children.Add(pick);
        heading.Children.Add(UI.Text("Picked color", 18, UI.Ink(dark), FontWeights.SemiBold)); body.Children.Add(heading);

        var hero = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        sample = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(12), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 16, 0) };
        DockPanel.SetDock(sample, Dock.Left); hero.Children.Add(sample);
        var description = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hexTitle = UI.Text("", 24, UI.Ink(dark), FontWeights.SemiBold); description.Children.Add(hexTitle);
        var sub = UI.Text("Screen color", 11, UI.Muted(dark)); sub.Margin = new Thickness(0, 3, 0, 0); description.Children.Add(sub); hero.Children.Add(description); body.Children.Add(hero);

        body.Children.Add(UI.Text("RECENT PICKS", 10, UI.Muted(dark), FontWeights.SemiBold));
        body.Children.Add(new ScrollViewer { Content = history, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 8, 0, 16), MaxHeight = 58 });
        foreach (string format in new[] { "HEX", "RGB", "HSL", "CMYK" })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(UI.Text(format, 10, UI.Muted(dark), FontWeights.SemiBold));
            var field = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = UI.Ink(dark), FontSize = 12, Padding = new Thickness(0, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(field, format + " value"); fields.Add(format, field); Grid.SetColumn(field, 1); row.Children.Add(field);
            var copy = UI.Button("Copy " + format, () => Copy(format), false, dark); UI.Icon(copy, "\uE8C8", true);
            copy.Width = copy.Height = 32; copy.Padding = new Thickness(6); copy.Margin = new Thickness(8, 0, 0, 0); UI.Quiet(copy, dark);
            Grid.SetColumn(copy, 2); row.Children.Add(copy);
            var card = new Border { Background = UI.Surface(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(.5), CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 6, 8, 6), Child = row, Margin = new Thickness(0, 0, 0, 8) };
            // RGB does not identify a printer/ink profile; keep that distinction discoverable.
            if (format == "CMYK") card.ToolTip = "CMYK is an RGB conversion estimate. Printed colors depend on the printer and ICC profile.";
            body.Children.Add(card);
        }
        status = UI.Text("", 11, UI.Muted(dark)); status.Margin = new Thickness(0, 4, 0, 0); body.Children.Add(status);
        Content = body;
        Loaded += (_, _) => Position(); KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        ShowColor(color, screen, copied);
    }

    internal void ShowColor(Color color, Forms.Screen screen, bool copied)
    {
        this.screen = screen; UpdateValues(color);
        history.Children.Clear();
        foreach (string hex in app.Settings.RecentColors)
        {
            if (!ColorPickerWindow.TryParseHex(hex, out var previous)) continue;
            var swatch = UI.Button("Recent color " + hex, () => SelectHistory(previous), false, dark);
            UI.Swatch(swatch, previous); swatch.Width = swatch.Height = 32; swatch.Padding = new Thickness(5); swatch.Margin = new Thickness(0, 0, 5, 0); swatch.ToolTip = hex;
            UI.Select(swatch, previous == color, dark); history.Children.Add(swatch);
        }
        status.Text = copied ? "HEX copied to clipboard" : "Clipboard busy · use a copy button to try again";
        if (IsLoaded) { UpdateLayout(); Position(); }
    }

    private void UpdateValues(Color color)
    {
        SelectedColor = color; var values = ScreenColorValues.FromColor(color);
        sample.Background = new SolidColorBrush(color); hexTitle.Text = values.Hex;
        fields["HEX"].Text = values.Hex; fields["RGB"].Text = values.Rgb;
        fields["HSL"].Text = values.Hsl; fields["CMYK"].Text = values.Cmyk;
    }
    private async void SelectHistory(Color color)
    {
        bool copied = await ClipboardService.TextAsync(ScreenColorValues.FromColor(color).Hex);
        ShowColor(color, screen, copied);
    }
    private async void Copy(string format)
    {
        string value = fields[format].Text;
        bool copied = await ClipboardService.TextAsync(value);
        status.Text = copied ? format + " copied to clipboard" : "Clipboard busy · try again";
    }
    private void Position()
    {
        Native.MoveToMonitor(this, screen); UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = screen.WorkingArea;
        Native.Place(this, new System.Drawing.Rectangle(area.Left + Math.Max(0, (area.Width - width) / 2), area.Top + Math.Max(0, (area.Height - height) / 2), width, height));
    }
}
