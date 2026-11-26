using System.Net;
using System.Text;
using System.Text.Json;
using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;

namespace DocAnon.Extraction.Native;

/// <summary>Anonymized and restored copies of text files: plain text, CSV, JSON, XML and HTML.</summary>
internal static class PlainCopies
{
    /// <summary>A text file as it was read: its text, whether it had a BOM, and its CSV fields when it is a table.</summary>
    private sealed class TextFile
    {
        public required string Text { get; set; }
        public bool Bom { get; init; }
        public char Separator { get; init; }
        public List<List<CsvField>>? Rows { get; init; }
    }

    private sealed class CsvField
    {
        public required string Value { get; set; }
        public bool Quoted { get; init; }
    }

    private static TextFile Read(byte[] bytes, bool csv)
    {
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text = PlainTextReader.ReadText(bytes);
        if (!csv) return new TextFile { Text = text, Bom = bom };
        var lines = text.Split('\n');
        char sep = lines.Take(5).Sum(l => l.Count(c => c == ';')) > lines.Take(5).Sum(l => l.Count(c => c == ',')) ? ';' : ',';
        return new TextFile { Text = text, Bom = bom, Separator = sep, Rows = ParseCsv(text, sep) };
    }

    private static byte[] Write(TextFile f)
    {
        string text = f.Rows is null ? f.Text : WriteCsv(f.Rows, f.Separator, f.Text.Contains("\r\n") ? "\r\n" : "\n", f.Text.EndsWith('\n'));
        var enc = new UTF8Encoding(f.Bom);
        return [.. enc.GetPreamble(), .. enc.GetBytes(text)];
    }

    private static TextMap Map(TextFile f)
    {
        var map = new TextMap();
        if (f.Rows is null)
        {
            map.Add(f.Text, v => f.Text = v);
            return map;
        }
        // CSV rows as a Markdown table, so a unit in the header applies to the numbers below it.
        bool first = true;
        foreach (var row in f.Rows)
        {
            map.Separator("|");
            foreach (var field in row)
            {
                map.Separator(" ");
                var fld = field;
                map.Add(field.Value.Replace('\n', ' ').Replace('|', '/'), v => fld.Value = v);
                map.Separator(" |");
            }
            map.Separator("\n");
            if (first)
            {
                map.Separator("|" + string.Concat(Enumerable.Repeat("---|", Math.Max(1, row.Count))) + "\n");
                first = false;
            }
        }
        return map;
    }

    public static Func<NativeCopyContext, Stream, Task> CopyWriter(byte[] bytes, bool csv) => async (c, output) =>
    {
        var f = Read(bytes, csv);
        var map = Map(f);
        TextMap.CheckLeaks(c, map.Anonymize(c), "cópia");
        await output.WriteAsync(Write(f));
    };

    public static (byte[] Bytes, List<RestoredItem> Items) Restore(byte[] bytes, string extension, EntityRegistry registry, ScaleFactors factors)
    {
        bool csv = extension is ".csv" or ".tsv";
        var f = Read(bytes, csv);
        Func<string, string> encode = extension switch
        {
            ".html" or ".htm" or ".xml" or ".svg" => WebUtility.HtmlEncode,
            ".json" => s => JsonEncodedText.Encode(s, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString(),
            _ => s => s,
        };
        if (f.Rows is not null)
        {
            var items = Map(f).Restore(registry, factors, unscale: true);
            return (Write(f), items);
        }
        // Real values may hold characters that HTML, XML or JSON have to escape.
        var r = Reidentifier.Restore(f.Text, registry, factors);
        var sb = new StringBuilder(f.Text.Length);
        int cursor = 0;
        foreach (var i in r.Items.Where(i => i.Kind != RestoreKind.UnknownMarker).OrderBy(i => i.SourceStart))
        {
            sb.Append(f.Text, cursor, i.SourceStart - cursor).Append(i.Kind == RestoreKind.Marker ? encode(i.Restored) : i.Restored);
            cursor = i.SourceStart + i.Found.Length;
        }
        sb.Append(f.Text, cursor, f.Text.Length - cursor);
        f.Text = sb.ToString();
        return (Write(f), [.. r.Items]);
    }

    // ---- CSV ------------------------------------------------------------------------------------

    private static List<List<CsvField>> ParseCsv(string text, char sep)
    {
        var rows = new List<List<CsvField>>();
        var row = new List<CsvField>();
        var sb = new StringBuilder();
        bool quoted = false, wasQuoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
                continue;
            }
            if (ch == '"' && sb.Length == 0)
            {
                quoted = wasQuoted = true;
                continue;
            }
            if (ch == sep)
            {
                row.Add(new CsvField { Value = sb.ToString(), Quoted = wasQuoted });
                sb.Clear();
                wasQuoted = false;
                continue;
            }
            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue;
            if (ch == '\n')
            {
                row.Add(new CsvField { Value = sb.ToString(), Quoted = wasQuoted });
                rows.Add(row);
                row = [];
                sb.Clear();
                wasQuoted = false;
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0 || row.Count > 0) row.Add(new CsvField { Value = sb.ToString(), Quoted = wasQuoted });
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    private static string WriteCsv(List<List<CsvField>> rows, char sep, string newline, bool trailingNewline)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows.Count; r++)
        {
            if (r > 0) sb.Append(newline);
            for (int c = 0; c < rows[r].Count; c++)
            {
                if (c > 0) sb.Append(sep);
                var f = rows[r][c];
                bool quote = f.Quoted || f.Value.IndexOfAny([sep, '"', '\n', '\r']) >= 0;
                sb.Append(quote ? "\"" + f.Value.Replace("\"", "\"\"") + "\"" : f.Value);
            }
        }
        if (trailingNewline) sb.Append(newline);
        return sb.ToString();
    }
}
