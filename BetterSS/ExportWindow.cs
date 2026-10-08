using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BetterSS;

internal sealed class ExportWindow : Window
{
    internal ExportWindow(App app, BitmapSource image, string sourcePath)
    {
        var dark = UI.Dark(app.Settings); UI.SetupWindow(this, "Export screenshot", 560, 600, dark);
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
        var body = new StackPanel { Margin = new Thickness(28) }; body.Children.Add(UI.PageHeader("Export screenshot", "Choose a format and size for your finished image.", dark, "\uE74E"));
        var summary = UI.Text($"{image.PixelWidth:N0} × {image.PixelHeight:N0} pixels · flattened image", 12, UI.Muted(dark));
        var preview = new DockPanel(); var thumbnail = new Image { Source = image, Width = 72, Height = 52, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 16, 0) }; DockPanel.SetDock(thumbnail, Dock.Left); preview.Children.Add(thumbnail); preview.Children.Add(summary); body.Children.Add(UI.Card(preview, dark, new Thickness(16)));
        string format = app.Settings.ExportFormat, colorSpace = app.Settings.ExportColorSpace; int scale = 100, quality = app.Settings.JpegQuality;
        body.Children.Add(UI.Text("FORMAT", 10, UI.Muted(dark), FontWeights.SemiBold));
        var formatButtons = new WrapPanel();
        var choices = new System.Collections.Generic.Dictionary<string, Button>();
        var qualityLabel = UI.Text("JPEG quality", 13, UI.Ink(dark));
        var qualitySlider = new Slider { Minimum = 1, Maximum = 100, Value = quality, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 10, 0, 16) }; UI.StyleSlider(qualitySlider, dark);
        void Refresh() { foreach (var entry in choices) UI.Select(entry.Value, entry.Key == format, dark); qualitySlider.IsEnabled = format == "JPEG"; qualityLabel.Opacity = format == "JPEG" ? 1 : .45; }
        foreach (var f in ExportService.Formats) { var choice = f; var b = UI.Button(f, () => { format = choice; Refresh(); }, false, dark); UI.Segment(b, dark); choices.Add(f, b); formatButtons.Children.Add(b); }
        var formats = UI.Segmented(formatButtons, dark); formats.Margin = new Thickness(0, 8, 0, 16); body.Children.Add(formats);
        body.Children.Add(UI.Text("COLOR SPACE", 10, UI.Muted(dark), FontWeights.SemiBold));
        var colorButtons = new WrapPanel();
        var colorChoices = new System.Collections.Generic.Dictionary<string, Button>();
        void RefreshColor() { foreach (var entry in colorChoices) UI.Select(entry.Value, entry.Key == colorSpace, dark); }
        foreach (var space in new[] { "RGB", "CMYK" }) { var choice = space; var b = UI.Button(choice, () => { colorSpace = choice; RefreshColor(); }, false, dark); UI.Segment(b, dark); colorChoices.Add(space, b); colorButtons.Children.Add(b); }
        var colors = UI.Segmented(colorButtons, dark); colors.Margin = new Thickness(0, 8, 0, 16); body.Children.Add(colors); body.Children.Add(UI.Text("IMAGE SIZE", 10, UI.Muted(dark), FontWeights.SemiBold));
        var scaleButtons = new WrapPanel { Margin = new Thickness(0, 10, 0, 18) }; var sizes = new System.Collections.Generic.Dictionary<int, Button>();
        foreach (int percent in new[] { 25, 50, 75, 100, 150, 200 }) { int size = percent; var b = UI.Button(percent + "%", () => { scale = size; foreach (var e in sizes) UI.Select(e.Value, e.Key == size, dark); summary.Text = $"{Math.Round(image.PixelWidth * scale / 100.0):N0} × {Math.Round(image.PixelHeight * scale / 100.0):N0} pixels · flattened image"; }, false, dark); UI.Select(b, size == scale, dark); b.Padding = new Thickness(10, 9, 10, 9); sizes.Add(size, b); scaleButtons.Children.Add(b); }
        foreach (Button b in scaleButtons.Children) { UI.Segment(b, dark); UI.Select(b, (string)b.Content == scale + "%", dark); }
        scaleButtons.Margin = new Thickness(0); var scales = UI.Segmented(scaleButtons, dark); scales.Margin = new Thickness(0, 8, 0, 18); body.Children.Add(scales); body.Children.Add(qualityLabel); body.Children.Add(qualitySlider);
        qualitySlider.ValueChanged += (_, _) => { quality = (int)qualitySlider.Value; qualityLabel.Text = "JPEG quality · " + quality; };
        var status = UI.Text("PDF embeds your image at full quality. CMYK is preserved by JPEG and TIFF; the other formats export RGB.", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 0, 0, 20); body.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(UI.Button("Cancel", Close, false, dark)); var save = UI.Button("Choose location…", () => { }, true, dark); actions.Children.Add(save); body.Children.Add(actions);
        save.Click += async (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = $"{format} file|*.{ExportService.Extension(format)}", FileName = Path.GetFileNameWithoutExtension(sourcePath) + "." + ExportService.Extension(format), InitialDirectory = app.Settings.SaveFolder, AddExtension = true, DefaultExt = ExportService.Extension(format) };
            if (dialog.ShowDialog(this) != true) return;
            save.IsEnabled = false; status.Text = "Exporting…";
            try { var resized = ExportService.Resize(image, scale); await Task.Run(() => ExportService.Save(resized, dialog.FileName, format, quality, colorSpace)); app.Settings.ExportFormat = format; app.Settings.ExportColorSpace = colorSpace; app.Settings.JpegQuality = quality; app.Persist(); app.Toast("Screenshot saved"); Close(); }
            catch (Exception ex) { status.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = body; Refresh(); RefreshColor();
    }
}
