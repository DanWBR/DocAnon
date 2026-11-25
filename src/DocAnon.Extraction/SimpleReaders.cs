using System.Text;
using DocAnon.Core.Model;

namespace DocAnon.Extraction;

/// <summary>Plain text, Markdown and CSV.</summary>
public sealed class PlainTextReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".txt", ".md", ".markdown", ".csv", ".log", ".json", ".xml"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        string text = ReadText(File.ReadAllBytes(path)).Replace("\r\n", "\n");
        string ext = Path.GetExtension(path).ToLowerInvariant();
        var b = new DocumentBuilder(Path.GetFileName(path), ext.TrimStart('.'));
        if (ext == ".csv") b.Table(ParseCsv(text), "csv");
        else b.Paragraph(text, "texto");
        return Task.FromResult(b.Build());
    }

    /// <summary>UTF-8 with or without BOM, UTF-16 with BOM, or Windows-1252 when the bytes are not valid UTF-8.</summary>
    public static string ReadText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    internal static List<IReadOnlyList<string>> ParseCsv(string text)
    {
        var lines = text.Split('\n');
        char sep = lines.Take(5).Sum(l => l.Count(c => c == ';')) > lines.Take(5).Sum(l => l.Count(c => c == ',')) ? ';' : ',';
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0) continue;
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == sep && !quoted)
                {
                    cells.Add(sb.ToString());
                    sb.Clear();
                }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            rows.Add(cells);
        }
        return rows;
    }
}

/// <summary>Raster images, read through OCR.</summary>
public sealed class ImageReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        var b = new DocumentBuilder(Path.GetFileName(path), "image");
        ImageTools.AddImage(b, o, File.ReadAllBytes(path), "imagem", asLabels: true);
        return Task.FromResult(b.Build());
    }
}
