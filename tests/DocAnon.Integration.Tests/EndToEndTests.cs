using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Text;
using DocAnon.Core.Vault;
using DocAnon.Services;

namespace DocAnon.Integration.Tests;

public sealed class EngineFixture : IDisposable
{
    public Engine Engine { get; } = new();
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "docanon-fixtures-" + Guid.NewGuid().ToString("N"));
    public List<string> Files { get; }

    public EngineFixture() => Files = FixtureFactory.All(Folder);

    public void Dispose()
    {
        Engine.Dispose();
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
    }
}

/// <summary>
/// Runs the whole flow on fictitious documents of every format and checks that no secret survives
/// in the text or in the redacted images, and that scaled numbers come back after reidentification.
/// </summary>
public class EndToEndTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    public static TheoryData<string> Formats => new() { "contrato.docx", "avaliacao.xlsx", "relatorio.pptx", "nativo.pdf", "escaneado.pdf", "pfd.pdf", "fluxograma.svg", "desenho.dxf", "simulacao.dwxmz" };

    private static readonly Dictionary<string, string[]> Present = new()
    {
        ["contrato.docx"] = FixtureFactory.Secrets,
        ["avaliacao.xlsx"] = [FixtureFactory.Person1, FixtureFactory.Person2, FixtureFactory.Project, FixtureFactory.Company],
        ["relatorio.pptx"] = FixtureFactory.Secrets,
        ["nativo.pdf"] = FixtureFactory.Secrets,
        ["escaneado.pdf"] = FixtureFactory.Secrets,
        ["pfd.pdf"] = [FixtureFactory.Client, FixtureFactory.Project, FixtureFactory.Person1, FixtureFactory.Company],
        ["fluxograma.svg"] = [FixtureFactory.Client, FixtureFactory.Person1],
        ["desenho.dxf"] = [FixtureFactory.Client, FixtureFactory.Person2],
        ["simulacao.dwxmz"] = [FixtureFactory.Project, FixtureFactory.Person1, FixtureFactory.Client, FixtureFactory.Person2],
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task NoSecretSurvives(string file)
    {
        string path = fx.Files.Single(f => f.EndsWith(file, StringComparison.Ordinal));
        var doc = await fx.Engine.ExtractAsync(path);
        string folded = TextFold.Key(doc.Text);

        // Extraction must have found the secrets in the first place; OCR may lose punctuation, so the
        // check is on the folded key of each value.
        foreach (var s in Present[file])
            Assert.True(folded.Contains(TextFold.Key(s), StringComparison.Ordinal), $"{file}: a extração não encontrou \"{s}\".");

        var session = Session.CreateNew("teste");
        var profile = FixtureFactory.Profile();
        var spans = await fx.Engine.DetectAsync(doc, profile, session: session);
        var pkg = fx.Engine.Export(doc, spans, session, profile);

        string outFolded = TextFold.Key(pkg.Anonymized.Text);
        foreach (var s in Present[file])
            Assert.False(outFolded.Contains(TextFold.Key(s), StringComparison.Ordinal), $"{file}: \"{s}\" sobreviveu no texto anonimizado.");
        Assert.DoesNotContain(pkg.TextLeaks, l => Present[file].Any(s => TextFold.Key(s).Contains(TextFold.Key(l.Text), StringComparison.Ordinal)));
        Assert.Empty(pkg.VisualLeaks);
        Assert.Empty(pkg.SkippedPages);
        if (doc.VisualPages.Count > 0) Assert.NotEmpty(pkg.Pages);
    }

    [Fact]
    public async Task RoundTripRestoresNamesAndNumbers()
    {
        string path = fx.Files.Single(f => f.EndsWith("contrato.docx", StringComparison.Ordinal));
        var doc = await fx.Engine.ExtractAsync(path);
        var session = Session.CreateNew("ida-e-volta");
        var spans = await fx.Engine.DetectAsync(doc, FixtureFactory.Profile(), session: session);
        var pkg = fx.Engine.Export(doc, spans, session, FixtureFactory.Profile(), images: false);

        Assert.DoesNotContain("1.200 t/d", pkg.Anonymized.Text);
        Assert.Contains("350 °C", pkg.Anonymized.Text);

        var back = Reidentifier.Restore(pkg.Anonymized.Text, session.Registry, session.Factors);
        Assert.Contains(FixtureFactory.Person1, back.Text);
        Assert.Contains(FixtureFactory.Cpf, back.Text);
        Assert.Contains("1.200 t/d", back.Text);
        Assert.Contains("R$ 45 milhões", back.Text);
        Assert.Equal(0, back.UnknownMarkers);
    }

    [Fact]
    public void ExportedFileNamesAreAnonymized()
    {
        var session = Session.CreateNew("nomes");
        string stem = Engine.SafeFileStem($"Relatório {FixtureFactory.Client} {FixtureFactory.Unit}-P-101A: revisão", session, FixtureFactory.Profile());
        Assert.DoesNotContain("Atlântica", stem);
        Assert.DoesNotContain(FixtureFactory.Unit, stem);
        Assert.Contains("[CLIENTE_1]", stem);
        Assert.Contains("P-101A", stem);
        Assert.DoesNotContain(':', stem);
    }

    [Fact]
    public async Task VisioCopyIsAnonymized()
    {
        // A minimal .vsdx built by hand: one page with one shape text.
        string path = Path.Combine(fx.Folder, "desenho.vsdx");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(content);
            }
            Add("visio/pages/page1.xml", $"""<?xml version="1.0"?><PageContents xmlns="http://schemas.microsoft.com/office/visio/2012/main"><Shapes><Shape ID="1"><Text>Cliente {FixtureFactory.Client}</Text></Shape><Shape ID="2"><Text>Bomba {FixtureFactory.Unit}-P-101A</Text></Shape></Shapes></PageContents>""");
            Add("docProps/core.xml", """<?xml version="1.0"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:creator>Carlos Eduardo Mendes</dc:creator></cp:coreProperties>""");
        }
        var doc = await fx.Engine.ExtractAsync(path);
        var session = Session.CreateNew("visio");
        var spans = await fx.Engine.DetectAsync(doc, FixtureFactory.Profile(), session: session);
        var pkg = fx.Engine.Export(doc, spans, session, FixtureFactory.Profile());
        using var ms = new MemoryStream();
        await doc.WriteNativeCopy!(pkg.Anonymized.AsTextReplacements(), ms);
        ms.Position = 0;
        using var copy = new System.IO.Compression.ZipArchive(ms);
        string all = string.Concat(copy.Entries.Select(e => new StreamReader(e.Open()).ReadToEnd()));
        Assert.DoesNotContain(FixtureFactory.Client, all);
        Assert.DoesNotContain(FixtureFactory.Person1, all);
        Assert.Contains("[CLIENTE_1]", all);
    }
}
