using DocAnon.Core.Model;
using SkiaSharp;

namespace DocAnon.Ocr;

/// <summary>
/// Drawings carry vertical text (document numbers along the border, labels on vertical lines).
/// Tesseract reads horizontal text only, so the image is also read turned 90° each way, and the
/// boxes are mapped back to the upright image.
/// </summary>
public static class OcrOrientation
{
    /// <summary>Words from the rotated passes below this confidence are dropped, because graphics produce noise there.</summary>
    public const float RotatedMinConfidence = 0.7f;

    /// <summary>Words from the upright pass below this confidence are dropped.</summary>
    public const float UprightMinConfidence = 0.4f;

    public static OcrResult RecognizeAllOrientations(IOcrEngine ocr, byte[] png, bool includeRotated = true)
    {
        if (!includeRotated)
        {
            var only = ocr.Recognize(png, sparseText: false);
            return new OcrResult(only.WidthPx, only.HeightPx, only.Words.Where(w => w.Confidence >= UprightMinConfidence && w.Text.Any(char.IsLetterOrDigit)).ToList());
        }

        int w, h;
        byte[] r90, r270;
        using (var bmp = SKBitmap.Decode(png))
        {
            w = bmp.Width;
            h = bmp.Height;
            r90 = Rotate(bmp, 90);
            r270 = Rotate(bmp, 270);
        }
        // The three passes run in parallel when the engine is a pool.
        var tasks = new[] { png, r90, r270 }.Select(img => Task.Run(() => ocr.Recognize(img, sparseText: true))).ToArray();
        Task.WaitAll(tasks);

        var words = tasks[0].Result.Words.Where(x => x.Confidence >= UprightMinConfidence && x.Text.Any(char.IsLetterOrDigit)).ToList();
        int lineOffset = 1_000_000;
        foreach (var (angle, result) in new[] { (90, tasks[1].Result), (270, tasks[2].Result) })
        {
            foreach (var word in result.Words)
            {
                if (word.Confidence < RotatedMinConfidence || word.Text.Count(char.IsLetterOrDigit) < 2) continue;
                var box = MapBack(word.Box, angle, w, h);
                // Vertical text read sideways by the upright pass comes out as low-confidence noise.
                // The more confident reading wins the overlap.
                var clashes = words.Where(x => x.Box.Intersects(box)).ToList();
                if (clashes.Any(x => x.Confidence >= word.Confidence)) continue;
                foreach (var c in clashes) words.Remove(c);
                words.Add(word with { Box = box, Line = word.Line + lineOffset });
            }
            lineOffset += 1_000_000;
        }
        return new OcrResult(w, h, words);
    }

    private static byte[] Rotate(SKBitmap src, int angle)
    {
        using var dst = new SKBitmap(src.Height, src.Width);
        using (var c = new SKCanvas(dst))
        {
            c.Clear(SKColors.White);
            if (angle == 90)
            {
                c.Translate(src.Height, 0);
                c.RotateDegrees(90);
            }
            else
            {
                c.Translate(0, src.Width);
                c.RotateDegrees(270);
            }
            c.DrawBitmap(src, 0, 0);
        }
        using var img = SKImage.FromBitmap(dst);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Maps a box found in the rotated image back to the upright image of size w × h.</summary>
    private static BoundingBox MapBack(BoundingBox b, int angle, int w, int h)
    {
        (float X, float Y) P(float x, float y) => angle == 90
            // Clockwise: upright (x, y) went to (h - y, x).
            ? (y, h - x)
            // Counter-clockwise: upright (x, y) went to (y, w - x).
            : (w - y, x);
        var p1 = P(b.X, b.Y);
        var p2 = P(b.Right, b.Bottom);
        float x0 = Math.Min(p1.X, p2.X), x1 = Math.Max(p1.X, p2.X), y0 = Math.Min(p1.Y, p2.Y), y1 = Math.Max(p1.Y, p2.Y);
        return new BoundingBox(x0, y0, x1 - x0, y1 - y0);
    }
}
