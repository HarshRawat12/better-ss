using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BetterSS;

internal static class InteractionTests
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] internal MouseInput Mouse; [FieldOffset(0)] internal KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputUnion Value; }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);

    internal static void Run(App app, BitmapSource sample, string path, Action<bool, string> check)
    {
        var originalPointer = Native.CursorPosition;
        var previousWindow = WindowCatalog.GetForegroundWindow();
        PreviewWindow? preview = null;
        Window? anchor = null;
        try
        {
            anchor = new Window { Title = "Better SS input test", Width = 240, Height = 140, Topmost = true, Background = Brushes.White, Content = new Border { Background = Brushes.White } };
            anchor.Show(); Pump(160); FocusTestWindow(anchor);
            var editor = new EditorWindow(app, sample, path) { Topmost = true }; editor.Show(); editor.Activate(); Pump(180);
            var red = Find<Button>(editor).Single(b => b.Content is string text && text == "Red");
            Click(red, editor);
            check(editor.SelectedColor == Color.FromRgb(240, 68, 82), "mouse click selects red annotation color");
            check(((SolidColorBrush)red.Background).Color == ((SolidColorBrush)UI.Ink(UI.Dark(app.Settings))).Color, "selected color button has visible selection feedback");
            editor.Begin(new Point(100, 100)); editor.Move(new Point(300, 100)); Await(editor.EndAsync());
            var pixel = new byte[4]; new FormatConvertedBitmap(editor.Render(), PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(180, 100, 1, 1), pixel, 4, 0);
            check(pixel[2] == 240 && pixel[1] == 68 && pixel[0] == 82, "selected red ink appears in exported pixels");
            var custom = Color.FromRgb(18, 171, 205); editor.SelectColor(custom);
            editor.Begin(new Point(100, 140)); editor.Move(new Point(300, 140)); Await(editor.EndAsync());
            new FormatConvertedBitmap(editor.Render(), PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(180, 140, 1, 1), pixel, 4, 0);
            check(pixel[2] == 18 && pixel[1] == 171 && pixel[0] == 205, "custom RGB ink survives rendering"); editor.Close();
            check(ColorPickerWindow.TryParseHex("#12ABCD", out var parsed) && parsed == custom && !ColorPickerWindow.TryParseHex("#ZZZZZZ", out _), "color picker accepts hex colors and rejects invalid input");
            var picker = new ColorPickerWindow(Colors.Black, UI.Dark(app.Settings)) { Topmost = true }; picker.Show(); picker.Activate(); Pump(180);
            var blue = Find<Button>(picker).Single(b => b.ToolTip as string == "#3478F6"); Click(blue, picker);
            check(picker.SelectedColor == Color.FromRgb(52, 120, 246), "color picker swatches respond to mouse clicks"); picker.Close();

            anchor.Activate();
            preview = new PreviewWindow(app, sample, path, System.Windows.Forms.Screen.FromPoint(originalPointer)); preview.Show(); Pump(240);
            var hwnd = new WindowInteropHelper(preview).Handle;
            var row = (StackPanel)preview.Content; var actions = (Border)row.Children[0]; var picture = (Border)row.Children[1];
            Move(picture.PointToScreen(new Point(picture.ActualWidth / 2, picture.ActualHeight / 2)), hwnd); Pump(80);
            var bridge = picture.PointToScreen(new Point(-5, picture.ActualHeight / 2));
            Move(bridge, hwnd); Pump(450);
            check(actions.IsHitTestVisible && actions.Opacity == 1, "hover actions remain available while crossing and pausing in the gap");
            var pin = Find<Button>(preview).Single(b => b.Tag as string == "Pin");
            Click(pin, preview);
            check(pin.Tag as string == "Unpin", "first mouse click operates Pin on a non-activating preview");
            check(WindowCatalog.GetForegroundWindow() != hwnd, "preview action does not steal foreground focus");
            Click(pin, preview); check(pin.Tag as string == "Pin", "second mouse click unpins the preview");
            Click(Find<Button>(preview).Single(b => b.Tag as string == "Export"), preview);
            var export = app.Windows.OfType<ExportWindow>().Single(); check(export.IsVisible, "hover Export action opens export window"); export.Close();
            Click(Find<Button>(preview).Single(b => b.Tag as string == "Text"), preview);
            var textWindow = app.Windows.OfType<OcrWindow>().Single(); check(textWindow.IsVisible, "hover Text action opens OCR window"); textWindow.Close();
            Click(Find<Button>(preview).Single(b => b.Tag as string == "Edit"), preview);
            var edit = app.Windows.OfType<EditorWindow>().Single(); check(edit.IsVisible, "hover Edit action opens editor"); edit.Close(); Pump(220);
            preview = new PreviewWindow(app, sample, path, System.Windows.Forms.Screen.FromPoint(originalPointer)); preview.Show(); Pump(240);
            var image = (Border)((StackPanel)preview.Content).Children[1];
            Move(image.PointToScreen(new Point(image.ActualWidth / 2, image.ActualHeight / 2)), new WindowInteropHelper(preview).Handle); Pump(80);
            Click(Find<Button>(preview).Single(b => b.Tag as string == "Dismiss"), preview); Pump(220);
            check(!preview.IsVisible, "hover Dismiss action closes preview");
            TestResets(app, check);
            TestSetup(app, check);
            Await(TestDragAsync(app, sample, path, check));
        }
        finally
        {
            preview?.Close(); anchor?.Close(); SetCursorPos(originalPointer.X, originalPointer.Y);
            if (WindowCatalog.IsWindow(previousWindow)) WindowCatalog.SetForegroundWindow(previousWindow);
        }
    }
    internal static void FocusTestWindow(Window window)
    {
        var point = Native.CursorPosition;
        try { Click((FrameworkElement)window.Content, window); Pump(120); }
        finally { SetCursorPos(point.X, point.Y); }
    }
    private static void TestResets(App app, Action<bool, string> check)
    {
        var original = app.Settings; var originalPath = app.SettingsPathOverride; var originalStartup = app.ConfigureStartup;
        PreferencesWindow? preferences = null;
        try
        {
            app.SettingsPathOverride = System.IO.Path.GetFullPath("test-results/reset-settings.json");
            app.ConfigureStartup = _ => { };
            app.Settings = new Settings
            {
                Hotkey = "Disabled", Delay = 10, Duration = 25, PreviewWidth = 410, Theme = "Dark",
                Shadow = 40, Stroke = 3, SoundEnabled = false, Volume = .2, SoundPath = "custom.mp3",
                AutoSave = false, SaveFolder = System.IO.Path.GetFullPath("test-results"), ExportFormat = "PDF", JpegQuality = 42, StartOnLogin = true
            };
            var cases = new[]
            {
                ("Capture", "Launch shortcut", "Hotkey"), ("Capture", "Capture delay", "Delay"),
                ("Floating preview", "Time on screen", "Duration"), ("Floating preview", "Preview width", "PreviewWidth"),
                ("Appearance", "Theme", "Theme"), ("Appearance", "Preview shadow", "Shadow"), ("Appearance", "Preview border", "Stroke"),
                ("Sound", "Play sound after capture", "SoundEnabled"), ("Sound", "Volume", "Volume"), ("Sound", "Sound file", "SoundPath"),
                ("Storage", "Automatically save screenshots", "AutoSave"), ("Storage", "Save folder", "SaveFolder"),
                ("Storage", "Preferred format", "ExportFormat"), ("Storage", "JPEG quality", "JpegQuality"), ("Startup & guide", "Start when I sign in", "StartOnLogin")
            };
            var properties = typeof(Settings).GetProperties().Where(p => p.CanWrite).ToArray();
            var defaults = new Settings();
            preferences = new PreferencesWindow(app) { Topmost = true }; preferences.Show(); preferences.Activate(); Pump(180);
            foreach (var (page, title, property) in cases)
            {
                preferences.ShowPage(page); preferences.UpdateLayout(); Pump(80);
                var button = Find<Button>(preferences).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Reset " + title + " to default");
                button.BringIntoView(); Pump(80);
                var before = properties.ToDictionary(p => p.Name, p => p.GetValue(app.Settings));
                Click(button, preferences);
                var setting = properties.Single(p => p.Name == property);
                check(Equals(setting.GetValue(app.Settings), setting.GetValue(defaults)), title + " reset responds to mouse and restores default");
                check(properties.Where(p => p.Name != property).All(p => Equals(p.GetValue(app.Settings), before[p.Name])), title + " reset leaves other settings unchanged");
                var saved = Settings.Load(app.SettingsPathOverride);
                check(properties.All(p => Equals(p.GetValue(saved), p.GetValue(app.Settings))), title + " reset persists to disk");
            }
            check(properties.All(p => Equals(p.GetValue(app.Settings), p.GetValue(defaults))), "every configurable preference has a working default reset");
        }
        finally { preferences?.Close(); app.Settings = original; app.SettingsPathOverride = originalPath; app.ConfigureStartup = originalStartup; }
    }

    private static void TestSetup(App app, Action<bool, string> check)
    {
        var original = app.Settings; var originalPath = app.SettingsPathOverride; var originalStartup = app.ConfigureStartup; var originalHotkeys = app.Hotkeys;
        WelcomeWindow? guide = null; HotkeyWindow? recorder = null;
        using var competitor = new HotkeyService(() => { });
        int fires = 0; using var service = new HotkeyService(() => fires++);
        try
        {
            app.Settings = new Settings { Hotkey = "Disabled" }; app.SettingsPathOverride = System.IO.Path.GetFullPath("test-results/setup-settings.json");
            app.Hotkeys = service; int startupWrites = 0; bool requestedStartup = false;
            app.ConfigureStartup = v => { startupWrites++; requestedStartup = v; };
            check(!new Settings().IntroductionSeen && !new Settings().StartOnLogin, "fresh profiles show the guide and leave startup off");
            string oldProfile = System.IO.Path.GetFullPath("test-results/legacy-settings.json");
            System.IO.File.WriteAllText(oldProfile, "{\"Hotkey\":\"Alt + Ctrl + S\",\"Theme\":\"Dark\"}");
            var migrated = Settings.Load(oldProfile);
            check(migrated.IntroductionSeen && !migrated.StartOnLogin && migrated.Theme == "Dark" && migrated.Hotkey == "Alt + Ctrl + S", "upgrades preserve preferences and do not force onboarding or startup");
            app.Settings.Save(app.SettingsPathOverride);
            check(!Settings.Load(app.SettingsPathOverride).IntroductionSeen, "unfinished fresh setup remains eligible for the first-run guide");
            check(HotkeyBinding.TryParse("Alt + Ctrl + S", out var legacy, out _) && legacy!.Label == "Ctrl + Alt + S", "legacy hotkey presets migrate to general shortcut bindings");
            check(!HotkeyBinding.TryParse("Shift + A", out _, out _) && !HotkeyBinding.TryParse("Ctrl + F12", out _, out _) && !HotkeyBinding.TryParse("broken", out _, out _), "invalid and reserved hotkeys are rejected");
            string? occupied = null, available = null;
            foreach (var key in new[] { "F6", "F7", "F9", "F10", "F11", "F8" })
            {
                string value = "Ctrl + Alt + Shift + " + key;
                if (occupied == null && competitor.TrySet(value, out _)) occupied = value;
                else if (occupied != null && service.TrySet(value, out _)) { available = value; break; }
            }
            check(occupied != null && available != null, "test shortcut combinations register with Windows");
            check(!service.TrySet(occupied!, out _) && service.Current == available, "conflicting hotkey change preserves working registration");
            service.TrySet("Disabled", out _);
            recorder = new HotkeyWindow(app) { Topmost = true }; recorder.Show(); recorder.Activate(); Pump(180);
            Chord(occupied!, recorder);
            Render(recorder, "shortcut-recorder.png");
            check(recorder.Candidate == occupied && app.Settings.Hotkey == "Disabled", "recorder captures an occupied chord without saving it");
            Click(Find<Button>(recorder).Single(b => b.Content as string == "Use shortcut"), recorder);
            check(recorder.IsVisible && !recorder.Saved && app.Settings.Hotkey == "Disabled", "confirmation reports shortcut conflict without changing settings");
            Click(Find<Button>(recorder).Single(b => b.Content as string == "Record again"), recorder);
            Chord(available!, recorder);
            Click(Find<Button>(recorder).Single(b => b.Content as string == "Use shortcut"), recorder);
            check(recorder.Saved && app.Settings.Hotkey == available && Settings.Load(app.SettingsPathOverride).Hotkey == available, "confirmed recorded hotkey registers and persists");
            var anchor = new Window { Title = "Better SS shortcut test", Width = 300, Height = 200, Topmost = true, Content = new Border { Background = Brushes.White } };
            try { anchor.Show(); anchor.Activate(); Pump(160); Chord(available!, anchor); check(fires == 1, "custom hotkey triggers capture callback through Windows"); }
            finally { anchor.Close(); }
            recorder = new HotkeyWindow(app) { Topmost = true }; recorder.Show(); recorder.Activate(); Pump(180);
            Chord(available!, recorder); Click(Find<Button>(recorder).Single(b => b.Content as string == "Cancel"), recorder);
            check(app.Settings.Hotkey == available && service.Current == available && fires == 1, "cancelling recording restores shortcut without capturing");

            guide = new WelcomeWindow(app) { Topmost = true }; guide.Show(); guide.Activate(); Pump(150);
            for (int step = 0; step < 3; step++) { check(guide.Step == step, "guide step " + (step + 1) + " opens"); Render(guide, "guide-" + (step + 1) + ".png"); Click(Find<Button>(guide).Single(b => b.Content as string == "Next"), guide); }
            Render(guide, "guide-4.png");
            check(guide.Step == 3 && Find<CheckBox>(guide).Single().IsChecked == false && startupWrites == 0, "first-run startup prompt is unchecked and makes no registry changes");
            Click(Find<Button>(guide).Single(b => b.Content as string == "Finish setup"), guide);
            check(app.Settings.IntroductionSeen && Settings.Load(app.SettingsPathOverride).IntroductionSeen && startupWrites == 0, "finishing guide records completion without enabling startup");
            guide = new WelcomeWindow(app) { Topmost = true }; guide.Show(); guide.ShowStep(3); guide.Activate(); Pump(150);
            Click(Find<CheckBox>(guide).Single(), guide);
            check(startupWrites == 0, "startup selection waits for explicit Finish setup confirmation");
            Click(Find<Button>(guide).Single(b => b.Content as string == "Finish setup"), guide);
            check(startupWrites == 1 && requestedStartup && app.Settings.StartOnLogin, "guide enables startup only after opt-in confirmation");
            app.ConfigureStartup = _ => throw new InvalidOperationException("test denial");
            check(!app.SetStartupEnabled(false, out _) && app.Settings.StartOnLogin, "startup write failure leaves prior preference unchanged");
            string keyPath = @"Software\Better SS\SelfTest-" + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath);
                StartupService.WriteSetting(key, true, @"C:\Test Folder\Better SS.exe");
                check(key.GetValue("Better SS") as string == "\"C:\\Test Folder\\Better SS.exe\" --background", "startup entry quotes spaced executable path and requests quiet launch");
                StartupService.WriteSetting(key, false, @"C:\Test Folder\Better SS.exe");
                check(key.GetValue("Better SS") == null, "disabling startup removes only Better SS's registry value");
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(keyPath, false); }
        }
        finally
        {
            recorder?.Close(); guide?.Close(); app.Hotkeys = originalHotkeys; app.Settings = original; app.SettingsPathOverride = originalPath; app.ConfigureStartup = originalStartup;
        }
    }
    private static void Chord(string shortcut, Window target)
    {
        if (!HotkeyBinding.TryParse(shortcut, out var binding, out _) || binding == null) throw new InvalidOperationException("Invalid test chord.");
        if (WindowCatalog.GetForegroundWindow() != new WindowInteropHelper(target).Handle) throw new InvalidOperationException("Keyboard test stopped: test window is not foreground.");
        var keys = new System.Collections.Generic.List<ushort>();
        if ((binding.Modifiers & 2) != 0) keys.Add(0x11);
        if ((binding.Modifiers & 1) != 0) keys.Add(0x12);
        if ((binding.Modifiers & 4) != 0) keys.Add(0x10);
        keys.Add((ushort)binding.KeyCode);
        try { foreach (var key in keys) { SendKey(key, false); Pump(25); } }
        finally { foreach (var key in keys.AsEnumerable().Reverse()) { SendKey(key, true); Pump(25); } }
        Pump(80);
    }
    private static void SendKey(ushort key, bool up)
    {
        var input = new Input { Type = 1, Value = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Keyboard input could not be sent.");
    }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); Native.SavePng(bitmap, System.IO.Path.GetFullPath(System.IO.Path.Combine("test-results", name)));
    }

    private static async Task TestDragAsync(App app, BitmapSource sample, string path, Action<bool, string> check)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(Native.CursorPosition);
        var other = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName != screen.DeviceName) ?? screen;
        var target = new Window { Title = "Better SS drop test", Width = 420, Height = 320, Topmost = true, AllowDrop = true, Background = Brushes.White };
        PreviewWindow? source = null; bool delivered = false;
        target.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        target.Drop += (_, e) =>
        {
            delivered = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 && files[0] == path && e.Data.GetDataPresent(DataFormats.Bitmap);
            e.Effects = DragDropEffects.Copy; e.Handled = true;
        };
        try
        {
            target.Show(); target.Activate();
            Native.Place(target, new System.Drawing.Rectangle(other.WorkingArea.Left + 50, other.WorkingArea.Top + 50, 420, 320));
            var targetHandle = new WindowInteropHelper(target).Handle;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                target.AllowDrop = attempt == 0;
                source = new PreviewWindow(app, sample, path, screen); source.Show(); await Task.Delay(240);
                var row = (StackPanel)source.Content; var picture = (Border)row.Children[1];
                var grab = picture.PointToScreen(new Point(picture.ActualWidth * .4, picture.ActualHeight * .4));
                Move(grab, new WindowInteropHelper(source).Handle); SendMouse(2); await Task.Delay(60);
                SetCursorPos((int)grab.X - 20, (int)grab.Y - 20); await Task.Delay(300);
                var ghost = app.Windows.OfType<DragPreviewWindow>().Single();
                check(ghost.IsVisible && Math.Abs(ghost.PreviewScale - .9) < .001 && row.Opacity == 0, "drag animates to 90 percent size and lifts the whole screenshot");
                Native.GetWindowRect(new WindowInteropHelper(ghost).Handle, out var firstBounds);
                var destination = target.PointToScreen(new Point(190, 130));
                Move(destination, targetHandle); await Task.Delay(120);
                Native.GetWindowRect(new WindowInteropHelper(ghost).Handle, out var movedBounds);
                check(Math.Abs((movedBounds.Left - firstBounds.Left) - (destination.X - (grab.X - 20))) < 3 &&
                      Math.Abs((movedBounds.Top - firstBounds.Top) - (destination.Y - (grab.Y - 20))) < 3,
                    "drag image tracks physical cursor coordinates across the desktop");
                check(WindowFromPoint(new Native.Point { X = (int)destination.X, Y = (int)destination.Y }) == targetHandle,
                    "drag image is click-through so the destination remains reachable");
                if (attempt == 2) { SendMouse(8); await Task.Delay(80); SendMouse(16); }
                SendMouse(4); await Task.Delay(300);
                check(!app.Windows.OfType<DragPreviewWindow>().Any(), "drag visual is removed after drop or cancellation");
                if (attempt == 0) check(delivered && !source.IsVisible, "native drop delivers file and bitmap and dismisses source");
                else check(source.IsVisible && row.Opacity == 1, attempt == 1 ? "rejected drop restores original preview" : "right-click cancels drag without a keyboard and restores preview");
                source.Close(); source = null;
            }
        }
        finally
        {
            SendMouse(4); SendMouse(16);
            foreach (var ghost in app.Windows.OfType<DragPreviewWindow>().ToArray()) ghost.Close();
            source?.Close(); target.Close();
        }
    }
    private static void SendMouse(uint flags)
    {
        var input = new Input { Type = 0, Value = new InputUnion { Mouse = new MouseInput { Flags = flags } } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Mouse input could not be sent.");
    }
    private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var descendant in Find<T>(child)) yield return descendant; }
    }
    private static void Move(Point point, IntPtr expected)
    {
        SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)); Pump(70);
        var p = new Native.Point { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) };
        var actual = WindowFromPoint(p);
        if (actual != expected && GetAncestor(actual, 2) != expected)
        {
            Native.GetWindowRect(expected, out var bounds); Native.GetWindowThreadProcessId(actual, out uint pid);
            throw new InvalidOperationException($"Mouse target is not the test window; input test stopped before clicking. Target ({p.X},{p.Y}), bounds {bounds.Bounds}, target belongs to test process: {pid == Environment.ProcessId}.");
        }
    }
    private static void Click(FrameworkElement target, Window window)
    {
        window.UpdateLayout();
        Move(target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2)), new WindowInteropHelper(window).Handle);
        // Cursor warps can leave WPF's cached over-element stale after a window changes.
        Mouse.Synchronize(); Pump(40);
        var down = new Input { Type = 0, Value = new InputUnion { Mouse = new MouseInput { Flags = 2 } } };
        var up = new Input { Type = 0, Value = new InputUnion { Mouse = new MouseInput { Flags = 4 } } };
        if (SendInput(1, new[] { down }, Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Mouse input could not be sent.");
        Pump(60);
        bool pressed = target is not Button button || button.IsPressed;
        var pointer = Mouse.GetPosition(target); var native = Native.CursorPosition;
        SendInput(1, new[] { up }, Marshal.SizeOf<Input>()); Pump(120);
        if (!pressed) throw new InvalidOperationException($"Test mouse-down did not press '{(target as Button)?.Content}'. WPF cursor ({pointer.X:0},{pointer.Y:0}) within target {target.ActualWidth:0}×{target.ActualHeight:0}; native cursor ({native.X},{native.Y}); foreground is target: {WindowCatalog.GetForegroundWindow() == new WindowInteropHelper(window).Handle}.");
    }
    private static void Pump(int milliseconds) => Await(Task.Delay(milliseconds));
    private static void Await(Task task)
    {
        if (!task.IsCompleted) { var dispatcher = Dispatcher.CurrentDispatcher; var frame = new DispatcherFrame(); task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default); Dispatcher.PushFrame(frame); }
        task.GetAwaiter().GetResult();
    }
}
