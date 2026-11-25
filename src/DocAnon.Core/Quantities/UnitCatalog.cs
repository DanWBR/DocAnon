using DocAnon.Core.Model;

namespace DocAnon.Core.Quantities;

internal enum UnitClass
{
    /// <summary>Mass, moles, volume, energy. Counts as one power of the "extension" base.</summary>
    Ext,

    /// <summary>Energy per time or a shorthand flow (tpd, bpd). Already a flow, one power of extension.</summary>
    ExtRate,

    Time,
    Money,
    Intensive,
    Geometric,
}

public sealed record UnitParse(string Text, ScaleDimension Dimension, QuantityKind Kind);

/// <summary>
/// Unit lexicon and a small parser for unit expressions such as <c>kg/h</c>, <c>kJ/kg.K</c>,
/// <c>Nm³/d</c> or <c>US$/t</c>. The parser only cares about how a unit scales, not about
/// conversion factors.
/// </summary>
public static class UnitCatalog
{
    private static readonly Dictionary<string, UnitClass> Exact = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, UnitClass> Loose = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Units that are only accepted after a slash, because alone they are ordinary words or letters.</summary>
    private static readonly HashSet<string> DenominatorOnly = new(StringComparer.Ordinal) { "a", "d", "s", "h", "hr", "ano", "dia", "day", "yr", "mes", "mês", "month", "min", "semana", "week", "year" };

    static UnitCatalog()
    {
        void Add(UnitClass c, bool loose, params string[] symbols)
        {
            foreach (var s in symbols)
            {
                Exact[s] = c;
                if (loose) Loose[s] = c;
            }
        }

        Add(UnitClass.Ext, false, "g", "kg", "t", "Mt", "kt", "Gt", "lb", "lbs", "klb", "Mlb", "mg");
        Add(UnitClass.Ext, true, "ton", "tons", "tonne", "tonnes", "tonelada", "toneladas", "ton.");
        Add(UnitClass.Ext, false, "mol", "kmol", "gmol", "lbmol", "Mmol", "mmol", "kgmol");
        Add(UnitClass.Ext, false, "m³", "m3", "Nm³", "Nm3", "Sm³", "Sm3", "MMm³", "MMm3", "Mm³", "Mm3", "km³", "dam³",
            "L", "l", "kL", "ML", "mL", "bbl", "bbls", "kbbl", "MMbbl", "Mbbl", "gal", "gals", "scf", "Mscf", "MMscf", "MMSCF", "SCF", "ft³", "ft3", "Mcf", "MMcf", "Bcf");
        Add(UnitClass.Ext, false, "J", "kJ", "MJ", "GJ", "TJ", "PJ", "Wh", "kWh", "MWh", "GWh", "TWh",
            "Btu", "BTU", "kBtu", "MBtu", "MMBtu", "MMBTU", "MMbtu", "kcal", "Mcal", "Gcal", "cal", "therm", "tep", "toe", "boe", "Mboe", "MMboe");
        Add(UnitClass.ExtRate, false, "W", "kW", "MW", "GW", "hp", "HP", "cv", "CV", "kVA", "MVA", "TR", "RT", "kWe", "MWe", "MWt", "MWth",
            "tpd", "TPD", "tpa", "TPA", "tph", "TPH", "Mtpa", "MTPA", "MTA", "mtpa", "kta", "KTA", "ktpa", "KTPA",
            "bpd", "BPD", "kbpd", "KBPD", "kbd", "Mbpd", "MBPD", "boed", "MMSCFD", "MMscfd", "MMSCMD", "scfm", "SCFM", "scfh", "SCFH", "gpm", "GPM", "acfm", "ACFM");
        Add(UnitClass.Time, false, "s", "min", "h", "hr", "d", "a", "yr");
        Add(UnitClass.Time, true, "dia", "dias", "day", "days", "ano", "anos", "year", "years", "mês", "mes", "month", "semana", "week", "hora", "horas", "hour", "hours");
        Add(UnitClass.Intensive, false, "°C", "ºC", "°F", "ºF", "K", "°R", "bar", "bara", "barg", "bar(a)", "bar(g)", "mbar", "Pa", "kPa", "MPa", "GPa", "hPa",
            "psi", "psia", "psig", "atm", "mmHg", "mmH2O", "inH2O", "kgf/cm²", "kgf/cm2", "kgf/cm²g", "kgf/cm2g", "kgf/cm²a",
            "%", "ppm", "ppmv", "ppmw", "ppmm", "ppb", "ppbv", "mol%", "wt%", "vol%", "%mol", "%m", "%v", "%p", "%w", "%vol", "%peso",
            "cP", "cp", "mPa.s", "mPa·s", "Pa.s", "Pa·s", "cSt", "°API", "ºAPI", "API", "pH", "rpm", "RPM", "Hz", "kHz", "dB", "dBA", "°Brix", "ºBrix");
        Add(UnitClass.Geometric, false, "m", "mm", "cm", "km", "µm", "μm", "in", "\"", "ft", "pol", "m²", "m2", "ft²", "ft2", "mm²", "mm2", "cm²", "cm2", "km²", "km2", "ha");
    }

    public static bool IsKnownSingle(string s) => Exact.ContainsKey(s) || Loose.ContainsKey(s);

    private static bool TryClass(string part, bool inDenominator, out UnitClass c)
    {
        if (!inDenominator && DenominatorOnly.Contains(part))
        {
            c = default;
            return false;
        }
        return Exact.TryGetValue(part, out c) || Loose.TryGetValue(part, out c);
    }

    /// <summary>
    /// Parses a complete unit expression. Parts after the first slash go to the denominator
    /// (<c>kJ/kg.K</c> is kJ over kg·K). Returns null when any part is unknown.
    /// </summary>
    public static UnitParse? Parse(string expr) => Parse(expr, false);

    /// <summary>Parses the unit after a slash, where single letters such as "h" and "d" are allowed.</summary>
    public static UnitParse? ParseDenominator(string expr) => Parse(expr, true);

    private static UnitParse? Parse(string expr, bool denominatorContext)
    {
        string e = expr.Trim();
        if (e.Length == 0 || e.Length > 30) return null;

        // Whole-string lookups first, so "kgf/cm²" and "mPa.s" stay one unit.
        if (TryClass(e, denominatorContext, out var whole)) return Make(e, [(whole, 1)]);

        var parts = new List<(UnitClass, int)>();
        string[] segments = e.Split('/');
        for (int s = 0; s < segments.Length; s++)
        {
            int sign = s == 0 ? 1 : -1;
            bool denom = sign < 0 || denominatorContext;
            string seg = segments[s].Trim();
            if (seg.Length == 0) return null;
            if (TryClass(seg, denom, out var segClass))
            {
                parts.Add((segClass, sign));
                continue;
            }
            // Products such as "kg.K" or "kW·h".
            string[] pieces = seg.Split(['·', '*', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 2) return null;
            foreach (string piece in pieces)
            {
                if (!TryClass(piece, denom || parts.Count > 0, out var cls)) return null;
                parts.Add((cls, sign));
            }
        }
        return parts.Count == 0 ? null : Make(e, parts);
    }

    private static UnitParse Make(string text, List<(UnitClass Cls, int Sign)> parts)
    {
        int ext = 0, money = 0;
        bool geometric = false, intensive = false, onlyTime = true;
        foreach (var (cls, sign) in parts)
        {
            switch (cls)
            {
                case UnitClass.Ext:
                case UnitClass.ExtRate:
                    ext += sign;
                    onlyTime = false;
                    break;
                case UnitClass.Money:
                    money += sign;
                    onlyTime = false;
                    break;
                case UnitClass.Geometric:
                    geometric = true;
                    onlyTime = false;
                    break;
                case UnitClass.Intensive:
                    intensive = true;
                    onlyTime = false;
                    break;
                case UnitClass.Time:
                    break;
            }
        }
        if (geometric) return new UnitParse(text, ScaleDimension.None, ext == 0 && money == 0 ? QuantityKind.Geometric : QuantityKind.Intensive);
        if (intensive || onlyTime) return new UnitParse(text, ScaleDimension.None, QuantityKind.Intensive);
        var dim = new ScaleDimension(ext, money);
        if (money != 0) return new UnitParse(text, dim, QuantityKind.Monetary);
        if (ext != 0) return new UnitParse(text, dim, QuantityKind.Extensive);
        return new UnitParse(text, ScaleDimension.None, QuantityKind.Intensive);
    }

    /// <summary>Combines a currency with an optional physical denominator (<c>US$/t</c>).</summary>
    public static UnitParse Money(string text, UnitParse? denominator)
    {
        if (denominator is null) return new UnitParse(text, new ScaleDimension(0, 1), QuantityKind.Monetary);
        var dim = new ScaleDimension(-denominator.Dimension.Ext, 1 - denominator.Dimension.Money);
        return new UnitParse(text, dim, QuantityKind.Monetary);
    }
}
