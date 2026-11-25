using DocAnon.Core.Model;
using DocAnon.Ner;

namespace DocAnon.Integration.Tests;

public sealed class NerFixture : IDisposable
{
    public NerDetector? Detector { get; } = NerDetector.TryLoadDefault();
    public void Dispose() => Detector?.Dispose();
}

public class NerTests(NerFixture fx) : IClassFixture<NerFixture>
{
    private NerDetector Ner => fx.Detector ?? throw new InvalidOperationException("O modelo de NER não está em models/ner. Rode tools/export_ner_model.py.");

    [Fact]
    public void PortuguesePeopleOrganizationsPlaces()
    {
        string text = "O engenheiro João Carlos da Silva, da Petrobras, visitou a refinaria em Paulínia com Maria Fernanda Souza.";
        var spans = Ner.Detect(text);
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text.Contains("João Carlos da Silva"));
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text.Contains("Maria Fernanda Souza"));
        Assert.Contains(spans, s => s.Type == EntityType.EMPRESA && s.Text == "Petrobras");
        Assert.Contains(spans, s => s.Type == EntityType.LOCAL && s.Text == "Paulínia");
    }

    [Fact]
    public void EnglishEntities()
    {
        string text = "Dr. Emily Johnson from ExxonMobil met Robert Brown in Houston to review the cracker design.";
        var spans = Ner.Detect(text);
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text.Contains("Emily Johnson"));
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text.Contains("Robert Brown"));
        Assert.Contains(spans, s => s.Type == EntityType.EMPRESA && s.Text.Contains("ExxonMobil"));
        Assert.Contains(spans, s => s.Type == EntityType.LOCAL && s.Text == "Houston");
    }

    [Fact]
    public void LongTextUsesWindowsAndKeepsOffsets()
    {
        var parts = new List<string>();
        for (int i = 0; i < 120; i++) parts.Add($"No dia {i}, a equipe revisou o balanço de massa da coluna de destilação e registrou os resultados.");
        parts.Insert(60, "A reunião foi conduzida por Ana Beatriz Rocha.");
        string text = string.Join(' ', parts);
        var spans = Ner.Detect(text);
        var ana = Assert.Single(spans, s => s.Type == EntityType.PESSOA && s.Text.Contains("Ana Beatriz Rocha"));
        Assert.Equal(ana.Text, text.Substring(ana.Start, ana.Length));
    }
}
