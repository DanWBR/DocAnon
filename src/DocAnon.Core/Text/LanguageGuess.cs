using System.Text.RegularExpressions;

namespace DocAnon.Core.Text;

public enum DocLanguage
{
    Portuguese,
    English,
}

/// <summary>
/// Guesses the language and the decimal convention of a text. The decimal convention matters
/// because "1.234" is one thousand two hundred thirty-four in Portuguese and one point two
/// in English.
/// </summary>
public static partial class LanguageGuess
{
    private static readonly HashSet<string> Pt = ["de", "que", "não", "nao", "para", "com", "uma", "os", "as", "dos", "das", "pela", "pelo", "são", "foi", "na", "no", "em", "ao", "também", "vazão", "pressão"];
    private static readonly HashSet<string> En = ["the", "and", "of", "to", "is", "with", "for", "that", "this", "are", "was", "by", "from", "an", "be", "flow", "pressure", "which"];

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRx();

    [GeneratedRegex(@"\d,\d{1,2}(?!\d)")]
    private static partial Regex CommaDecimalRx();

    [GeneratedRegex(@"\d\.\d{1,2}(?!\d)")]
    private static partial Regex DotDecimalRx();

    public static DocLanguage Language(string text)
    {
        int pt = 0, en = 0, n = 0;
        foreach (Match m in WordRx().Matches(text))
        {
            string w = m.Value.ToLowerInvariant();
            if (Pt.Contains(w)) pt++;
            else if (En.Contains(w)) en++;
            if (++n > 20000) break;
        }
        return en > pt ? DocLanguage.English : DocLanguage.Portuguese;
    }

    /// <summary>True when the comma is the decimal separator (Portuguese convention).</summary>
    public static bool CommaIsDecimal(string text)
    {
        int comma = CommaDecimalRx().Count(text);
        int dot = DotDecimalRx().Count(text);
        if (comma != dot) return comma > dot;
        return Language(text) == DocLanguage.Portuguese;
    }
}
