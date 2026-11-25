using System.Globalization;
using System.Text;
using DocAnon.Core.Model;

namespace DocAnon.Core.Quantities;

/// <summary>Reads and writes numbers in Portuguese and English conventions.</summary>
public static class NumberText
{
    private const char Nbsp = ' ';
    private const char Thin = ' ';

    public static bool IsGroupSpace(char c) => c is Nbsp or Thin;

    /// <summary>
    /// Parses a number such as <c>1.234,5</c>, <c>1,234.5</c>, <c>-0,05</c> or <c>12 345</c> (with a
    /// non-breaking space). When a single separator is followed by exactly three digits
    /// ("1.234"), the document convention decides whether it is a thousands separator.
    /// </summary>
    public static bool TryParse(string token, bool commaIsDecimal, out double value, out NumberStyle style)
    {
        value = 0;
        style = new NumberStyle(commaIsDecimal ? ',' : '.', null, 0, 1, false);
        string s = token.Trim();
        bool negative = false;
        if (s.Length > 0 && s[0] is '-' or '−')
        {
            negative = true;
            s = s[1..];
        }
        if (s.Length == 0 || !char.IsAsciiDigit(s[0])) return false;

        int dots = 0, commas = 0, spaces = 0, lastDot = -1, lastComma = -1;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '.') { dots++; lastDot = i; }
            else if (c == ',') { commas++; lastComma = i; }
            else if (IsGroupSpace(c)) spaces++;
            else if (!char.IsAsciiDigit(c)) return false;
        }

        char? dec = null;
        char? group = null;
        if (dots > 0 && commas > 0)
        {
            dec = lastDot > lastComma ? '.' : ',';
            group = dec == '.' ? ',' : '.';
            if ((dec == '.' ? dots : commas) > 1) return false;
        }
        else if (dots + commas > 0)
        {
            char sep = dots > 0 ? '.' : ',';
            int count = Math.Max(dots, commas);
            int last = sep == '.' ? lastDot : lastComma;
            int after = s.Length - last - 1;
            if (count > 1) group = sep;
            else
            {
                int before = last;
                bool looksGrouped = after == 3 && before is >= 1 and <= 3 && s[0] != '0';
                if (looksGrouped)
                {
                    bool sepIsDecimal = sep == ',' ? commaIsDecimal : !commaIsDecimal;
                    if (sepIsDecimal) dec = sep;
                    else group = sep;
                }
                else dec = sep;
            }
        }
        if (spaces > 0)
        {
            if (group is not null && group != Nbsp) return false;
            group = s.Contains(Thin) ? Thin : Nbsp;
        }

        // Groups after the first must have exactly three digits.
        if (group is not null)
        {
            string intPart = dec is null ? s : s[..s.LastIndexOf(dec.Value)];
            string[] groups = intPart.Split(group.Value);
            if (groups[0].Length is 0 or > 3) return false;
            for (int i = 1; i < groups.Length; i++) if (groups[i].Length != 3) return false;
        }

        var sb = new StringBuilder();
        int decimals = 0;
        bool afterDec = false;
        foreach (char c in s)
        {
            if (char.IsAsciiDigit(c))
            {
                sb.Append(c);
                if (afterDec) decimals++;
            }
            else if (dec is not null && c == dec)
            {
                sb.Append('.');
                afterDec = true;
            }
        }
        if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
        if (negative) value = -value;

        int sig = 0;
        bool leading = true;
        foreach (char c in sb.ToString())
        {
            if (!char.IsAsciiDigit(c)) continue;
            if (leading && c == '0') continue;
            leading = false;
            sig++;
        }
        style = new NumberStyle(dec ?? (group == ',' ? '.' : group == '.' ? ',' : commaIsDecimal ? ',' : '.'), group, decimals, Math.Max(1, sig), negative);
        return true;
    }

    /// <summary>Scaled numbers keep at least this many significant digits, so rounding does not break balances.</summary>
    public const int MinScaledSignificantDigits = 4;

    /// <summary>
    /// Writes <paramref name="value"/> with the same separators as <paramref name="style"/>, with
    /// at least <paramref name="minSignificant"/> significant digits. Trailing zeros after the decimal
    /// separator are dropped beyond the number of decimals that the original had.
    /// </summary>
    public static string Format(double value, NumberStyle style, int minSignificant = MinScaledSignificantDigits)
    {
        int sig = Math.Max(minSignificant, style.SignificantDigits);
        double abs = Math.Abs(value);
        int decimals;
        if (abs == 0) decimals = style.Decimals;
        else
        {
            int magnitude = (int)Math.Floor(Math.Log10(abs));
            decimals = Math.Max(0, sig - magnitude - 1);
            decimals = Math.Min(decimals, Math.Max(style.Decimals, 6));
            double scale = Math.Pow(10, magnitude - sig + 1);
            if (decimals == 0 && scale > 1) abs = Math.Round(abs / scale) * scale;
        }
        string raw = Math.Round(abs, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture);
        if (raw.Contains('.'))
        {
            int keep = raw.IndexOf('.') + 1 + style.Decimals;
            int cut = raw.Length;
            while (cut > keep && raw[cut - 1] == '0') cut--;
            raw = raw[..cut].TrimEnd('.');
        }
        string intPart = raw, fracPart = "";
        int dot = raw.IndexOf('.');
        if (dot >= 0)
        {
            intPart = raw[..dot];
            fracPart = raw[(dot + 1)..];
        }
        var sb = new StringBuilder();
        if (value < 0) sb.Append('-');
        if (style.GroupSeparator is char g && intPart.Length > 3)
        {
            int first = intPart.Length % 3;
            if (first == 0) first = 3;
            sb.Append(intPart, 0, first);
            for (int i = first; i < intPart.Length; i += 3)
            {
                sb.Append(g);
                sb.Append(intPart, i, 3);
            }
        }
        else sb.Append(intPart);
        if (fracPart.Length > 0)
        {
            sb.Append(style.DecimalSeparator ?? '.');
            sb.Append(fracPart);
        }
        return sb.ToString();
    }
}
