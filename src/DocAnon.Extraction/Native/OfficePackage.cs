using System.IO.Compression;
using System.Xml.Linq;

namespace DocAnon.Extraction.Native;

internal enum OfficeKind
{
    Word,
    PowerPoint,
    Excel,
}

/// <summary>
/// A DOCX, PPTX or XLSX opened as a ZIP of XML parts. The parts are edited as XDocuments and
/// written back in their original order, so the copy keeps everything the app does not touch.
/// </summary>
internal sealed class OfficePackage
{
    public static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _renamed = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, XDocument> Xml { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> Binary { get; } = new(StringComparer.OrdinalIgnoreCase);
    public OfficeKind Kind { get; }

    private OfficePackage(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        foreach (var e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            _order.Add(e.FullName);
            using var s = e.Open();
            // System.IO.Packaging stores the core properties as a .psmdcp part, which is XML as well.
            if (e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                || e.FullName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            {
                Xml[e.FullName] = XDocument.Load(s, LoadOptions.PreserveWhitespace);
            }
            else
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                Binary[e.FullName] = ms.ToArray();
            }
        }
        Kind = Xml.ContainsKey("word/document.xml") ? OfficeKind.Word
            : Xml.ContainsKey("ppt/presentation.xml") ? OfficeKind.PowerPoint
            : Xml.ContainsKey("xl/workbook.xml") ? OfficeKind.Excel
            : throw new InvalidDataException("O arquivo não é um documento do Word, do PowerPoint nem do Excel.");
    }

    public static OfficePackage Load(byte[] bytes) => new(bytes);

    public IEnumerable<string> Names => _order.Select(n => _renamed.GetValueOrDefault(n, n));

    /// <summary>Parts whose name matches, in numeric order (slide2 before slide10).</summary>
    public List<string> Parts(Func<string, bool> match) => Xml.Keys.Where(match)
        .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Renames a binary part (a JPEG replaced by a PNG, for example) and updates the relationships
    /// that point to it and the content types.
    /// </summary>
    public void RenameBinary(string from, string to)
    {
        if (!Binary.Remove(from, out var data)) return;
        Binary[to] = data;
        string original = _renamed.FirstOrDefault(kv => kv.Value.Equals(from, StringComparison.OrdinalIgnoreCase)).Key ?? from;
        _renamed[original] = to;
        string fromFile = Path.GetFileName(from), toFile = Path.GetFileName(to);
        foreach (var (name, doc) in Xml.Where(x => x.Key.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var rel in doc.Descendants(Rel + "Relationship"))
            {
                var target = rel.Attribute("Target");
                if (target is null || rel.Attribute("TargetMode")?.Value == "External") continue;
                if (target.Value.EndsWith("/" + fromFile, StringComparison.OrdinalIgnoreCase) || target.Value.Equals(fromFile, StringComparison.OrdinalIgnoreCase))
                    target.Value = target.Value[..^fromFile.Length] + toFile;
            }
        }
        if (Xml.TryGetValue("[Content_Types].xml", out var ct) && ct.Root is { } root)
        {
            root.Elements(Ct + "Override").Where(o => string.Equals((string?)o.Attribute("PartName"), "/" + from, StringComparison.OrdinalIgnoreCase)).Remove();
            string ext = Path.GetExtension(to).TrimStart('.');
            if (!root.Elements(Ct + "Default").Any(d => string.Equals((string?)d.Attribute("Extension"), ext, StringComparison.OrdinalIgnoreCase)))
                root.AddFirst(new XElement(Ct + "Default", new XAttribute("Extension", ext), new XAttribute("ContentType", ext == "png" ? "image/png" : "application/octet-stream")));
        }
    }

    public byte[] Save()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in Names)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                if (Xml.TryGetValue(name, out var doc)) doc.Save(s, SaveOptions.DisableFormatting);
                else if (Binary.TryGetValue(name, out var data)) s.Write(data);
            }
        }
        return ms.ToArray();
    }

    // ---- text of the parts ----------------------------------------------------------------------

    /// <summary>Text of an element made of several text nodes, such as a w:t with comments inside.</summary>
    public static string TextOf(XElement e) => string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value));

    /// <summary>Writes a value into an element that holds text, keeping a single text node.</summary>
    public static void SetText(XElement e, string value)
    {
        var nodes = e.Nodes().OfType<XText>().ToList();
        if (nodes.Count == 0) e.Add(new XText(value));
        else
        {
            nodes[0].Value = value;
            foreach (var n in nodes.Skip(1)) n.Remove();
        }
        // Word drops leading and trailing spaces of a w:t unless xml:space is set.
        if (e.Name == W + "t" && value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            e.SetAttributeValue(XNamespace.Xml + "space", "preserve");
    }

    public void AddTextNode(TextMap map, XElement e) => map.Add(TextOf(e), v => SetText(e, v));

    /// <summary>The main text of the document as one map: Word paragraphs and tables, slides and notes, or the cells of a workbook.</summary>
    public (TextMap Map, HashSet<string> Parts) MainText(bool? excelNumbers = null)
    {
        var map = new TextMap();
        var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        switch (Kind)
        {
            case OfficeKind.Word:
                foreach (var name in Parts(n => n.StartsWith("word/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(n) is var f && (f == "document.xml" || f.StartsWith("header") || f.StartsWith("footer") || f is "footnotes.xml" or "endnotes.xml" or "comments.xml")))
                {
                    parts.Add(name);
                    if (Xml[name].Root is { } root) WordBlocks(map, root);
                    map.Break();
                }
                break;
            case OfficeKind.PowerPoint:
                foreach (var name in Parts(n => (n.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) || n.StartsWith("ppt/notesSlides/", StringComparison.OrdinalIgnoreCase))
                    && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add(name);
                    if (Xml[name].Root is { } root) DrawingBlocks(map, root);
                    map.Break();
                }
                break;
            case OfficeKind.Excel:
                ExcelStrings(map, parts);
                break;
        }
        return (map, parts);
    }

    private void WordBlocks(TextMap map, XElement container)
    {
        foreach (var el in container.Elements())
        {
            if (el.Name == W + "p")
            {
                WordParagraph(map, el, inTable: false);
                map.Break();
            }
            else if (el.Name == W + "tbl")
            {
                map.Break();
                bool first = true;
                foreach (var row in el.Elements(W + "tr"))
                {
                    var cells = row.Elements(W + "tc").ToList();
                    map.Separator("|");
                    foreach (var cell in cells)
                    {
                        map.Separator(" ");
                        bool firstPara = true;
                        foreach (var p in cell.Descendants(W + "p"))
                        {
                            if (!firstPara) map.Separator(" ");
                            firstPara = false;
                            WordParagraph(map, p, inTable: true, descendants: false);
                        }
                        map.Separator(" |");
                    }
                    map.Separator("\n");
                    if (first)
                    {
                        map.Separator("|" + string.Concat(Enumerable.Repeat("---|", Math.Max(1, cells.Count))) + "\n");
                        first = false;
                    }
                }
                map.Break();
            }
            else if (el.Descendants(W + "p").Any()) WordBlocks(map, el.Name == W + "sdt" ? el.Element(W + "sdtContent") ?? el : el);
        }
    }

    /// <summary>The runs of a paragraph. Inside a table cell, nested paragraphs are visited by the caller, so only direct runs count.</summary>
    private void WordParagraph(TextMap map, XElement p, bool inTable, bool descendants = true)
    {
        IEnumerable<XElement> nodes = descendants ? p.Descendants() : p.Descendants().Where(d => d.Ancestors(W + "p").First() == p);
        foreach (var d in nodes)
        {
            if (d.Name == W + "t") AddTextNode(map, d);
            else if (d.Name == W + "tab") map.Separator("\t");
            else if (d.Name == W + "br" || d.Name == W + "cr") map.Separator(inTable ? " " : "\n");
        }
    }

    private void DrawingBlocks(TextMap map, XElement root)
    {
        foreach (var el in root.Descendants())
        {
            if (el.Name == A + "tbl")
            {
                map.Break();
                bool first = true;
                foreach (var row in el.Elements(A + "tr"))
                {
                    var cells = row.Elements(A + "tc").ToList();
                    map.Separator("|");
                    foreach (var cell in cells)
                    {
                        map.Separator(" ");
                        bool firstPara = true;
                        foreach (var p in cell.Descendants(A + "p"))
                        {
                            if (!firstPara) map.Separator(" ");
                            firstPara = false;
                            foreach (var t in p.Descendants(A + "t")) AddTextNode(map, t);
                        }
                        map.Separator(" |");
                    }
                    map.Separator("\n");
                    if (first)
                    {
                        map.Separator("|" + string.Concat(Enumerable.Repeat("---|", Math.Max(1, cells.Count))) + "\n");
                        first = false;
                    }
                }
                map.Break();
            }
            else if (el.Name == A + "p" && !el.Ancestors(A + "tbl").Any())
            {
                foreach (var d in el.Descendants())
                {
                    if (d.Name == A + "t") AddTextNode(map, d);
                    else if (d.Name == A + "br") map.Separator("\n");
                }
                map.Break();
            }
        }
    }

    private void ExcelStrings(TextMap map, HashSet<string> parts)
    {
        if (Xml.TryGetValue("xl/sharedStrings.xml", out var sst) && sst.Root is { } root)
        {
            parts.Add("xl/sharedStrings.xml");
            foreach (var si in root.Elements(S + "si"))
            {
                foreach (var t in si.Descendants(S + "t").Where(t => !t.Ancestors(S + "rPh").Any())) AddTextNode(map, t);
                map.Break();
            }
        }
        foreach (var (name, _) in Sheets())
        {
            foreach (var t in Xml[name].Descendants(S + "is").SelectMany(i => i.Descendants(S + "t"))) AddTextNode(map, t);
            map.Break();
        }
    }

    // ---- Excel ----------------------------------------------------------------------------------

    /// <summary>The worksheets in workbook order, with whether each is visible.</summary>
    public List<(string Part, bool Visible)> Sheets()
    {
        var result = new List<(string, bool)>();
        if (!Xml.TryGetValue("xl/workbook.xml", out var wb) || !Xml.TryGetValue("xl/_rels/workbook.xml.rels", out var rels)) return result;
        var targets = rels.Descendants(Rel + "Relationship").ToDictionary(r => (string?)r.Attribute("Id") ?? "", r => (string?)r.Attribute("Target") ?? "");
        foreach (var sheet in wb.Descendants(S + "sheet"))
        {
            string id = (string?)sheet.Attribute(R + "id") ?? "";
            if (!targets.TryGetValue(id, out var target)) continue;
            string part = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
            if (!Xml.ContainsKey(part)) continue;
            string state = (string?)sheet.Attribute("state") ?? "visible";
            result.Add((part, state == "visible"));
        }
        return result;
    }

    /// <summary>
    /// The numeric cells of every visible sheet as Markdown tables (the text cells stay as layout),
    /// so the quantity detector can read the unit of a column from its header. Numbers are written
    /// as the reader writes them, and the setter stores the new value back in the cell.
    /// </summary>
    public TextMap ExcelNumbers(Action<string> warn)
    {
        var map = new TextMap();
        var sst = Xml.TryGetValue("xl/sharedStrings.xml", out var s)
            ? s.Root?.Elements(S + "si").Select(si => string.Concat(si.Descendants(S + "t").Where(t => !t.Ancestors(S + "rPh").Any()).Select(TextOf)).Trim()).ToArray() ?? []
            : [];
        var dates = DateStyles();
        foreach (var (name, visible) in Sheets())
        {
            if (!visible) continue;
            var grid = new SortedDictionary<int, SortedDictionary<int, XElement>>();
            foreach (var row in Xml[name].Descendants(S + "row"))
            {
                foreach (var cell in row.Elements(S + "c"))
                {
                    var (col, r) = ParseRef((string?)cell.Attribute("r"));
                    if (col == 0 || r == 0) continue;
                    if (!grid.TryGetValue(r, out var cols)) grid[r] = cols = [];
                    cols[col] = cell;
                }
            }
            if (grid.Count == 0) continue;
            var used = grid.Values.SelectMany(c => c.Keys).Distinct().Order().ToList();
            map.Break();
            bool first = true;
            foreach (var row in grid.Values)
            {
                map.Separator("|");
                foreach (int col in used)
                {
                    map.Separator(" ");
                    if (row.TryGetValue(col, out var cell)) ExcelCell(map, cell, sst, dates, warn);
                    map.Separator(" |");
                }
                map.Separator("\n");
                if (first)
                {
                    map.Separator("|" + string.Concat(Enumerable.Repeat("---|", used.Count)) + "\n");
                    first = false;
                }
            }
            map.Break();
        }
        return map;
    }

    private static void ExcelCell(TextMap map, XElement cell, string[] sst, HashSet<int> dates, Action<string> warn)
    {
        string type = (string?)cell.Attribute("t") ?? "n";
        var v = cell.Element(S + "v");
        if (type == "s")
        {
            if (v is not null && int.TryParse(v.Value, out int k) && k >= 0 && k < sst.Length) map.Separator(OneLine(sst[k]));
            return;
        }
        if (type == "inlineStr")
        {
            map.Separator(OneLine(string.Concat(cell.Descendants(S + "t").Select(TextOf))));
            return;
        }
        if (type is not "n" || v is null) return;
        if (!double.TryParse(v.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return;
        if (int.TryParse((string?)cell.Attribute("s"), out int style) && dates.Contains(style))
        {
            map.Separator("data");
            return;
        }
        string shown = d.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
        map.Add(shown, text =>
        {
            string clean = text.Replace(",", "").Replace(" ", "").Replace(" ", "");
            if (double.TryParse(clean, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double nv))
                v.Value = nv.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            else warn($"A célula {(string?)cell.Attribute("r")} recebeu \"{text}\", que não é um número; o valor original foi mantido.");
        });
    }

    private HashSet<int> DateStyles()
    {
        var result = new HashSet<int>();
        if (!Xml.TryGetValue("xl/styles.xml", out var st) || st.Root is null) return result;
        var custom = st.Root.Element(S + "numFmts")?.Elements(S + "numFmt")
            .ToDictionary(f => (int?)f.Attribute("numFmtId") ?? -1, f => (string?)f.Attribute("formatCode") ?? "") ?? [];
        int i = 0;
        foreach (var xf in st.Root.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
        {
            int id = (int?)xf.Attribute("numFmtId") ?? 0;
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

    private static string OneLine(string s) => string.Join(' ', s.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim().Replace("|", "/");

    // ---- everything else ------------------------------------------------------------------------

    /// <summary>Attributes that carry text a person wrote: alternative text and titles of pictures and shapes.</summary>
    private static readonly HashSet<string> TextAttributes = ["descr", "title"];

    /// <summary>
    /// Every other piece of text in the package (masters, layouts, charts, SmartArt, text in
    /// properties, alternative text), one node per line. Cell values and formulas of worksheets are
    /// left out, because a marker there would break the cell.
    /// </summary>
    public TextMap OtherText(HashSet<string> skip)
    {
        var map = new TextMap();
        foreach (var (name, doc) in Xml)
        {
            if (skip.Contains(name) || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) || name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase)) continue;
            bool sheet = name.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase);
            foreach (var e in doc.Descendants())
            {
                foreach (var a in e.Attributes().Where(a => TextAttributes.Contains(a.Name.LocalName) && a.Value.Length > 0))
                {
                    var attr = a;
                    map.Add(attr.Value, v => attr.Value = v);
                    map.Separator("\n");
                }
                if (e.HasElements || e.Nodes().OfType<XText>().All(t => t.Value.Trim().Length == 0)) continue;
                if (sheet && e.Name.LocalName is "v" or "f") continue;
                AddTextNode(map, e);
                map.Separator("\n");
            }
        }
        return map;
    }
}
