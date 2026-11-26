using System.Text.RegularExpressions;
using DocAnon.Core.Model;
using DocAnon.Core.Text;

namespace DocAnon.Core.Pseudonymization;

/// <summary>One real-world entity and the marker that stands for it.</summary>
public sealed class EntityRecord
{
    public EntityType Type { get; set; }
    public int Number { get; set; }
    public string Token => $"[{Type}_{Number}]";

    /// <summary>The form used when the marker is restored. It is the longest variant seen.</summary>
    public string Canonical { get; set; } = "";

    public List<string> Variants { get; set; } = [];
}

/// <summary>
/// Gives every entity a stable marker inside a session, and links spelling variants of the same
/// person or company to one marker.
/// </summary>
public sealed partial class EntityRegistry
{
    private readonly List<EntityRecord> _records = [];
    private readonly Dictionary<string, EntityRecord> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityType, int> _counters = [];

    public IReadOnlyList<EntityRecord> Records => _records;

    /// <summary>
    /// Scaled number text to original number text, keyed by dimension. It lets the answer's
    /// numbers that the AI copied from the document come back exactly. A null value marks a
    /// scaled text that two different originals produced.
    /// </summary>
    public Dictionary<string, string?> ScaledNumbers { get; } = new(StringComparer.Ordinal);

    private static string ScaledKey(ScaleDimension d, string scaledText) => $"{d.Ext},{d.Money}|{scaledText}";

    public void RecordScaled(ScaleDimension d, string scaledText, string originalText)
    {
        string k = ScaledKey(d, scaledText);
        if (!ScaledNumbers.TryGetValue(k, out var existing)) ScaledNumbers[k] = originalText;
        // "891" and "891,0" are the same number, so the key stays usable and keeps the first text.
        else if (existing is not null && existing != originalText && WithoutTrailingZeros(existing) != WithoutTrailingZeros(originalText)) ScaledNumbers[k] = null;
    }

    private static string WithoutTrailingZeros(string number)
    {
        // Three zeros after the separator may be a thousands group ("1.000"), so only one or two count.
        int sep = number.LastIndexOfAny([',', '.']);
        if (sep < 0) return number;
        string decimals = number[(sep + 1)..];
        return decimals.Length is 1 or 2 && decimals.All(c => c == '0') ? number[..sep] : number;
    }

    public string? OriginalOfScaled(ScaleDimension d, string scaledText) =>
        ScaledNumbers.GetValueOrDefault(ScaledKey(d, scaledText));

    private static readonly HashSet<string> NameParticles = ["de", "da", "do", "dos", "das", "e", "van", "von", "del", "di", "du", "la", "le"];
    private static readonly HashSet<string> Honorifics = ["sr", "sra", "srta", "dr", "dra", "prof", "profa", "eng", "mr", "mrs", "ms", "miss", "dr.", "sr.", "sra.", "eng.", "prof.", "mr.", "mrs.", "ms."];

    [GeneratedRegex(@"[\s,]+(?:s\.?\s?a\.?|ltda\.?|me|epp|eireli|inc\.?|llc|ltd\.?|corp\.?|co\.?|gmbh|plc|ag|nv|bv|spa|s\.?a\.?s\.?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompanySuffixRx();

    private static bool IsOrgFamily(EntityType t) => t is EntityType.EMPRESA or EntityType.CLIENTE or EntityType.FORNECEDOR;

    private static string Family(EntityType t) => IsOrgFamily(t) ? "ORG" : t.ToString();

    public static string NormalizeKey(EntityType type, string text)
    {
        if (EntityTypeInfo.IsNumericIdentifier(type))
        {
            string an = TextFold.AlnumUpper(text);
            if (an.Length > 0) return Family(type) + ":" + an;
        }
        string k = TextFold.Key(text);
        if (IsOrgFamily(type)) k = TextFold.Key(CompanySuffixRx().Replace(k, ""));
        if (type == EntityType.PESSOA) k = string.Join(' ', NameTokens(k));
        if (type == EntityType.EMAIL) k = k.ToLowerInvariant();
        return Family(type) + ":" + k;
    }

    private static List<string> NameTokens(string folded) =>
        folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ','))
            .Where(t => t.Length > 0 && !Honorifics.Contains(t) && !Honorifics.Contains(t + "."))
            .ToList();

    public EntityRecord? Find(EntityType type, int number) =>
        _records.FirstOrDefault(r => r.Type == type && r.Number == number);

    public EntityRecord? FindByText(EntityType type, string text) =>
        _byKey.GetValueOrDefault(NormalizeKey(type, text));

    /// <summary>Returns the marker for the text, creating a new entity when no match exists.</summary>
    public EntityRecord GetOrCreate(EntityType type, string text)
    {
        string key = NormalizeKey(type, text);
        if (_byKey.TryGetValue(key, out var rec))
        {
            AddVariant(rec, text, key);
            return rec;
        }

        var linked = type == EntityType.PESSOA ? LinkPerson(key) : IsOrgFamily(type) ? LinkOrganization(key) : null;
        if (linked is not null)
        {
            AddVariant(linked, text, key);
            return linked;
        }

        int n = _counters.GetValueOrDefault(type) + 1;
        _counters[type] = n;
        rec = new EntityRecord { Type = type, Number = n, Canonical = text.Trim() };
        _records.Add(rec);
        AddVariant(rec, text, key);
        return rec;
    }

    private void AddVariant(EntityRecord rec, string text, string key)
    {
        string t = text.Trim();
        if (!rec.Variants.Contains(t, StringComparer.Ordinal)) rec.Variants.Add(t);
        if (t.Length > rec.Canonical.Length) rec.Canonical = t;
        _byKey[key] = rec;
    }

    // "João Silva" and "Sr. Silva" point to "João Carlos Silva" when exactly one person matches.
    private EntityRecord? LinkPerson(string key)
    {
        var tokens = key[(key.IndexOf(':') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !NameParticles.Contains(t)).ToList();
        if (tokens.Count == 0) return null;
        var candidates = new HashSet<EntityRecord>();
        foreach (var r in _records.Where(r => r.Type == EntityType.PESSOA))
        {
            foreach (var v in r.Variants)
            {
                var vt = NameTokens(TextFold.Key(v)).Where(t => !NameParticles.Contains(t)).ToList();
                if (vt.Count == 0) continue;
                bool subset = tokens.All(vt.Contains) || vt.All(tokens.Contains);
                bool sameSurname = vt[^1] == tokens[^1];
                bool singleFirst = tokens.Count == 1 && vt[0] == tokens[0];
                if (subset && (sameSurname || singleFirst))
                {
                    candidates.Add(r);
                    break;
                }
            }
        }
        return candidates.Count == 1 ? candidates.First() : null;
    }

    // "Petroquímica Exemplo" links to "Petroquímica Exemplo do Sul" only when the match is unique.
    private EntityRecord? LinkOrganization(string key)
    {
        string k = key[(key.IndexOf(':') + 1)..];
        if (k.Length < 4) return null;
        var candidates = _byKey
            .Where(kv => kv.Key.StartsWith("ORG:", StringComparison.Ordinal))
            .Where(kv =>
            {
                string o = kv.Key[4..];
                return o.StartsWith(k + " ", StringComparison.Ordinal) || k.StartsWith(o + " ", StringComparison.Ordinal);
            })
            .Select(kv => kv.Value)
            .Distinct()
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Moves every variant of <paramref name="from"/> into <paramref name="into"/>.</summary>
    public void Merge(EntityRecord into, EntityRecord from)
    {
        if (ReferenceEquals(into, from)) return;
        foreach (var v in from.Variants) AddVariant(into, v, NormalizeKey(from.Type, v));
        foreach (var kv in _byKey.Where(kv => ReferenceEquals(kv.Value, from)).ToList()) _byKey[kv.Key] = into;
        _records.Remove(from);
    }

    /// <summary>Gives <paramref name="variant"/> its own marker again.</summary>
    public EntityRecord Split(EntityRecord rec, string variant)
    {
        rec.Variants.Remove(variant);
        string key = NormalizeKey(rec.Type, variant);
        _byKey.Remove(key);
        if (rec.Canonical == variant) rec.Canonical = rec.Variants.OrderByDescending(v => v.Length).FirstOrDefault() ?? "";
        int n = _counters.GetValueOrDefault(rec.Type) + 1;
        _counters[rec.Type] = n;
        var created = new EntityRecord { Type = rec.Type, Number = n, Canonical = variant };
        _records.Add(created);
        AddVariant(created, variant, key);
        return created;
    }

    public void Load(IEnumerable<EntityRecord> records)
    {
        foreach (var r in records)
        {
            _records.Add(r);
            _counters[r.Type] = Math.Max(_counters.GetValueOrDefault(r.Type), r.Number);
            foreach (var v in r.Variants) _byKey[NormalizeKey(r.Type, v)] = r;
        }
    }
}
