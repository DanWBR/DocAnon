using System.Text;
using DocAnon.Core.Model;

namespace DocAnon.Extraction;

/// <summary>A word with its box in the pixel space of a visual page.</summary>
public sealed record PlacedWord(string Text, BoundingBox Box, int Line = -1, float Confidence = 1);

/// <summary>Builds the Markdown text of a document together with its blocks and visual pages.</summary>
public sealed class DocumentBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly string _fileName;
    private readonly string _format;

    public DocumentBuilder(string fileName, string format)
    {
        _fileName = fileName;
        _format = format;
    }

    public List<DocumentBlock> Blocks { get; } = [];
    public List<VisualPage> Pages { get; } = [];
    public List<string> Warnings { get; } = [];
    public int Position => _sb.Length;

    private void EnsureBlankLine()
    {
        if (_sb.Length == 0) return;
        if (_sb[^1] != '\n') _sb.Append('\n');
        if (_sb.Length >= 2 && _sb[^2] != '\n') _sb.Append('\n');
    }

    private void Record(int start, BlockKind kind, string source, bool ocr = false, double? conf = null)
    {
        int len = _sb.Length - start;
        if (len > 0) Blocks.Add(new DocumentBlock(start, len, kind, source) { FromOcr = ocr, OcrConfidence = conf });
    }

    public void Heading(string text, int level, string source)
    {
        text = OneLine(text);
        if (text.Length == 0) return;
        EnsureBlankLine();
        int start = _sb.Length;
        _sb.Append(new string('#', Math.Clamp(level, 1, 6))).Append(' ').Append(text);
        Record(start, BlockKind.Heading, source);
    }

    public void Paragraph(string text, string source, BlockKind kind = BlockKind.Paragraph)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        EnsureBlankLine();
        int start = _sb.Length;
        _sb.Append(text);
        Record(start, kind, source);
    }

    public void ListItems(IEnumerable<string> items, string source) => ListItemsWithOffsets(items, source);

    /// <summary>
    /// Writes a bulleted list and returns, for each input item, where its text landed in the
    /// document. Empty items are skipped and get (-1, 0).
    /// </summary>
    public List<(int Start, int Length)> ListItemsWithOffsets(IEnumerable<string> items, string source)
    {
        var input = items.Select(OneLine).ToList();
        var offsets = new List<(int, int)>(input.Count);
        if (input.All(i => i.Length == 0))
        {
            offsets.AddRange(input.Select(_ => (-1, 0)));
            return offsets;
        }
        EnsureBlankLine();
        int start = _sb.Length;
        bool first = true;
        foreach (var item in input)
        {
            if (item.Length == 0)
            {
                offsets.Add((-1, 0));
                continue;
            }
            if (!first) _sb.Append('\n');
            first = false;
            _sb.Append("- ");
            offsets.Add((_sb.Length, item.Length));
            _sb.Append(item);
        }
        Record(start, BlockKind.List, source);
        return offsets;
    }

    /// <summary>Writes a Markdown table. The first row is the header.</summary>
    public void Table(IReadOnlyList<IReadOnlyList<string>> rows, string source)
    {
        if (rows.Count == 0) return;
        int cols = rows.Max(r => r.Count);
        if (cols == 0) return;
        EnsureBlankLine();
        int start = _sb.Length;
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0) _sb.Append('\n');
            _sb.Append('|');
            for (int c = 0; c < cols; c++)
            {
                string cell = c < rows[r].Count ? Cell(rows[r][c]) : "";
                _sb.Append(' ').Append(cell).Append(" |");
            }
            if (r == 0)
            {
                _sb.Append('\n').Append('|');
                for (int c = 0; c < cols; c++) _sb.Append("---|");
            }
        }
        Record(start, BlockKind.Table, source);
    }

    /// <summary>
    /// Writes placed words either as paragraphs or as a list of labels (for diagrams), and links
    /// each word to <paramref name="page"/> when one is given.
    /// </summary>
    public void PlacedText(IReadOnlyList<PlacedWord> words, string source, bool asLabels, VisualPage? page, bool fromOcr, double? confidence = null)
    {
        if (words.Count == 0) return;
        var groups = asLabels ? WordLayout.Labels(words) : WordLayout.Paragraphs(words);
        if (groups.Count == 0) return;
        EnsureBlankLine();
        int start = _sb.Length;
        bool firstGroup = true;
        foreach (var group in groups)
        {
            if (!firstGroup) _sb.Append(asLabels ? "\n" : "\n\n");
            firstGroup = false;
            if (asLabels) _sb.Append("- ");
            bool firstLine = true;
            foreach (var line in group)
            {
                if (!firstLine) _sb.Append(asLabels ? ' ' : '\n');
                firstLine = false;
                bool firstWord = true;
                foreach (var w in line)
                {
                    if (!firstWord) _sb.Append(' ');
                    firstWord = false;
                    int ws = _sb.Length;
                    string t = w.Text.Replace('\n', ' ');
                    _sb.Append(t);
                    page?.Words.Add(new VisualWord(ws, t.Length, w.Box));
                }
            }
        }
        Record(start, asLabels ? BlockKind.Diagram : BlockKind.Paragraph, source, fromOcr, confidence);
    }

    public ExtractedDocument Build(Func<IReadOnlyList<TextReplacement>, Stream, Task>? nativeCopy = null, string? nativeExt = null)
    {
        var doc = new ExtractedDocument(_fileName, _format, _sb.ToString().TrimEnd() + "\n")
        {
            WriteNativeCopy = nativeCopy,
            NativeCopyExtension = nativeExt,
        };
        doc.Blocks.AddRange(Blocks);
        doc.VisualPages.AddRange(Pages);
        doc.Warnings.AddRange(Warnings);
        return doc;
    }

    private static string OneLine(string s) => string.Join(' ', s.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Cell(string s) => OneLine(s).Replace("|", "/");
}
