using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BetterSS;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom); }
    internal delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] internal static extern int GetCloaked(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static void SquareCorners(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle; int square = 1, mode = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 33, ref square, 4); DwmSetWindowAttribute(hwnd, 20, ref mode, 4);
        int caption = dark ? 0x151515 : 0xF6F6F6, text = dark ? 0xEFEFEF : 0x181818;
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4); DwmSetWindowAttribute(hwnd, 36, ref text, 4);
    }
    internal static System.Drawing.Point CursorPosition { get { GetCursorPos(out var p); return new(p.X, p.Y); } }
    internal static void Exclude(Window w) => SetWindowDisplayAffinity(new WindowInteropHelper(w).Handle, 0x11);
    internal static void Place(Window w, Rectangle rect)
    {
        SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x10 | 0x40);
    }
    internal static void MoveToMonitor(Window w, System.Windows.Forms.Screen screen)
    {
        // Move first, so WPF receives the target monitor's DPI before measuring its size.
        SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1), screen.WorkingArea.Left + 16, screen.WorkingArea.Top + 16, 0, 0, 0x1 | 0x10);
        w.UpdateLayout();
    }
    internal static void NoActivate(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        SetWindowLongPtr(hwnd, -20, new IntPtr(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000 | 0x80));
        // A tool window must accept the first click without activating or eating it.
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // WM_MOUSEACTIVATE / MA_NOACTIVATE
            return IntPtr.Zero;
        });
    }
    internal static List<Rectangle> Windows()
    {
        var result = new List<Rectangle>();
        EnumWindows((h, _) => {
            GetWindowThreadProcessId(h, out var pid);
            if (pid == Environment.ProcessId || !IsWindowVisible(h) || IsIconic(h)) return true;
            GetCloaked(h, 14, out var cloaked, 4); if (cloaked != 0) return true;
            if (DwmGetWindowAttribute(h, 9, out var r, Marshal.SizeOf<Rect>()) != 0) GetWindowRect(h, out r);
            if (r.Bounds.Width > 30 && r.Bounds.Height > 30) result.Add(r.Bounds);
            return true;
        }, IntPtr.Zero);
        return result;
    }
    internal static BitmapSource Capture(Rectangle bounds)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        var pixels = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, pixels.Scan0, pixels.Stride * bitmap.Height, pixels.Stride);
            source.Freeze(); return source;
        }
        finally { bitmap.UnlockBits(pixels); }
    }
    internal static Int32Rect CropBounds(Rectangle selection, Rectangle desktop)
    {
        var clipped = Rectangle.Intersect(selection, desktop);
        return clipped.Width <= 0 || clipped.Height <= 0 ? Int32Rect.Empty : new(clipped.X - desktop.X, clipped.Y - desktop.Y, clipped.Width, clipped.Height);
    }
    internal static BitmapSource Crop(BitmapSource image, Rectangle selection, Rectangle desktop)
    {
        var area = CropBounds(selection, desktop); if (area.IsEmpty) throw new InvalidOperationException("Select an area inside a display.");
        var crop = new WriteableBitmap(new CroppedBitmap(image, area)); crop.Freeze(); return crop;
    }
    internal static void SavePng(BitmapSource image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var temp = path + ".tmp";
        try { using (var stream = File.Create(temp)) encoder.Save(stream); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
}
