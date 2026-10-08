using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BetterSS;

// Rounded lens geometry is cached. Only background pixels are resampled per frame.
internal static class LiquidLens
{
    internal sealed record Frame(BitmapSource Body, BitmapSource? Rim);
    private sealed record Map(float[] X, float[] Y, float[] NormalX, float[] NormalY, byte[] Rim);
    private static readonly ConcurrentDictionary<(int Width, int Height, int Radius), Map> maps = new();
    internal static Frame Render(BitmapSource source, int width, int height, double radius)
    {
        width = Math.Clamp(width, 1, 700); height = Math.Clamp(height, 1, 450);
        int rounded = (int)Math.Round(Math.Min(radius, Math.Min(width, height) / 2.0));
        if (maps.Count > 24) maps.Clear();
        var map = maps.GetOrAdd((width, height, rounded), key => MakeMap(key.Width, key.Height, key.Radius));
        BitmapSource resized = source;
        if (source.PixelWidth != width || source.PixelHeight != height)
        { var scaled = new TransformedBitmap(source, new ScaleTransform(width / (double)source.PixelWidth, height / (double)source.PixelHeight)); scaled.Freeze(); resized = scaled; }
        var pixels = new FormatConvertedBitmap(resized, PixelFormats.Pbgra32, null, 0); pixels.Freeze();
        int stride = width * 4; var input = new byte[stride * height]; pixels.CopyPixels(input, stride, 0);
        var body = new byte[input.Length]; var rim = new byte[input.Length];
        for (int i = 0; i < width * height; i++)
        {
            int destination = i * 4; double sx = map.X[i], sy = map.Y[i];
            for (int channel = 0; channel < 4; channel++)
            {
                double dispersion = channel == 0 ? -.7 : channel == 2 ? .7 : 0;
                byte value = Sample(input, width, height, sx + map.NormalX[i] * dispersion, sy + map.NormalY[i] * dispersion, channel);
                body[destination + channel] = value;
                rim[destination + channel] = (byte)(value * map.Rim[i] / 255);
            }
        }
        var bodyImage = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, body, stride); bodyImage.Freeze();
        var rimImage = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, rim, stride); rimImage.Freeze();
        return new(bodyImage, rimImage);
    }
    private static Map MakeMap(int width, int height, int radius)
    {
        var map = new Map(new float[width * height], new float[width * height], new float[width * height], new float[width * height], new byte[width * height]);
        double halfWidth = width / 2.0, halfHeight = height / 2.0, bevel = Math.Min(18, Math.Min(width, height) * .28);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x; double px = x + .5 - halfWidth, py = y + .5 - halfHeight;
            double qx = Math.Abs(px) - halfWidth + radius, qy = Math.Abs(py) - halfHeight + radius;
            double vx = Math.Max(qx, 0), vy = Math.Max(qy, 0), length = Math.Sqrt(vx * vx + vy * vy);
            double distance = -(length + Math.Min(Math.Max(qx, qy), 0) - radius);
            double nx = length > 0 ? vx / length * Math.Sign(px) : qx > qy ? Math.Sign(px) : 0;
            double ny = length > 0 ? vy / length * Math.Sign(py) : qy >= qx ? Math.Sign(py) : 0;
            double t = Math.Clamp(distance / bevel, 0, 1), wave = Math.Sin(t * Math.PI), displacement = 9 * wave;
            map.X[i] = (float)Math.Clamp(halfWidth + px * .985 - nx * displacement, 0, width - 1);
            map.Y[i] = (float)Math.Clamp(halfHeight + py * .985 - ny * displacement, 0, height - 1);
            map.NormalX[i] = (float)(nx * wave); map.NormalY[i] = (float)(ny * wave);
            map.Rim[i] = distance >= 0 && distance < bevel ? (byte)Math.Round(180 * Math.Pow(1 - t, 1.4)) : (byte)0;
        }
        return map;
    }
    private static byte Sample(byte[] input, int width, int height, double x, double y, int channel)
    {
        x = Math.Clamp(x, 0, width - 1); y = Math.Clamp(y, 0, height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        double tx = x - x0, ty = y - y0;
        double top = input[(y0 * width + x0) * 4 + channel] * (1 - tx) + input[(y0 * width + x1) * 4 + channel] * tx;
        double bottom = input[(y1 * width + x0) * 4 + channel] * (1 - tx) + input[(y1 * width + x1) * 4 + channel] * tx;
        return (byte)Math.Round(top * (1 - ty) + bottom * ty);
    }
}
