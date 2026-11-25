using System.Text.Json;
using System.Text.RegularExpressions;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace DocAnon.Ner;

/// <summary>Contents of ner-model.json, written by tools/export_ner_model.py.</summary>
public sealed class NerModelDescriptor
{
    public string ModelFile { get; set; } = "model.onnx";
    public string VocabFile { get; set; } = "vocab.txt";
    public List<string> Labels { get; set; } = [];
    public bool LowerCase { get; set; }
    public int MaxSequenceLength { get; set; } = 512;
    public bool UsesTokenTypeIds { get; set; } = true;
    public Dictionary<string, string> LabelMap { get; set; } = [];
    public string Source { get; set; } = "";
    public string License { get; set; } = "";
}

/// <summary>
/// Named-entity recognition with a BERT token-classification model in ONNX. The text is split
/// into words first, so that every entity maps back to exact character offsets. Long texts are
/// processed in overlapping windows, and each token takes its label from the window where it sits
/// farthest from the edges.
/// </summary>
public sealed partial class NerDetector : IAsyncDetector, IDisposable
{
    public string Id => "ner";

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly NerModelDescriptor _desc;
    private readonly Dictionary<string, EntityType> _typeMap = [];
    private readonly int _cls, _sep;

    /// <summary>Entities below this mean probability are dropped.</summary>
    public double MinScore { get; set; } = 0.55;

    /// <summary>Entities below this mean probability are kept but start unchecked in review.</summary>
    public double IncludeScore { get; set; } = 0.7;

    public int Overlap { get; set; } = 128;

    public string ModelInfo => $"{_desc.Source} ({_desc.License})";

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’]*|\d+|[^\s\p{L}\d]")]
    private static partial Regex WordRx();

    private NerDetector(string folder, NerModelDescriptor desc)
    {
        _desc = desc;
        var opts = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _session = new InferenceSession(Path.Combine(folder, desc.ModelFile), opts);
        _tokenizer = BertTokenizer.Create(Path.Combine(folder, desc.VocabFile), new BertOptions
        {
            LowerCaseBeforeTokenization = desc.LowerCase,
            RemoveNonSpacingMarks = false,
        });
        _cls = _tokenizer.ClassificationTokenId;
        _sep = _tokenizer.SeparatorTokenId;
        foreach (var (k, v) in desc.LabelMap)
            if (Enum.TryParse<EntityType>(v, out var t)) _typeMap[k] = t;
    }

    /// <summary>Loads the model from a folder that holds ner-model.json, or returns null when it is missing.</summary>
    public static NerDetector? TryLoad(string folder)
    {
        string json = Path.Combine(folder, "ner-model.json");
        if (!File.Exists(json)) return null;
        var desc = JsonSerializer.Deserialize<NerModelDescriptor>(File.ReadAllText(json), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (desc is null || !File.Exists(Path.Combine(folder, desc.ModelFile)) || !File.Exists(Path.Combine(folder, desc.VocabFile))) return null;
        return new NerDetector(folder, desc);
    }

    /// <summary>Finds the model next to the executable (models/ner) or in the given folders.</summary>
    public static NerDetector? TryLoadDefault(params string[] extraFolders)
    {
        var candidates = extraFolders.Append(Path.Combine(AppContext.BaseDirectory, "models", "ner"));
        foreach (var f in candidates)
            if (TryLoad(f) is { } d) return d;
        return null;
    }

    private sealed record Word(int Start, int Length, int FirstToken, int TokenCount);

    public Task<IReadOnlyList<DetectedSpan>> DetectAsync(DetectionContext context, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() => Detect(context.Text, progress, ct), ct);

    /// <summary>
    /// Title blocks and tables often write names in capitals ("RENATO T."), which a cased model reads
    /// badly. When this is on, words in capitals are shown to the model in title case. Offsets do not change.
    /// </summary>
    public bool NormalizeAllCaps { get; set; } = true;

    private static string TitleCaseCapitals(string text)
    {
        var chars = text.ToCharArray();
        foreach (Match m in WordRx().Matches(text))
        {
            if (m.Length < 2 || !m.Value.Any(char.IsLetter) || m.Value.Any(char.IsLower)) continue;
            for (int i = m.Index + 1; i < m.Index + m.Length; i++) chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }

    public IReadOnlyList<DetectedSpan> Detect(string original, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string text = NormalizeAllCaps ? TitleCaseCapitals(original) : original;
        // 1. Words and their WordPiece ids.
        var words = new List<Word>();
        var ids = new List<int>();
        foreach (Match m in WordRx().Matches(text))
        {
            var wid = _tokenizer.EncodeToIds(m.Value, considerPreTokenization: false, considerNormalization: true);
            var pieces = wid.Where(i => i != _cls && i != _sep).ToList();
            if (pieces.Count == 0) continue;
            if (pieces.Count > 16) pieces = pieces.Take(16).ToList();
            words.Add(new Word(m.Index, m.Length, ids.Count, pieces.Count));
            ids.AddRange(pieces);
        }
        if (ids.Count == 0) return [];

        // 2. Sliding windows over the token sequence.
        int content = Math.Min(_desc.MaxSequenceLength, 512) - 2;
        int overlap = Math.Min(Overlap, content / 2);
        int stride = content - overlap;
        int labelsCount = _desc.Labels.Count;
        var bestLabel = new int[ids.Count];
        var bestProb = new float[ids.Count];
        var bestMargin = new int[ids.Count];
        Array.Fill(bestMargin, -1);

        int windows = ids.Count <= content ? 1 : 1 + (int)Math.Ceiling((ids.Count - content) / (double)stride);
        for (int w = 0; w < windows; w++)
        {
            ct.ThrowIfCancellationRequested();
            int start = Math.Min(w * stride, Math.Max(0, ids.Count - content));
            int len = Math.Min(content, ids.Count - start);
            int seq = len + 2;
            var inputIds = new DenseTensor<long>([1, seq]);
            var mask = new DenseTensor<long>([1, seq]);
            var types = new DenseTensor<long>([1, seq]);
            inputIds[0, 0] = _cls;
            for (int i = 0; i < len; i++) inputIds[0, i + 1] = ids[start + i];
            inputIds[0, seq - 1] = _sep;
            for (int i = 0; i < seq; i++) mask[0, i] = 1;

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", mask),
            };
            if (_desc.UsesTokenTypeIds) inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", types));

            using var results = _session.Run(inputs);
            var logits = results.First().AsTensor<float>();
            for (int i = 0; i < len; i++)
            {
                int tok = start + i;
                int margin = Math.Min(i, len - 1 - i);
                if (margin <= bestMargin[tok]) continue;
                // Softmax over the labels of token i + 1 (after [CLS]).
                float max = float.MinValue;
                for (int l = 0; l < labelsCount; l++) max = Math.Max(max, logits[0, i + 1, l]);
                float sum = 0;
                int arg = 0;
                float argVal = float.MinValue;
                for (int l = 0; l < labelsCount; l++)
                {
                    float e = MathF.Exp(logits[0, i + 1, l] - max);
                    sum += e;
                    if (logits[0, i + 1, l] > argVal)
                    {
                        argVal = logits[0, i + 1, l];
                        arg = l;
                    }
                }
                bestLabel[tok] = arg;
                bestProb[tok] = MathF.Exp(argVal - max) / sum;
                bestMargin[tok] = margin;
            }
            progress?.Report((w + 1) / (double)windows);
        }

        // 3. Word labels come from their first piece; BIO tags are merged into entities.
        var spans = new List<DetectedSpan>();
        string? curType = null;
        int curStart = 0, curEnd = 0;
        double probSum = 0;
        int probCount = 0;

        void Flush()
        {
            if (curType is null) return;
            double score = probSum / Math.Max(1, probCount);
            if (score >= MinScore && _typeMap.TryGetValue(curType, out var et))
            {
                string surface = original[curStart..curEnd];
                if (surface.Any(char.IsLetter))
                {
                    spans.Add(new DetectedSpan(curStart, curEnd - curStart, et, surface, Id, score)
                    {
                        Include = score >= IncludeScore,
                        Note = $"NER {curType} {score:P0}",
                    });
                }
            }
            curType = null;
            probSum = 0;
            probCount = 0;
        }

        foreach (var word in words)
        {
            string label = _desc.Labels[bestLabel[word.FirstToken]];
            float p = bestProb[word.FirstToken];
            if (label == "O")
            {
                Flush();
                continue;
            }
            string prefix = label.Length > 2 && label[1] == '-' ? label[..1] : "B";
            string type = label.Length > 2 && label[1] == '-' ? label[2..] : label;
            // An entity does not continue across a line break.
            bool lineBreak = curType is not null && text.AsSpan(curEnd, word.Start - curEnd).Contains('\n');
            if (prefix == "B" || type != curType || lineBreak)
            {
                // Punctuation inside a name ("S.A.", "Jr.") continues the entity when the model says I-.
                Flush();
                curType = type;
                curStart = word.Start;
            }
            curEnd = word.Start + word.Length;
            probSum += p;
            probCount++;
        }
        Flush();

        return TrimPunctuation(original, spans);
    }

    private static List<DetectedSpan> TrimPunctuation(string text, List<DetectedSpan> spans)
    {
        var result = new List<DetectedSpan>(spans.Count);
        foreach (var s in spans)
        {
            int a = s.Start, b = s.End;
            while (a < b && !char.IsLetterOrDigit(text[a])) a++;
            while (b > a && !char.IsLetterOrDigit(text[b - 1]) && text[b - 1] != '.') b--;
            if (b - a < 2)
            {
                continue;
            }
            if (a == s.Start && b == s.End)
            {
                result.Add(s);
                continue;
            }
            result.Add(new DetectedSpan(a, b - a, s.Type, text[a..b], s.Detector, s.Score) { Include = s.Include, Note = s.Note });
        }
        return result;
    }

    public void Dispose() => _session.Dispose();
}
