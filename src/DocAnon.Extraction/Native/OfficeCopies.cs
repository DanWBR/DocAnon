using System.Xml.Linq;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;
using SkiaSharp;
using static DocAnon.Extraction.Native.OfficePackage;

namespace DocAnon.Extraction.Native;

/// <summary>Anonymized copies and restored copies of DOCX, PPTX and XLSX files.</summary>
internal static class OfficeCopies
{
    /// <summary>
    /// Writes an anonymized copy of an Office file. The text gets the session's markers and scaled
    /// numbers. Each picture is replaced by its redacted version, or by a grey placeholder when the
    /// picture was not exported. Authors, deleted revisions, embedded files and chart data are
    /// removed, because the review did not show them.
    /// </summary>
    public static byte[] Anonymize(byte[] source, NativeCopyContext c, IReadOnlyDictionary<string, VisualPage> mediaPages)
    {
        var pkg = OfficePackage.Load(source);
        if (pkg.Kind == OfficeKind.Word) RemoveDeletedRevisions(pkg);

        var (main, _) = pkg.MainText();
        TextMap.CheckLeaks(c, main.Anonymize(c), "texto");
        if (pkg.Kind == OfficeKind.Excel)
        {
            var numbers = pkg.ExcelNumbers(c.Warnings.Add);
            numbers.Anonymize(c, commaIsDecimal: false);
            HideSheets(pkg, c);
            if (pkg.Xml.TryGetValue("xl/workbook.xml", out var wb) && wb.Root is { } root)
            {
                // Formulas that read the scaled cells are recalculated when the file opens.
                var calc = root.Element(S + "calcPr");
                if (calc is null) root.Add(calc = new XElement(S + "calcPr"));
                calc.SetAttributeValue("fullCalcOnLoad", "1");
            }
        }
        var other = pkg.OtherText([]);
        TextMap.CheckLeaks(c, other.Anonymize(c, quantities: false), "outras partes do arquivo");

        ScrubPeople(pkg);
        ClearExternalLinks(pkg, c);
        ClearCharts(pkg, c);
        ReplaceMedia(pkg, c, mediaPages);
        return pkg.Save();
    }

    /// <summary>Restores markers and scaled numbers in an Office file that the AI wrote.</summary>
    public static (byte[] Bytes, List<RestoredItem> Items) Restore(byte[] source, EntityRegistry registry, ScaleFactors factors)
    {
        var pkg = OfficePackage.Load(source);
        var items = new List<RestoredItem>();
        // The numbers of a workbook are restored first, because the column headers they read are text.
        if (pkg.Kind == OfficeKind.Excel) items.AddRange(pkg.ExcelNumbers(_ => { }).Restore(registry, factors, unscale: true, commaIsDecimal: false));
        var (main, parts) = pkg.MainText();
        items.AddRange(main.Restore(registry, factors, unscale: true));
        items.AddRange(pkg.OtherText(parts).Restore(registry, factors, unscale: false));
        return (pkg.Save(), items);
    }

    private static void RemoveDeletedRevisions(OfficePackage pkg)
    {
        foreach (var doc in pkg.Xml.Values)
        {
            doc.Descendants().Where(e => e.Name == W + "del" || e.Name == W + "moveFrom" || e.Name == W + "delText" || e.Name == W + "delInstrText").ToList().ForEach(e => e.Remove());
        }
    }

    /// <summary>The reader skips hidden sheets, so the review never saw them, and the copy leaves them empty.</summary>
    private static void HideSheets(OfficePackage pkg, NativeCopyContext c)
    {
        foreach (var (part, visible) in pkg.Sheets().Where(s => !s.Visible))
        {
            var data = pkg.Xml[part].Root?.Element(S + "sheetData");
            if (data is null || !data.HasElements) continue;
            data.RemoveNodes();
            c.Warnings.Add($"A planilha oculta {Path.GetFileNameWithoutExtension(part)} ficou vazia na cópia, porque não passou pela revisão.");
        }
    }

    /// <summary>Authors of the file, of comments and of revisions.</summary>
    private static void ScrubPeople(OfficePackage pkg)
    {
        foreach (var (name, doc) in pkg.Xml)
        {
            if (doc.Root?.Name.LocalName is "coreProperties" or "Properties")
            {
                foreach (var e in doc.Descendants().Where(e => !e.HasElements && e.Name.LocalName is "creator" or "lastModifiedBy" or "Company" or "Manager" or "HyperlinkBase"))
                    e.Value = "";
            }
            foreach (var a in doc.Descendants().SelectMany(e => e.Attributes()).ToList())
            {
                switch (a.Name.LocalName)
                {
                    // A VML shape may carry a base64 copy of its DrawingML twin, text boxes included.
                    case "gfxdata":
                        a.Remove();
                        break;
                    case "author":
                        a.Value = "Autor";
                        break;
                    case "initials":
                        a.Value = "A";
                        break;
                    case "userId":
                        a.Value = "";
                        break;
                    case "name" when a.Parent?.Name.LocalName is "cmAuthor" or "author" or "person":
                        a.Value = "Autor";
                        break;
                }
            }
            // People in threaded comments and in xl/persons.
            foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "person"))
                foreach (var a in e.Attributes().Where(a => a.Name.LocalName is "displayName" or "userId"))
                    a.Value = a.Name.LocalName == "displayName" ? "Autor" : "";
        }
    }

    /// <summary>Links to outside addresses (e-mails, intranet pages) are cleared when they hold a value the session hides.</summary>
    private static void ClearExternalLinks(OfficePackage pkg, NativeCopyContext c)
    {
        var detector = new SessionTermsDetector(c.Registry);
        foreach (var (_, doc) in pkg.Xml.Where(x => x.Key.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var rel in doc.Descendants(Rel + "Relationship").Where(r => (string?)r.Attribute("TargetMode") == "External"))
            {
                var target = rel.Attribute("Target")!;
                string decoded = Uri.UnescapeDataString(target.Value);
                if (detector.Detect(new DetectionContext(decoded, c.Profile)).Any() || LeakCheckerHits(decoded, c))
                    target.Value = "about:blank";
            }
        }
    }

    private static bool LeakCheckerHits(string text, NativeCopyContext c) => DocAnon.Core.Export.LeakChecker.Check(text, c.Registry, c.Profile).Count > 0;

    /// <summary>
    /// Charts keep a copy of their numbers and an embedded workbook with the source data. The
    /// review does not show either, so the copy removes the numbers and the embedded files.
    /// </summary>
    private static void ClearCharts(OfficePackage pkg, NativeCopyContext c)
    {
        int charts = 0;
        foreach (var (name, doc) in pkg.Xml.Where(x => x.Key.Contains("/charts/", StringComparison.OrdinalIgnoreCase)))
        {
            var caches = doc.Descendants(C + "numCache").ToList();
            foreach (var cache in caches) cache.Elements(C + "pt").Remove();
            if (caches.Count > 0) charts++;
        }
        if (charts > 0) c.Warnings.Add($"{charts} gráfico(s) ficaram sem os valores na cópia, porque os números dos gráficos não passam pela revisão.");
        var embedded = pkg.Binary.Keys.Where(k => k.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in embedded) pkg.Binary[k] = [];
        if (embedded.Count > 0) c.Warnings.Add($"{embedded.Count} arquivo(s) incorporado(s) (planilhas, objetos OLE) foram esvaziados na cópia, porque o app não revisa o conteúdo deles.");
        if (pkg.Binary.Keys.Any(k => k.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
            c.Warnings.Add("O arquivo tem macros. O código das macros não é revisado e vai junto na cópia.");
    }

    private static void ReplaceMedia(OfficePackage pkg, NativeCopyContext c, IReadOnlyDictionary<string, VisualPage> mediaPages)
    {
        int placeholders = 0;
        foreach (var name in pkg.Binary.Keys.ToList())
        {
            bool media = name.Contains("/media/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase);
            if (!media) continue;
            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext == ".svg")
            {
                // The PNG fallback next to an SVG is what the reader saw; the SVG itself is emptied.
                pkg.Binary[name] = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>"u8.ToArray();
                continue;
            }
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".emf" or ".wmf" or ".wdp" or ".jfif")) continue;
            byte[] png;
            if (mediaPages.TryGetValue(name, out var page) && c.RedactedImages.TryGetValue(page, out var redacted)) png = redacted;
            else
            {
                png = Placeholder(pkg.Binary[name]);
                placeholders++;
            }
            if (ext == ".png") pkg.Binary[name] = png;
            else if (ext is ".jpg" or ".jpeg" or ".jfif") pkg.Binary[name] = Reencode(png, SKEncodedImageFormat.Jpeg);
            else
            {
                pkg.Binary[name] = png;
                pkg.RenameBinary(name, Path.ChangeExtension(name, ".png"));
            }
        }
        if (placeholders > 0) c.Warnings.Add($"{placeholders} imagem(ns) que não foram exportadas viraram um retângulo cinza na cópia.");
    }

    private static byte[] Reencode(byte[] png, SKEncodedImageFormat format)
    {
        using var bmp = SKBitmap.Decode(png);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(format, 90);
        return data.ToArray();
    }

    /// <summary>A grey picture of the same size as the original, or 400×300 when the original cannot be decoded.</summary>
    internal static byte[] Placeholder(byte[] original)
    {
        int w = 400, h = 300;
        using (var codec = SKCodec.Create(new MemoryStream(original)))
        {
            if (codec is not null)
            {
                w = Math.Clamp(codec.Info.Width, 8, 4000);
                h = Math.Clamp(codec.Info.Height, 8, 4000);
            }
        }
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        surface.Canvas.Clear(new SKColor(0xC8, 0xC8, 0xC8));
        if (w >= 120 && h >= 30)
        {
            using var font = new SKFont(SKTypeface.Default, Math.Min(h / 6f, w / 12f));
            using var paint = new SKPaint { Color = new SKColor(0x50, 0x50, 0x50), IsAntialias = true };
            surface.Canvas.DrawText("[IMAGEM]", w / 2f, h / 2f + font.Size / 3, SKTextAlign.Center, font, paint);
        }
        using var snap = surface.Snapshot();
        using var enc = snap.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }
}
