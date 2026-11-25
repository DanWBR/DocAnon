namespace DocAnon.Core.Model;

/// <summary>How a quantity reacts to the session's scale factors.</summary>
public enum QuantityKind
{
    /// <summary>Grows with plant size (mass, moles, volume, energy, power, and their flows).</summary>
    Extensive,

    /// <summary>Money, or a ratio that involves money (R$/t).</summary>
    Monetary,

    /// <summary>Does not change with plant size (temperature, pressure, composition, specific energy).</summary>
    Intensive,

    /// <summary>Lengths and areas of equipment. They do not scale linearly with capacity.</summary>
    Geometric,

    /// <summary>A number whose unit the catalog does not know.</summary>
    Unknown,
}

/// <summary>
/// Exponents of the two scaling bases. A quantity is multiplied by
/// <c>kExt^Ext * kMoney^Money</c>.
/// </summary>
public readonly record struct ScaleDimension(int Ext, int Money)
{
    public static readonly ScaleDimension None = new(0, 0);
    public bool IsScalable => Ext != 0 || Money != 0;
    public static ScaleDimension operator +(ScaleDimension a, ScaleDimension b) => new(a.Ext + b.Ext, a.Money + b.Money);
    public static ScaleDimension operator -(ScaleDimension a, ScaleDimension b) => new(a.Ext - b.Ext, a.Money - b.Money);
    public override string ToString() => $"ext^{Ext} moeda^{Money}";
}

/// <summary>How a number was written, so a scaled value can be written the same way.</summary>
public sealed record NumberStyle(char? DecimalSeparator, char? GroupSeparator, int Decimals, int SignificantDigits, bool Negative);

/// <summary>One number inside a quantity span. Ranges such as "80-120 t/d" hold two of them.</summary>
public sealed record NumberToken(int Start, int Length, double Value, NumberStyle Style)
{
    public int End => Start + Length;
}

public sealed record QuantityInfo(
    IReadOnlyList<NumberToken> Numbers,
    string Unit,
    ScaleDimension Dimension,
    QuantityKind Kind,
    bool UnitFromColumnHeader = false);
