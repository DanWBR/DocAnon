using DocAnon.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace DocAnon.Extraction;

/// <summary>
/// Reads PDFs with PdfPig. Pages without a text layer go through OCR. Pages that look like
/// diagrams (many vector paths, short scattered labels) become visual pages: their text is listed
/// as labels and the page is exported as a redacted image as well. Large raster images inside text
/// pages are cropped and handled like embedded images.
/// </summary>
public sealed class PdfDocumentReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".pdf"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var b = new DocumentBuilder(Path.GetFileName(path), "pdf");
        using var doc = PdfDocument.Open(bytes);
        float scale = o.Dpi / 72f;

        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            int index = page.Number - 1;
            string source = $"página {page.Number}";

            var crop = page.CropBox.Bounds;
            int rotation = ((page.Rotation.Value % 360) + 360) % 360;
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w =>
                {
                    // Rotated words have a rotated rectangle, so the axis-aligned box comes from its four corners.
                    var r = w.BoundingBox;
                    double[] xs = [r.TopLeft.X, r.TopRight.X, r.BottomLeft.X, r.BottomRight.X];
                    double[] ys = [r.TopLeft.Y, r.TopRight.Y, r.BottomLeft.Y, r.BottomRight.Y];
                    return new PlacedWord(w.Text, ToPixels(xs.Min(), ys.Min(), xs.Max(), ys.Max(), crop, rotation, scale));
                })
                .ToList();
            int letters = words.Sum(w => w.Text.Length);

            // A font without a ToUnicode map gives control or private-use characters instead of
            // text. If the detectors read that layer, they would not see the names drawn on the
            // page, so a page with unreadable text goes through OCR like a scan.
            int unreadable = words.Sum(w => w.Text.Count(Unreadable));
            if (unreadable > 0 && unreadable > 0.05 * letters)
            {
                b.Warnings.Add($"{source}: a camada de texto do PDF não é legível ({unreadable} de {letters} caracteres), por isso a página passou pelo OCR.");
                words.Clear();
                letters = 0;
            }

            byte[]? rendered = null;
            int rw = 0, rh = 0;
            bool Render()
            {
                if (rendered is not null) return true;
                if (o.Rasterizer is null) return false;
                (rendered, rw, rh) = o.Rasterizer.Render(bytes, index, o.Dpi);
                return true;
            }

            if (letters < 20)
            {
                // Without a text layer the page is either a scan or a drawing whose text was turned
                // into vector strokes. Many paths mean a drawing, which gets the label layout and the
                // rotated OCR passes for vertical text.
                if (!Render())
                {
                    b.Warnings.Add($"{source}: a página não tem texto e o renderizador de PDF não está disponível.");
                    continue;
                }
                bool vectorDrawing = o.Diagrams == DiagramDetection.AllPages || (o.Diagrams == DiagramDetection.Auto && page.Paths.Count >= 40);
                if (vectorDrawing) b.Heading($"Diagrama, {source}", 2, source);
                var whole = ImageTools.AddImage(b, o, rendered!, source, asLabels: vectorDrawing, pdfPage: index);
                if (vectorDrawing && whole is not null)
                {
                    var vp = whole;
                    foreach (var img in page.GetImages())
                    {
                        var r = img.BoundingBox;
                        double a = r.Width * r.Height;
                        if (a < 0.15 * page.Width * page.Height && a > 0.0002 * page.Width * page.Height)
                            vp.AutoCovers.Add(new CoverBox(ToPixels(r.Left, r.Bottom, r.Right, r.Top, crop, rotation, scale), "imagem pequena (logotipo, carimbo ou assinatura)"));
                    }
                }
                continue;
            }

            bool diagram = o.Diagrams switch
            {
                DiagramDetection.AllPages => true,
                DiagramDetection.Never => false,
                _ => LooksLikeDiagram(page, words),
            };

            if (diagram)
            {
                if (!Render())
                {
                    b.Warnings.Add($"{source}: parece um diagrama, mas o renderizador de PDF não está disponível; só o texto foi extraído.");
                    b.PlacedText(words, source, asLabels: true, page: null, fromOcr: false);
                    continue;
                }
                byte[] png = rendered!;
                var vp = new VisualPage(b.Pages.Count, source, rw, rh, () => png) { PdfPage = index };
                b.Pages.Add(vp);
                b.Heading($"Diagrama, {source}", 2, source);
                b.PlacedText(words, source, asLabels: true, page: vp, fromOcr: false);
                AddImageOnlyText(b, o, png, words, vp, source);
                // Small raster images on a drawing are usually logos, stamps or scanned signatures.
                double area = page.Width * page.Height;
                foreach (var img in page.GetImages())
                {
                    var r = img.BoundingBox;
                    double a = r.Width * r.Height;
                    if (a < 0.15 * area && a > 0.0002 * area)
                        vp.AutoCovers.Add(new CoverBox(ToPixels(r.Left, r.Bottom, r.Right, r.Top, crop, rotation, scale), "imagem pequena (logotipo, carimbo ou assinatura)"));
                }
                continue;
            }

            // A text page goes out as text. For the PDF copy it is also rendered and redacted, so its
            // words are linked to a page that only the native copy uses.
            VisualPage? native = null;
            if (o.Rasterizer is { } rasterizer)
            {
                bool turned = rotation is 90 or 270;
                int pw = (int)Math.Round((turned ? crop.Height : crop.Width) * scale), ph = (int)Math.Round((turned ? crop.Width : crop.Height) * scale);
                int pageIndex = index;
                native = new VisualPage(b.NativePages.Count, source, pw, ph, () => rendered ?? rasterizer.Render(bytes, pageIndex, o.Dpi).Png) { PdfPage = index, TextVerified = true };
                b.NativePages.Add(native);
            }
            b.PlacedText(words, source, asLabels: false, page: native, fromOcr: false);

            double pageArea = page.Width * page.Height;
            int k = 0;
            foreach (var img in page.GetImages())
            {
                var r = img.BoundingBox;
                if (r.Width * r.Height < 0.1 * pageArea) continue;
                if (!Render())
                {
                    b.Warnings.Add($"{source}: a página tem uma imagem grande que não pôde ser lida sem o renderizador de PDF.");
                    break;
                }
                k++;
                var box = ToPixels(r.Left, r.Bottom, r.Right, r.Top, crop, rotation, scale);
                var (cropped, _, _) = ImageTools.Crop(rendered!, box);
                ImageTools.AddImage(b, o, cropped, $"{source}, imagem {k}", pdfPage: index, boxOnPage: box);
            }
        }
        return Task.FromResult(b.Build());
    }

    /// <summary>
    /// Maps a rectangle in PDF user space (origin at the bottom-left of the crop box) to the pixel
    /// space of the page as PDFium renders it, which applies the page's /Rotate.
    /// </summary>
    internal static BoundingBox ToPixels(double left, double bottom, double right, double top, UglyToad.PdfPig.Core.PdfRectangle crop, int rotation, float scale)
    {
        double w0 = crop.Width, h0 = crop.Height;
        (double X, double Y) Map(double x, double y)
        {
            x -= crop.Left;
            y -= crop.Bottom;
            return rotation switch
            {
                90 => (y, x),
                180 => (w0 - x, y),
                270 => (h0 - y, w0 - x),
                _ => (x, h0 - y),
            };
        }
        var a = Map(left, bottom);
        var b = Map(right, top);
        double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        return new BoundingBox((float)(x0 * scale), (float)(y0 * scale), (float)((x1 - x0) * scale), (float)((y1 - y0) * scale));
    }

    private static bool Unreadable(char c) =>
        c == '�' || (char.IsControl(c) && c is not ('\t' or '\n' or '\r')) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.PrivateUse;

    private static bool LooksLikeDiagram(Page page, List<PlacedWord> words)
    {
        if (page.Paths.Count < 40) return false;
        var labels = WordLayout.Labels(words);
        if (labels.Count < 8) return false;
        double wordsPerLabel = labels.Average(l => l.Sum(line => line.Count));
        return wordsPerLabel < 3.5;
    }

    /// <summary>
    /// Text drawn inside raster images of a diagram page is not in the text layer. OCR finds it,
    /// and words that do not overlap a text-layer word are added to the page.
    /// </summary>
    private static void AddImageOnlyText(DocumentBuilder b, ExtractionOptions o, byte[] png, List<PlacedWord> layerWords, VisualPage vp, string source)
    {
        if (o.Ocr is null)
        {
            b.Warnings.Add($"{source}: OCR indisponível; o texto dentro de imagens do diagrama não pode ser verificado, por isso a página não será exportada como imagem.");
            return;
        }
        var r = DocAnon.Ocr.OcrOrientation.RecognizeAllOrientations(o.Ocr, png);
        var extra = r.Words
            .Where(w => !layerWords.Any(l => l.Box.Inflate(2).Intersects(w.Box)))
            .Select(w => new PlacedWord(w.Text, w.Box, w.Line, w.Confidence))
            .ToList();
        if (extra.Count > 0) b.PlacedText(extra, source + " (texto em imagem)", asLabels: true, page: vp, fromOcr: true, r.MeanConfidence);
        vp.TextVerified = true;
    }
}
