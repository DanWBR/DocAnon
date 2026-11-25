using System.Text.RegularExpressions;
using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>
/// Marks every value that the session already anonymized in another document, so that a name
/// found by NER in the report is also replaced in the drawing where NER did not see it.
/// </summary>
public sealed class SessionTermsDetector(EntityRegistry registry) : IDetector
{
    public string Id => "sessao";

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        var entries = registry.Records
            .Where(r => r.Type != EntityType.VALOR)
            .SelectMany(r => r.Variants.Select(v => (Variant: v.Trim(), Record: r)))
            .Where(e => e.Variant.Length >= 3)
            .OrderByDescending(e => e.Variant.Length)
            .ToList();
        foreach (var (variant, rec) in entries)
        {
            string folded = TextFold.FoldPreservingLength(variant);
            string pattern = @"(?<![\p{L}\d])" + Regex.Escape(folded).Replace(@"\ ", @"\s+") + @"(?![\p{L}\d])";
            foreach (Match m in Regex.Matches(context.Folded, pattern, RegexOptions.CultureInvariant))
            {
                yield return new DetectedSpan(m.Index, m.Length, rec.Type, context.Text.Substring(m.Index, m.Length), Id, 1.0)
                {
                    Note = $"já anonimizado na sessão como {rec.Token}",
                };
            }
        }
    }
}
