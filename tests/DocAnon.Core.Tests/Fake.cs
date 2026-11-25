using System.Numerics;

namespace DocAnon.Core.Tests;

/// <summary>Generates valid fake identifiers for tests.</summary>
internal static class Fake
{
    public static string Cpf(string nine)
    {
        int[] d = nine.Select(c => c - '0').ToArray();
        int Dv(int count, int w)
        {
            int s = 0;
            for (int i = 0; i < count; i++) s += (i < d.Length ? d[i] : 0) * (w - i);
            int r = s % 11;
            return r < 2 ? 0 : 11 - r;
        }
        d = [.. d, Dv(9, 10)];
        d = [.. d, Dv(10, 11)];
        string x = string.Concat(d);
        return $"{x[..3]}.{x[3..6]}.{x[6..9]}-{x[9..]}";
    }

    public static string Pis(string ten)
    {
        int[] w = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int s = 0;
        for (int i = 0; i < 10; i++) s += (ten[i] - '0') * w[i];
        int r = 11 - s % 11;
        if (r >= 10) r = 0;
        string x = ten + r;
        return $"{x[..3]}.{x[3..8]}.{x[8..10]}-{x[10]}";
    }

    public static string Cnj(string seq7, string rest11)
    {
        var num = BigInteger.Parse(seq7 + rest11 + "00");
        int dv = 98 - (int)(num % 97);
        string all = seq7 + dv.ToString("00") + rest11;
        return $"{all[..7]}-{all[7..9]}.{all[9..13]}.{all[13]}.{all[14..16]}.{all[16..]}";
    }
}
