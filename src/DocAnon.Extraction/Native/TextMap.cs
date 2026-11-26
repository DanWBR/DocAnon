using System.Text;
using DocAnon.Core.Detection;
using DocAnon.Core.Export;
using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;

namespace DocAnon.Extraction.Native;

/// <summary>
/// One string built from many text nodes of a file (the runs of a Word paragraph, the cells of a
/// table), so the detectors see whole sentences and table headers. When an edit spans two nodes,
/// the first node receives the replacement and the rest of the range is removed from the others,
/// so the formatting of the first run is kept.
/// </summary>
internal sealed class TextMap
{
    private sealed record Segment(int Start, string Original, Action<string> Set);

    private readonly StringBuilder _sb = new();
    private readonly List<Segment> _segments = [];

    public string Text => _sb.ToString();
    public int Count => _segments.Count;

    public void Add(string text, Action<string> set)
    {
        if (text.Length == 0) return;
        _segments.Add(new Segment(_sb.Length, text, set));
        _sb.Append(text);
    }

    /// <summary>Text that belongs to the layout (spaces, table bars, line breaks) and is never written back.</summary>
    public void Separator(string s) => _sb.Append(s);

    /// <summary>Ends the current paragraph with a blank line, unless the map already ends with one.</summary>
    public void Break()
    {
        if (_sb.Length == 0) return;
        if (_sb[^1] != '\n') _sb.Append('\n');
        if (_sb.Length < 2 || _sb[^2] != '\n') _sb.Append('\n');
    }

    /// <summary>Writes the edits, given in offsets of <see cref="Text"/>, back to the nodes. Returns how many nodes changed.</summary>
    public int Apply(IEnumerable<(int Start, int Length, string Replacement)> edits)
    {
        var perSegment = new SortedDictionary<int, List<(int A, int B, string Insert)>>();
        int first = 0;
        foreach (var (start, length, replacement) in edits.OrderBy(e => e.Start))
        {
            int end = start + length;
            while (first < _segments.Count && _segments[first].Start + _segments[first].Original.Length <= start) first++;
            bool inserted = false;
            for (int i = first; i < _segments.Count && _segments[i].Start < end; i++)
            {
                var s = _segments[i];
                int a = Math.Max(start, s.Start) - s.Start;
                int b = Math.Min(end, s.Start + s.Original.Length) - s.Start;
                if (b < a) continue;
                if (!perSegment.TryGetValue(i, out var ops)) perSegment[i] = ops = [];
                ops.Add((a, b, inserted ? "" : replacement));
                inserted = true;
            }
        }
        foreach (var (i, ops) in perSegment)
        {
            string t = _segments[i].Original;
            foreach (var (a, b, insert) in ops.OrderByDescending(o => o.A)) t = t[..a] + insert + t[b..];
            if (t != _segments[i].Original) _segments[i].Set(t);
        }
        return perSegment.Count;
    }

    // ---- the two directions -------------------------------------------------------------------

    /// <summary>
    /// Applies the session's decisions to a native copy. Every value that the review sent to the
    /// registry becomes its marker wherever it appears, and a quantity is scaled when the review
    /// scaled the same text (for example "1.200 kg/h", or "1200" under a "Vazão (kg/h)" header).
    /// Returns the anonymized text of the map, for the leak check.
    /// </summary>
    public string Anonymize(NativeCopyContext c, bool quantities = true, bool? commaIsDecimal = null)
    {
        string text = Text;
        if (_segments.Count == 0) return text;
        var ctx = new DetectionContext(text, c.Profile, commaIsDecimal ?? c.CommaIsDecimal);
        var spans = new List<DetectedSpan>(new SessionTermsDetector(c.Registry).Detect(ctx));
        if (quantities)
        {
            var keys = c.Replacements.Where(r => r.Type == EntityType.QUANTIDADE).Select(r => QuantityKey(r.Original)).ToHashSet(StringComparer.Ordinal);
            foreach (var q in new QuantityDetector().Detect(ctx))
            {
                if (q.Quantity is not { Dimension.IsScalable: true } || !keys.Contains(QuantityKey(q.Text))) continue;
                q.Action = SpanAction.Scale;
                spans.Add(q);
            }
        }
        var result = Anonymizer.Apply(text, spans, c.Registry, c.Factors);
        Apply(result.Replacements.Select(r => (r.SourceStart, r.SourceLength, r.Replacement)));
        return result.Text;
    }

    /// <summary>Restores markers (and, when <paramref name="unscale"/> is true, scaled numbers) in the nodes.</summary>
    public List<RestoredItem> Restore(EntityRegistry registry, ScaleFactors factors, bool unscale, bool? commaIsDecimal = null)
    {
        if (_segments.Count == 0) return [];
        var r = Reidentifier.Restore(Text, registry, factors, unscale, commaIsDecimal);
        Apply(r.Items.Where(i => i.Kind != RestoreKind.UnknownMarker).Select(i => (i.SourceStart, i.Found.Length, i.Restored)));
        return [.. r.Items];
    }

    /// <summary>Adds what the leak check still finds in <paramref name="anonymized"/> to the context.</summary>
    public static void CheckLeaks(NativeCopyContext c, string anonymized, string where)
    {
        foreach (var l in LeakChecker.Check(anonymized, c.Registry, c.Profile))
            c.Leaks.Add(l with { Reason = $"{l.Reason} ({where})" });
    }

    private static string QuantityKey(string s) => new(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
}
