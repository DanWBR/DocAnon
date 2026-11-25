using System.Text.Json;

namespace DocAnon.App;

/// <summary>
/// Non-sensitive preferences, stored in plain JSON next to the vaults. Nothing here identifies a
/// document, a person or a company.
/// </summary>
public sealed class AppSettings
{
    public bool UseNer { get; set; } = true;
    public bool UseFirstNames { get; set; } = true;
    public int Dpi { get; set; } = 200;
    public double ScaleMin { get; set; } = 0.4;
    public double ScaleMax { get; set; } = 2.5;

    /// <summary>"windows" protects vaults with the Windows user (DPAPI); "senha" asks for a password once per run.</summary>
    public string Protection { get; set; } = OperatingSystem.IsWindows() ? "windows" : "senha";

    public bool UseLlm { get; set; }
    public string OllamaAddress { get; set; } = "http://127.0.0.1:11434";
    public string LlmTextModel { get; set; } = "";
    public string LlmVisionModel { get; set; } = "";
    public bool LlmAudit { get; set; } = true;
    public string? LastProfileId { get; set; }

    private static string PathFor(string root) => Path.Combine(root, "settings.json");

    public static AppSettings Load(string root)
    {
        try
        {
            string p = PathFor(root);
            if (File.Exists(p)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        return new AppSettings();
    }

    public void Save(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(PathFor(root), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
