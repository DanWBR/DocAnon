using DocAnon.Core.Model;
using DocAnon.Core.Text;
using SkiaSharp;

namespace DocAnon.Redaction;

public sealed record RedactedPage(VisualPage Page, byte[] Png, int Boxes, int Width, int Height);

public sealed record VisualLeak(int PageIndex, string Source, string Text, BoundingBox Box)
{
    /// <summary>The marker of the leaked value, used to label the box that covers it.</summary>
    public string? Token { get; init; }
}

/// <summary>
/// Produces the image version of a visual page. Formats that can redraw their own text (DXF, SVG)
/// are redrawn with the replacements. For the others, every word touched by a replacement is
/// covered with an opaque box that carries the marker or the scaled value. Manual boxes are always
/// painted black. The output is a plain bitmap, so no hidden text layer survives.
/// </summary>
public sealed class PageRedactor
{
    private static readonly SKColor MarkerFill = new(0x20, 0x20, 0x20);
    private static readonly SKColor ScaledFill = new(0xFF, 0xF1, 0xA8);

    public RedactedPage Render(VisualPage page, IReadOnlyList<TextReplacement> replacements)
    {
        if (!page.TextVerified)
            throw new InvalidOperationException($"{page.Source}: o texto da página não foi verificado (OCR indisponível ou imagem embutida), então a imagem não pode ser exportada.");

        byte[] basePng = page.RenderWithReplacements is { } redraw ? redraw(replacements) : page.RenderOriginalPng();
        using var bmp = SKBitmap.Decode(basePng) ?? throw new InvalidDataException("Imagem inválida.");
        using var surface = SKSurface.Create(new SKImageInfo(bmp.Width, bmp.Height));
        var c = surface.Canvas;
        c.DrawBitmap(bmp, 0, 0);

        // Boxes are in the page's pixel space; the base image may have another size.
        float sx = bmp.Width / (float)Math.Max(1, page.WidthPx), sy = bmp.Height / (float)Math.Max(1, page.HeightPx);
        BoundingBox Scale(BoundingBox b) => new(b.X * sx, b.Y * sy, b.Width * sx, b.Height * sy);

        int boxes = 0;
        if (page.RenderWithReplacements is null)
        {
            foreach (var rep in replacements)
            {
                var touched = page.Words.Where(w => w.TextStart < rep.Start + rep.Length && rep.Start < w.TextStart + w.TextLength).ToList();
                if (touched.Count == 0) continue;
                bool scaled = rep.Type == EntityType.QUANTIDADE;
                var groups = GroupByLine(touched);
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var box = Scale(groups[gi].Select(w => PartialBox(w, rep)).Aggregate((a, b) => a.Union(b))).Inflate(2);
                    // The label goes on the first line only; continuation lines are just covered.
                    DrawBox(c, box, gi == 0 ? rep.Replacement : "", scaled);
                    boxes++;
                }
            }
        }
        using (var black = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill })
        {
            foreach (var mb in page.ManualBoxes)
            {
                var b = Scale(mb);
                c.DrawRect(b.X, b.Y, b.Width, b.Height, black);
                boxes++;
            }
            foreach (var cover in page.AutoCovers.Where(a => a.Include))
            {
                var b = Scale(cover.Box).Inflate(3);
                DrawBox(c, b, cover.Label ?? (b.Width > 60 && b.Height > 14 ? "[LOGO]" : ""), scaled: false);
                boxes++;
            }
        }
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new RedactedPage(page, data.ToArray(), boxes, bmp.Width, bmp.Height);
    }

    /// <summary>
    /// The part of a word box that holds the replaced characters, assuming evenly spaced letters.
    /// Vertical words are covered whole, because their reading direction is unknown.
    /// </summary>
    private static BoundingBox PartialBox(VisualWord w, TextReplacement rep)
    {
        int a = Math.Max(rep.Start, w.TextStart) - w.TextStart;
        int b = Math.Min(rep.Start + rep.Length, w.TextStart + w.TextLength) - w.TextStart;
        if (w.TextLength <= 3 || (a == 0 && b == w.TextLength)) return w.Box;
        bool horizontal = w.Box.Width >= w.Box.Height;
        if (!horizontal) return w.Box;
        float per = w.Box.Width / w.TextLength;
        // One extra character on each side absorbs uneven letter widths.
        float x0 = w.Box.X + Math.Max(0, a - 1) * per;
        float x1 = w.Box.X + Math.Min(w.TextLength, b + 1) * per;
        return new BoundingBox(x0, w.Box.Y, x1 - x0, w.Box.Height);
    }

    private static List<List<VisualWord>> GroupByLine(List<VisualWord> words)
    {
        var groups = new List<List<VisualWord>>();
        foreach (var w in words.OrderBy(w => w.TextStart))
        {
            var last = groups.LastOrDefault();
            if (last is not null)
            {
                var p = last[^1].Box;
                float overlap = Math.Min(p.Bottom, w.Box.Bottom) - Math.Max(p.Y, w.Box.Y);
                if (overlap > 0.5f * Math.Min(p.Height, w.Box.Height) && w.Box.X >= p.X - p.Height)
                {
                    last.Add(w);
                    continue;
                }
            }
            groups.Add([w]);
        }
        return groups;
    }

    private static void DrawBox(SKCanvas c, BoundingBox box, string label, bool scaled)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default, Math.Max(6, box.Height * 0.75f));
        float width = box.Width;
        if (label.Length > 0)
        {
            // Shrink the font until the label fits; if it is still too wide, widen the box.
            while (font.Size > 6 && font.MeasureText(label) > box.Width - 2) font.Size -= 0.5f;
            width = Math.Max(box.Width, font.MeasureText(label) + 4);
        }
        using var fill = new SKPaint { Color = scaled ? ScaledFill : MarkerFill, Style = SKPaintStyle.Fill };
        c.DrawRect(box.X, box.Y, width, box.Height, fill);
        if (label.Length == 0) return;
        using var ink = new SKPaint { Color = scaled ? SKColors.Black : SKColors.White, IsAntialias = true };
        float baseline = box.Y + box.Height / 2 + font.Size * 0.35f;
        c.DrawText(label, box.X + 2, baseline, font, ink);
    }

    /// <summary>
    /// Runs OCR on redacted images and reports every original value that can still be read, with
    /// the box of the words that carry it. Matches respect word boundaries, as in the text check.
    /// </summary>
    public static List<VisualLeak> CheckWithOcr(IOcrEngine ocr, IEnumerable<RedactedPage> pages, IEnumerable<(string Original, string Token)> originals)
    {
        var needles = originals
            .Select(o => (Key: TextFold.Key(o.Original), o.Token))
            .Where(n => n.Key.Length >= 4)
            .DistinctBy(n => n.Key)
            .ToList();
        var leaks = new List<VisualLeak>();
        foreach (var p in pages)
        {
            var r = DocAnon.Ocr.OcrOrientation.RecognizeAllOrientations(ocr, p.Png);
            foreach (var line in r.Words.GroupBy(w => w.Line))
            {
                var words = line.OrderBy(w => w.Line >= 1_000_000 ? w.Box.Y : w.Box.X).ToList();
                var ranges = new List<(int Start, int End, OcrWord Word)>();
                var sb = new System.Text.StringBuilder();
                foreach (var w in words)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    string k = TextFold.Key(w.Text);
                    ranges.Add((sb.Length, sb.Length + k.Length, w));
                    sb.Append(k);
                }
                string text = sb.ToString();
                foreach (var (key, token) in needles)
                {
                    int from = 0;
                    while (from < text.Length)
                    {
                        int i = text.IndexOf(key, from, StringComparison.Ordinal);
                        if (i < 0) break;
                        from = i + 1;
                        int end = i + key.Length;
                        bool startOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                        bool endOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
                        if (!startOk || !endOk) continue;
                        var hit = ranges.Where(x => x.Start < end && i < x.End).ToList();
                        if (hit.Count == 0) continue;
                        // Within a single horizontal word, the box narrows to the characters of the value.
                        var box = hit.Count == 1 && hit[0].Word.Box.Width >= hit[0].Word.Box.Height
                            ? Narrow(hit[0].Word.Box, hit[0].End - hit[0].Start, i - hit[0].Start, end - hit[0].Start)
                            : hit.Select(x => x.Word.Box).Aggregate((x, y) => x.Union(y));
                        leaks.Add(new VisualLeak(p.Page.Index, p.Page.Source, key, box) { Token = token });
                    }
                }
            }
        }
        return leaks;
    }

    private static BoundingBox Narrow(BoundingBox b, int len, int a, int z)
    {
        if (len <= 0) return b;
        float per = b.Width / len;
        float x0 = b.X + Math.Max(0, a - 1) * per, x1 = b.X + Math.Min(len, z + 1) * per;
        return new BoundingBox(x0, b.Y, x1 - x0, b.Height);
    }

    /// <summary>Writes the redacted pages into a PDF that holds only images.</summary>
    public static void WriteImagePdf(IEnumerable<RedactedPage> pages, Stream output)
    {
        using var doc = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Producer = "DocAnon", Creator = "DocAnon", RasterDpi = 200 });
        foreach (var p in pages)
        {
            using var bmp = SKBitmap.Decode(p.Png);
            // 200 dpi images on 72 dpi pages.
            float w = bmp.Width * 72f / 200f, h = bmp.Height * 72f / 200f;
            var canvas = doc.BeginPage(w, h);
            canvas.DrawBitmap(bmp, new SKRect(0, 0, w, h));
            doc.EndPage();
        }
        doc.Close();
    }
}
