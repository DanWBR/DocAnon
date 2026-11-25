using DocAnon.Redaction;
using SkiaSharp;

namespace DocAnon.Integration.Tests;

public class LogoMatcherTests
{
    private static SKBitmap Logo()
    {
        var bmp = new SKBitmap(120, 80);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);
        using var black = new SKPaint { Color = SKColors.Black };
        c.DrawRect(10, 10, 100, 60, black);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 44);
        c.DrawText("XY", 22, 58, font, white);
        return bmp;
    }

    private static byte[] Png(SKBitmap b)
    {
        using var img = SKImage.FromBitmap(b);
        using var d = img.Encode(SKEncodedImageFormat.Png, 100);
        return d.ToArray();
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(0.63f)]
    [InlineData(1.85f)]
    public void FindsLogoAtOtherSizes(float scale)
    {
        using var logo = Logo();
        using var page = new SKBitmap(3000, 2100);
        using (var c = new SKCanvas(page))
        {
            c.Clear(SKColors.White);
            using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, Style = SKPaintStyle.Stroke };
            var rnd = new Random(7);
            for (int i = 0; i < 200; i++) c.DrawLine(rnd.Next(3000), rnd.Next(2100), rnd.Next(3000), rnd.Next(2100), pen);
            using var font = new SKFont(SKTypeface.Default, 30);
            using var ink = new SKPaint { Color = SKColors.Black };
            for (int i = 0; i < 40; i++) c.DrawText("TEXTO QUALQUER 123", rnd.Next(2600), rnd.Next(2100), font, ink);
            c.DrawRect(2380, 1780, 140 * scale, 100 * scale, new SKPaint { Color = SKColors.White });
            c.DrawBitmap(logo, new SKRect(2400, 1800, 2400 + 120 * scale, 1800 + 80 * scale));
        }
        var hits = LogoMatcher.Find(Png(page), Png(logo));
        var best = Assert.Single(hits, h => h.Score > 0.8);
        Assert.InRange(best.Box.X, 2400 - 15, 2400 + 15);
        Assert.InRange(best.Box.Y, 1800 - 15, 1800 + 15);
        Assert.InRange(best.Box.Width, 120 * scale * 0.85f, 120 * scale * 1.15f);
    }
}
