using DocAnon.Core.Model;
using SkiaSharp;

namespace DocAnon.Redaction;

/// <summary>
/// Finds a logo on a page by normalized cross-correlation over a range of sizes. Each size is
/// searched on a page copy scaled so the template is about 40 pixels wide, with a stride of two,
/// and the best candidates are refined at stride one. The match survives changes of brightness and
/// contrast, but not rotation.
/// </summary>
public static class LogoMatcher
{
    public static List<(BoundingBox Box, double Score)> Find(byte[] pagePng, byte[] logoPng, double threshold = 0.7,
        double minWidthFraction = 0.01, double maxWidthFraction = 0.35)
    {
        using var page = SKBitmap.Decode(pagePng);
        using var logo = SKBitmap.Decode(logoPng);
        if (page is null || logo is null) return [];
        double aspect = logo.Height / (double)logo.Width;
        var results = new List<(BoundingBox, double)>();

        const int templateW = 40;
        int steps = 40;
        double ratio = Math.Pow(maxWidthFraction / minWidthFraction, 1.0 / (steps - 1));
        var bag = new System.Collections.Concurrent.ConcurrentBag<(BoundingBox, double)>();
        Parallel.For(0, steps, k =>
        {
            double logoWidthPx = page.Width * minWidthFraction * Math.Pow(ratio, k);
            double s = templateW / logoWidthPx;
            int pw = (int)Math.Round(page.Width * s), ph = (int)Math.Round(page.Height * s);
            int tw = templateW, th = Math.Max(4, (int)Math.Round(templateW * aspect));
            if (pw < tw + 2 || ph < th + 2 || pw > 6000 || ph > 6000) return;

            float[] img = Gray(page, pw, ph);
            float[] tpl = Gray(logo, tw, th);
            var (tz, tnorm) = ZeroMean(tpl);
            if (tnorm < 1e-3) return;
            var (sum, sq) = Integrals(img, pw, ph);

            var candidates = new List<(int X, int Y, double Score)>();
            for (int y = 0; y + th <= ph; y += 2)
                for (int x = 0; x + tw <= pw; x += 2)
                {
                    double sc = Ncc(img, pw, sum, sq, tz, tnorm, tw, th, x, y);
                    if (sc >= threshold - 0.1) candidates.Add((x, y, sc));
                }
            foreach (var (cx, cy, _) in candidates.OrderByDescending(c => c.Score).Take(20))
            {
                double best = -1;
                int bx = cx, by = cy;
                for (int y = Math.Max(0, cy - 2); y <= Math.Min(ph - th, cy + 2); y++)
                    for (int x = Math.Max(0, cx - 2); x <= Math.Min(pw - tw, cx + 2); x++)
                    {
                        double sc = Ncc(img, pw, sum, sq, tz, tnorm, tw, th, x, y);
                        if (sc > best) (best, bx, by) = (sc, x, y);
                    }
                if (best >= threshold)
                    bag.Add((new BoundingBox((float)(bx / s), (float)(by / s), (float)(tw / s), (float)(th / s)), best));
            }
        });
        results.AddRange(bag);

        // Non-maximum suppression across sizes.
        var kept = new List<(BoundingBox Box, double Score)>();
        foreach (var r in results.OrderByDescending(r => r.Item2))
            if (!kept.Any(k => Iou(k.Box, r.Item1) > 0.3)) kept.Add(r);
        return kept;
    }

    private static float[] Gray(SKBitmap src, int w, int h)
    {
        using var scaled = src.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        var px = scaled.Pixels;
        var g = new float[w * h];
        for (int i = 0; i < g.Length; i++)
        {
            var c = px[i];
            // Transparent pixels count as white paper.
            float a = c.Alpha / 255f;
            g[i] = (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) * a + 255f * (1 - a);
        }
        return g;
    }

    private static (float[] Z, double Norm) ZeroMean(float[] t)
    {
        double mean = t.Average(v => (double)v);
        var z = new float[t.Length];
        double n2 = 0;
        for (int i = 0; i < t.Length; i++)
        {
            z[i] = (float)(t[i] - mean);
            n2 += z[i] * (double)z[i];
        }
        return (z, Math.Sqrt(n2));
    }

    private static (double[] Sum, double[] Sq) Integrals(float[] img, int w, int h)
    {
        var sum = new double[(w + 1) * (h + 1)];
        var sq = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double rs = 0, rq = 0;
            for (int x = 0; x < w; x++)
            {
                double v = img[y * w + x];
                rs += v;
                rq += v * v;
                sum[(y + 1) * (w + 1) + x + 1] = sum[y * (w + 1) + x + 1] + rs;
                sq[(y + 1) * (w + 1) + x + 1] = sq[y * (w + 1) + x + 1] + rq;
            }
        }
        return (sum, sq);
    }

    private static double Ncc(float[] img, int w, double[] sum, double[] sq, float[] tz, double tnorm, int tw, int th, int x, int y)
    {
        int W = w + 1;
        double s = sum[(y + th) * W + x + tw] - sum[y * W + x + tw] - sum[(y + th) * W + x] + sum[y * W + x];
        double q = sq[(y + th) * W + x + tw] - sq[y * W + x + tw] - sq[(y + th) * W + x] + sq[y * W + x];
        int n = tw * th;
        double var = q - s * s / n;
        if (var < 1e-3) return 0;
        double dot = 0;
        for (int j = 0; j < th; j++)
        {
            int row = (y + j) * w + x, trow = j * tw;
            for (int i = 0; i < tw; i++) dot += tz[trow + i] * img[row + i];
        }
        return dot / (tnorm * Math.Sqrt(var));
    }

    private static double Iou(BoundingBox a, BoundingBox b)
    {
        float ix = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X));
        float iy = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        float inter = ix * iy;
        return inter / (a.Width * a.Height + b.Width * b.Height - inter);
    }
}
