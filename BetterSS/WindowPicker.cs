using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal sealed record WindowEntry(IntPtr Handle, string Title, string Application, bool Minimized);

internal static class WindowCatalog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int length);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    internal static List<WindowEntry> Enumerate(bool includeOwn = false)
    {
        var windows = new List<WindowEntry>();
        Native.EnumWindows((handle, _) => {
            Native.GetWindowThreadProcessId(handle, out uint pid);
            if ((!includeOwn && pid == Environment.ProcessId) || !Native.IsWindowVisible(handle) || GetWindowTextLength(handle) == 0) return true;
            Native.GetCloaked(handle, 14, out int cloaked, 4); if (cloaked != 0) return true;
            if ((Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x80) != 0) return true;
            var title = new StringBuilder(1024); GetWindowText(handle, title, title.Capacity); string name = title.ToString();
            if (name is "Program Manager" or "Windows Input Experience") return true;
            string application = "Application"; try { using var process = Process.GetProcessById((int)pid); application = process.ProcessName; } catch { }
            windows.Add(new WindowEntry(handle, name, application, Native.IsIconic(handle))); return true;
        }, IntPtr.Zero);
        return windows.OrderBy(w => w.Application, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static async Task<(BitmapSource Image, System.Windows.Forms.Screen Screen, System.Drawing.Rectangle Bounds)> CaptureAsync(WindowEntry entry)
    {
        if (!IsWindow(entry.Handle)) throw new InvalidOperationException("That window has closed. Choose another window.");
        bool minimized = Native.IsIconic(entry.Handle); var previous = GetForegroundWindow();
        try
        {
            if (minimized) ShowWindowAsync(entry.Handle, 9);
            SetForegroundWindow(entry.Handle);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(80);
                if (!IsWindow(entry.Handle)) throw new InvalidOperationException("The selected window closed before capture.");
                if (!Native.IsIconic(entry.Handle) && GetForegroundWindow() == entry.Handle) break;
            }
            if (Native.IsIconic(entry.Handle) || GetForegroundWindow() != entry.Handle)
                throw new InvalidOperationException("Windows couldn't bring that window forward. Try selecting it again.");
            await Task.Delay(280); // Let the restored application repaint before reading screen pixels.
            if (Native.DwmGetWindowAttribute(entry.Handle, 9, out var bounds, Marshal.SizeOf<Native.Rect>()) != 0) Native.GetWindowRect(entry.Handle, out bounds);
            var clipped = System.Drawing.Rectangle.Intersect(bounds.Bounds, System.Windows.Forms.SystemInformation.VirtualScreen);
            if (clipped.Width < 1 || clipped.Height < 1) throw new InvalidOperationException("That window is outside the desktop.");
            var image = Native.Capture(clipped);
            return (image, System.Windows.Forms.Screen.FromRectangle(clipped), clipped);
        }
        finally
        {
            if (minimized && IsWindow(entry.Handle)) ShowWindowAsync(entry.Handle, 6);
            if (previous != entry.Handle && IsWindow(previous)) SetForegroundWindow(previous);
        }
    }
    internal static System.Drawing.Rectangle RecordingBounds(WindowEntry entry)
    {
        if (!IsWindow(entry.Handle)) throw new InvalidOperationException("That application has closed.");
        if (Native.IsIconic(entry.Handle)) throw new InvalidOperationException("Restore the application window before recording it.");
        if (Native.DwmGetWindowAttribute(entry.Handle, 9, out var bounds, Marshal.SizeOf<Native.Rect>()) != 0) Native.GetWindowRect(entry.Handle, out bounds);
        if (bounds.Bounds.Width < 2 || bounds.Bounds.Height < 2) throw new InvalidOperationException("That application has no capturable area.");
        return bounds.Bounds;
    }
}

internal sealed class WindowPicker : Window
{
    internal WindowEntry? Selection { get; private set; }
    private List<WindowEntry> windows;
    internal WindowPicker(App app, bool recording = false)
    {
        bool dark = UI.Dark(app.Settings); UI.SetupWindow(this, "Choose a window", 760, 690, dark); MinWidth = 520; MinHeight = 440;
        windows = WindowCatalog.Enumerate();
        var root = new DockPanel { Margin = new Thickness(28) };
        var header = new StackPanel(); header.Children.Add(UI.Text("Choose a window", 26, UI.Ink(dark), FontWeights.SemiBold));
        var note = UI.Text(recording ? "Record only this window, even when moved between displays or covered by another app. Keep it restored: minimized apps may stop drawing frames." : "Open and minimized windows. Minimized windows are briefly restored for capture, then minimized again.", 12, UI.Muted(dark)); note.Margin = new Thickness(0, 10, 0, 18); header.Children.Add(note);
        var search = UI.TextBox(dark); search.ToolTip = "Search by application or window title"; search.Margin = new Thickness(0, 0, 0, 18); header.Children.Add(search); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) }; var count = UI.Text("", 11, UI.Muted(dark)); footer.Children.Add(count);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(UI.Button("Cancel", () => { DialogResult = false; }, false, dark)); var capture = UI.Button(recording ? "Use this window" : "Capture window", () => { if (Selection != null) DialogResult = true; }, true, dark); capture.IsEnabled = false; actions.Children.Add(capture); DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var list = new StackPanel(); var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll); Content = root;
        void Refresh()
        {
            Selection = null; capture.IsEnabled = false; list.Children.Clear(); var rows = new List<Button>();
            var filtered = windows.Where(w => (w.Title + " " + w.Application).Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray(); count.Text = filtered.Length + " windows";
            foreach (var item in filtered)
            {
                var entry = item; var row = UI.Button("", () => { }, false, dark); row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Margin = new Thickness(0, 0, 0, 8);
                var content = new StackPanel(); var title = UI.Text(entry.Title, 13, UI.Ink(dark), FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; content.Children.Add(title);
                var description = UI.Text(entry.Application + (entry.Minimized ? "  /  MINIMIZED" : "  /  OPEN"), 10, UI.Muted(dark)); description.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(description); row.Content = content;
                row.Click += (_, _) => { Selection = entry; capture.IsEnabled = true; foreach (var button in rows) button.BorderThickness = new Thickness(button == row ? 2 : 1); };
                row.MouseDoubleClick += (_, _) => { Selection = entry; DialogResult = true; }; list.Children.Add(row); rows.Add(row);
            }
            if (filtered.Length == 0) list.Children.Add(UI.Text("No matching windows. Try another name.", 13, UI.Muted(dark)));
        }
        search.TextChanged += (_, _) => Refresh(); Refresh();
    }
}
