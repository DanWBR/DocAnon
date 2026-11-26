using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Writer;

namespace DocAnon.Extraction.Native;

/// <summary>
/// Restores a PDF that the AI wrote. The text of a PDF cannot be edited in place, so each marker
/// and each scaled number is covered with a white box, and the real value is written over it at
/// the same position and size. When the real value is longer than the marker, the font shrinks
/// down to 55 % so that it fits in the space up to the next word.
/// </summary>
internal static class PdfRestorer
{
    public static (byte[] Bytes, List<RestoredItem> Items, List<string> Warnings) Restore(byte[] source, EntityRegistry registry, ScaleFactors factors)
    {
        var items = new List<RestoredItem>();
        var warnings = new List<string>();
        using var doc = PdfDocument.Open(source);
        var builder = new PdfDocumentBuilder();
        var font = LoadFont(builder, bold: false) ?? throw new InvalidOperationException("O app não encontrou uma fonte TrueType no sistema para escrever os valores no PDF.");
        var boldFont = LoadFont(builder, bold: true) ?? font;

        foreach (var page in doc.GetPages())
        {
            var pb = builder.AddPage(doc, page.Number);
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            if (words.Count == 0) continue;

            // The page text, word by word, with each character linked to its letter.
            var text = new System.Text.StringBuilder();
            var letterAt = new List<(int Word, Letter Letter)>();
            Word? previous = null;
            for (int wi = 0; wi < words.Count; wi++)
            {
                var w = words[wi];
                if (previous is not null)
                {
                    bool newLine = Math.Abs(previous.BoundingBox.Bottom - w.BoundingBox.Bottom) > 0.5 * Math.Max(1, w.BoundingBox.Height);
                    text.Append(newLine ? '\n' : ' ');
                    letterAt.Add((-1, null!));
                }
                foreach (var l in w.Letters)
                {
                    foreach (char ch in l.Value)
                    {
                        text.Append(ch);
                        letterAt.Add((wi, l));
                    }
                }
                previous = w;
            }

            var r = Reidentifier.Restore(text.ToString(), registry, factors);
            items.AddRange(r.Items);
            foreach (var item in r.Items.Where(i => i.Kind != RestoreKind.UnknownMarker))
            {
                var letters = Enumerable.Range(item.SourceStart, item.Found.Length)
                    .Where(i => i < letterAt.Count && letterAt[i].Word >= 0)
                    .Select(i => letterAt[i])
                    .ToList();
                if (letters.Count == 0) continue;
                if (letters.Any(l => l.Letter.TextOrientation != TextOrientation.Horizontal))
                {
                    warnings.Add($"Página {page.Number}: \"{item.Found}\" está em texto girado e não foi trocado.");
                    continue;
                }
                // One box per line that the marker occupies; the real value goes in the first one.
                bool first = true;
                foreach (var line in letters.GroupBy(l => Math.Round(l.Letter.StartBaseLine.Y, 1)))
                {
                    var ls = line.Select(l => l.Letter).Distinct().ToList();
                    double size = ls.Max(l => l.PointSize > 0 ? l.PointSize : l.BoundingBox.Height);
                    double x0 = ls.Min(l => l.BoundingBox.Left), x1 = ls.Max(l => l.BoundingBox.Right);
                    double baseline = ls[0].StartBaseLine.Y;
                    double bottom = baseline - 0.25 * size, top = baseline + 0.95 * size;
                    pb.SetStrokeColor(255, 255, 255);
                    pb.SetTextAndFillColor(255, 255, 255);
                    pb.DrawRectangle(new PdfPoint(x0 - 0.5, bottom), x1 - x0 + 1, top - bottom, 0.1, fill: true);
                    if (!first) continue;
                    first = false;

                    int lastWord = line.Max(l => l.Word);
                    double room = x1 - x0;
                    if (lastWord + 1 < words.Count && Math.Abs(words[lastWord + 1].BoundingBox.Bottom - words[lastWord].BoundingBox.Bottom) < 0.5 * Math.Max(1, words[lastWord].BoundingBox.Height))
                        room = Math.Max(room, words[lastWord + 1].BoundingBox.Left - x0 - 0.25 * size);
                    // A value may hold a line break or a tab from the source document; on one line it is a space.
                    var face = ls[0].FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true ? boldFont : font;
                    string value = Printable(string.Join(' ', item.Restored.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), face, pb);
                    double width = Width(pb, value, size, face);
                    double fit = width > room && width > 0 ? Math.Max(0.55, room / width) : 1;
                    pb.SetTextAndFillColor(0, 0, 0);
                    pb.AddText(value, size * fit, new PdfPoint(x0, baseline), face);
                }
            }
        }
        return (builder.Build(), items, warnings);
    }

    private static double Width(PdfPageBuilder pb, string text, double size, PdfDocumentBuilder.AddedFont font)
    {
        var letters = pb.MeasureText(text, size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters.Max(l => l.EndBaseLine.X) - letters.Min(l => l.StartBaseLine.X);
    }

    /// <summary>Replaces the characters that the font cannot draw with "?", so one odd character does not stop the file.</summary>
    private static string Printable(string text, PdfDocumentBuilder.AddedFont font, PdfPageBuilder pb)
    {
        try
        {
            pb.MeasureText(text, 10, new PdfPoint(0, 0), font);
            return text;
        }
        catch (Exception)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in text)
            {
                try
                {
                    pb.MeasureText(ch.ToString(), 10, new PdfPoint(0, 0), font);
                    sb.Append(ch);
                }
                catch (Exception)
                {
                    sb.Append('?');
                }
            }
            return sb.ToString();
        }
    }

    private static PdfDocumentBuilder.AddedFont? LoadFont(PdfDocumentBuilder builder, bool bold)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        string[] files = bold
            ?
            [
                Path.Combine(windows, "arialbd.ttf"),
                Path.Combine(windows, "segoeuib.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
                "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
                "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
            ]
            :
            [
                Path.Combine(windows, "arial.ttf"),
                Path.Combine(windows, "segoeui.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
                "/usr/share/fonts/TTF/DejaVuSans.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf",
            ];
        foreach (var f in files.Where(File.Exists))
        {
            var bytes = File.ReadAllBytes(f);
            if (builder.CanUseTrueTypeFont(bytes, out _)) return builder.AddTrueTypeFont(bytes);
        }
        if (bold) return null;
        foreach (var family in new[] { "Arial", "Liberation Sans", "DejaVu Sans", "Helvetica" })
        {
            using var tf = SKFontManager.Default.MatchFamily(family);
            using var stream = tf?.OpenStream();
            if (stream is null) continue;
            var data = new byte[stream.Length];
            if (stream.Read(data, data.Length) != data.Length) continue;
            if (builder.CanUseTrueTypeFont(data, out _)) return builder.AddTrueTypeFont(data);
        }
        return null;
    }
}
