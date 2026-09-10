using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterSS;

internal sealed class HotkeyWindow : Window
{
    private readonly App app;
    private readonly TextBlock keys, message;
    private readonly Button record, confirm;
    private string? candidate;
    private ShortcutRecorder? recorder;
    internal bool Recording { get; private set; }
    internal bool Saved { get; private set; }
    internal string? Candidate => candidate;

    internal HotkeyWindow(App app)
    {
        this.app = app; bool dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Record a shortcut", 580, 420, dark); ResizeMode = ResizeMode.NoResize;
        var root = new StackPanel { Margin = new Thickness(30) };
        root.Children.Add(UI.Text("Your capture shortcut", 24, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text("Press Ctrl or Alt plus any supported key. Add Shift if you like. Release the keys, then confirm. Mouse-only capture stays available in the tray.", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 12, 0, 20); root.Children.Add(note);
        keys = UI.Text("", 22, UI.Ink(dark), FontWeights.SemiBold); keys.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(UI.Card(keys, dark, new Thickness(20)));
        message = UI.Text("", 12, UI.Muted(dark)); message.MinHeight = 44; root.Children.Add(message);
        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        record = UI.Button("Record again", StartRecording, false, dark); actions.Children.Add(record);
        actions.Children.Add(UI.Button("Cancel", Close, false, dark));
        confirm = UI.Button("Use shortcut", Confirm, true, dark); confirm.IsEnabled = false; actions.Children.Add(confirm);
        root.Children.Add(actions); Content = root;
        Loaded += (_, _) =>
        {
            try { recorder = new ShortcutRecorder(new System.Windows.Interop.WindowInteropHelper(this).Handle, () => Recording, RecordKey); StartRecording(); }
            catch (System.Exception ex) { message.Text = "Couldn't start keyboard recording: " + ex.Message; record.IsEnabled = false; }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!Recording) return;
            e.Handled = true; if (e.IsRepeat) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Recording = false; keys.Text = "Recording stopped"; message.Text = "Click Record again to try another combination."; return; }
            RecordKey(key, Keyboard.Modifiers);
        };
        Closed += (_, _) => { recorder?.Dispose(); app.ResumeHotkey(); };
    }
    private void StartRecording()
    {
        app.Hotkeys?.Suspend(); Recording = true; candidate = null; confirm.IsEnabled = false;
        keys.Text = "Listening…"; message.Text = "Press your shortcut now. Escape stops recording.";
        Focus();
    }
    internal void RecordKey(Key key, ModifierKeys modifiers)
    {
        if (!Recording) return;
        if (key == Key.Escape) { Recording = false; keys.Text = "Recording stopped"; message.Text = "Click Record again to try another combination."; return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
        if (!HotkeyBinding.TryCreate(key, modifiers, out var binding, out var error)) { message.Text = error; return; }
        candidate = binding!.Label; keys.Text = candidate; Recording = false; confirm.IsEnabled = true;
        message.Text = "Ready to confirm. Your current shortcut has not been changed.";
    }
    private void Confirm()
    {
        if (candidate == null) return;
        if (!app.TrySetHotkey(candidate, out var error)) { message.Text = error; return; }
        Saved = true; Close();
    }
}
