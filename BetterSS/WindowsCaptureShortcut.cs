using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace BetterSS;

// Windows reserves Win shortcuts. Suppress only Win+Shift+S while opted in,
// on a dedicated hook thread so rendering or capture cannot time out the hook.
internal sealed class WindowsCaptureShortcut : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    private readonly HookProc callback;
    private readonly Thread thread;
    private Dispatcher? hookDispatcher;
    private IntPtr hook;
    private bool swallowed;
    private volatile bool suspended;
    internal bool Suspended { get => suspended; set => suspended = value; }

    internal WindowsCaptureShortcut(Action capture)
    {
        var ui = Dispatcher.CurrentDispatcher;
        callback = (code, message, data) =>
        {
            if (code >= 0 && Marshal.ReadInt32(data) == 0x53)
            {
                int kind = message.ToInt32();
                bool down = kind is 0x100 or 0x104, up = kind is 0x101 or 0x105;
                if (up && swallowed) { swallowed = false; return new IntPtr(1); }
                if (down && swallowed) return new IntPtr(1);
                if (down && !suspended && (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) &&
                    GetAsyncKeyState(0x10) < 0 && GetAsyncKeyState(0x11) >= 0 && GetAsyncKeyState(0x12) >= 0)
                {
                    swallowed = true;
                    if (!ui.HasShutdownStarted) ui.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!suspended && hook != IntPtr.Zero) capture(); }));
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(hook, code, message, data);
        };
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            hookDispatcher = Dispatcher.CurrentDispatcher;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) failure = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (failure == null) Dispatcher.Run();
            var installed = Interlocked.Exchange(ref hook, IntPtr.Zero);
            if (installed != IntPtr.Zero) UnhookWindowsHookEx(installed);
        }) { IsBackground = true, Name = "Better SS Windows capture shortcut" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if (failure != null) throw failure;
    }
    public void Dispose()
    {
        suspended = true;
        var installed = Interlocked.Exchange(ref hook, IntPtr.Zero);
        if (installed != IntPtr.Zero) UnhookWindowsHookEx(installed);
        hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
    }
}
