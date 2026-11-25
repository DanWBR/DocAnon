using System.IO.Compression;
using System.Xml.Linq;
using DocAnon.Core.Model;
using netDxf;
using netDxf.Entities;
using SkiaSharp;
using Svg.Skia;

namespace DocAnon.Extraction;

/// <summary>
/// DXF drawings. Inserts are exploded into model-space entities, the text of TEXT, MTEXT and block
/// attributes is listed as labels, and a simple Skia renderer draws lines, polylines, arcs, circles
/// and text, so the redacted image redraws the drawing with the markers in place of the text.
/// </summary>
public sealed class DxfReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".dxf"];

    private sealed record DxfText(string Value, double X, double Y, double Height, double RotationDeg);

    private sealed class Geometry
    {
        public List<(double X1, double Y1, double X2, double Y2)> Segments { get; } = [];
        public List<(double Cx, double Cy, double R, double A0, double A1)> Arcs { get; } = [];
        public List<DxfText> Texts { get; } = [];
    }

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        var dxf = DxfDocument.Load(path) ?? throw new InvalidDataException("Não foi possível ler o DXF.");
        var g = new Geometry();
        Collect(dxf.Entities.All, g, 0);
        // Exploded inserts may repeat their attributes as text entities.
        var unique = g.Texts.DistinctBy(t => (t.Value, Math.Round(t.X, 3), Math.Round(t.Y, 3))).ToList();
        g.Texts.Clear();
        g.Texts.AddRange(unique);
        var b = new DocumentBuilder(Path.GetFileName(path), "dxf");
        if (dxf.Entities.Images.Any()) b.Warnings.Add("O DXF referencia imagens externas, que não entram na imagem exportada.");

        // Extents and transform to pixels.
        var xs = g.Segments.SelectMany(s => new[] { s.X1, s.X2 }).Concat(g.Arcs.SelectMany(a => new[] { a.Cx - a.R, a.Cx + a.R })).Concat(g.Texts.Select(t => t.X)).ToList();
        var ys = g.Segments.SelectMany(s => new[] { s.Y1, s.Y2 }).Concat(g.Arcs.SelectMany(a => new[] { a.Cy - a.R, a.Cy + a.R })).Concat(g.Texts.Select(t => t.Y)).ToList();
        if (xs.Count == 0)
        {
            b.Warnings.Add("O DXF não tem entidades desenháveis.");
            return Task.FromResult(b.Build());
        }
        double minX = xs.Min(), maxX = xs.Max(), minY = ys.Min(), maxY = ys.Max() + g.Texts.Select(t => t.Height).DefaultIfEmpty(0).Max();
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        const int target = 3000, margin = 40;
        double scale = (target - 2 * margin) / Math.Max(w, h);
        int pw = (int)(w * scale) + 2 * margin, ph = (int)(h * scale) + 2 * margin;
        float X(double x) => (float)((x - minX) * scale + margin);
        float Y(double y) => (float)((maxY - y) * scale + margin);

        using var measureFont = new SKFont(SKTypeface.Default, 12);
        var placed = new List<PlacedWord>();
        var boxToText = new Dictionary<BoundingBox, int>();
        for (int i = 0; i < g.Texts.Count; i++)
        {
            var t = g.Texts[i];
            float size = (float)Math.Max(6, t.Height * scale);
            measureFont.Size = size;
            float tw = measureFont.MeasureText(t.Value);
            bool vertical = Math.Abs(Math.Sin(t.RotationDeg * Math.PI / 180)) > 0.7;
            var box = vertical
                ? new BoundingBox(X(t.X) - size, Y(t.Y) - tw, size * 1.2f, tw)
                : new BoundingBox(X(t.X), Y(t.Y) - size, tw, size * 1.2f);
            while (boxToText.ContainsKey(box)) box = box with { X = box.X + 0.01f };
            boxToText[box] = i;
            placed.Add(new PlacedWord(t.Value, box));
        }

        byte[] Render(Func<int, string> textOf)
        {
            using var surface = SKSurface.Create(new SKImageInfo(pw, ph));
            var c = surface.Canvas;
            c.Clear(SKColors.White);
            using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            foreach (var s in g.Segments) c.DrawLine(X(s.X1), Y(s.Y1), X(s.X2), Y(s.Y2), pen);
            foreach (var a in g.Arcs)
            {
                var rect = new SKRect(X(a.Cx - a.R), Y(a.Cy + a.R), X(a.Cx + a.R), Y(a.Cy - a.R));
                double sweep = a.A1 - a.A0;
                if (sweep <= 0) sweep += 360;
                using var path = new SKPath();
                path.AddArc(rect, (float)-a.A0, (float)-sweep);
                c.DrawPath(path, pen);
            }
            using var fill = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 12);
            for (int i = 0; i < g.Texts.Count; i++)
            {
                var t = g.Texts[i];
                font.Size = (float)Math.Max(6, t.Height * scale);
                c.Save();
                c.Translate(X(t.X), Y(t.Y));
                c.RotateDegrees((float)-t.RotationDeg);
                c.DrawText(textOf(i), 0, 0, font, fill);
                c.Restore();
            }
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        byte[]? original = null;
        VisualPage? page = null;
        page = new VisualPage(0, "desenho", pw, ph, () => original ??= Render(i => g.Texts[i].Value))
        {
            RenderWithReplacements = reps =>
            {
                var byText = new Dictionary<int, string>();
                foreach (var vw in page!.Words)
                {
                    if (!boxToText.TryGetValue(vw.Box, out int idx)) continue;
                    byText[idx] = RangeReplacer.Apply(g.Texts[idx].Value, vw.TextStart, reps);
                }
                return Render(i => byText.GetValueOrDefault(i, g.Texts[i].Value));
            },
        };
        page.TextVerified = true;
        b.Pages.Add(page);
        b.Heading("Desenho DXF", 1, "desenho");
        b.PlacedText(placed, "desenho", asLabels: true, page, fromOcr: false);
        return Task.FromResult(b.Build());
    }

    private static void Collect(IEnumerable<EntityObject> entities, Geometry g, int depth)
    {
        foreach (var e in entities)
        {
            switch (e)
            {
                case Line l:
                    g.Segments.Add((l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y));
                    break;
                case Polyline2D p:
                    var v = p.Vertexes;
                    for (int i = 1; i < v.Count; i++) g.Segments.Add((v[i - 1].Position.X, v[i - 1].Position.Y, v[i].Position.X, v[i].Position.Y));
                    if (p.IsClosed && v.Count > 2) g.Segments.Add((v[^1].Position.X, v[^1].Position.Y, v[0].Position.X, v[0].Position.Y));
                    break;
                case Polyline3D p3:
                    var v3 = p3.Vertexes;
                    for (int i = 1; i < v3.Count; i++) g.Segments.Add((v3[i - 1].X, v3[i - 1].Y, v3[i].X, v3[i].Y));
                    break;
                case Arc a:
                    g.Arcs.Add((a.Center.X, a.Center.Y, a.Radius, a.StartAngle, a.EndAngle));
                    break;
                case Circle ci:
                    g.Arcs.Add((ci.Center.X, ci.Center.Y, ci.Radius, 0, 360));
                    break;
                case Text t when !string.IsNullOrWhiteSpace(t.Value):
                    g.Texts.Add(new DxfText(t.Value, t.Position.X, t.Position.Y, t.Height, t.Rotation));
                    break;
                case MText m:
                    string plain = m.PlainText();
                    if (!string.IsNullOrWhiteSpace(plain))
                    {
                        // MTEXT position is the top-left corner by default; lines go down.
                        var lines = plain.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                            if (lines[i].Trim().Length > 0)
                                g.Texts.Add(new DxfText(lines[i].Trim(), m.Position.X, m.Position.Y - m.Height * (1 + i * 1.6), m.Height, m.Rotation));
                    }
                    break;
                case Insert ins when depth < 8:
                    foreach (var at in ins.Attributes)
                        if (at.IsVisible && !string.IsNullOrWhiteSpace(at.Value))
                            g.Texts.Add(new DxfText(at.Value, at.Position.X, at.Position.Y, at.Height, at.Rotation));
                    Collect(ins.Explode(), g, depth + 1);
                    break;
            }
        }
    }
}

/// <summary>SVG drawings. Text elements are replaced in the SVG itself before rendering.</summary>
public sealed class SvgReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".svg"];

    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        string xml = File.ReadAllText(path);
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var b = new DocumentBuilder(Path.GetFileName(path), "svg");

        // Each <text>, <title> and <desc> is one label. Nested <tspan> elements belong to their <text>.
        static bool IsTextual(XElement e) => e.Name.LocalName is "text" or "title" or "desc";
        var textual = doc.Descendants().Where(IsTextual).ToList();
        var values = textual.Select(e => string.Concat(e.DescendantNodes().OfType<XText>().Select(t => t.Value))).ToList();

        b.Heading("Desenho SVG", 1, "desenho");
        var offsets = b.ListItemsWithOffsets(values, "desenho");
        bool hasImages = doc.Descendants().Any(e => e.Name.LocalName == "image");
        if (hasImages) b.Warnings.Add("O SVG contém imagens raster, cujo texto não pode ser verificado; a imagem tarjada não será exportada.");

        byte[] Render(XDocument d)
        {
            using var svg = new SKSvg();
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(d.ToString(SaveOptions.DisableFormatting)));
            var picture = svg.Load(ms) ?? throw new InvalidDataException("SVG inválido.");
            var rect = picture.CullRect;
            float scale = 2400f / Math.Max(1, Math.Max(rect.Width, rect.Height));
            int w = Math.Max(1, (int)(rect.Width * scale)), h = Math.Max(1, (int)(rect.Height * scale));
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            surface.Canvas.Clear(SKColors.White);
            surface.Canvas.Scale(scale);
            surface.Canvas.Translate(-rect.Left, -rect.Top);
            surface.Canvas.DrawPicture(picture);
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        byte[] first = Render(doc);
        using var probe = SKBitmap.Decode(first);
        var page = new VisualPage(0, "desenho", probe.Width, probe.Height, () => first)
        {
            RenderWithReplacements = reps =>
            {
                var copy = new XDocument(doc);
                var targets = copy.Descendants().Where(IsTextual).ToList();
                for (int i = 0; i < targets.Count && i < offsets.Count; i++)
                {
                    var (start, _) = offsets[i];
                    if (start < 0) continue;
                    string replaced = RangeReplacer.Apply(values[i].Trim(), start, reps);
                    var nodes = targets[i].DescendantNodes().OfType<XText>().ToList();
                    if (nodes.Count == 0) continue;
                    nodes[0].Value = replaced;
                    foreach (var n in nodes.Skip(1)) n.Value = "";
                }
                return Render(copy);
            },
            TextVerified = !hasImages,
        };
        b.Pages.Add(page);
        return Task.FromResult(b.Build());
    }
}

/// <summary>
/// Visio drawings (.vsdx). The text of every shape in pages and masters is listed, and an
/// anonymized copy of the .vsdx can be written. The app does not render Visio, so there is not a
/// redacted image unless the user exports the drawing to PDF or SVG first.
/// </summary>
public sealed class VisioReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".vsdx", ".vsdm"];

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var b = new DocumentBuilder(Path.GetFileName(path), "vsdx");
        b.Warnings.Add("O DocAnon não desenha arquivos Visio. Para exportar a imagem tarjada, salve o desenho como PDF ou SVG e abra esse arquivo.");

        // entry name -> list of (text element index, document offset, original text)
        var map = new Dictionary<string, List<(int Index, int Start, string Text)>>();
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var parts = zip.Entries
                .Where(e => e.FullName.StartsWith("visio/pages/page", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("visio/masters/master", StringComparison.OrdinalIgnoreCase))
                .Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Contains("masters", StringComparison.OrdinalIgnoreCase))
                .ThenBy(e => e.FullName.Length).ThenBy(e => e.FullName)
                .ToList();
            foreach (var part in parts)
            {
                XDocument x;
                using (var s = part.Open()) x = XDocument.Load(s);
                var texts = x.Descendants().Where(e => e.Name.LocalName == "Text").ToList();
                var values = texts.Select(TextOf).ToList();
                if (values.All(v => v.Trim().Length == 0)) continue;
                string label = Path.GetFileNameWithoutExtension(part.FullName);
                b.Heading(part.FullName.Contains("masters", StringComparison.OrdinalIgnoreCase) ? $"Mestre {label}" : $"Página {label.Replace("page", "")}", 2, part.FullName);
                var offs = b.ListItemsWithOffsets(values, part.FullName);
                map[part.FullName] = values.Select((v, i) => (i, offs[i].Start, v)).Where(t => t.Start >= 0).ToList();
            }
        }

        async Task WriteCopy(IReadOnlyList<TextReplacement> reps, Stream output)
        {
            using var src = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            using var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var entry in src.Entries)
            {
                var outEntry = dst.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                await using var os = outEntry.Open();
                await using var es = entry.Open();
                if (map.TryGetValue(entry.FullName, out var items))
                {
                    var x = await XDocument.LoadAsync(es, LoadOptions.PreserveWhitespace, CancellationToken.None);
                    var texts = x.Descendants().Where(e => e.Name.LocalName == "Text").ToList();
                    foreach (var (index, start, text) in items)
                    {
                        string oneLine = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
                        SetText(texts[index], RangeReplacer.Apply(oneLine, start, reps));
                    }
                    await x.SaveAsync(os, SaveOptions.DisableFormatting, CancellationToken.None);
                }
                else if (entry.FullName.Equals("docProps/core.xml", StringComparison.OrdinalIgnoreCase) || entry.FullName.Equals("docProps/app.xml", StringComparison.OrdinalIgnoreCase)
                         || entry.FullName.Contains("comments", StringComparison.OrdinalIgnoreCase))
                {
                    // Authors, company and comments are blanked, because they were not reviewed.
                    var x = await XDocument.LoadAsync(es, LoadOptions.None, CancellationToken.None);
                    foreach (var e in x.Descendants().Where(e => !e.HasElements && e.Name.LocalName is "creator" or "lastModifiedBy" or "Company" or "Manager" or "title" or "subject" or "description" or "keywords" or "Comment" or "Text"))
                        e.Value = "";
                    foreach (var a in x.Descendants().SelectMany(e => e.Attributes()).Where(a => a.Name.LocalName is "Name" or "Initials" or "Author").ToList())
                        a.Value = "";
                    await x.SaveAsync(os, SaveOptions.DisableFormatting, CancellationToken.None);
                }
                else await es.CopyToAsync(os);
            }
        }

        return Task.FromResult(b.Build(WriteCopy, ".vsdx"));
    }

    private static string TextOf(XElement text) => string.Concat(text.DescendantNodes().OfType<XText>().Select(t => t.Value));

    private static void SetText(XElement text, string value)
    {
        var nodes = text.DescendantNodes().OfType<XText>().ToList();
        if (nodes.Count == 0)
        {
            text.Add(new XText(value));
            return;
        }
        nodes[0].Value = value;
        foreach (var n in nodes.Skip(1)) n.Value = "";
    }
}
