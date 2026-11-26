using System.Text;
using System.Text.RegularExpressions;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Quantities;

namespace DocAnon.Core.Pseudonymization;

public enum RestoreKind
{
    Marker,
    UnknownMarker,
    Quantity,
}

/// <summary>A restored value, with its position in the output and in the answer (<paramref name="SourceStart"/>, length of <paramref name="Found"/>).</summary>
public sealed record RestoredItem(int OutputStart, int OutputLength, string Found, string Restored, RestoreKind Kind, int SourceStart = -1);

public sealed record ReidentificationResult(string Text, IReadOnlyList<RestoredItem> Items)
{
    public int UnknownMarkers => Items.Count(i => i.Kind == RestoreKind.UnknownMarker);
}

/// <summary>Restores the real values in an answer that the AI wrote over anonymized text.</summary>
public static class Reidentifier
{
    /// <summary>English names the AI may use when it answers in English.</summary>
    private static readonly Dictionary<string, EntityType> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PERSON"] = EntityType.PESSOA,
        ["NAME"] = EntityType.PESSOA,
        ["COMPANY"] = EntityType.EMPRESA,
        ["ORGANIZATION"] = EntityType.EMPRESA,
        ["ORGANIZACAO"] = EntityType.EMPRESA,
        ["ORGANIZAÇÃO"] = EntityType.EMPRESA,
        ["ADDRESS"] = EntityType.ENDERECO,
        ["ENDEREÇO"] = EntityType.ENDERECO,
        ["PLANT"] = EntityType.PLANTA,
        ["UNIT"] = EntityType.UNIDADE,
        ["PROJECT"] = EntityType.PROJETO,
        ["CLIENT"] = EntityType.CLIENTE,
        ["CUSTOMER"] = EntityType.CLIENTE,
        ["SUPPLIER"] = EntityType.FORNECEDOR,
        ["VENDOR"] = EntityType.FORNECEDOR,
        ["LOCATION"] = EntityType.LOCAL,
        ["PRODUCT"] = EntityType.PRODUTO,
        ["CATALYST"] = EntityType.CATALISADOR,
        ["CONTRACT"] = EntityType.CONTRATO,
        ["VALUE"] = EntityType.VALOR,
        ["PHONE"] = EntityType.TELEFONE,
        ["CARD"] = EntityType.CARTAO,
        ["CARTÃO"] = EntityType.CARTAO,
        ["PASSPORT"] = EntityType.PASSAPORTE,
        ["PLATE"] = EntityType.PLACA,
        ["CASE"] = EntityType.PROCESSO,
        ["BIRTHDATE"] = EntityType.DATA_NASCIMENTO,
        ["TÍTULO_ELEITOR"] = EntityType.TITULO_ELEITOR,
    };

    private static readonly Regex MarkerRx = BuildMarkerRegex();
    private static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);

    private static Regex BuildMarkerRegex()
    {
        var names = Enum.GetNames<EntityType>().Concat(Aliases.Keys)
            .OrderByDescending(n => n.Length)
            .Select(n => Regex.Escape(n).Replace("_", "[ _-]"));
        string alt = string.Join('|', names);
        // "[PESSOA_1]", "[Pessoa 1]", "[ PESSOA-1 ]", or bare "PESSOA_1".
        string pattern = $@"\[\s*(?<t>{alt})[ _-](?<n>\d+)\s*\]|(?<![\p{{L}}\d_])(?<t>{alt.Replace("[ _-]", "_")})_(?<n>\d+)(?![\p{{L}}\d_])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static EntityType? ResolveType(string raw)
    {
        string t = Regex.Replace(raw.Trim(), "[ -]", "_").ToUpperInvariant();
        if (Enum.TryParse<EntityType>(t, out var e)) return e;
        if (Aliases.TryGetValue(t, out e)) return e;
        if (Aliases.TryGetValue(raw.Trim(), out e)) return e;
        return null;
    }

    /// <summary>
    /// Replaces markers with their real values and divides scaled quantities by the session
    /// factors. When <paramref name="unscaleQuantities"/> is false, numbers are left alone.
    /// </summary>
    public static ReidentificationResult Restore(string answer, EntityRegistry registry, ScaleFactors factors, bool unscaleQuantities = true, bool? commaIsDecimal = null)
    {
        var edits = new List<(int Start, int Length, string Replacement, RestoreKind Kind)>();

        foreach (Match m in MarkerRx.Matches(answer))
        {
            var type = ResolveType(m.Groups["t"].Value);
            int n = int.Parse(m.Groups["n"].Value);
            var rec = type is null ? null : registry.Find(type.Value, n);
            if (rec is null) edits.Add((m.Index, m.Length, m.Value, RestoreKind.UnknownMarker));
            // A value found across a line break of the source comes back on one line.
            else edits.Add((m.Index, m.Length, WhitespaceRx.Replace(rec.Canonical.Trim(), " "), RestoreKind.Marker));
        }

        if (unscaleQuantities)
        {
            var ctx = new DetectionContext(answer, commaIsDecimal: commaIsDecimal);
            foreach (var q in new QuantityDetector().Detect(ctx))
            {
                if (q.Quantity is not { Dimension.IsScalable: true } info) continue;
                if (edits.Any(e => e.Start < q.End && q.Start < e.Start + e.Length)) continue;
                string restored = factors.ScaleSpanText(answer, q.Start, info, inverse: true, known: n => registry.OriginalOfScaled(info.Dimension, answer.Substring(n.Start, n.Length)));
                var last = info.Numbers.MaxBy(x => x.End)!;
                restored += answer[last.End..q.End];
                edits.Add((q.Start, q.Length, restored, RestoreKind.Quantity));
            }
        }

        edits.Sort((a, b) => a.Start.CompareTo(b.Start));
        var sb = new StringBuilder(answer.Length);
        var items = new List<RestoredItem>();
        int cursor = 0;
        foreach (var (start, length, replacement, kind) in edits)
        {
            if (start < cursor) continue;
            sb.Append(answer, cursor, start - cursor);
            items.Add(new RestoredItem(sb.Length, replacement.Length, answer.Substring(start, length), replacement, kind, start));
            sb.Append(replacement);
            cursor = start + length;
        }
        sb.Append(answer, cursor, answer.Length - cursor);
        return new ReidentificationResult(sb.ToString(), items);
    }
}
