using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BetterSS;

// Intercept only while the recorder is foreground and listening, so an occupied
// combination can be displayed and checked without launching another app's action.
internal sealed class ShortcutRecorder : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    private readonly HookProc callback;
    private readonly HashSet<int> swallowed = new();
    private IntPtr hook;
    internal ShortcutRecorder(IntPtr window, Func<bool> listening, Action<Key, ModifierKeys> record)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0)
            {
                int vk = Marshal.ReadInt32(data), kind = message.ToInt32();
                bool down = kind is 0x100 or 0x104, up = kind is 0x101 or 0x105;
                if (up && swallowed.Remove(vk)) return new IntPtr(1);
                if (down && swallowed.Contains(vk)) return new IntPtr(1);
                if (down && listening() && WindowCatalog.GetForegroundWindow() == window && vk is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C))
                {
                    ModifierKeys modifiers = ModifierKeys.None;
                    if (GetAsyncKeyState(0x11) < 0) modifiers |= ModifierKeys.Control;
                    if (GetAsyncKeyState(0x12) < 0) modifiers |= ModifierKeys.Alt;
                    if (GetAsyncKeyState(0x10) < 0) modifiers |= ModifierKeys.Shift;
                    if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) modifiers |= ModifierKeys.Windows;
                    swallowed.Add(vk); record(KeyInterop.KeyFromVirtualKey(vk), modifiers); return new IntPtr(1);
                }
            }
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
}
