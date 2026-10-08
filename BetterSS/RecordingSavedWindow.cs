using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BetterSS;

// Saving completes before this window opens. Editing is a separate, explicit action.
internal sealed class RecordingSavedWindow : Window
{
    internal RecordingSavedWindow(App app, string path, VideoExportSettings? settings)
    {
        bool dark = UI.Dark(app.Settings);
        UI.SetupWindow(this, "Recording saved", 520, 290, dark);
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(UI.PageHeader("Recording saved", "Your video is ready to use.", dark, "\uE73E"));
        var name = UI.Text(Path.GetFileName(path), 13, UI.Ink(dark), FontWeights.SemiBold);
        name.Margin = new Thickness(0, 16, 0, 6); body.Children.Add(name);
        var location = UI.Text(Path.GetDirectoryName(path) ?? path, 12, UI.Muted(dark));
        location.MaxHeight = 70; location.TextTrimming = TextTrimming.CharacterEllipsis; location.ToolTip = path; body.Children.Add(location);
        var note = UI.Text("Your video is ready. You can optionally trim or cut it in Better SS Split.", 12, UI.Muted(dark));
        note.Margin = new Thickness(0, 16, 0, 20); body.Children.Add(note);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var done = UI.Button("Done", Close, false, dark); done.IsCancel = true; actions.Children.Add(done);
        actions.Children.Add(UI.Button("Edit in Better SS Split", () => { new VideoEditorWindow(app, path, settings).Show(); Close(); }, true, dark));
        body.Children.Add(actions); Content = body;
    }
}
