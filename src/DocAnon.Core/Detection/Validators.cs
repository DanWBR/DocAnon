using System.Numerics;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>Check-digit algorithms for Brazilian and international identifiers.</summary>
public static class Validators
{
    public static bool Cpf(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 11 || AllSame(d)) return false;
        int dv1 = Mod11Cpf(d, 9, 10);
        int dv2 = Mod11Cpf(d, 10, 11);
        return d[9] - '0' == dv1 && d[10] - '0' == dv2;
    }

    private static int Mod11Cpf(string d, int count, int firstWeight)
    {
        int sum = 0;
        for (int i = 0; i < count; i++) sum += (d[i] - '0') * (firstWeight - i);
        int r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }

    /// <summary>
    /// Validates numeric and alphanumeric CNPJ (Receita Federal, from July 2026). Each of the first
    /// 12 characters is worth its ASCII code minus 48, and the last 2 are numeric check digits.
    /// </summary>
    public static bool Cnpj(string value)
    {
        string s = TextFold.AlnumUpper(value);
        if (s.Length != 14) return false;
        if (!char.IsAsciiDigit(s[12]) || !char.IsAsciiDigit(s[13])) return false;
        if (AllSame(s)) return false;
        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int dv1 = CnpjDigit(s, w1);
        int dv2 = CnpjDigit(s, w2);
        return s[12] - '0' == dv1 && s[13] - '0' == dv2;
    }

    private static int CnpjDigit(string s, int[] weights)
    {
        int sum = 0;
        for (int i = 0; i < weights.Length; i++) sum += (s[i] - 48) * weights[i];
        int r = sum % 11;
        return r < 2 ? 0 : 11 - r;
    }

    public static bool Luhn(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length < 13 || d.Length > 19 || AllSame(d)) return false;
        int sum = 0;
        bool dbl = false;
        for (int i = d.Length - 1; i >= 0; i--)
        {
            int x = d[i] - '0';
            if (dbl)
            {
                x *= 2;
                if (x > 9) x -= 9;
            }
            sum += x;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    /// <summary>Card numbers also need a known issuer prefix, which removes most random Luhn hits.</summary>
    public static bool CreditCard(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (!Luhn(d)) return false;
        return d[0] == '4'
            || (d[0] == '5' && d[1] is >= '1' and <= '5')
            || (d[0] == '2' && d[1] is >= '2' and <= '7')
            || (d[0] == '3' && d[1] is '4' or '7' or '6' or '8')
            || d[0] == '6';
    }

    public static bool Pis(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 11 || AllSame(d)) return false;
        int[] w = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int sum = 0;
        for (int i = 0; i < 10; i++) sum += (d[i] - '0') * w[i];
        int r = 11 - sum % 11;
        if (r >= 10) r = 0;
        return d[10] - '0' == r;
    }

    public static bool TituloEleitor(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 12 || AllSame(d)) return false;
        int uf = int.Parse(d.AsSpan(8, 2));
        if (uf is < 1 or > 28) return false;
        bool spOrMg = uf is 1 or 2;
        int sum = 0;
        for (int i = 0; i < 8; i++) sum += (d[i] - '0') * (i + 2);
        int dv1 = sum % 11;
        if (dv1 == 10) dv1 = 0;
        else if (dv1 == 0 && spOrMg) dv1 = 1;
        int sum2 = (d[8] - '0') * 7 + (d[9] - '0') * 8 + dv1 * 9;
        int dv2 = sum2 % 11;
        if (dv2 == 10) dv2 = 0;
        else if (dv2 == 0 && spOrMg) dv2 = 1;
        return d[10] - '0' == dv1 && d[11] - '0' == dv2;
    }

    public static bool Cnh(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 11 || AllSame(d)) return false;
        int sum = 0;
        for (int i = 0, j = 9; i < 9; i++, j--) sum += (d[i] - '0') * j;
        int dv1 = sum % 11;
        int desc = 0;
        if (dv1 >= 10)
        {
            dv1 = 0;
            desc = 2;
        }
        sum = 0;
        for (int i = 0, j = 1; i < 9; i++, j++) sum += (d[i] - '0') * j;
        int x = sum % 11;
        int dv2 = x >= 10 ? 0 : x - desc;
        if (dv2 < 0) dv2 += 11;
        if (dv2 >= 10) dv2 = 0;
        return d[9] - '0' == dv1 && d[10] - '0' == dv2;
    }

    /// <summary>CNJ case number NNNNNNN-DD.AAAA.J.TR.OOOO, checked with ISO 7064 mod 97-10.</summary>
    public static bool ProcessoCnj(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 20) return false;
        string n = d[..7], dv = d.Substring(7, 2), rest = d[9..];
        var num = BigInteger.Parse(n + rest + "00");
        int expected = 98 - (int)(num % 97);
        return int.Parse(dv) == expected;
    }

    public static bool Iban(string value)
    {
        string s = TextFold.AlnumUpper(value);
        if (s.Length is < 15 or > 34 || !char.IsAsciiLetter(s[0]) || !char.IsAsciiLetter(s[1])) return false;
        string r = s[4..] + s[..4];
        int mod = 0;
        foreach (char c in r)
        {
            int v = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            mod = v >= 10 ? (mod * 100 + v) % 97 : (mod * 10 + v) % 97;
        }
        return mod == 1;
    }

    public static bool Ssn(string value)
    {
        string d = TextFold.DigitsOnly(value);
        if (d.Length != 9) return false;
        int area = int.Parse(d.AsSpan(0, 3)), group = int.Parse(d.AsSpan(3, 2)), serial = int.Parse(d.AsSpan(5, 4));
        return area is not (0 or 666) && area < 900 && group != 0 && serial != 0;
    }

    public static bool IPv4(string value)
    {
        string[] parts = value.Split('.');
        if (parts.Length != 4) return false;
        foreach (string p in parts)
        {
            if (p.Length is 0 or > 3 || (p.Length > 1 && p[0] == '0')) return false;
            if (!int.TryParse(p, out int x) || x > 255) return false;
        }
        return true;
    }

    private static bool AllSame(string s)
    {
        for (int i = 1; i < s.Length; i++) if (s[i] != s[0]) return false;
        return true;
    }
}
