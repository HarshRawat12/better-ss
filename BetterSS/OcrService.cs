using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace BetterSS;

internal static class OcrService
{
    internal static async Task<string> RecognizeAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
            engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
        if (engine == null) throw new InvalidOperationException("No Windows OCR language is installed. Add a language in Windows Settings → Time & language → Language & region, then try again.");
        var max = OcrEngine.MaxImageDimension; var scale = Math.Min(1, (double)max / Math.Max(image.PixelWidth, image.PixelHeight));
        BitmapSource input = scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
        var bitmap = new FormatConvertedBitmap(input, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        using var software = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bitmap.PixelWidth, bitmap.PixelHeight, BitmapAlphaMode.Ignore);
        software.CopyFromBuffer(pixels.AsBuffer());
        var result = await engine.RecognizeAsync(software);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
    }
}
