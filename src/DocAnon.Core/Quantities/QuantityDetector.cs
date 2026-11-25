using System.Text.RegularExpressions;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;

namespace DocAnon.Core.Quantities;

/// <summary>
/// Finds numbers with units, money amounts and numeric cells of Markdown tables whose column
/// header (or row label) carries the unit. Every span gets a <see cref="QuantityInfo"/>; scalable
/// ones default to <see cref="SpanAction.Scale"/> and the others to <see cref="SpanAction.Keep"/>.
/// </summary>
public sealed partial class QuantityDetector : IDetector
{
    public string Id => "quantidade";

    [GeneratedRegex(@"(?<![\p{L}\d.,_/\-−])(?<n>[-−]?\d{1,3}(?:[.,  ]\d{3})+(?:[.,]\d+)?|[-−]?\d+(?:[.,]\d+)?)(?![\d]|[.,]\d)")]
    private static partial Regex NumberRx();

    [GeneratedRegex(@"^[  ]?(?:-|–|a|até|to)[  ]?(?<n>\d{1,3}(?:[.,  ]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)(?![\d]|[.,]\d)")]
    private static partial Regex RangeRx();

    [GeneratedRegex(@"^[  ]?(?<m>milhões|milhoes|milhão|milhao|mil|mi|bilhões|bilhoes|bilhão|bilhao|bi|trilhões|tri|millions|million|billions|billion|thousand|bn|MM|M|k|K|B)(?![\p{L}\d])")]
    private static partial Regex MagnitudeRx();

    [GeneratedRegex(@"^[  ]?(?:de[  ]+)?(?<c>reais|real|dólares|dolares|dólar|dolar|USD|BRL|EUR|euros|euro|R\$|US\$|U\$|€|£)(?![\p{L}\d])")]
    private static partial Regex CurrencySuffixRx();

    [GeneratedRegex(@"(?<c>MMUS\$|MUS\$|kUS\$|MMR\$|MR\$|US\$|U\$|R\$|USD|BRL|EUR|€|£|\$)[  ]?(?:(?<m>MM|M|mil|k)[  ]?)?$")]
    private static partial Regex CurrencyPrefixRx();

    [GeneratedRegex(@"^[  ]?/[  ]?(?<u>[^\s,;:()\[\]{}|]+)")]
    private static partial Regex PerUnitRx();

    [GeneratedRegex(@"^(?:[  ]?de)?[  ]?(?<u>[^\s,;:()\[\]{}|]+)")]
    private static partial Regex UnitCandidateRx();

    public IEnumerable<DetectedSpan> Detect(DetectionContext context)
    {
        var spans = new List<DetectedSpan>();
        DetectInline(context, spans);
        DetectTables(context, spans);
        return spans;
    }

    private void DetectInline(DetectionContext ctx, List<DetectedSpan> spans)
    {
        string text = ctx.Text;
        foreach (Match m in NumberRx().Matches(text))
        {
            var g = m.Groups["n"];
            if (!NumberText.TryParse(g.Value, ctx.CommaIsDecimal, out double v1, out var st1)) continue;
            var numbers = new List<NumberToken> { new(g.Index, g.Length, v1, st1) };
            int pos = g.Index + g.Length;
            int spanStart = g.Index;

            var range = RangeRx().Match(text[pos..Math.Min(text.Length, pos + 40)]);
            if (range.Success)
            {
                var rg = range.Groups["n"];
                if (NumberText.TryParse(rg.Value, ctx.CommaIsDecimal, out double v2, out var st2))
                {
                    numbers.Add(new NumberToken(pos + rg.Index, rg.Length, v2, st2));
                    pos += range.Length;
                }
            }

            // Currency before the number: "R$ 12,5", "US$ MM 3,4".
            string before = text[Math.Max(0, spanStart - 12)..spanStart];
            var pre = CurrencyPrefixRx().Match(before);
            string tail = text[pos..Math.Min(text.Length, pos + 40)];
            int consumed = 0;
            string? magnitude = null;
            var mag = MagnitudeRx().Match(tail);

            UnitParse? unit = null;
            if (pre.Success)
            {
                spanStart -= before.Length - pre.Index;
                if (mag.Success)
                {
                    magnitude = mag.Groups["m"].Value;
                    consumed += mag.Length;
                }
                var per = PerUnitRx().Match(tail[consumed..]);
                UnitParse? denom = null;
                if (per.Success && LongestUnit(per.Groups["u"].Value, tail, consumed + per.Groups["u"].Index, inDenominator: true) is { } du)
                {
                    denom = du.Unit;
                    consumed += per.Groups["u"].Index + du.Length;
                }
                unit = UnitCatalog.Money(pre.Groups["c"].Value + (magnitude is null ? "" : " " + magnitude) + (denom is null ? "" : "/" + denom.Text), denom);
            }
            else
            {
                // A magnitude word may sit between the number and the unit: "12 mil t/ano", "3,4 MM US$".
                int afterMag = 0;
                if (mag.Success && mag.Groups["m"].Value is not ("M" or "k" or "K" or "B")) afterMag = mag.Length;
                else if (mag.Success && CurrencySuffixRx().IsMatch(tail[mag.Length..])) afterMag = mag.Length;

                var cur = CurrencySuffixRx().Match(tail[afterMag..]);
                if (cur.Success)
                {
                    consumed = afterMag + cur.Length;
                    var per = PerUnitRx().Match(tail[consumed..]);
                    UnitParse? denom = null;
                    if (per.Success && LongestUnit(per.Groups["u"].Value, tail, consumed + per.Groups["u"].Index, inDenominator: true) is { } du)
                    {
                        denom = du.Unit;
                        consumed += per.Groups["u"].Index + du.Length;
                    }
                    unit = UnitCatalog.Money(cur.Groups["c"].Value + (denom is null ? "" : "/" + denom.Text), denom);
                }
                else
                {
                    var cand = UnitCandidateRx().Match(tail[afterMag..]);
                    if (cand.Success && LongestUnit(cand.Groups["u"].Value, tail, afterMag + cand.Groups["u"].Index, inDenominator: false) is { } u)
                    {
                        unit = u.Unit;
                        consumed = afterMag + cand.Groups["u"].Index + u.Length;
                    }
                }
            }
            if (unit is null) continue;

            int end = pos + consumed;
            spans.Add(MakeSpan(text, spanStart, end, numbers, unit, fromHeader: false));
        }
    }

    /// <summary>Finds the longest prefix of <paramref name="candidate"/> that is a unit and ends on a word boundary.</summary>
    private static (UnitParse Unit, int Length)? LongestUnit(string candidate, string tail, int offsetInTail, bool inDenominator)
    {
        for (int len = candidate.Length; len >= 1; len--)
        {
            int next = offsetInTail + len;
            if (next < tail.Length && char.IsLetterOrDigit(tail[next]) && len < candidate.Length && char.IsLetterOrDigit(candidate[len - 1])) continue;
            string c = candidate[..len];
            var parsed = inDenominator ? UnitCatalog.ParseDenominator(c) : UnitCatalog.Parse(c);
            if (parsed is not null) return (parsed, len);
        }
        return null;
    }

    private static DetectedSpan MakeSpan(string text, int start, int end, List<NumberToken> numbers, UnitParse unit, bool fromHeader)
    {
        var info = new QuantityInfo(numbers, unit.Text, unit.Dimension, unit.Kind, fromHeader);
        bool scalable = unit.Dimension.IsScalable;
        return new DetectedSpan(start, end - start, EntityType.QUANTIDADE, text[start..end], "quantidade", scalable ? 0.9 : 0.5)
        {
            Quantity = info,
            Action = scalable ? SpanAction.Scale : SpanAction.Keep,
            Note = unit.Kind switch
            {
                QuantityKind.Extensive => "extensiva, escalada",
                QuantityKind.Monetary => "monetária, escalada",
                QuantityKind.Intensive => "intensiva, mantida",
                QuantityKind.Geometric => "dimensão de equipamento, mantida",
                _ => null,
            },
        };
    }

    // ---- Markdown tables ------------------------------------------------------------------

    [GeneratedRegex(@"[\(\[]\s*(?<u>[^\)\]]+?)\s*[\)\]]\s*$")]
    private static partial Regex HeaderUnitRx();

    [GeneratedRegex(@"^\s*[-−]?\d[\d.,  ]*\s*$")]
    private static partial Regex NumericCellRx();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}")]
    private static partial Regex SeparatorRowRx();

    /// <summary>Parses a unit written in a header or a unit column, including money ("MM US$", "R$/t").</summary>
    public static UnitParse? ParseHeaderUnit(string s)
    {
        string u = s.Trim();
        if (u.Length == 0) return null;
        var money = Regex.Match(u, @"^(?:(?<m>MM|M|mil|k)\s?)?(?<c>MMUS\$|MUS\$|kUS\$|MMR\$|MR\$|US\$|U\$|R\$|USD|BRL|EUR|€|£|\$)(?:\s?(?<m2>MM|M|mil|mi|k))?(?:\s?/\s?(?<d>.+))?$");
        if (money.Success)
        {
            UnitParse? denom = money.Groups["d"].Success ? UnitCatalog.Parse(money.Groups["d"].Value) : null;
            if (money.Groups["d"].Success && denom is null) return null;
            return UnitCatalog.Money(u, denom);
        }
        return UnitCatalog.Parse(u);
    }

    private static UnitParse? UnitFromLabel(string cell)
    {
        var m = HeaderUnitRx().Match(cell);
        if (m.Success) return ParseHeaderUnit(m.Groups["u"].Value);
        int comma = cell.LastIndexOf(',');
        if (comma >= 0) return ParseHeaderUnit(cell[(comma + 1)..]);
        return null;
    }

    private void DetectTables(DetectionContext ctx, List<DetectedSpan> spans)
    {
        string text = ctx.Text;
        var lines = new List<(int Start, string Line)>();
        int p = 0;
        while (p <= text.Length)
        {
            int nl = text.IndexOf('\n', p);
            if (nl < 0) nl = text.Length;
            lines.Add((p, text[p..nl].TrimEnd('\r')));
            p = nl + 1;
        }

        int i = 0;
        while (i < lines.Count)
        {
            if (!lines[i].Line.TrimStart().StartsWith('|'))
            {
                i++;
                continue;
            }
            int first = i;
            while (i < lines.Count && lines[i].Line.TrimStart().StartsWith('|')) i++;
            ProcessTable(ctx, lines.GetRange(first, i - first), spans);
        }
    }

    private static List<(int Start, int Length, string Text)> Cells(int lineStart, string line)
    {
        var cells = new List<(int, int, string)>();
        int idx = line.IndexOf('|');
        while (idx >= 0)
        {
            int next = line.IndexOf('|', idx + 1);
            if (next < 0) break;
            string raw = line[(idx + 1)..next];
            int lead = raw.Length - raw.TrimStart().Length;
            string trimmed = raw.Trim();
            cells.Add((lineStart + idx + 1 + lead, trimmed.Length, trimmed));
            idx = next;
        }
        return cells;
    }

    private static void ProcessTable(DetectionContext ctx, List<(int Start, string Line)> rows, List<DetectedSpan> spans)
    {
        if (rows.Count < 2) return;
        var header = Cells(rows[0].Start, rows[0].Line);
        var columnUnits = header.Select(h => UnitFromLabel(h.Text)).ToArray();

        // Columns named "Unidade" or "Unit" hold the unit of each row.
        int unitColumn = header.FindIndex(h => h.Text.Trim().ToLowerInvariant() is "unidade" or "unid." or "unit" or "units" or "uom" or "un.");

        for (int r = 1; r < rows.Count; r++)
        {
            if (SeparatorRowRx().IsMatch(rows[r].Line)) continue;
            var cells = Cells(rows[r].Start, rows[r].Line);
            UnitParse? rowUnit = null;
            if (unitColumn >= 0 && unitColumn < cells.Count) rowUnit = ParseHeaderUnit(cells[unitColumn].Text);
            if (rowUnit is null && cells.Count > 0) rowUnit = UnitFromLabel(cells[0].Text);

            for (int c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                if (cell.Length == 0 || !NumericCellRx().IsMatch(cell.Text)) continue;
                UnitParse? unit = rowUnit ?? (c < columnUnits.Length ? columnUnits[c] : null);
                if (unit is null) continue;
                if (!NumberText.TryParse(cell.Text, ctx.CommaIsDecimal, out double v, out var st)) continue;
                var numbers = new List<NumberToken> { new(cell.Start, cell.Length, v, st) };
                spans.Add(MakeSpan(ctx.Text, cell.Start, cell.Start + cell.Length, numbers, unit, fromHeader: true));
            }
        }
    }
}
