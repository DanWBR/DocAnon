using DocAnon.Core.Model;

namespace DocAnon.Extraction;

public static class RangeReplacer
{
    /// <summary>
    /// Applies the replacements that touch the document range [<paramref name="rangeStart"/>,
    /// rangeStart + original.Length) to <paramref name="original"/>. When a replacement spans two
    /// text objects, the first one receives the marker and the rest of the second one is removed.
    /// </summary>
    public static string Apply(string original, int rangeStart, IReadOnlyList<TextReplacement> replacements)
    {
        if (rangeStart < 0) return original;
        int rangeEnd = rangeStart + original.Length;
        var hits = replacements
            .Where(r => r.Start < rangeEnd && r.Start + r.Length > rangeStart)
            .OrderByDescending(r => r.Start)
            .ToList();
        if (hits.Count == 0) return original;
        string s = original;
        foreach (var r in hits)
        {
            int a = Math.Max(r.Start, rangeStart) - rangeStart;
            int b = Math.Min(r.Start + r.Length, rangeEnd) - rangeStart;
            string insert = r.Start >= rangeStart ? r.Replacement : "";
            s = s[..a] + insert + s[b..];
        }
        return s;
    }
}
