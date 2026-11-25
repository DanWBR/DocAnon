namespace DocAnon.Core.Model;

/// <summary>A recognized word. Line and block indexes let the caller rebuild the reading order.</summary>
public sealed record OcrWord(string Text, BoundingBox Box, float Confidence, int Block, int Line);

public sealed record OcrResult(int WidthPx, int HeightPx, IReadOnlyList<OcrWord> Words)
{
    public double MeanConfidence => Words.Count == 0 ? 0 : Words.Average(w => w.Confidence);
}

public interface IOcrEngine
{
    /// <summary>
    /// Recognizes the text of a PNG, JPEG or TIFF image. With <paramref name="sparseText"/> the
    /// engine looks for scattered text in no particular order, which suits drawings.
    /// </summary>
    OcrResult Recognize(byte[] image, bool sparseText = false);
}

/// <summary>Renders pages of a PDF to PNG. Implemented with PDFium in DocAnon.Ocr.</summary>
public interface IPdfRasterizer
{
    int PageCount(byte[] pdf);

    /// <summary>Renders one page (zero-based) at the given resolution.</summary>
    (byte[] Png, int WidthPx, int HeightPx) Render(byte[] pdf, int pageIndex, int dpi);
}
