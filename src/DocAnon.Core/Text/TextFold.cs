using System.Globalization;
using System.Text;

namespace DocAnon.Core.Text;

/// <summary>Case- and accent-insensitive helpers that keep character offsets unchanged.</summary>
public static class TextFold
{
    /// <summary>
    /// Lowercases the string and strips diacritics one character at a time, so that index
    /// <c>i</c> of the result corresponds to index <c>i</c> of the input.
    /// </summary>
    public static string FoldPreservingLength(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(FoldChar(c));
        return sb.ToString();
    }

    public static char FoldChar(char c)
    {
        if (c < 128) return char.ToLowerInvariant(c);
        string d = c.ToString().Normalize(NormalizationForm.FormD);
        char b = d[0];
        if (CharUnicodeInfo.GetUnicodeCategory(b) == UnicodeCategory.NonSpacingMark) b = c;
        return char.ToLowerInvariant(b);
    }

    /// <summary>Folds and collapses whitespace. Used as a dictionary key, not for offsets.</summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space && sb.Length > 0) sb.Append(' ');
            space = false;
            sb.Append(FoldChar(c));
        }
        return sb.ToString();
    }

    public static string DigitsOnly(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsAsciiDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static string AlnumUpper(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
