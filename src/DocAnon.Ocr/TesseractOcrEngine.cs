using DocAnon.Core.Model;
using SkiaSharp;
using Tesseract;

namespace DocAnon.Ocr;

/// <summary>
/// Tesseract 5 with the LSTM engine and the Portuguese and English models. The engine is not
/// thread-safe, so calls are serialized.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly Lock _lock = new();

    public TesseractOcrEngine(string tessdataFolder, string languages = "por+eng")
    {
        _engine = new TesseractEngine(tessdataFolder, languages, EngineMode.LstmOnly);
        // Engineering drawings have sparse text, so automatic segmentation is the safest default.
        _engine.DefaultPageSegMode = PageSegMode.Auto;
        // Tesseract prints diagnostics such as "Estimating resolution" to stderr; send them nowhere.
        _engine.SetVariable("debug_file", OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
    }

    /// <summary>Images whose longest side is below this are enlarged before OCR, because small text reads badly.</summary>
    public int UpscaleBelowPx { get; set; } = 2500;

    /// <summary>Finds tessdata next to the executable (models/tessdata), or returns null.</summary>
    public static string? FindTessdata(params string[] extraFolders)
    {
        foreach (var f in extraFolders.Append(Path.Combine(AppContext.BaseDirectory, "models", "tessdata")))
            if (File.Exists(Path.Combine(f, "por.traineddata")) && File.Exists(Path.Combine(f, "eng.traineddata"))) return f;
        return null;
    }

    public OcrResult Recognize(byte[] image, bool sparseText = false)
    {
        float factor = 1;
        byte[] input = image;
        using (var probe = SKBitmap.Decode(image))
        {
            if (probe is not null && Math.Max(probe.Width, probe.Height) < UpscaleBelowPx)
            {
                factor = Math.Min(3f, UpscaleBelowPx / (float)Math.Max(probe.Width, probe.Height));
                factor = MathF.Max(1, MathF.Round(factor));
                if (factor > 1)
                {
                    var info = new SKImageInfo((int)(probe.Width * factor), (int)(probe.Height * factor));
                    using var big = probe.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
                    using var img = SKImage.FromBitmap(big);
                    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    input = data.ToArray();
                }
            }
        }

        lock (_lock)
        {
            using var pix = Pix.LoadFromMemory(input);
            using var page = _engine.Process(pix, sparseText ? PageSegMode.SparseText : PageSegMode.Auto);
            var words = new List<OcrWord>();
            using var it = page.GetIterator();
            it.Begin();
            int block = -1, line = -1;
            do
            {
                if (it.IsAtBeginningOf(PageIteratorLevel.Block)) block++;
                if (it.IsAtBeginningOf(PageIteratorLevel.TextLine)) line++;
                string? text = it.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var r)) continue;
                float conf = it.GetConfidence(PageIteratorLevel.Word) / 100f;
                var box = new BoundingBox(r.X1 / factor, r.Y1 / factor, r.Width / factor, r.Height / factor);
                words.Add(new OcrWord(text.Trim(), box, conf, Math.Max(0, block), Math.Max(0, line)));
            }
            while (it.Next(PageIteratorLevel.Word));
            return new OcrResult((int)(pix.Width / factor), (int)(pix.Height / factor), words);
        }
    }

    public void Dispose() => _engine.Dispose();
}

/// <summary>
/// Several Tesseract engines behind one <see cref="IOcrEngine"/>, so the upright and rotated passes
/// of a drawing run at the same time. Each engine holds its own models in memory (about 100 MB).
/// </summary>
public sealed class TesseractOcrPool : IOcrEngine, IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentBag<TesseractOcrEngine> _free = [];
    private readonly List<TesseractOcrEngine> _all = [];
    private readonly SemaphoreSlim _slots;

    public TesseractOcrPool(string tessdataFolder, int size)
    {
        for (int i = 0; i < size; i++)
        {
            var e = new TesseractOcrEngine(tessdataFolder);
            _all.Add(e);
            _free.Add(e);
        }
        _slots = new SemaphoreSlim(size, size);
    }

    public static TesseractOcrPool? TryCreateDefault(params string[] extraFolders)
    {
        string? folder = TesseractOcrEngine.FindTessdata(extraFolders);
        return folder is null ? null : new TesseractOcrPool(folder, Math.Clamp(Environment.ProcessorCount / 2, 1, 3));
    }

    public OcrResult Recognize(byte[] image, bool sparseText = false)
    {
        _slots.Wait();
        _free.TryTake(out var engine);
        try
        {
            return engine!.Recognize(image, sparseText);
        }
        finally
        {
            _free.Add(engine!);
            _slots.Release();
        }
    }

    public void Dispose()
    {
        foreach (var e in _all) e.Dispose();
        _slots.Dispose();
    }
}
