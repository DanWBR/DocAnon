using DocAnon.Core.Model;
using SkiaSharp;

namespace DocAnon.Extraction;

internal static class ImageTools
{
    /// <summary>Decodes any image Skia understands and re-encodes it as PNG.</summary>
    public static (byte[] Png, int W, int H)? ToPng(byte[] data)
    {
        using var bmp = TryDecode(data);
        if (bmp is null) return null;
        using var img = SKImage.FromBitmap(bmp);
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return (enc.ToArray(), bmp.Width, bmp.Height);
    }

    /// <summary>
    /// Decodes an image, or returns null when Skia does not know the format (EMF, WMF, empty data).
    /// SKBitmap.Decode throws in that case instead of returning null.
    /// </summary>
    public static SKBitmap? TryDecode(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        using var codec = SKCodec.Create(new SKMemoryStream(data));
        return codec is null ? null : SKBitmap.Decode(codec);
    }

    public static (byte[] Png, int W, int H) Crop(byte[] png, BoundingBox box)
    {
        using var bmp = SKBitmap.Decode(png);
        var r = SKRectI.Create(
            Math.Clamp((int)box.X, 0, bmp.Width - 1),
            Math.Clamp((int)box.Y, 0, bmp.Height - 1),
            1, 1);
        r.Right = Math.Clamp((int)box.Right, r.Left + 1, bmp.Width);
        r.Bottom = Math.Clamp((int)box.Bottom, r.Top + 1, bmp.Height);
        using var sub = new SKBitmap(r.Width, r.Height);
        bmp.ExtractSubset(sub, r);
        using var img = SKImage.FromBitmap(sub);
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return (enc.ToArray(), r.Width, r.Height);
    }

    /// <summary>
    /// Runs OCR on an image, adds a visual page for it and writes the recognized text. When OCR
    /// is not available, the page is still added but marked as unverified, so the export refuses it.
    /// </summary>
    public static VisualPage? AddImage(DocumentBuilder b, ExtractionOptions o, byte[] data, string source, bool asLabels = true, int pdfPage = -1, BoundingBox? boxOnPage = null)
    {
        var png = ToPng(data);
        if (png is null)
        {
            b.Warnings.Add($"{source}: formato de imagem que o app não consegue ler (por exemplo EMF/WMF). A imagem não foi incluída.");
            return null;
        }
        var (bytes, w, h) = png.Value;
        var page = new VisualPage(b.Pages.Count, source, w, h, () => bytes) { PdfPage = pdfPage, BoxOnPage = boxOnPage };
        b.Pages.Add(page);
        // Small images in documents are usually logos or icons; they are not sent unless the user checks them.
        if (Math.Max(w, h) < 300)
        {
            page.Export = false;
            b.Warnings.Add($"{source}: imagem pequena ({w}×{h}), provavelmente logotipo ou ícone; não será exportada a menos que você a marque.");
        }
        if (o.Ocr is null)
        {
            b.Warnings.Add($"{source}: OCR indisponível, por isso a imagem não pode ser exportada.");
            return page;
        }
        // Diagrams (labels) may carry vertical text; running text pages do not need the rotated passes.
        var r = DocAnon.Ocr.OcrOrientation.RecognizeAllOrientations(o.Ocr, bytes, includeRotated: asLabels);
        page.TextVerified = true;
        var words = r.Words.Select(x => new PlacedWord(x.Text, x.Box, x.Line, x.Confidence)).ToList();
        b.PlacedText(words, source, asLabels, page, fromOcr: true, r.MeanConfidence);
        if (words.Count > 0 && r.MeanConfidence < 0.6)
            b.Warnings.Add($"{source}: confiança média do OCR baixa ({r.MeanConfidence:P0}). Revise a imagem com atenção.");
        return page;
    }
}
