using DocAnon.Core.Model;

namespace DocAnon.Extraction;

public enum DiagramDetection
{
    Auto,
    AllPages,
    Never,
}

public sealed class ExtractionOptions
{
    public IOcrEngine? Ocr { get; init; }
    public IPdfRasterizer? Rasterizer { get; init; }

    /// <summary>Resolution used to render pages for OCR and for the redacted images.</summary>
    public int Dpi { get; init; } = 200;

    public DiagramDetection Diagrams { get; init; } = DiagramDetection.Auto;
}

public interface IDocumentReader
{
    IReadOnlyCollection<string> Extensions { get; }

    Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions options, CancellationToken ct);
}

/// <summary>Picks the reader by file extension.</summary>
public sealed class DocumentExtractor
{
    private readonly List<IDocumentReader> _readers =
    [
        new PlainTextReader(),
        new PdfDocumentReader(),
        new DocxReader(),
        new XlsxReader(),
        new PptxReader(),
        new ImageReader(),
        new DwsimReader(),
        new DxfReader(),
        new SvgReader(),
        new VisioReader(),
    ];

    public ExtractionOptions Options { get; }

    public DocumentExtractor(ExtractionOptions options) => Options = options;

    public static IReadOnlyCollection<string> SupportedExtensions =>
        new DocumentExtractor(new ExtractionOptions())._readers.SelectMany(r => r.Extensions).ToList();

    public bool CanRead(string path) => Find(path) is not null;

    private IDocumentReader? Find(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return _readers.FirstOrDefault(r => r.Extensions.Contains(ext));
    }

    public Task<ExtractedDocument> ReadAsync(string path, CancellationToken ct = default)
    {
        var reader = Find(path) ?? throw new NotSupportedException($"Formato não suportado: {Path.GetExtension(path)}");
        return Task.Run(() => reader.ReadAsync(path, Options, ct), ct);
    }

    /// <summary>Wraps pasted text as a document.</summary>
    public static ExtractedDocument FromText(string text, string name = "texto colado")
    {
        var b = new DocumentBuilder(name, "paste");
        b.Paragraph(text.Replace("\r\n", "\n"), "texto colado");
        return b.Build();
    }
}
