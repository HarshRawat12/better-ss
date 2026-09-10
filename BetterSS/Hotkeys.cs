using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace BetterSS;

internal sealed record HotkeyBinding(uint Modifiers, uint KeyCode, string Label)
{
    internal static bool TryParse(string text, out HotkeyBinding? binding, out string error)
    {
        binding = null; error = "Use Ctrl or Alt with a key; Shift is optional.";
        if (text == "Disabled") { error = ""; return true; }
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries); ModifierKeys modifiers = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToLowerInvariant() switch { "ctrl" => ModifierKeys.Control, "alt" => ModifierKeys.Alt, "shift" => ModifierKeys.Shift, _ => ModifierKeys.None };
            if (modifier == ModifierKeys.None || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        string name = parts[^1]; if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
        return Enum.TryParse<Key>(name, true, out var key) && TryCreate(key, modifiers, out binding, out error);
    }
    internal static bool TryCreate(Key key, ModifierKeys modifiers, out HotkeyBinding? binding, out string error)
    {
        binding = null; error = "Use Ctrl or Alt with a key; Shift is optional. Windows-key combinations stay with Windows.";
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 || (modifiers & ModifierKeys.Windows) != 0) return false;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.System or Key.DeadCharProcessed or Key.ImeProcessed) return false;
        if (key == Key.F12 || key == Key.Escape || (modifiers == ModifierKeys.Alt && key is Key.Tab or Key.F4) ||
            ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.Delete))
        { error = "That combination is reserved. Choose a different key."; return false; }
        int vk = KeyInterop.VirtualKeyFromKey(key); if (vk < 8 || vk > 254) return false;
        var labels = new List<string>(); uint native = 0;
        if ((modifiers & ModifierKeys.Control) != 0) { labels.Add("Ctrl"); native |= 2; }
        if ((modifiers & ModifierKeys.Alt) != 0) { labels.Add("Alt"); native |= 1; }
        if ((modifiers & ModifierKeys.Shift) != 0) { labels.Add("Shift"); native |= 4; }
        labels.Add(key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        binding = new HotkeyBinding(native, (uint)vk, string.Join(" + ", labels)); error = ""; return true;
    }
}

internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private int activeId = 1;
    private HotkeyBinding? current;
    private bool suspended;
    internal string Current => current?.Label ?? "Disabled";
    internal HotkeyService(Action capture)
    {
        source = new HwndSource(new HwndSourceParameters("Better SS hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        source.AddHook((IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x312 && !suspended && wp.ToInt32() == activeId) { handled = true; capture(); }
            return IntPtr.Zero;
        });
    }
    internal bool TrySet(string value, out string error)
    {
        if (!HotkeyBinding.TryParse(value, out var next, out error)) return false;
        if (!suspended && next == current) return true;
        int nextId = activeId == 1 ? 2 : 1;
        if (next != null && !Native.RegisterHotKey(source.Handle, nextId, next.Modifiers | 0x4000, next.KeyCode))
        { error = "That shortcut is already in use or reserved by Windows. Record another combination."; return false; }
        // Reserve the replacement first. A failed change leaves the previous shortcut intact.
        Native.UnregisterHotKey(source.Handle, activeId);
        activeId = nextId; current = next; suspended = false; error = ""; return true;
    }
    internal void Suspend() { Native.UnregisterHotKey(source.Handle, activeId); suspended = true; }
    internal bool Resume(out string error) => TrySet(Current, out error);
    public void Dispose() { Native.UnregisterHotKey(source.Handle, activeId); source.Dispose(); }
}
