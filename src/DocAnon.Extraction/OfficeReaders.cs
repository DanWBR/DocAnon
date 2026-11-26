using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocAnon.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocAnon.Extraction;

internal static class OpenXmlImages
{
    public static byte[]? Read(OpenXmlPart owner, string? relId) => ReadNamed(owner, relId).Data;

    /// <summary>The bytes of a picture and the name of its part inside the package ("word/media/image1.png").</summary>
    public static (byte[]? Data, string? Part) ReadNamed(OpenXmlPart owner, string? relId)
    {
        if (string.IsNullOrEmpty(relId)) return (null, null);
        try
        {
            if (owner.GetPartById(relId) is not ImagePart part) return (null, null);
            using var s = part.GetStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return (ms.ToArray(), part.Uri.OriginalString.TrimStart('/'));
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, null);
        }
    }

    /// <summary>The native-copy writer of an Office file: markers and scaled numbers in the text, redacted pictures.</summary>
    public static Func<NativeCopyContext, Stream, Task> CopyWriter(byte[] bytes, IReadOnlyDictionary<string, VisualPage> mediaPages) =>
        async (c, output) => await output.WriteAsync(Native.OfficeCopies.Anonymize(bytes, c, mediaPages));
}

/// <summary>Word documents: body, tables, headers, footers, footnotes, comments and embedded images.</summary>
public sealed class DocxReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".docx", ".docm", ".dotx"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        var b = new DocumentBuilder(Path.GetFileName(path), "docx");
        byte[] bytes = File.ReadAllBytes(path);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var main = doc.MainDocumentPart ?? throw new InvalidDataException("DOCX sem corpo.");
        int images = 0;
        var media = new Dictionary<string, VisualPage>(StringComparer.OrdinalIgnoreCase);

        void Walk(OpenXmlElement container, OpenXmlPart owner, string source)
        {
            foreach (var el in container.Elements())
            {
                ct.ThrowIfCancellationRequested();
                switch (el)
                {
                    case W.Paragraph p:
                        Paragraph(p, owner, source);
                        break;
                    case W.Table t:
                        b.Table(t.Elements<W.TableRow>()
                            .Select(r => (IReadOnlyList<string>)r.Elements<W.TableCell>().Select(c => string.Join(' ', c.Descendants<W.Paragraph>().Select(RunText))).ToList())
                            .ToList(), source);
                        foreach (var blip in t.Descendants<A.Blip>()) Image(owner, blip.Embed?.Value, source);
                        break;
                    case W.SdtBlock sdt when sdt.SdtContentBlock is { } content:
                        Walk(content, owner, source);
                        break;
                }
            }
        }

        void Paragraph(W.Paragraph p, OpenXmlPart owner, string source)
        {
            string text = RunText(p);
            string? style = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            int level = HeadingLevel(style);
            if (level > 0) b.Heading(text, level, source);
            else if (p.ParagraphProperties?.NumberingProperties is not null) b.ListItems([text], source);
            else b.Paragraph(text, source);
            foreach (var blip in p.Descendants<A.Blip>()) Image(owner, blip.Embed?.Value, source);
        }

        void Image(OpenXmlPart owner, string? relId, string source)
        {
            var (data, part) = OpenXmlImages.ReadNamed(owner, relId);
            if (data is null) return;
            images++;
            if (ImageTools.AddImage(b, o, data, $"{source}, imagem {images}") is { } page && part is not null) media.TryAdd(part, page);
        }

        Walk(main.Document?.Body ?? throw new InvalidDataException("DOCX sem corpo."), main, "corpo");

        var seen = new HashSet<string>();
        foreach (var h in main.HeaderParts)
        {
            string t = string.Join('\n', (h.Header?.Descendants<W.Paragraph>() ?? []).Select(RunText)).Trim();
            if (t.Length > 0 && seen.Add(t)) b.Paragraph("Cabeçalho: " + t, "cabeçalho", BlockKind.Note);
        }
        foreach (var f in main.FooterParts)
        {
            string t = string.Join('\n', (f.Footer?.Descendants<W.Paragraph>() ?? []).Select(RunText)).Trim();
            if (t.Length > 0 && seen.Add(t)) b.Paragraph("Rodapé: " + t, "rodapé", BlockKind.Note);
        }
        if (main.FootnotesPart?.Footnotes is { } fns)
        {
            foreach (var fn in fns.Elements<W.Footnote>())
            {
                string t = string.Join(' ', fn.Descendants<W.Paragraph>().Select(RunText)).Trim();
                if (t.Length > 0) b.Paragraph("Nota de rodapé: " + t, "nota de rodapé", BlockKind.Note);
            }
        }
        if (main.WordprocessingCommentsPart?.Comments is { } comments)
        {
            foreach (var c in comments.Elements<W.Comment>())
            {
                string t = string.Join(' ', c.Descendants<W.Paragraph>().Select(RunText)).Trim();
                if (t.Length > 0) b.Paragraph($"Comentário de {c.Author?.Value ?? "?"}: {t}", "comentário", BlockKind.Note);
            }
        }
        return Task.FromResult(b.Build(OpenXmlImages.CopyWriter(bytes, media), Path.GetExtension(path).ToLowerInvariant()));
    }

    private static int HeadingLevel(string? style)
    {
        if (style is null) return 0;
        string s = style.ToLowerInvariant();
        if (s is "title" or "ttulo" or "titulo") return 1;
        if (s.StartsWith("heading") || s.StartsWith("ttulo") || s.StartsWith("titulo"))
        {
            string digits = new(s.Where(char.IsAsciiDigit).ToArray());
            return int.TryParse(digits, out int n) ? Math.Clamp(n, 1, 6) : 1;
        }
        return 0;
    }

    /// <summary>Visible text of a paragraph, without deleted revisions and field codes.</summary>
    internal static string RunText(W.Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var el in p.Descendants())
        {
            switch (el)
            {
                case W.Text t when t.Parent is not W.DeletedRun:
                    sb.Append(t.Text);
                    break;
                case W.TabChar:
                    sb.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    sb.Append('\n');
                    break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>Spreadsheets: each visible sheet becomes a Markdown table of calculated values.</summary>
public sealed class XlsxReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".xlsx", ".xlsm"];

    private const int MaxRows = 5000, MaxCols = 80;

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        var b = new DocumentBuilder(Path.GetFileName(path), "xlsx");
        byte[] bytes = File.ReadAllBytes(path);
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var wb = doc.WorkbookPart ?? throw new InvalidDataException("XLSX sem pasta de trabalho.");
        var sst = wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray() ?? [];
        var formats = wb.WorkbookStylesPart?.Stylesheet;
        var dateStyles = DateStyleIndexes(formats);

        foreach (var sheet in wb.Workbook?.Sheets?.Elements<Sheet>() ?? [])
        {
            ct.ThrowIfCancellationRequested();
            string name = sheet.Name?.Value ?? "Planilha";
            if (sheet.State?.Value is SheetStateValues v && v != SheetStateValues.Visible)
            {
                b.Warnings.Add($"A planilha oculta \"{name}\" não foi incluída.");
                continue;
            }
            if (sheet.Id?.Value is not string rid || wb.GetPartById(rid) is not WorksheetPart wsp) continue;

            var grid = new SortedDictionary<int, SortedDictionary<int, string>>();
            bool truncated = false;
            foreach (var row in (wsp.Worksheet?.Descendants<Row>() ?? []))
            {
                int r = (int)(row.RowIndex?.Value ?? 0);
                if (r > MaxRows)
                {
                    truncated = true;
                    break;
                }
                foreach (var cell in row.Elements<Cell>())
                {
                    var (col, _) = ParseRef(cell.CellReference?.Value);
                    if (col > MaxCols)
                    {
                        truncated = true;
                        continue;
                    }
                    string text = CellText(cell, sst, dateStyles);
                    if (text.Length == 0) continue;
                    if (!grid.TryGetValue(r, out var cols)) grid[r] = cols = [];
                    cols[col] = text;
                }
            }
            if (truncated) b.Warnings.Add($"A planilha \"{name}\" foi cortada em {MaxRows} linhas e {MaxCols} colunas.");
            if (grid.Count == 0) continue;

            var usedCols = grid.Values.SelectMany(c => c.Keys).Distinct().OrderBy(c => c).ToList();
            var rows = grid.Values.Select(c => (IReadOnlyList<string>)usedCols.Select(k => c.GetValueOrDefault(k, "")).ToList()).ToList();
            b.Heading($"Planilha {name}", 2, name);
            b.Table(rows, name);
        }
        return Task.FromResult(b.Build(OpenXmlImages.CopyWriter(bytes, new Dictionary<string, VisualPage>()), Path.GetExtension(path).ToLowerInvariant()));
    }

    private static HashSet<uint> DateStyleIndexes(Stylesheet? ss)
    {
        var result = new HashSet<uint>();
        if (ss?.CellFormats is null) return result;
        var custom = ss.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.NumberFormatId?.Value is not null)
            .ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode?.Value ?? "") ?? [];
        uint i = 0;
        foreach (var cf in ss.CellFormats.Elements<CellFormat>())
        {
            uint id = cf.NumberFormatId?.Value ?? 0;
            bool isDate = id is >= 14 and <= 22 or >= 45 and <= 47;
            if (!isDate && custom.TryGetValue(id, out var code))
            {
                string c = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]", "").ToLowerInvariant();
                isDate = c.Contains('y') || c.Contains('d') || (c.Contains('m') && !c.Contains('0') && !c.Contains('#'));
            }
            if (isDate) result.Add(i);
            i++;
        }
        return result;
    }

    private static (int Col, int Row) ParseRef(string? r)
    {
        if (string.IsNullOrEmpty(r)) return (0, 0);
        int col = 0, i = 0;
        while (i < r.Length && char.IsAsciiLetter(r[i])) col = col * 26 + (char.ToUpperInvariant(r[i++]) - 'A' + 1);
        int.TryParse(r.AsSpan(i), out int row);
        return (col, row);
    }

    private static string CellText(Cell c, string[] sst, HashSet<uint> dateStyles)
    {
        string raw = c.CellValue?.Text ?? "";
        var type = c.DataType?.Value;
        if (type == CellValues.SharedString) return int.TryParse(raw, out int k) && k < sst.Length ? sst[k].Trim() : "";
        if (type == CellValues.InlineString) return c.InlineString?.InnerText.Trim() ?? "";
        if (type == CellValues.Boolean) return raw == "1" ? "VERDADEIRO" : "FALSO";
        if (type == CellValues.Error) return "";
        if (type == CellValues.String) return raw.Trim();
        if (raw.Length == 0) return "";
        if (c.StyleIndex?.Value is uint si && dateStyles.Contains(si) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double oa) && oa is > 0 and < 2958466)
            return DateTime.FromOADate(oa).ToString(oa % 1 == 0 ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d.ToString("0.############", CultureInfo.InvariantCulture);
        return raw;
    }
}

/// <summary>Presentations: slide text, tables, notes and pictures.</summary>
public sealed class PptxReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".pptx", ".pptm"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        var b = new DocumentBuilder(Path.GetFileName(path), "pptx");
        byte[] bytes = File.ReadAllBytes(path);
        using var doc = PresentationDocument.Open(new MemoryStream(bytes), false);
        var pres = doc.PresentationPart ?? throw new InvalidDataException("PPTX sem apresentação.");
        int n = 0;
        var media = new Dictionary<string, VisualPage>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in pres.Presentation?.SlideIdList?.Elements<P.SlideId>() ?? [])
        {
            ct.ThrowIfCancellationRequested();
            n++;
            if (sid.RelationshipId?.Value is not string rid || pres.GetPartById(rid) is not SlidePart sp) continue;
            string source = $"slide {n}";
            b.Heading($"Slide {n}", 2, source);
            int img = 0;
            var tree = sp.Slide?.CommonSlideData?.ShapeTree;
            if (tree is null) continue;
            foreach (var el in tree.Descendants())
            {
                switch (el)
                {
                    case P.Shape shape when shape.TextBody is { } body:
                        foreach (var para in body.Elements<A.Paragraph>())
                        {
                            string t = string.Concat(para.Descendants<A.Text>().Select(x => x.Text));
                            if (para.ParagraphProperties?.Level?.Value > 0 || para.ParagraphProperties?.GetFirstChild<A.CharacterBullet>() is not null) b.ListItems([t], source);
                            else b.Paragraph(t, source);
                        }
                        break;
                    case A.Table table:
                        b.Table(table.Elements<A.TableRow>()
                            .Select(r => (IReadOnlyList<string>)r.Elements<A.TableCell>().Select(c => string.Join(' ', c.Descendants<A.Text>().Select(x => x.Text))).ToList())
                            .ToList(), source);
                        break;
                    case P.Picture pic:
                        var (data, part) = OpenXmlImages.ReadNamed(sp, pic.BlipFill?.Blip?.Embed?.Value);
                        if (data is not null)
                        {
                            img++;
                            if (ImageTools.AddImage(b, o, data, $"{source}, imagem {img}") is { } page && part is not null) media.TryAdd(part, page);
                        }
                        break;
                }
            }
            if (sp.NotesSlidePart?.NotesSlide is { } notes)
            {
                var texts = notes.Descendants<P.Shape>()
                    .Where(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value != P.PlaceholderValues.SlideNumber)
                    .Select(s => string.Join(' ', s.Descendants<A.Text>().Select(x => x.Text)).Trim())
                    .Where(t => t.Length > 0);
                string all = string.Join(' ', texts);
                if (all.Length > 0) b.Paragraph("Notas do slide: " + all, source, BlockKind.Note);
            }
        }
        return Task.FromResult(b.Build(OpenXmlImages.CopyWriter(bytes, media), Path.GetExtension(path).ToLowerInvariant()));
    }
}
