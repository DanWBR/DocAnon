using System.Text;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Quantities;

namespace DocAnon.Core.Pseudonymization;

/// <summary>A replacement as applied, with its position in both texts.</summary>
public sealed record AppliedReplacement(int SourceStart, int SourceLength, int OutputStart, int OutputLength, string Original, string Replacement, EntityType Type, SpanAction Action);

public sealed record AnonymizationResult(string Text, IReadOnlyList<AppliedReplacement> Replacements)
{
    public IReadOnlyList<TextReplacement> AsTextReplacements() =>
        Replacements.Select(r => new TextReplacement(r.SourceStart, r.SourceLength, r.Original, r.Replacement, r.Type)).ToList();
}

public static class Anonymizer
{
    /// <summary>
    /// Applies the included spans to the text. Pseudonymized spans become markers from
    /// <paramref name="registry"/>, and scaled spans get their numbers multiplied by
    /// <paramref name="factors"/>.
    /// </summary>
    public static AnonymizationResult Apply(string text, IEnumerable<DetectedSpan> spans, EntityRegistry registry, ScaleFactors factors)
    {
        var active = DetectionPipeline.Resolve(spans.Where(s => s.Include && s.Action != SpanAction.Keep));
        var sb = new StringBuilder(text.Length);
        var applied = new List<AppliedReplacement>();
        int cursor = 0;
        foreach (var s in active)
        {
            sb.Append(text, cursor, s.Start - cursor);
            string replacement;
            var type = s.Type == EntityType.QUANTIDADE && s.Action == SpanAction.Pseudonymize ? EntityType.VALOR : s.Type;
            if (s.Action == SpanAction.Scale && s.Quantity is { Dimension.IsScalable: true } q)
            {
                replacement = factors.ScaleSpanText(text, s.Start, q, onScaled: (n, t) => registry.RecordScaled(q.Dimension, t, text.Substring(n.Start, n.Length)));
                // The unit text after the last number stays as it is.
                var last = q.Numbers.MaxBy(n => n.End)!;
                replacement += text[last.End..s.End];
            }
            else
            {
                replacement = registry.GetOrCreate(type, s.Text).Token;
            }
            applied.Add(new AppliedReplacement(s.Start, s.Length, sb.Length, replacement.Length, s.Text, replacement, type, s.Action));
            sb.Append(replacement);
            cursor = s.End;
        }
        sb.Append(text, cursor, text.Length - cursor);
        return new AnonymizationResult(sb.ToString(), applied);
    }
}
