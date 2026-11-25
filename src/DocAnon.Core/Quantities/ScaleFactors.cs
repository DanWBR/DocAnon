using System.Security.Cryptography;
using System.Text;
using DocAnon.Core.Model;

namespace DocAnon.Core.Quantities;

/// <summary>
/// The two secret factors of a session. Every extensive quantity is multiplied by
/// <see cref="KExt"/> to the power of its extension exponent and by <see cref="KMoney"/> to the
/// power of its money exponent, so mass and energy balances and cost-to-capacity ratios stay
/// consistent.
/// </summary>
public sealed record ScaleFactors(double KExt, double KMoney)
{
    public static readonly ScaleFactors Identity = new(1, 1);

    /// <summary>
    /// Draws both factors from a cryptographic random source, inside [<paramref name="min"/>,
    /// <paramref name="max"/>] and outside [1/<paramref name="avoidNearOne"/>, <paramref name="avoidNearOne"/>].
    /// </summary>
    public static ScaleFactors Random(double min = 0.4, double max = 2.5, double avoidNearOne = 1.25)
    {
        return new ScaleFactors(Draw(min, max, avoidNearOne), Draw(min, max, avoidNearOne));
    }

    private static double Draw(double min, double max, double avoid)
    {
        // Log-uniform, so 0.5 and 2.0 are equally likely.
        double lo = Math.Log(min), hi = Math.Log(max);
        for (int i = 0; i < 1000; i++)
        {
            double u = RandomNumberGenerator.GetInt32(int.MaxValue) / (double)int.MaxValue;
            double k = Math.Exp(lo + u * (hi - lo));
            if (k < 1 / avoid || k > avoid) return k;
        }
        return max;
    }

    public double Factor(ScaleDimension d) => Math.Pow(KExt, d.Ext) * Math.Pow(KMoney, d.Money);

    /// <summary>
    /// Rewrites the numbers of a quantity span. With <paramref name="inverse"/> it divides
    /// instead, which is how the AI's answer is converted back. <paramref name="known"/> may
    /// return the exact text for a number (an original that was recorded when it was scaled),
    /// and <paramref name="onScaled"/> receives each number with its new text.
    /// </summary>
    public string ScaleSpanText(string sourceText, int spanStart, QuantityInfo q, bool inverse = false,
        Func<NumberToken, string?>? known = null, Action<NumberToken, string>? onScaled = null)
    {
        double f = Factor(q.Dimension);
        if (inverse) f = 1 / f;
        var sb = new StringBuilder();
        int cursor = spanStart;
        foreach (var n in q.Numbers.OrderBy(n => n.Start))
        {
            sb.Append(sourceText, cursor, n.Start - cursor);
            string text = known?.Invoke(n) ?? NumberText.Format(n.Value * f, n.Style, inverse ? 2 : NumberText.MinScaledSignificantDigits);
            onScaled?.Invoke(n, text);
            sb.Append(text);
            cursor = n.End;
        }
        return sb.ToString();
    }
}
