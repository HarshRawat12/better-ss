using System;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace BetterSS;

internal sealed class VideoToolsWindow : Window
{
    private readonly VideoToolsManager manager;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock status;
    private readonly ProgressBar progress;
    private readonly Button download, cancel;
    private bool busy, finished;
    internal bool Accepted { get; private set; }
    internal Button DownloadButton => download;

    internal VideoToolsWindow(App app, VideoFeature? feature, VideoToolsManager? manager = null)
    {
        this.manager = manager ?? VideoToolsManager.Default;
        bool dark = UI.Dark(app.Settings), installed = this.manager.IsAvailable;
        string title = feature == VideoFeature.Recording ? "Enable screen recording" : feature == VideoFeature.Editing ? "Enable video editing" : "Download video tools";
        UI.SetupWindow(this, "Video tools", 540, 430, dark); MinWidth = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        var body = new StackPanel { Margin = new Thickness(26) };
        body.Children.Add(UI.PageHeader(title, installed ? "The shared video tools are already on this PC." : "Add video tools when you need recording or editing.", dark));
        var note = UI.Text(installed ? "Turn this feature on to continue. No download is needed." : "Recording and Better SS Split share one download. After installation, both can work offline. Your screenshots, OCR and color picker are ready now.", 13, UI.Ink(dark));
        note.Margin = new Thickness(0, 18, 0, 14); body.Children.Add(note);
        var details = UI.Text(installed ? "You can switch each video feature off in Preferences → Optional features." : "Downloads FFmpeg from Gyan's Windows builds (about 109 MB). The installed tools use about 201 MiB. The download size can change with new releases.", 12, UI.Muted(dark));
        details.Margin = new Thickness(0, 0, 0, 18); body.Children.Add(details);
        status = UI.Text(installed ? "Ready to enable." : "Download once. Keep using it offline.", 12, UI.Muted(dark)); status.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(status);
        progress = new ProgressBar { Height = 5, Visibility = Visibility.Collapsed, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 20) }; body.Children.Add(progress);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        cancel = UI.Button("Not now", () => { if (busy) { lifetime.Cancel(); cancel!.IsEnabled = false; status.Text = "Cancelling…"; } else Close(); }, false, dark); actions.Children.Add(cancel);
        download = UI.Button(installed ? "Enable and continue" : "Download and continue", Download, true, dark); download.IsDefault = true; actions.Children.Add(download); body.Children.Add(actions); Content = body;
        Closing += (_, e) => { if (busy && !finished) { e.Cancel = true; lifetime.Cancel(); cancel.IsEnabled = false; status.Text = "Cancelling…"; } };
        Closed += (_, _) => lifetime.Dispose();
    }
    private async void Download()
    {
        if (busy) return;
        busy = true; download.IsEnabled = false; cancel.Content = "Cancel download"; progress.Visibility = Visibility.Visible;
        var updates = new Progress<VideoToolsProgress>(value =>
        {
            if (!busy) return;
            status.Text = value.Message;
            if (value.Received > 0) status.Text += value.Total > 0 ? $" {value.Received / 1048576d:0.0} / {value.Total / 1048576d:0.0} MiB" : $" {value.Received / 1048576d:0.0} MiB";
            progress.IsIndeterminate = value.Total is null or <= 0;
            if (!progress.IsIndeterminate) progress.Value = Math.Clamp(value.Received * 100d / value.Total!.Value, 0, 100);
        });
        try
        {
            await manager.InstallAsync(updates, lifetime.Token);
            Accepted = true; finished = true; busy = false; Close();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { busy = false; Close(); }
        catch (Exception ex)
        {
            busy = false; progress.Visibility = Visibility.Collapsed; cancel.Content = "Not now"; cancel.IsEnabled = true; download.IsEnabled = true; download.Content = "Retry download";
            status.Text = ex is HttpRequestException ? "Couldn't download video tools. Check your connection and try again. Screenshots and other image tools are still available." : "Video tools weren't installed: " + ex.Message;
        }
    }
}
