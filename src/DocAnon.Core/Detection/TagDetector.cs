using System.Text.RegularExpressions;
using DocAnon.Core.Model;

namespace DocAnon.Core.Detection;

/// <summary>
/// Recognizes equipment tags (P-101A, E-201, TK-3001) and ISA instrument tags (FIC-2301, PT-101).
/// Tags themselves are kept by default, because the AI needs them to follow the process. A unit or
/// area prefix ("2100-P-101", "U-2100") identifies the plant, so it becomes an UNIDADE span that is
/// anonymized by default.
/// </summary>
public sealed partial class TagDetector : IDetector
{
    public string Id => "tag";

    private static readonly HashSet<string> EquipmentLetters = new(StringComparer.Ordinal)
    {
        "P", "E", "V", "T", "C", "R", "K", "D", "F", "H", "B", "G", "M", "X", "Y", "Z",
        "TK", "TQ", "HX", "EX", "AC", "CT", "BC", "DR", "AG", "MX", "BL", "SC", "CY", "PU", "GE", "GT", "ST", "ME", "BA", "BB", "PP", "VS", "UV",
    };

    private const string IsaFirst = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string IsaFollow = "ACDEFGHIKLMNOQRSTUVWXYZ";

    /// <summary>Uppercase prefixes that look like tags but are standards, roads, states or common acronyms.</summary>
    private static readonly HashSet<string> NotTags = new(StringComparer.Ordinal)
    {
        "API", "ISO", "NR", "NBR", "ABNT", "ASME", "ASTM", "IEC", "DIN", "EN", "ANSI", "NFPA", "ISA", "IEEE", "AISI", "SAE", "AWS", "TEMA", "BS", "JIS", "UL", "CE",
        "BR", "AL", "AP", "AM", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SP", "SE", "TO",
        "COVID", "MP", "PL", "PEC", "RDC", "CLT", "CPC", "CPF", "CNPJ", "OAB", "CRM", "CREA", "CEP", "RG", "CNH", "NF", "NFE", "ICMS", "IPI", "PIS", "ISS", "IR", "ID", "QR", "USB", "HDMI", "PDF", "SQL", "WIN", "VER", "REV", "DOC", "FIG", "TAB", "ANEXO", "ITEM", "DN", "PN", "SCH", "NPS", "RFC", "ONU", "UN",
    };

    [GeneratedRegex(@"(?<![\p{L}\d\-/.])(?:(?<pre>(?:[A-Z]{1,3}-)?\d{2,5})-)?(?<tag>(?<letters>[A-Z]{1,5})-?(?<num>\d{2,7})(?<suf>[A-Z]{1,2})?)(?![\p{L}\d]|[.,]\d)")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"(?<![\p{L}\d])(?:U|UN|Unidade|Unit|Área|Area|AREA|ÁREA)[\s-](?<code>\d{2,5})(?![\p{L}\d])")]
    private static partial Regex UnitRefRx();

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        var profile = context.Profile;
        var prefixPatterns = profile.UnitPrefixPatterns.Select(p => new Regex("^(?:" + p + ")$", RegexOptions.CultureInvariant)).ToList();

        foreach (Match m in UnitRefRx().Matches(context.Text))
        {
            var g = m.Groups["code"];
            yield return new DetectedSpan(g.Index, g.Length, EntityType.UNIDADE, g.Value, Id, 0.9) { Note = "código de unidade" };
        }

        foreach (Match m in TagRx().Matches(context.Text))
        {
            string letters = m.Groups["letters"].Value;
            if (NotTags.Contains(letters)) continue;

            // Without a hyphen, only instrument-like letter groups count ("FIC2301"), not words.
            bool hyphen = m.Groups["tag"].Value.Contains('-');
            EntityType? type = null;
            if (EquipmentLetters.Contains(letters) && hyphen) type = EntityType.TAG_EQUIP;
            else if (letters.Length is >= 2 and <= 5 && IsaFirst.Contains(letters[0]) && letters[1..].All(c => IsaFollow.Contains(c)) && (hyphen || letters.Length >= 3))
                type = EntityType.TAG_INSTR;
            if (type is null) continue;

            var pre = m.Groups["pre"];
            if (pre.Success)
            {
                bool isUnit = prefixPatterns.Count == 0 || prefixPatterns.Any(r => r.IsMatch(pre.Value));
                if (isUnit)
                {
                    // Only the digits of "U-2100" are replaced, so the text reads "U-[UNIDADE_1]".
                    int dash = pre.Value.LastIndexOf('-');
                    int s = dash >= 0 ? pre.Index + dash + 1 : pre.Index;
                    int len = pre.Index + pre.Length - s;
                    yield return new DetectedSpan(s, len, EntityType.UNIDADE, context.Text.Substring(s, len), Id, 0.9) { Note = "prefixo de unidade na tag" };
                }
            }

            var tag = m.Groups["tag"];
            yield return new DetectedSpan(tag.Index, tag.Length, type.Value, tag.Value, Id, 0.7)
            {
                Include = profile.AnonymizeAllTags,
                Note = profile.AnonymizeAllTags ? "tag" : "tag genérica, mantida por padrão",
            };
        }
    }
}
