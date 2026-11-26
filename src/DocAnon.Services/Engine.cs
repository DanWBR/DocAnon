using DocAnon.Core.Detection;
using DocAnon.Core.Export;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Text;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Vault;
using DocAnon.Extraction;
using DocAnon.Llm;
using DocAnon.Ner;
using DocAnon.Ocr;
using DocAnon.Redaction;

namespace DocAnon.Services;

public sealed class EngineOptions
{
    public bool UseNer { get; init; } = true;
    public bool UseOcr { get; init; } = true;
    public int Dpi { get; init; } = 200;
    public DiagramDetection Diagrams { get; init; } = DiagramDetection.Auto;

    /// <summary>Extra folders to look for models/ner and models/tessdata.</summary>
    public string? ModelsFolder { get; init; }
}

/// <summary>What leaves the app: the anonymized text, the redacted images and the checks that ran on them.</summary>
public sealed class ExportPackage
{
    public required string SourceName { get; init; }
    public required AnonymizationResult Anonymized { get; init; }
    public List<RedactedPage> Pages { get; } = [];
    public List<string> SkippedPages { get; } = [];
    public List<LeakFinding> TextLeaks { get; init; } = [];
    public List<VisualLeak> VisualLeaks { get; } = [];

    /// <summary>The anonymized copy in the format of the source file (DOCX, PPTX, XLSX, PDF, CSV, TXT), when the format has one.</summary>
    public byte[]? NativeCopy { get; set; }

    public string? NativeCopyExtension { get; set; }

    /// <summary>Real values that the check still finds in the native copy.</summary>
    public List<LeakFinding> NativeLeaks { get; } = [];

    public List<string> NativeWarnings { get; } = [];

    public bool HasLeaks => TextLeaks.Count > 0 || VisualLeaks.Count > 0 || NativeLeaks.Count > 0;
}

/// <summary>
/// Loads the local models once and runs the whole flow: extraction, detection, anonymization,
/// redaction and leak checks. Used by both the CLI and the desktop app.
/// </summary>
public sealed class Engine : IDisposable
{
    public NerDetector? Ner { get; }
    public TesseractOcrPool? Ocr { get; }
    public DocumentExtractor Extractor { get; }
    public EngineOptions Options { get; }
    public List<string> StartupWarnings { get; } = [];

    /// <summary>Optional detectors added by the host, such as the Ollama suggestions.</summary>
    public List<IAsyncDetector> ExtraDetectors { get; } = [];

    /// <summary>The local LLM, when the user turned it on.</summary>
    public LlmAssistant? Llm { get; private set; }

    private OllamaClient? _ollama;

    /// <summary>Connects to Ollama on this computer and adds the suggestion detector.</summary>
    public async Task<bool> EnableLlmAsync(LlmOptions options, CancellationToken ct = default)
    {
        _ollama?.Dispose();
        _ollama = new OllamaClient(options.Address);
        if (!await _ollama.IsAvailableAsync(ct))
        {
            StartupWarnings.Add($"Ollama não respondeu em {options.Address}; o LLM local ficou desligado.");
            return false;
        }
        Llm = new LlmAssistant(_ollama, options);
        ExtraDetectors.RemoveAll(d => d is LlmEntityDetector);
        if (!string.IsNullOrEmpty(options.TextModel)) ExtraDetectors.Add(new LlmEntityDetector(Llm));
        return true;
    }

    /// <summary>
    /// <see cref="Export"/> followed by the LLM checks: the audit of the anonymized text and, with a
    /// vision model, a transcription of each redacted image compared with the session's real values.
    /// </summary>
    public async Task<ExportPackage> ExportAsync(ExtractedDocument doc, IEnumerable<DetectedSpan> spans, Session session, CompanyProfile? profile, bool images = true, CancellationToken ct = default)
    {
        var pkg = Export(doc, spans, session, profile, images);
        if (Llm is null) return pkg;
        if (Llm.HasTextModel) pkg.TextLeaks.AddRange(await Llm.AuditAsync(pkg.Anonymized.Text, ct));
        if (Llm.HasVisionModel)
        {
            var originals = session.Registry.Records.Where(r => r.Type != EntityType.VALOR).SelectMany(r => r.Variants)
                .Select(TextFold.Key).Where(v => v.Length >= 4).Distinct().ToList();
            foreach (var page in pkg.Pages)
            {
                var (texts, _) = await Llm.ReadImageAsync(page.Png, ct);
                foreach (var t in texts)
                {
                    string k = TextFold.Key(t);
                    foreach (var o in originals.Where(o => k.Contains(o, StringComparison.Ordinal)))
                        pkg.VisualLeaks.Add(new VisualLeak(page.Page.Index, page.Page.Source + " (modelo de visão)", o, default));
                }
            }
        }
        return pkg;
    }

    public Engine(EngineOptions? options = null)
    {
        Options = options ?? new EngineOptions();
        // The installer puts the command-line tool in cli\, next to the app's models folder.
        string parentModels = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "models"));
        string[] extra = Options.ModelsFolder is { } m ? [m, parentModels] : [parentModels];
        if (Options.UseNer)
        {
            Ner = NerDetector.TryLoadDefault(extra.Select(f => Path.Combine(f, "ner")).ToArray());
            if (Ner is null) StartupWarnings.Add("Modelo de NER não encontrado em models/ner; nomes de pessoas, empresas e lugares não serão detectados automaticamente.");
        }
        if (Options.UseOcr)
        {
            try
            {
                Ocr = TesseractOcrPool.TryCreateDefault(extra.Select(f => Path.Combine(f, "tessdata")).ToArray());
                if (Ocr is null) StartupWarnings.Add("Dados do Tesseract não encontrados em models/tessdata; imagens e PDFs escaneados não poderão ser lidos.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or InvalidOperationException)
            {
                StartupWarnings.Add("Não foi possível iniciar o Tesseract: " + ex.Message);
            }
        }
        Extractor = new DocumentExtractor(new ExtractionOptions
        {
            Ocr = Ocr,
            Rasterizer = new PdfiumRasterizer(),
            Dpi = Options.Dpi,
            Diagrams = Options.Diagrams,
        });
    }

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken ct = default) => Extractor.ReadAsync(path, ct);

    public DetectionPipeline BuildPipeline(bool useNer = true, bool useExtra = true, Session? session = null)
    {
        var p = DetectionPipeline.CreateDefault();
        if (session is not null) p.Add(new SessionTermsDetector(session.Registry));
        if (useNer && Ner is not null) p.Add(Ner);
        if (useExtra) foreach (var d in ExtraDetectors) p.Add(d);
        return p;
    }

    public Task<List<DetectedSpan>> DetectAsync(ExtractedDocument doc, CompanyProfile? profile, bool useNer = true, bool useExtra = true,
        IProgress<(string Stage, double Fraction)>? progress = null, CancellationToken ct = default, Session? session = null)
    {
        var ctx = new DetectionContext(doc.Text, profile);
        return BuildPipeline(useNer, useExtra, session).RunAsync(ctx, progress, ct);
    }

    /// <summary>
    /// Anonymizes the text and the visual pages with the session's markers and factors, then runs
    /// the leak checks on both.
    /// </summary>
    public ExportPackage Export(ExtractedDocument doc, IEnumerable<DetectedSpan> spans, Session session, CompanyProfile? profile, bool images = true)
    {
        var result = Anonymizer.Apply(doc.Text, spans, session.Registry, session.Factors);
        if (!session.Documents.Contains(doc.FileName)) session.Documents.Add(doc.FileName);
        var pkg = new ExportPackage
        {
            SourceName = doc.FileName,
            Anonymized = result,
            TextLeaks = LeakChecker.Check(result.Text, session.Registry, profile),
        };
        if (!images) return pkg;

        var reps = result.AsTextReplacements();
        var redactor = new PageRedactor();
        foreach (var page in doc.VisualPages)
        {
            if (!page.Export)
            {
                pkg.SkippedPages.Add($"{page.Source}: desmarcada para exportação.");
                continue;
            }
            ApplyProfileCovers(page, profile);
            try
            {
                pkg.Pages.Add(redactor.Render(page, reps));
            }
            catch (InvalidOperationException ex)
            {
                pkg.SkippedPages.Add(ex.Message);
            }
        }
        var originals = session.Registry.Records.Where(r => r.Type != EntityType.VALOR)
            .SelectMany(r => r.Variants.Select(v => (v, r.Token)))
            .Concat(profile?.Terms.SelectMany(t => t.Aliases.Prepend(t.Text)).Select(t => (t, "[OCULTO]")) ?? [])
            .ToList();
        for (int i = 0; i < pkg.Pages.Count; i++)
        {
            var page = pkg.Pages[i].Page;
            pkg.Pages[i] = CoverWhatOcrStillReads(page, () => redactor.Render(page, reps), pkg.Pages[i], originals, pkg.VisualLeaks);
        }

        if (doc.Format == "pdf") WritePdfCopy(doc, pkg, profile, reps, redactor, originals);
        else if (doc.WriteNativeCopy is { } write)
        {
            var ctx = new NativeCopyContext
            {
                Replacements = reps,
                Registry = session.Registry,
                Factors = session.Factors,
                Profile = profile,
                CommaIsDecimal = LanguageGuess.CommaIsDecimal(doc.Text),
                RedactedImages = pkg.Pages.ToDictionary(p => p.Page, p => p.Png),
            };
            using var ms = new MemoryStream();
            write(ctx, ms).GetAwaiter().GetResult();
            pkg.NativeCopy = ms.ToArray();
            pkg.NativeCopyExtension = doc.NativeCopyExtension;
            pkg.NativeLeaks.AddRange(ctx.Leaks);
            pkg.NativeWarnings.AddRange(ctx.Warnings);
        }
        return pkg;
    }

    /// <summary>
    /// Runs OCR on a redacted page. Whatever it can still read is covered with its marker, and the
    /// page is rendered and checked again, up to two more times. What is left goes to <paramref name="leaks"/>.
    /// </summary>
    private RedactedPage CoverWhatOcrStillReads(VisualPage page, Func<RedactedPage> render, RedactedPage current, List<(string, string)> originals, List<VisualLeak> leaks)
    {
        if (Ocr is null) return current;
        var found = PageRedactor.CheckWithOcr(Ocr, [current], originals);
        for (int round = 0; round < 2 && found.Count > 0; round++)
        {
            float sx = page.WidthPx / (float)Math.Max(1, current.Width), sy = page.HeightPx / (float)Math.Max(1, current.Height);
            foreach (var l in found)
                page.AutoCovers.Add(new CoverBox(new BoundingBox(l.Box.X * sx, l.Box.Y * sy, l.Box.Width * sx, l.Box.Height * sy), "valor ainda legível, coberto automaticamente", l.Token));
            current = render();
            found = PageRedactor.CheckWithOcr(Ocr, [current], originals);
        }
        leaks.AddRange(found);
        return current;
    }

    /// <summary>
    /// The PDF copy has every page of the source as a redacted image, in order: the text pages are
    /// rendered and covered where the text was replaced, the pictures cropped from them are pasted
    /// back already redacted, and the diagram and scanned pages reuse their exported image.
    /// </summary>
    private void WritePdfCopy(ExtractedDocument doc, ExportPackage pkg, CompanyProfile? profile, IReadOnlyList<TextReplacement> reps, PageRedactor redactor, List<(string, string)> originals)
    {
        int count = doc.VisualPages.Concat(doc.NativePages).Select(p => p.PdfPage).DefaultIfEmpty(-1).Max() + 1;
        if (count == 0) return;
        if (doc.NativePages.Count == 0 && doc.VisualPages.All(p => p.BoxOnPage is not null))
        {
            pkg.NativeWarnings.Add("O renderizador de PDF não está disponível, por isso a cópia em PDF não foi gerada.");
            return;
        }
        var byPage = pkg.Pages.ToDictionary(p => p.Page);
        var pages = new List<RedactedPage>();
        for (int n = 0; n < count; n++)
        {
            var whole = doc.VisualPages.FirstOrDefault(p => p.PdfPage == n && p.BoxOnPage is null);
            if (whole is not null)
            {
                pages.Add(byPage.TryGetValue(whole, out var done) ? done : BlankPage(whole, "página não exportada"));
                if (!byPage.ContainsKey(whole)) pkg.NativeWarnings.Add($"{whole.Source}: a página não foi exportada e ficou em branco na cópia.");
                continue;
            }
            var native = doc.NativePages.FirstOrDefault(p => p.PdfPage == n);
            if (native is null)
            {
                pkg.NativeWarnings.Add($"página {n + 1}: a página não pôde ser desenhada e ficou de fora da cópia.");
                continue;
            }
            // The redacted pictures go onto the page first, so the covers drawn afterwards stay on top of them.
            var crops = doc.VisualPages.Where(p => p.PdfPage == n && p.BoxOnPage is not null).ToList();
            var composite = new VisualPage(native.Index, native.Source, native.WidthPx, native.HeightPx, () => PasteCrops(native.RenderOriginalPng(), native, crops, byPage))
            {
                PdfPage = n,
                TextVerified = true,
            };
            composite.Words.AddRange(native.Words);
            composite.ManualBoxes.AddRange(native.ManualBoxes);
            composite.AutoCovers.AddRange(native.AutoCovers);
            ApplyProfileCovers(composite, profile);
            RedactedPage Render() => redactor.Render(composite, reps);
            var page = Render();
            var leaks = new List<VisualLeak>();
            page = CoverWhatOcrStillReads(composite, Render, page, originals, leaks);
            pkg.VisualLeaks.AddRange(leaks.Select(l => l with { Source = l.Source + " (cópia PDF)" }));
            pages.Add(page);
        }
        using var ms = new MemoryStream();
        PageRedactor.WriteImagePdf(pages, ms);
        pkg.NativeCopy = ms.ToArray();
        pkg.NativeCopyExtension = ".pdf";
    }

    /// <summary>Draws each redacted picture over its place on the page, or a grey box when the picture was not exported.</summary>
    private static byte[] PasteCrops(byte[] pagePng, VisualPage native, List<VisualPage> crops, Dictionary<VisualPage, RedactedPage> exported)
    {
        if (crops.Count == 0) return pagePng;
        using var bmp = SkiaSharp.SKBitmap.Decode(pagePng);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        float sx = bmp.Width / (float)Math.Max(1, native.WidthPx), sy = bmp.Height / (float)Math.Max(1, native.HeightPx);
        foreach (var crop in crops)
        {
            var b = crop.BoxOnPage!.Value;
            var rect = new SkiaSharp.SKRect(b.X * sx, b.Y * sy, b.Right * sx, b.Bottom * sy);
            if (exported.TryGetValue(crop, out var red))
            {
                using var img = SkiaSharp.SKBitmap.Decode(red.Png);
                canvas.DrawBitmap(img, rect);
            }
            else
            {
                using var grey = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(0xC8, 0xC8, 0xC8) };
                canvas.DrawRect(rect, grey);
            }
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static RedactedPage BlankPage(VisualPage page, string label)
    {
        int w = Math.Max(1, page.WidthPx), h = Math.Max(1, page.HeightPx);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(w, h));
        surface.Canvas.Clear(SkiaSharp.SKColors.White);
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, Math.Max(12, w / 30f));
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Gray, IsAntialias = true };
        surface.Canvas.DrawText($"[{label}]", w / 2f, h / 2f, SkiaSharp.SKTextAlign.Center, font, paint);
        using var snap = surface.Snapshot();
        using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return new RedactedPage(page, data.ToArray(), 0, w, h);
    }

    /// <summary>
    /// Adds the profile's fixed regions and the matches of its logos to the page's automatic covers.
    /// It runs once per page, so a second export does not duplicate the boxes.
    /// </summary>
    public static void ApplyProfileCovers(VisualPage page, CompanyProfile? profile)
    {
        if (profile is null || page.AutoCovers.Any(c => c.Reason.StartsWith("perfil:", StringComparison.Ordinal))) return;
        if (!profile.CoverSmallImages)
            foreach (var c in page.AutoCovers.Where(c => c.Reason.StartsWith("imagem pequena", StringComparison.Ordinal))) c.Include = false;
        bool landscape = page.WidthPx >= page.HeightPx;
        foreach (var r in profile.FixedRegions)
        {
            if (r.Orientation == PageOrientation.Landscape && !landscape || r.Orientation == PageOrientation.Portrait && landscape) continue;
            page.AutoCovers.Add(new CoverBox(new BoundingBox((float)(r.X * page.WidthPx), (float)(r.Y * page.HeightPx), (float)(r.Width * page.WidthPx), (float)(r.Height * page.HeightPx)), "perfil: " + r.Label));
        }
        if (profile.Logos.Count == 0) return;
        byte[] png = page.RenderOriginalPng();
        foreach (var logo in profile.Logos)
        {
            byte[] bytes;
            try
            {
                bytes = logo.Bytes();
            }
            catch (FormatException)
            {
                continue;
            }
            foreach (var (box, score) in LogoMatcher.Find(png, bytes))
                page.AutoCovers.Add(new CoverBox(box, $"perfil: logotipo {logo.Name} ({score:P0})"));
        }
    }

    /// <summary>
    /// The exported files go to the AI too, so their names must not carry what the text hides.
    /// The original file name goes through the same detectors, with the session's markers, and
    /// the characters that Windows does not accept in file names are replaced.
    /// </summary>
    public static string SafeFileStem(string original, Session? session, CompanyProfile? profile)
    {
        if (session is null) return original;
        var pipeline = DetectionPipeline.CreateDefault().Add(new SessionTermsDetector(session.Registry));
        var ctx = new DetectionContext(original, profile);
        var spans = pipeline.Run(ctx);
        string stem = Anonymizer.Apply(original, spans, session.Registry, session.Factors).Text;
        foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        return stem.Length > 120 ? stem[..120] : stem;
    }

    /// <summary>Writes the package to a folder: Markdown, one PNG per page, an image-only PDF and the native copy.</summary>
    public static async Task<List<string>> SaveAsync(ExportPackage pkg, ExtractedDocument doc, string folder, Session? session = null, CompanyProfile? profile = null)
    {
        Directory.CreateDirectory(folder);
        string stem = SafeFileStem(Path.GetFileNameWithoutExtension(pkg.SourceName), session, profile) + ".anon";
        var written = new List<string>();
        string md = Path.Combine(folder, stem + ".md");
        await File.WriteAllTextAsync(md, pkg.Anonymized.Text);
        written.Add(md);
        for (int i = 0; i < pkg.Pages.Count; i++)
        {
            string png = Path.Combine(folder, $"{stem}.p{i + 1:00}.png");
            await File.WriteAllBytesAsync(png, pkg.Pages[i].Png);
            written.Add(png);
        }
        // When the copy is a PDF it already has every page, so the PDF of the images would repeat part of it.
        if (pkg.Pages.Count > 0 && pkg.NativeCopyExtension != ".pdf")
        {
            string pdf = Path.Combine(folder, stem + ".imagens.pdf");
            await using (var fs = File.Create(pdf)) PageRedactor.WriteImagePdf(pkg.Pages, fs);
            written.Add(pdf);
        }
        if (pkg.NativeCopy is { } copy && pkg.NativeCopyExtension is { } ext)
        {
            string native = Path.Combine(folder, stem + ext);
            await File.WriteAllBytesAsync(native, copy);
            written.Add(native);
        }
        return written;
    }

    public void Dispose()
    {
        _ollama?.Dispose();
        Ner?.Dispose();
        Ocr?.Dispose();
    }
}
