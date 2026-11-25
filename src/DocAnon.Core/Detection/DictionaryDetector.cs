using System.Text.RegularExpressions;
using DocAnon.Core.Model;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>Finds the terms of the company profile, ignoring case and accents, and the matches of its patterns.</summary>
public sealed class DictionaryDetector : IDetector
{
    public string Id => "perfil";

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        var entries = new List<(string Folded, EntityType Type)>();
        foreach (var term in context.Profile.Terms)
        {
            foreach (string s in term.Aliases.Prepend(term.Text))
            {
                string f = TextFold.FoldPreservingLength(s.Trim());
                if (f.Length >= 2) entries.Add((f, term.Type));
            }
        }
        // Longer terms first, so "Refinaria Exemplo Sul" wins over "Refinaria Exemplo".
        foreach (var (folded, type) in entries.OrderByDescending(e => e.Folded.Length))
        {
            string pattern = @"(?<![\p{L}\d])" + Regex.Escape(folded).Replace(@"\ ", @"\s+") + @"(?![\p{L}\d])";
            foreach (Match m in Regex.Matches(context.Folded, pattern, RegexOptions.CultureInvariant))
            {
                yield return new DetectedSpan(m.Index, m.Length, type, context.Text.Substring(m.Index, m.Length), Id, 1.0)
                {
                    Note = "termo do perfil da empresa",
                };
            }
        }

        foreach (var p in context.Profile.Patterns)
        {
            if (string.IsNullOrWhiteSpace(p.Pattern)) continue;
            Regex rx;
            try
            {
                rx = new Regex(p.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException)
            {
                // An invalid expression in the profile must not stop the detection of the rest.
                continue;
            }
            MatchCollection matches;
            try
            {
                matches = rx.Matches(context.Text);
                _ = matches.Count;
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            foreach (Match m in matches)
            {
                if (m.Length == 0) continue;
                yield return new DetectedSpan(m.Index, m.Length, p.Type, m.Value, Id, 1.0)
                {
                    Note = "padrão do perfil" + (string.IsNullOrWhiteSpace(p.Label) ? "" : ": " + p.Label),
                };
            }
        }
    }
}
