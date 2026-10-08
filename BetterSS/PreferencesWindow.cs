using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BetterSS;

internal sealed class PreferencesWindow : Window
{
    private readonly App app;
    private readonly Settings defaults = new();
    private readonly Dictionary<string, Button> navigation = new();
    private StackPanel body = new();
    private bool dark;
    private string page = "Capture";
    internal PreferencesWindow(App app)
    {
        this.app = app; UI.SetupWindow(this, "Preferences", 940, 740, UI.Dark(app.Settings)); MinWidth = 820; MinHeight = 600; Build();
    }
    private void Build()
    {
        dark = UI.Dark(app.Settings); UI.ApplyTheme(this, dark);
        var root = new Grid { Background = UI.Background(dark) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(196) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new DockPanel { Margin = new Thickness(16, 24, 16, 20) };
        var brand = new StackPanel { Margin = new Thickness(8, 0, 0, 28) }; var mark = UI.Mark(38); mark.HorizontalAlignment = HorizontalAlignment.Left; mark.Margin = new Thickness(0, 0, 0, 12); brand.Children.Add(mark); brand.Children.Add(UI.Text("Better SS", 18, UI.Ink(dark), FontWeights.SemiBold));
        var tagline = UI.Text("Preferences", 11, UI.Muted(dark)); tagline.Margin = new Thickness(0, 5, 0, 0); brand.Children.Add(tagline); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var footer = new StackPanel(); footer.Children.Add(UI.Text(app.DevelopmentMode ? "Live development" : "Ready in your tray", 11, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text("Close preferences to keep capturing. Quit from the tray.", 11, UI.Muted(dark)); note.LineHeight = 17; note.Margin = new Thickness(0, 10, 0, 24); footer.Children.Add(note); footer.Children.Add(UI.Text("Version " + typeof(App).Assembly.GetName().Version?.ToString(3), 10, UI.Muted(dark))); var credit = UI.Text("By Harsh Rawat", 10, UI.Muted(dark)); credit.Margin = new Thickness(0, 4, 0, 0); footer.Children.Add(credit); DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
        var nav = new StackPanel(); navigation.Clear();
        foreach (var name in new[] { "Capture", "Floating preview", "Appearance", "Sound", "Storage", "Startup & guide" }) { string selected = name; var button = UI.Button(name, () => ShowPage(selected), false, dark); UI.Navigation(button, dark); button.Margin = new Thickness(0, 0, 0, 5); UI.Icon(button, name switch { "Capture" => "\uE722", "Floating preview" => "\uE8A7", "Appearance" => "\uE790", "Sound" => "\uE767", "Storage" => "\uE8B7", _ => "\uE946" }); navigation.Add(name, button); nav.Children.Add(button); }
        side.Children.Add(nav); root.Children.Add(new Border { Background = UI.Sidebar(dark), BorderBrush = UI.Line(dark), BorderThickness = new Thickness(0, 0, 1, 0), Child = side });
        body = new StackPanel { Margin = new Thickness(28, 24, 28, 24) }; var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetColumn(scroll, 1); root.Children.Add(scroll); Content = root; ShowPage(page);
    }
    internal void ShowPage(string name)
    {
        page = name; body.Children.Clear(); foreach (var entry in navigation) UI.Select(entry.Value, entry.Key == name, dark);
        var descriptions = new Dictionary<string, string> { ["Capture"] = "Capture a moment. Keep moving.", ["Floating preview"] = "A temporary home for your screenshot.", ["Appearance"] = "Neutral surfaces, clear controls, and a theme that fits your desktop.", ["Sound"] = "Your capture sound, at the right level.", ["Storage"] = "Local files. Flexible exports.", ["Startup & guide"] = "Ready when you need it. Always your choice." };
        body.Children.Add(UI.PageHeader(page, descriptions[name], dark));
        switch (name) { case "Capture": CapturePage(); break; case "Floating preview": PreviewPage(); break; case "Appearance": AppearancePage(); break; case "Sound": SoundPage(); break; case "Startup & guide": StartupPage(); break; default: StoragePage(); break; }
    }
    private void CapturePage()
    {
        var panel = new StackPanel(); Heading(panel, "Choose what to capture", "Every capture is automatically copied. A quiet confirmation appears only after the clipboard is ready.");
        var modes = new WrapPanel();
        foreach (var mode in Enum.GetValues<CaptureMode>()) { var choice = mode; var button = UI.Button(CaptureSession.Label(mode), () => app.BeginCapture(choice), false, dark); UI.Segment(button, dark); UI.Select(button, mode == CaptureMode.Region, dark); modes.Children.Add(button); }
        panel.Children.Add(UI.Segmented(modes, dark)); var detail = UI.Text("Window opens a searchable list, including minimized windows.", 12, UI.Muted(dark)); detail.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(detail); body.Children.Add(UI.Card(panel, dark));
        var video = new StackPanel(); Heading(video, "Video recording", "Record a display or application, then preview, cut and export your video. The video cutter has one track with split, trim, delete and mute controls."); var videoActions = new WrapPanel(); videoActions.Children.Add(UI.Button("Record video…", app.ShowVideoRecording, true, dark)); videoActions.Children.Add(UI.Button("Cut video…", app.ShowVideoEditor, false, dark)); video.Children.Add(videoActions); body.Children.Add(UI.Card(video, dark));
        var options = new StackPanel(); Heading(options, "Launch controls", "Use a hotkey or double-click the tray icon. Capture controls work entirely with the mouse.");
        Toggle(options, "Use Windows + Shift + S for Better SS", app.Settings.UseWindowsCaptureShortcut, defaults.UseWindowsCaptureShortcut, v =>
        { if (!app.TrySetWindowsCaptureShortcut(v, out var problem)) app.Notify("Shortcut unchanged", problem); ShowPage(page); });
        var takeoverNote = UI.Text("While this is on and Better SS is running, Windows + Shift + S opens Better SS. Turn it off to restore Snipping Tool and use your saved shortcut below. Quitting Better SS also releases the Windows shortcut.", 12, UI.Muted(dark)); takeoverNote.Margin = new Thickness(0, 0, 0, 18); options.Children.Add(takeoverNote);
        Choice(options, "Launch shortcut", new[] { "Ctrl + Shift + S", "Alt + Ctrl + S", "Ctrl + Shift + F8", "Disabled" }, app.Settings.Hotkey, defaults.Hotkey, v => { if (!app.TrySetHotkey(v, out var error)) app.HotkeyStatus = error; });
        var custom = UI.Button("Record custom shortcut…", () => { new HotkeyWindow(app) { Owner = this }.ShowDialog(); ShowPage(page); }, false, dark);
        custom.HorizontalAlignment = HorizontalAlignment.Left; custom.Margin = new Thickness(0, 0, 0, 14); options.Children.Add(custom);
        var status = UI.Text(app.HotkeyStatus, 11, UI.Muted(dark)); status.Margin = new Thickness(0, 0, 0, 16); options.Children.Add(status);
        Choice(options, "Capture delay", new[] { "Off", "3 seconds", "5 seconds", "10 seconds" }, app.Settings.Delay == 0 ? "Off" : app.Settings.Delay + " seconds", "Off", v => app.Settings.Delay = v == "Off" ? 0 : int.Parse(v.Split(' ')[0])); body.Children.Add(UI.Card(options, dark));
        body.Children.Add(UI.Text("In the editor: Copy image, Extract text, highlighter, blur brush, mosaic, and six export formats.", 12, UI.Muted(dark)));
    }
    private void PreviewPage()
    {
        var panel = new StackPanel(); Heading(panel, "Ready to drag", "Drag into another app or folder. Click to edit. The action strip offers Edit, Text, Export, Pin, and Dismiss.");
        Slider(panel, "Time on screen", 2, 30, app.Settings.Duration, defaults.Duration, "s", v => app.Settings.Duration = v);
        Slider(panel, "Preview width", 220, 440, app.Settings.PreviewWidth, defaults.PreviewWidth, "px", v => app.Settings.PreviewWidth = v);
        body.Children.Add(UI.Card(panel, dark)); body.Children.Add(UI.Button("Test floating preview", app.DemoPreview, true, dark));
        var animation = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        Heading(animation, "Capture animation", "The captured display or selected area flashes, then shrinks into its preview. Starts immediately after capture.");
        Toggle(animation, "Animate screenshots", app.Settings.CaptureAnimation, defaults.CaptureAnimation, v => app.Settings.CaptureAnimation = v);
        Slider(animation, "Animation duration", 160, 700, app.Settings.AnimationDuration, defaults.AnimationDuration, "ms", v => app.Settings.AnimationDuration = v);
        Slider(animation, "Flash strength", 0, 100, app.Settings.FlashOpacity, defaults.FlashOpacity, "%", v => app.Settings.FlashOpacity = v);
        body.Children.Add(UI.Card(animation, dark));
        var note = UI.Text("Hovering pauses dismissal. Up to three previews stack on their capture display. The image is already copied, so the preview has no copy button.", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 22, 0, 0); body.Children.Add(note);
    }
    private void AppearancePage()
    {
        var panel = new StackPanel(); Heading(panel, "Interface", "Soft neutral surfaces, rounded controls, and blue accents for actions and selections. Choose a theme and adjust your floating preview.");
        var themes = new WrapPanel(); foreach (string mode in new[] { "System", "Light", "Dark" }) { string selected = mode; var button = UI.Button(mode, () => { app.Settings.Theme = selected; app.Persist(); Build(); }, false, dark); UI.Segment(button, dark); UI.Select(button, app.Settings.Theme == mode, dark); themes.Children.Add(button); } var themeRow = UI.Segmented(themes, dark); themeRow.Margin = new Thickness(0, 0, 0, 14); panel.Children.Add(themeRow);
        var glass = UI.Text("Liquid glass adapts to the background. Turn off transparency in Windows settings for solid surfaces.", 12, UI.Muted(dark)); glass.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(glass);
        Slider(panel, "Preview shadow", 0, 48, app.Settings.Shadow, defaults.Shadow, "px", v => app.Settings.Shadow = v);
        Slider(panel, "Preview border", 0, 3, app.Settings.Stroke, defaults.Stroke, "px", v => app.Settings.Stroke = v);
        body.Children.Add(UI.Card(panel, dark)); body.Children.Add(UI.Button("Preview appearance", app.DemoPreview, true, dark));
    }
    private void SoundPage()
    {
        var panel = new StackPanel(); Heading(panel, "Capture sound", "A Windows system sound plays by default. Choose an audio file to customize it.");
        Toggle(panel, "Play sound after capture", app.Settings.SoundEnabled, defaults.SoundEnabled, v => app.Settings.SoundEnabled = v);
        Slider(panel, "Volume", 0, 100, app.Settings.Volume * 100, defaults.Volume * 100, "%", v => app.Settings.Volume = v / 100);
        SoundChoice(panel);
        var actions = new WrapPanel(); actions.Children.Add(UI.Button("Play sound", app.PlaySound, true, dark)); actions.Children.Add(UI.Button("Choose file…", () => { var dialog = new OpenFileDialog { Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a" }; if (dialog.ShowDialog(this) == true) { app.Settings.SoundPath = dialog.FileName; app.Persist(); ShowPage(page); } }, false, dark)); panel.Children.Add(actions); body.Children.Add(UI.Card(panel, dark));
    }
    private void SoundChoice(StackPanel panel)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 16) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(UI.Text("Sound file", 13, UI.Ink(dark)));
        var current = SoundCatalog.SelectedFileName(app.Settings.SoundPath);
        var menu = UI.Menu(dark);
        var button = UI.Button(current + "    ▾", () => menu.IsOpen = true, false, dark); button.Margin = new Thickness(0); menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        foreach (var fileName in new[] { SoundCatalog.SystemSoundName }.Concat(SoundCatalog.AvailableBuiltInFileNames))
        {
            var selected = fileName;
            var item = new MenuItem { Header = selected, IsCheckable = true, IsChecked = selected.Equals(current, StringComparison.OrdinalIgnoreCase), Padding = new Thickness(10, 8, 10, 8) };
            item.Click += (_, _) => { menu.IsOpen = false; app.Settings.SoundPath = SoundCatalog.SettingValue(selected); app.Persist(); ShowPage(page); };
            menu.Items.Add(item);
        }
        Grid.SetColumn(button, 1); row.Children.Add(button);
        var reset = ResetButton("Sound file", () => app.Settings.SoundPath = defaults.SoundPath); Grid.SetColumn(reset, 2); row.Children.Add(reset);
        panel.Children.Add(row);
        var selectedName = UI.Text(current, 12, UI.Muted(dark)); selectedName.Margin = new Thickness(0, -8, 0, 14); panel.Children.Add(selectedName);
    }
    private void StoragePage()
    {
        var panel = new StackPanel(); Heading(panel, "Original captures", "Originals are stored as lossless PNGs. Use Export to choose another format or size.");
        Toggle(panel, "Automatically save screenshots", app.Settings.AutoSave, defaults.AutoSave, v => app.Settings.AutoSave = v);
        var location = UI.Text(app.Settings.SaveFolder, 12, UI.Muted(dark)); location.Margin = new Thickness(0, 12, 0, 20); panel.Children.Add(location);
        var actions = new WrapPanel(); actions.Children.Add(UI.Button("Choose folder…", () => { using var dialog = new Forms.FolderBrowserDialog { SelectedPath = app.Settings.SaveFolder }; if (dialog.ShowDialog() == Forms.DialogResult.OK) { app.Settings.SaveFolder = dialog.SelectedPath; app.Persist(); ShowPage(page); } }, false, dark)); actions.Children.Add(UI.Button("Open folder", () => Native.OpenFolder(app.Settings.SaveFolder), false, dark)); actions.Children.Add(ResetButton("Save folder", () => app.Settings.SaveFolder = defaults.SaveFolder)); panel.Children.Add(actions); body.Children.Add(UI.Card(panel, dark));
        var export = new StackPanel(); Heading(export, "Export defaults", "PNG, JPEG, PDF, TIFF, BMP, and GIF. Export also offers resizing and RGB/CMYK output."); Choice(export, "Preferred format", ExportService.Formats, app.Settings.ExportFormat, defaults.ExportFormat, v => app.Settings.ExportFormat = v); Choice(export, "Color space", new[] { "RGB", "CMYK" }, app.Settings.ExportColorSpace, defaults.ExportColorSpace, v => app.Settings.ExportColorSpace = v);
        Slider(export, "JPEG quality", 1, 100, app.Settings.JpegQuality, defaults.JpegQuality, "%", v => app.Settings.JpegQuality = (int)v); body.Children.Add(UI.Card(export, dark));
        body.Children.Add(UI.Text("With automatic saving off, local drag copies remain in DragCache. OCR runs on this PC. Nothing is uploaded.", 12, UI.Muted(dark))); var cache = UI.Button("Open drag cache", () => Native.OpenFolder(Path.Combine(Settings.DataFolder, "DragCache")), false, dark); cache.Margin = new Thickness(0, 18, 0, 0); body.Children.Add(cache);
    }
    private void Heading(StackPanel panel, string title, string description)
    { panel.Children.Add(UI.Text(title, 16, UI.Ink(dark), FontWeights.SemiBold)); var note = UI.Text(description, 12, UI.Muted(dark)); note.Margin = new Thickness(0, 6, 0, 18); panel.Children.Add(note); }
    private void StartupPage()
    {
        var panel = new StackPanel(); Heading(panel, "Start with Windows", "Optional startup for your Windows account. Better SS opens quietly in the tray when you sign in.");
        Toggle(panel, "Start when I sign in", app.Settings.StartOnLogin, defaults.StartOnLogin, v =>
        { if (!app.SetStartupEnabled(v, out var error)) app.Notify("Startup unchanged", error); ShowPage(page); });
        panel.Children.Add(UI.Text("Keep this executable in its current folder. If you move it, turn startup off and on here to update its location. Windows Task Manager can also disable startup apps.", 12, UI.Muted(dark))); body.Children.Add(UI.Card(panel, dark));
        var guide = new StackPanel(); Heading(guide, "Make yourself at home", "Replay the walkthrough: assign a shortcut, take a real screenshot, learn to drag it, find Edit, Text and Pin, then adjust preview size and duration.");
        guide.Children.Add(UI.Button("Open walkthrough", app.ShowGuide, true, dark)); body.Children.Add(UI.Card(guide, dark));
        body.Children.Add(UI.Text("To keep the tray icon visible: open the hidden-icons arrow (^) beside the clock and drag Better SS into the visible tray. You can also use Windows taskbar settings.", 12, UI.Muted(dark)));
        var taskbar = UI.Button("Open Windows taskbar settings", () => { try { StartupService.OpenTaskbarSettings(); } catch (Exception ex) { app.Notify("Taskbar settings unavailable", ex.Message); } }, false, dark); taskbar.Margin = new Thickness(0, 18, 0, 0); body.Children.Add(taskbar);
    }
    private Button ResetButton(string title, Action restore)
    {
        var button = UI.Button("Reset", () => { restore(); app.Persist(); Build(); }, false, dark);
        UI.Quiet(button, dark);
        button.FontSize = 11; button.Padding = new Thickness(8, 5, 8, 5); button.Margin = new Thickness(10, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center; button.ToolTip = "Reset " + title.ToLowerInvariant() + " to default";
        System.Windows.Automation.AutomationProperties.SetName(button, "Reset " + title + " to default");
        return button;
    }
    private void Choice(StackPanel panel, string title, string[] values, string current, string defaultValue, Action<string> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 16) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(UI.Text(title, 13, UI.Ink(dark)));
        var menu = UI.Menu(dark);
        var button = UI.Button(current + "    ▾", () => menu.IsOpen = true, false, dark); button.Margin = new Thickness(0); menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        foreach (string option in values) { string selected = option; var item = new MenuItem { Header = option, IsCheckable = true, IsChecked = option == current, Padding = new Thickness(10, 8, 10, 8) }; item.Click += (_, _) => { menu.IsOpen = false; changed(selected); app.Persist(); Build(); }; menu.Items.Add(item); }
        Grid.SetColumn(button, 1); row.Children.Add(button);
        var reset = ResetButton(title, () => changed(defaultValue)); Grid.SetColumn(reset, 2); row.Children.Add(reset); panel.Children.Add(row);
    }
    private void Toggle(StackPanel panel, string title, bool value, bool defaultValue, Action<bool> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 12) };
        var reset = ResetButton(title, () => changed(defaultValue)); DockPanel.SetDock(reset, Dock.Right); row.Children.Add(reset);
        var toggle = UI.Switch(title, value, dark);
        toggle.Click += (_, _) => { changed(toggle.IsChecked == true); app.Persist(); }; row.Children.Add(toggle); panel.Children.Add(row);
    }
    private void Slider(StackPanel panel, string title, double min, double max, double value, double defaultValue, string unit, Action<double> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        var reset = ResetButton(title, () => changed(defaultValue)); DockPanel.SetDock(reset, Dock.Right); row.Children.Add(reset);
        var label = UI.Text(Math.Round(value) + " " + unit, 12, UI.Muted(dark)); DockPanel.SetDock(label, Dock.Right); row.Children.Add(label); row.Children.Add(UI.Text(title, 13, UI.Ink(dark))); panel.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 0, 16) }; UI.StyleSlider(slider, dark);
        slider.ValueChanged += (_, _) => { changed(slider.Value); label.Text = Math.Round(slider.Value) + " " + unit; };
        slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent, new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => app.Persist())); slider.PreviewMouseLeftButtonUp += (_, _) => app.Persist(); slider.KeyUp += (_, _) => app.Persist(); panel.Children.Add(slider);
    }
}
