using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Quantities;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>Runs all detectors and keeps one span per region of text.</summary>
public sealed class DetectionPipeline
{
    private readonly List<IDetector> _detectors = [];
    private readonly List<IAsyncDetector> _asyncDetectors = [];

    public DetectionPipeline Add(IDetector d)
    {
        _detectors.Add(d);
        return this;
    }

    public DetectionPipeline Add(IAsyncDetector d)
    {
        _asyncDetectors.Add(d);
        return this;
    }

    /// <summary>The regex, tag, quantity and profile detectors, which do not need any model.</summary>
    public static DetectionPipeline CreateDefault() => new DetectionPipeline()
        .Add(new DictionaryDetector())
        .Add(new PersonalDataDetector())
        .Add(new TagDetector())
        .Add(new QuantityDetector())
        .Add(new FirstNameDetector());

    public IReadOnlyList<IDetector> Detectors => _detectors;
    public IReadOnlyList<IAsyncDetector> AsyncDetectors => _asyncDetectors;

    public List<DetectedSpan> Run(DetectionContext ctx)
    {
        var all = _detectors.SelectMany(d => d.Detect(ctx)).ToList();
        return Propagate(ctx, Resolve(all, ctx.Profile));
    }

    /// <summary>
    /// Every value that will be replaced by a marker is searched in the whole text, so a name that
    /// NER found once is also replaced where NER missed it.
    /// </summary>
    public static List<DetectedSpan> Propagate(DetectionContext ctx, List<DetectedSpan> spans)
    {
        var extra = new List<DetectedSpan>();
        var removed = new HashSet<DetectedSpan>();
        var seeds = spans
            .Where(s => s.Include && s.Action == SpanAction.Pseudonymize && s.Text.Trim().Length >= 3)
            .GroupBy(s => TextFold.Key(s.Text))
            .Select(g => g.First())
            .OrderByDescending(s => s.Length);
        foreach (var seed in seeds)
        {
            string folded = TextFold.FoldPreservingLength(seed.Text.Trim());
            string pattern = @"(?<![\p{L}\d])" + System.Text.RegularExpressions.Regex.Escape(folded).Replace(@"\ ", @"\s+") + @"(?![\p{L}\d])";
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(ctx.Folded, pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                // A single name ("Paulo") only spreads to capitalized occurrences that do not follow
                // "São", "Santo" or "Santa", so "São Paulo" stays a place.
                if (seed.Type == EntityType.PESSOA && !seed.Text.Trim().Contains(' '))
                {
                    if (!char.IsUpper(ctx.Text[m.Index])) continue;
                    string before = ctx.Folded[Math.Max(0, m.Index - 7)..m.Index];
                    if (System.Text.RegularExpressions.Regex.IsMatch(before, @"(?:sao|santo|santa|s\.)\s+$")) continue;
                }
                var clashes = spans.Where(s => s.Start < m.Index + m.Length && m.Index < s.End).ToList();
                // An unchecked or kept span (a low-score NER hit, a generic tag) gives way to a known entity.
                if (clashes.Any(c => c.Include && c.Action != SpanAction.Keep || c.Detector == "manual")) continue;
                foreach (var c in clashes) removed.Add(c);
                extra.Add(new DetectedSpan(m.Index, m.Length, seed.Type, ctx.Text.Substring(m.Index, m.Length), "propagacao", seed.Score)
                {
                    Note = "mesma entidade encontrada em outro ponto do documento",
                });
            }
        }
        if (extra.Count == 0) return spans;
        return Resolve(spans.Where(s => !removed.Contains(s)).Concat(extra), ctx.Profile);
    }

    public async Task<List<DetectedSpan>> RunAsync(DetectionContext ctx, IProgress<(string Stage, double Fraction)>? progress = null, CancellationToken ct = default)
    {
        var all = new List<DetectedSpan>();
        foreach (var d in _detectors)
        {
            ct.ThrowIfCancellationRequested();
            all.AddRange(d.Detect(ctx));
        }
        foreach (var d in _asyncDetectors)
        {
            ct.ThrowIfCancellationRequested();
            var p = progress is null ? null : new Progress<double>(f => progress.Report((d.Id, f)));
            all.AddRange(await d.DetectAsync(ctx, p, ct).ConfigureAwait(false));
        }
        return Propagate(ctx, Resolve(all, ctx.Profile));
    }

    private static int Priority(DetectedSpan s) => s.Detector switch
    {
        "manual" => 0,
        _ when s.Validated => 1,
        "perfil" or "sessao" or "propagacao" => 2,
        "regex" => 3,
        "tag" => 4,
        "quantidade" => 5,
        "ner" or "nomes" => 6,
        "llm" => 7,
        _ => 8,
    };

    /// <summary>
    /// Drops exceptions from the profile and resolves overlaps: manual marks win, then validated
    /// identifiers, profile terms, regex rules, tags, quantities, NER and LLM suggestions. Inside the
    /// same tier the longer span wins, then the higher score.
    /// </summary>
    public static List<DetectedSpan> Resolve(IEnumerable<DetectedSpan> candidates, CompanyProfile? profile = null)
    {
        var exceptions = new HashSet<string>((profile?.Exceptions ?? []).Select(TextFold.Key));
        var ordered = TechnicalTerms.Filter(candidates)
            .Where(s => !exceptions.Contains(TextFold.Key(s.Text)))
            .OrderBy(Priority)
            .ThenByDescending(s => s.Length)
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.Start);

        var accepted = new List<DetectedSpan>();
        foreach (var s in ordered)
        {
            // Accepted spans are few compared with the text, so a linear scan is enough.
            bool clash = false;
            foreach (var a in accepted)
            {
                if (a.Overlaps(s))
                {
                    clash = true;
                    break;
                }
            }
            if (!clash) accepted.Add(s);
        }
        accepted.Sort((a, b) => a.Start.CompareTo(b.Start));
        return accepted;
    }
}
