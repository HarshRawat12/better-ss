using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BetterSS;

internal sealed class ColorPickerWindow : Window
{
    internal Color SelectedColor { get; private set; }
    private bool updating;
    internal static bool TryParseHex(string text, out Color color)
    {
        color = Colors.Black; var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value)) return false;
        color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value); return true;
    }
    internal ColorPickerWindow(Color current, bool dark)
    {
        SelectedColor = current; UI.SetupWindow(this, "Annotation color", 480, 620, dark); ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
        var body = new StackPanel { Margin = new Thickness(28) };
        body.Children.Add(UI.Text("Annotation color", 23, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text("Choose a swatch or adjust the RGB sliders.", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 8, 0, 20); body.Children.Add(note);
        var sample = new Border { Height = 52, Background = new SolidColorBrush(current), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 18) }; body.Children.Add(sample);
        var hexInput = UI.TextBox(dark); hexInput.MaxLength = 7; hexInput.Margin = new Thickness(0, 10, 0, 6);
        var validation = UI.Text("", 11, UI.Muted(dark)); validation.Margin = new Thickness(0, 0, 0, 12);
        var sliders = new List<Slider>(); var readouts = new List<TextBlock>();
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; var swatches = new Dictionary<Color, Button>();
        void Update(Color value, bool updateHex = true)
        {
            updating = true; SelectedColor = value; sample.Background = new SolidColorBrush(value);
            int[] channels = { value.R, value.G, value.B };
            for (int i = 0; i < sliders.Count; i++) { sliders[i].Value = channels[i]; readouts[i].Text = new[] { "Red", "Green", "Blue" }[i] + " · " + channels[i]; }
            if (updateHex) hexInput.Text = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            foreach (var entry in swatches) entry.Value.BorderThickness = new Thickness(entry.Key == value ? 3 : 1);
            validation.Text = ""; updating = false;
        }
        foreach (var value in new[] { "#000000", "#FFFFFF", "#808080", "#F04452", "#F28C28", "#FFD43B", "#36A866", "#21B8AD", "#3478F6", "#7957D5", "#D94B9B", "#885C43" })
        {
            TryParseHex(value, out var color); var selected = color; var button = UI.Button("", () => Update(selected), false, dark);
            button.Width = 58; button.Height = 36; button.Padding = new Thickness(4); button.Margin = new Thickness(0, 0, 8, 8); button.ToolTip = value;
            button.Content = new Rectangle { Fill = new SolidColorBrush(selected), Width = 44, Height = 22 }; swatches.Add(selected, button); presets.Children.Add(button);
        }
        body.Children.Add(presets);
        foreach (var name in new[] { "Red", "Green", "Blue" })
        {
            int index = sliders.Count; var readout = UI.Text(name, 12, UI.Ink(dark)); readouts.Add(readout); body.Children.Add(readout);
            var slider = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 10) }; UI.StyleSlider(slider, dark); sliders.Add(slider);
            slider.ValueChanged += (_, _) => { if (!updating) Update(Color.FromRgb((byte)sliders[0].Value, (byte)sliders[1].Value, (byte)sliders[2].Value)); };
            body.Children.Add(slider);
        }
        body.Children.Add(UI.Text("HEX COLOR", 10, UI.Muted(dark), FontWeights.SemiBold)); body.Children.Add(hexInput); body.Children.Add(validation);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(UI.Button("Cancel", () => DialogResult = false, false, dark));
        var apply = UI.Button("Use color", () => DialogResult = true, true, dark); buttons.Children.Add(apply); body.Children.Add(buttons);
        hexInput.TextChanged += (_, _) =>
        {
            if (updating) return;
            bool valid = TryParseHex(hexInput.Text, out var value); apply.IsEnabled = valid;
            if (valid) Update(value, false); else validation.Text = "Enter six hexadecimal digits, for example #3478F6.";
        };
        hexInput.TextChanged += (_, _) => { if (TryParseHex(hexInput.Text, out _)) apply.IsEnabled = true; };
        Content = body; Update(current);
    }
}
