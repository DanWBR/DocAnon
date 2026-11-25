using System.Text.RegularExpressions;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Text;

namespace DocAnon.Core.Export;

public sealed record LeakFinding(int Start, int Length, string Text, string Reason, EntityType? Type);

/// <summary>
/// Last check before the anonymized text leaves the app. It looks for every real value stored in
/// the session, for the profile terms, and runs the personal-data detectors again.
/// </summary>
public static partial class LeakChecker
{
    [GeneratedRegex(@"\[[A-Z_]+_\d+\]")]
    private static partial Regex MarkerRx();

    public static List<LeakFinding> Check(string output, EntityRegistry registry, CompanyProfile? profile = null, IEnumerable<IDetector>? extraDetectors = null)
    {
        var findings = new List<LeakFinding>();
        string folded = TextFold.FoldPreservingLength(output);
        var markers = MarkerRx().Matches(output).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        bool InsideMarker(int s, int e) => markers.Any(m => s < m.End && m.Index < e);

        void Search(string needle, string reason, EntityType? type)
        {
            string f = TextFold.FoldPreservingLength(needle.Trim());
            if (f.Length < 3) return;
            string pattern = @"(?<![\p{L}\d])" + Regex.Escape(f).Replace(@"\ ", @"\s+") + @"(?![\p{L}\d])";
            foreach (Match m in Regex.Matches(folded, pattern, RegexOptions.CultureInvariant))
            {
                if (InsideMarker(m.Index, m.Index + m.Length)) continue;
                findings.Add(new LeakFinding(m.Index, m.Length, output.Substring(m.Index, m.Length), reason, type));
            }
        }

        foreach (var rec in registry.Records)
        {
            if (rec.Type == EntityType.VALOR) continue;
            foreach (var v in rec.Variants)
            {
                Search(v, $"valor real de {rec.Token}", rec.Type);
                // Identifiers may come back with other formatting, such as a CPF without dots.
                if (EntityTypeInfo.IsNumericIdentifier(rec.Type))
                {
                    string digits = TextFold.DigitsOnly(v);
                    if (digits.Length >= 6 && digits != v) Search(digits, $"valor real de {rec.Token} sem formatação", rec.Type);
                }
            }
        }

        if (profile is not null)
        {
            foreach (var t in profile.Terms)
                foreach (var s in t.Aliases.Prepend(t.Text))
                    Search(s, "termo do perfil da empresa", t.Type);
        }

        var ctx = new DetectionContext(output, profile);
        var detectors = new List<IDetector> { new PersonalDataDetector() };
        if (extraDetectors is not null) detectors.AddRange(extraDetectors);
        foreach (var d in detectors)
        {
            foreach (var s in d.Detect(ctx))
            {
                if (InsideMarker(s.Start, s.End)) continue;
                findings.Add(new LeakFinding(s.Start, s.Length, s.Text, $"o detector {s.Detector} ainda encontra {EntityTypeInfo.Label(s.Type)}", s.Type));
            }
        }

        return findings
            .GroupBy(f => (f.Start, f.Length))
            .Select(g => g.First())
            .OrderBy(f => f.Start)
            .ToList();
    }
}
