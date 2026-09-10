using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal static class ClipboardService
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
    private static HwndSource? owner;

    internal static byte[] Dib(BitmapSource image)
    {
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        int stride = (bitmap.PixelWidth * 3 + 3) & ~3;
        var pixels = new byte[checked(stride * bitmap.PixelHeight)]; bitmap.CopyPixels(pixels, stride, 0);
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write(40); writer.Write(bitmap.PixelWidth); writer.Write(bitmap.PixelHeight);
        writer.Write((short)1); writer.Write((short)24); writer.Write(0); writer.Write(pixels.Length);
        writer.Write(3780); writer.Write(3780); writer.Write(0); writer.Write(0);
        for (int y = bitmap.PixelHeight - 1; y >= 0; y--) writer.Write(pixels, y * stride, stride);
        return data.ToArray();
    }
    internal static async Task<bool> ImageAsync(BitmapSource image)
    {
        // Encoding large displays must not block the first frames of the capture animation.
        var frozen = image.IsFrozen ? image : image.Clone(); if (!frozen.IsFrozen) frozen.Freeze();
        var data = await Task.Run(() =>
        {
            var dib = Dib(frozen); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frozen));
            using var stream = new MemoryStream(); encoder.Save(stream); return (Dib: dib, Png: stream.ToArray());
        });
        return await WriteAsync(8, data.Dib, RegisterClipboardFormat("PNG"), data.Png);
    }
    internal static Task<bool> TextAsync(string text) => WriteAsync(13, Encoding.Unicode.GetBytes(text + '\0'));
    private static async Task<bool> WriteAsync(uint format, byte[] bytes, uint secondary = 0, byte[]? second = null)
    {
        // Publish actual CF_DIB / CF_UNICODETEXT bytes, not a serialized managed object.
        owner ??= new HwndSource(new HwndSourceParameters("Better SS clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner.Handle))
            {
                try
                {
                    if (EmptyClipboard() && Publish(format, bytes))
                    { if (secondary != 0 && second != null) Publish(secondary, second); return true; }
                }
                finally { CloseClipboard(); }
            }
            await Task.Delay(100);
        }
        return false;
    }
    private static bool Publish(uint format, byte[] bytes)
    {
        var memory = GlobalAlloc(2, (UIntPtr)bytes.Length); if (memory == IntPtr.Zero) return false;
        var address = GlobalLock(memory); if (address == IntPtr.Zero) { GlobalFree(memory); return false; }
        Marshal.Copy(bytes, 0, address, bytes.Length); GlobalUnlock(memory);
        if (SetClipboardData(format, memory) != IntPtr.Zero) return true; // Windows now owns it.
        GlobalFree(memory); return false;
    }
}
