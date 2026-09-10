using System;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

internal static class ImageEffects
{
    private static byte[] Pixels(BitmapSource source)
    { var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var data = new byte[checked(source.PixelWidth * source.PixelHeight * 4)]; converted.CopyPixels(data, source.PixelWidth * 4, 0); return data; }
    private static BitmapSource Bitmap(byte[] data, int width, int height)
    { var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4); result.Freeze(); return result; }
    internal static BitmapSource Gaussian(BitmapSource source, double sigma)
    {
        sigma = Math.Clamp(sigma, .5, 30); int radius = (int)Math.Ceiling(3 * sigma), width = source.PixelWidth, height = source.PixelHeight;
        var weights = new double[radius * 2 + 1]; double sum = 0;
        for (int i = -radius; i <= radius; i++) { double weight = Math.Exp(-i * i / (2 * sigma * sigma)); weights[i + radius] = weight; sum += weight; }
        for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
        var input = Pixels(source); var horizontal = new byte[input.Length]; var output = new byte[input.Length];
        Parallel.For(0, height, y => {
            for (int x = 0; x < width; x++) for (int channel = 0; channel < 3; channel++)
            { double value = 0; for (int k = -radius; k <= radius; k++) value += input[(y * width + Math.Clamp(x + k, 0, width - 1)) * 4 + channel] * weights[k + radius]; horizontal[(y * width + x) * 4 + channel] = (byte)Math.Round(value); }
        });
        Parallel.For(0, height, y => {
            for (int x = 0; x < width; x++)
            {
                for (int channel = 0; channel < 3; channel++) { double value = 0; for (int k = -radius; k <= radius; k++) value += horizontal[(Math.Clamp(y + k, 0, height - 1) * width + x) * 4 + channel] * weights[k + radius]; output[(y * width + x) * 4 + channel] = (byte)Math.Round(value); }
                output[(y * width + x) * 4 + 3] = 255;
            }
        });
        return Bitmap(output, width, height);
    }
    internal static BitmapSource Mosaic(BitmapSource source, int blockSize)
    {
        int width = source.PixelWidth, height = source.PixelHeight; blockSize = Math.Clamp(blockSize, 2, 96);
        var data = Pixels(source);
        for (int y = 0; y < height; y += blockSize) for (int x = 0; x < width; x += blockSize)
        {
            long blue = 0, green = 0, red = 0; int right = Math.Min(width, x + blockSize), bottom = Math.Min(height, y + blockSize), count = (right - x) * (bottom - y);
            for (int yy = y; yy < bottom; yy++) for (int xx = x; xx < right; xx++) { int i = (yy * width + xx) * 4; blue += data[i]; green += data[i + 1]; red += data[i + 2]; }
            for (int yy = y; yy < bottom; yy++) for (int xx = x; xx < right; xx++) { int i = (yy * width + xx) * 4; data[i] = (byte)(blue / count); data[i + 1] = (byte)(green / count); data[i + 2] = (byte)(red / count); data[i + 3] = 255; }
        }
        return Bitmap(data, width, height);
    }
    internal static BitmapSource Adjust(BitmapSource source, double contrast = 1, double saturation = 1, bool grayscale = false, bool sharp = false)
    {
        var data = Pixels(source); int width = source.PixelWidth, height = source.PixelHeight;
        for (int i = 0; i < data.Length; i += 4)
        {
            double b = data[i], g = data[i + 1], r = data[i + 2];
            if (grayscale) r = g = b = .299 * r + .587 * g + .114 * b;
            else
            {
                double luma = .299 * r + .587 * g + .114 * b;
                r = luma + (r - luma) * saturation; g = luma + (g - luma) * saturation; b = luma + (b - luma) * saturation;
            }
            data[i] = (byte)Math.Clamp((b - 128) * contrast + 128, 0, 255);
            data[i + 1] = (byte)Math.Clamp((g - 128) * contrast + 128, 0, 255);
            data[i + 2] = (byte)Math.Clamp((r - 128) * contrast + 128, 0, 255);
        }
        if (!sharp) return Bitmap(data, width, height);
        var output = (byte[])data.Clone();
        for (int y = 1; y < height - 1; y++) for (int x = 1; x < width - 1; x++) for (int c = 0; c < 3; c++)
        {
            int i = (y * width + x) * 4 + c;
            int value = data[i] * 5 - data[i - 4] - data[i + 4] - data[i - width * 4] - data[i + width * 4];
            output[i] = (byte)Math.Clamp(value, 0, 255);
        }
        return Bitmap(output, width, height);
    }
}
