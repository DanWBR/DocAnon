namespace DocAnon.Core.Model;

public enum BlockKind
{
    Heading,
    Paragraph,
    Table,
    List,
    Note,
    Diagram,
    Metadata,
}

/// <summary>A region of <see cref="ExtractedDocument.Text"/> and where it came from in the source file.</summary>
public sealed record DocumentBlock(int Start, int Length, BlockKind Kind, string Source)
{
    public bool FromOcr { get; init; }

    /// <summary>Mean OCR confidence between 0 and 1, when the block came from OCR.</summary>
    public double? OcrConfidence { get; init; }
}

/// <summary>Axis-aligned box in the pixel space of a rendered page (origin at the top-left).</summary>
public readonly record struct BoundingBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public BoundingBox Union(BoundingBox o)
    {
        float x = Math.Min(X, o.X), y = Math.Min(Y, o.Y);
        return new BoundingBox(x, y, Math.Max(Right, o.Right) - x, Math.Max(Bottom, o.Bottom) - y);
    }

    public BoundingBox Inflate(float d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);

    public bool Intersects(BoundingBox o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>A word drawn on a visual page, linked to its position in the document text.</summary>
public sealed record VisualWord(int TextStart, int TextLength, BoundingBox Box);

/// <summary>A region to black out that does not come from text.</summary>
public sealed class CoverBox(BoundingBox box, string reason, string? label = null)
{
    public BoundingBox Box { get; } = box;
    public string Reason { get; } = reason;

    /// <summary>Text written on the box, such as the marker of the value it hides. Null writes "[LOGO]" on wide boxes.</summary>
    public string? Label { get; } = label;

    public bool Include { get; set; } = true;
}

/// <summary>A replacement to apply to the document text, in document offsets.</summary>
public sealed record TextReplacement(int Start, int Length, string Original, string Replacement, EntityType Type);

/// <summary>
/// A page or drawing that has to be exported as an image as well as text
/// (diagram pages of a PDF, scanned pages, embedded images, DXF and SVG drawings).
/// </summary>
public sealed class VisualPage
{
    public VisualPage(int index, string source, int widthPx, int heightPx, Func<byte[]> renderOriginalPng)
    {
        Index = index;
        Source = source;
        WidthPx = widthPx;
        HeightPx = heightPx;
        RenderOriginalPng = renderOriginalPng;
    }

    public int Index { get; }
    public string Source { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }

    /// <summary>Renders the page as it is in the source file.</summary>
    public Func<byte[]> RenderOriginalPng { get; }

    /// <summary>
    /// Set by formats that can redraw their own text (SVG, DXF). When it is null, the
    /// redaction step paints opaque boxes over <see cref="Words"/> instead.
    /// </summary>
    public Func<IReadOnlyList<TextReplacement>, byte[]>? RenderWithReplacements { get; init; }

    public List<VisualWord> Words { get; } = [];

    /// <summary>Zero-based PDF page this image belongs to, or -1 when it does not come from a PDF page.</summary>
    public int PdfPage { get; init; } = -1;

    /// <summary>Where an image cropped from a PDF page sits on that page, in the page's pixels. Null for a whole page.</summary>
    public BoundingBox? BoxOnPage { get; init; }

    /// <summary>
    /// True once every piece of text on the page is known, from the text layer or from OCR.
    /// The export refuses pages that are not verified, because their text could not be redacted.
    /// </summary>
    public bool TextVerified { get; set; }

    /// <summary>Boxes the user drew by hand on the review screen. They are always blacked out.</summary>
    public List<BoundingBox> ManualBoxes { get; } = [];

    /// <summary>
    /// Boxes the app proposes to black out without text: small embedded images (logos, stamps,
    /// scanned signatures), matches of the company logo and fixed regions of the profile.
    /// </summary>
    public List<CoverBox> AutoCovers { get; } = [];

    /// <summary>Whether the page goes out as an image. Small decorative images start unchecked.</summary>
    public bool Export { get; set; } = true;
}

public sealed class ExtractedDocument
{
    public ExtractedDocument(string fileName, string format, string text)
    {
        FileName = fileName;
        Format = format;
        Text = text;
    }

    public string FileName { get; }

    /// <summary>"pdf", "docx", "xlsx", "pptx", "txt", "md", "image", "dwsim", "dxf", "svg", "vsdx", "paste".</summary>
    public string Format { get; }

    /// <summary>The whole document as Markdown. All detection works on this string.</summary>
    public string Text { get; }

    public List<DocumentBlock> Blocks { get; } = [];
    public List<VisualPage> VisualPages { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Pages that only go into the anonymized copy in the source format: the text pages of a
    /// PDF, which the text export does not send as images.
    /// </summary>
    public List<VisualPage> NativePages { get; } = [];

    /// <summary>Writes an anonymized copy of the source file in its native format (DOCX, PPTX, XLSX, CSV, Visio).</summary>
    public Func<NativeCopyContext, Stream, Task>? WriteNativeCopy { get; init; }

    public string? NativeCopyExtension { get; init; }
}

/// <summary>What a native-copy writer needs: the reviewed replacements and the session that made them.</summary>
public sealed class NativeCopyContext
{
    /// <summary>The replacements applied to <see cref="ExtractedDocument.Text"/>, in document offsets.</summary>
    public required IReadOnlyList<TextReplacement> Replacements { get; init; }

    public required DocAnon.Core.Pseudonymization.EntityRegistry Registry { get; init; }
    public required DocAnon.Core.Quantities.ScaleFactors Factors { get; init; }
    public DocAnon.Core.Profiles.CompanyProfile? Profile { get; init; }

    /// <summary>The decimal separator that the review used, so the copy reads numbers the same way.</summary>
    public bool? CommaIsDecimal { get; init; }

    /// <summary>The redacted PNG of each exported visual page. Pages that are missing here were not exported.</summary>
    public IReadOnlyDictionary<VisualPage, byte[]> RedactedImages { get; init; } = new Dictionary<VisualPage, byte[]>();

    /// <summary>Real values that the writer still finds in the copy after the replacements.</summary>
    public List<DocAnon.Core.Export.LeakFinding> Leaks { get; } = [];

    public List<string> Warnings { get; } = [];
}
