using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RenderWatch.App.Controls;

public static class GraphiteTexture
{
    public static Brush Grain { get; } = CreateGrain();

    private static Brush CreateGrain()
    {
        const int size = 128;
        const int stride = size * 4;
        var pixels = new byte[size * stride];
        var random = new Random(1703);

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var tone = random.Next(-17, 8);
            var alpha = (byte)Math.Abs(tone);
            var channel = tone > 0 ? alpha : (byte)0;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = channel;
            pixels[offset + 3] = alpha;
        }

        var texture = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        texture.Freeze();

        var brush = new ImageBrush(texture)
        {
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }
}
