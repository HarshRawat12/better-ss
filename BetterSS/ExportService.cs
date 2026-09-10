using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal static class ExportService
{
    internal static readonly string[] Formats = { "PNG", "JPEG", "PDF", "TIFF", "BMP", "GIF" };
    internal static string Extension(string format) => format == "JPEG" ? "jpg" : format == "TIFF" ? "tif" : format.ToLowerInvariant();
    internal static BitmapSource Resize(BitmapSource image, int percent)
    {
        if (percent == 100) return image;
        double factor = percent / 100.0;
        if (image.PixelWidth * factor * image.PixelHeight * factor > 64_000_000) throw new InvalidOperationException("That export size is too large. Choose a smaller scale.");
        var resized = new TransformedBitmap(image, new ScaleTransform(factor, factor)); resized.Freeze(); return resized;
    }
    internal static void Save(BitmapSource image, string path, string format, int quality = 95, string colorSpace = "RGB")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            {
                if (format == "PDF") WritePdf(image, file);
                else
                {
                    BitmapEncoder encoder = format switch { "JPEG" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) }, "TIFF" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip }, "BMP" => new BmpBitmapEncoder(), "GIF" => new GifBitmapEncoder(), _ => new PngBitmapEncoder() };
                    // JPEG and TIFF retain a CMYK bitmap; formats such as PNG and GIF do not
                    // have reliable CMYK support in WPF, so those remain RGB by definition.
                    BitmapSource output = colorSpace == "CMYK" && format is "JPEG" or "TIFF"
                        ? new FormatConvertedBitmap(image, PixelFormats.Cmyk32, null, 0)
                        : image;
                    encoder.Frames.Add(BitmapFrame.Create(output)); encoder.Save(file);
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void WritePdf(BitmapSource image, Stream output)
    {
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0);
        var pixels = new byte[checked(rgb.PixelWidth * rgb.PixelHeight * 3)]; rgb.CopyPixels(pixels, rgb.PixelWidth * 3, 0);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, true)) deflate.Write(pixels);
        double pw = image.PixelWidth * .75, ph = image.PixelHeight * .75, iw = pw, ih = ph, x = 0, y = 0;
        static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        void Text(string text) { output.Write(Encoding.ASCII.GetBytes(text)); }
        long[] offsets = new long[6];
        void Object(int number, string text) { offsets[number] = output.Position; Text($"{number} 0 obj\n{text}\nendobj\n"); }
        Text("%PDF-1.4\n%Better SS\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pw)} {N(ph)}] /Resources << /XObject << /Shot 4 0 R >> >> /Contents 5 0 R >>");
        offsets[4] = output.Position;
        Text($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.PixelWidth} /Height {image.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n"); compressed.Position = 0; compressed.CopyTo(output); Text("\nendstream\nendobj\n");
        string content = $"q\n{N(iw)} 0 0 {N(ih)} {N(x)} {N(y)} cm\n/Shot Do\nQ\n";
        Object(5, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
        long xref = output.Position; Text("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++) Text(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
