using System.Text.Json;
using System.Text.Json.Serialization;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;

namespace DocAnon.Core.Vault;

/// <summary>
/// A conversation with an external AI. All documents of a session share the same markers and
/// the same scale factors, so the AI sees one consistent picture.
/// </summary>
public sealed class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Label written in the file name. It is not encrypted, so it should not contain sensitive names.</summary>
    public string Label { get; set; } = "sessao";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? ProfileId { get; set; }
    public double KExt { get; set; } = 1;
    public double KMoney { get; set; } = 1;
    public List<EntityRecord> Entities { get; set; } = [];
    public List<string> Documents { get; set; } = [];
    public Dictionary<string, string?> ScaledNumbers { get; set; } = [];

    [JsonIgnore]
    public EntityRegistry Registry { get; private set; } = new();

    [JsonIgnore]
    public ScaleFactors Factors => new(KExt, KMoney);

    public static Session CreateNew(string label, double min = 0.4, double max = 2.5)
    {
        var f = ScaleFactors.Random(min, max);
        return new Session { Label = label, KExt = f.KExt, KMoney = f.KMoney };
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public byte[] Serialize()
    {
        Entities = Registry.Records.ToList();
        ScaledNumbers = new Dictionary<string, string?>(Registry.ScaledNumbers);
        return JsonSerializer.SerializeToUtf8Bytes(this, Json);
    }

    public static Session Deserialize(byte[] utf8)
    {
        var s = JsonSerializer.Deserialize<Session>(utf8, Json) ?? throw new InvalidDataException("Sessão vazia.");
        s.Registry = new EntityRegistry();
        s.Registry.Load(s.Entities);
        foreach (var kv in s.ScaledNumbers) s.Registry.ScaledNumbers[kv.Key] = kv.Value;
        return s;
    }
}
