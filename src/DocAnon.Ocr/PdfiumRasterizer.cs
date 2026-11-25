using DocAnon.Core.Model;
using PDFtoImage;
using SkiaSharp;

namespace DocAnon.Ocr;

/// <summary>Renders PDF pages with PDFium (through PDFtoImage).</summary>
public sealed class PdfiumRasterizer : IPdfRasterizer
{
    public int PageCount(byte[] pdf) => Conversion.GetPageCount(pdf);

    public (byte[] Png, int WidthPx, int HeightPx) Render(byte[] pdf, int pageIndex, int dpi)
    {
        using SKBitmap bmp = Conversion.ToImage(pdf, page: pageIndex, options: new RenderOptions(Dpi: dpi, WithAnnotations: true, BackgroundColor: SKColors.White));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return (data.ToArray(), bmp.Width, bmp.Height);
    }
}
