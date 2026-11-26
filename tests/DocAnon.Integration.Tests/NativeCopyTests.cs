using System.IO.Compression;
using DocAnon.Core.Text;
using DocAnon.Core.Vault;
using DocAnon.Extraction;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using UglyToad.PdfPig;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocAnon.Integration.Tests;

/// <summary>
/// The anonymized copy in the source format, and the way back: a file the AI writes in any of the
/// supported formats gets its real values restored and keeps its format.
/// </summary>
public class NativeCopyTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    public static TheoryData<string, string> Formats => new()
    {
        { "contrato.docx", ".docx" },
        { "avaliacao.xlsx", ".xlsx" },
        { "relatorio.pptx", ".pptx" },
        { "nativo.pdf", ".pdf" },
        { "escaneado.pdf", ".pdf" },
    };

    private static readonly Dictionary<string, string[]> Present = new()
    {
        ["contrato.docx"] = FixtureFactory.Secrets,
        ["avaliacao.xlsx"] = [FixtureFactory.Person1, FixtureFactory.Person2, FixtureFactory.Project, FixtureFactory.Company],
        ["relatorio.pptx"] = FixtureFactory.Secrets,
        ["nativo.pdf"] = FixtureFactory.Secrets,
        ["escaneado.pdf"] = FixtureFactory.Secrets,
    };

    private async Task<(Session Session, Services.ExportPackage Package, string CopyPath)> AnonymizeAsync(string file, string label)
    {
        string path = fx.Files.Single(f => f.EndsWith(file, StringComparison.Ordinal));
        var doc = await fx.Engine.ExtractAsync(path);
        var session = Session.CreateNew(label);
        var profile = FixtureFactory.Profile();
        var spans = await fx.Engine.DetectAsync(doc, profile, session: session);
        var pkg = fx.Engine.Export(doc, spans, session, profile);
        string copy = Path.Combine(fx.Folder, $"{label}-{Path.GetFileNameWithoutExtension(file)}.anon{pkg.NativeCopyExtension}");
        if (pkg.NativeCopy is { } bytes) await File.WriteAllBytesAsync(copy, bytes);
        return (session, pkg, copy);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task CopyKeepsTheFormatWithoutSecrets(string file, string extension)
    {
        var (_, pkg, copy) = await AnonymizeAsync(file, "copia");
        Assert.Equal(extension, pkg.NativeCopyExtension);
        Assert.Empty(pkg.NativeLeaks);
        Assert.Empty(pkg.VisualLeaks);

        // The copy opens with the app's own reader, and the secrets are gone from it.
        var back = await fx.Engine.ExtractAsync(copy);
        string folded = TextFold.Key(back.Text);
        foreach (var s in Present[file])
            Assert.False(folded.Contains(TextFold.Key(s), StringComparison.Ordinal), $"{file}: \"{s}\" sobreviveu na cópia {extension}.");
        if (extension != ".pdf") Assert.Contains("[PESSOA_1]", back.Text);
    }

    [Theory]
    [InlineData("contrato.docx", "1.200 t/d")]
    [InlineData("contrato.docx", "50.000")]
    [InlineData("relatorio.pptx", "1.200 t/d")]
    [InlineData("avaliacao.xlsx", "1200")]
    [InlineData("avaliacao.xlsx", "12.5")]
    public async Task OfficeCopyComesBackExactly(string file, string number)
    {
        var (session, pkg, copy) = await AnonymizeAsync(file, "volta");
        var anonymized = await fx.Engine.ExtractAsync(copy);
        Assert.DoesNotContain(number, anonymized.Text);

        // The AI returns the file as it got it; the restore brings back names and numbers.
        string restored = FileReidentifier.OutputPathFor(copy, session.Registry, session.Factors);
        var r = await FileReidentifier.RestoreAsync(copy, restored, session.Registry, session.Factors);
        Assert.Empty(r.UnknownMarkers);
        Assert.EndsWith(".restaurado" + Path.GetExtension(file), restored);
        var doc = await fx.Engine.ExtractAsync(restored);
        foreach (var s in Present[file]) Assert.Contains(s, doc.Text);
        Assert.Contains(number, doc.Text);
    }

    [Fact]
    public async Task MarkerSplitAcrossRunsKeepsTheFirstRunFormatting()
    {
        var (session, _, _) = await AnonymizeAsync("contrato.docx", "runs");
        string path = Path.Combine(fx.Folder, "resposta-ia.docx");
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(
                new W.Run(new W.Text("Responsável: ") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
                new W.Run(new W.RunProperties(new W.Bold()), new W.Text("[PESSOA")),
                new W.Run(new W.Text("_1] confirmou.") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }))));
        }
        string restored = Path.Combine(fx.Folder, "resposta-ia.restaurado.docx");
        var r = await FileReidentifier.RestoreAsync(path, restored, session.Registry, session.Factors);
        Assert.Equal(1, r.Markers);
        using var check = WordprocessingDocument.Open(restored, false);
        var runs = check.MainDocumentPart!.Document!.Body!.Descendants<W.Run>().ToList();
        Assert.Equal(FixtureFactory.Person1, runs[1].InnerText);
        Assert.NotNull(runs[1].RunProperties?.Bold);
        Assert.Equal(" confirmou.", runs[2].InnerText);
    }

    [Fact]
    public async Task PdfFromTheAiGetsTheRealValues()
    {
        var (session, _, _) = await AnonymizeAsync("contrato.docx", "pdf-ia");
        var person = session.Registry.Records.First(r => r.Canonical == FixtureFactory.Person1);
        string path = Path.Combine(fx.Folder, "resposta-ia.pdf");
        using (var fs = File.Create(path))
        using (var pdf = SKDocument.CreatePdf(fs))
        {
            var c = pdf.BeginPage(595, 842);
            using var font = new SKFont(SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default, 11);
            using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            c.DrawText($"O engenheiro {person.Token} aprovou o relatório.", 50, 80, font, ink);
            pdf.EndPage();
            pdf.Close();
        }
        string restored = FileReidentifier.OutputPathFor(path, session.Registry, session.Factors);
        var r = await FileReidentifier.RestoreAsync(path, restored, session.Registry, session.Factors);
        Assert.Equal(1, r.Markers);
        using var check = PdfDocument.Open(restored);
        string text = string.Concat(check.GetPages().Select(p => p.Text));
        Assert.Contains(TextFold.Key(FixtureFactory.Person1), TextFold.Key(text));
    }

    [Fact]
    public async Task CsvCopyScalesByTheHeaderAndComesBack()
    {
        string path = Path.Combine(fx.Folder, "correntes.csv");
        await File.WriteAllTextAsync(path, $"Corrente;Vazão (kg/h);Responsável\nCarga;50000;{FixtureFactory.Person1}\nProduto;\"42500\";{FixtureFactory.Person2}\n");
        var doc = await fx.Engine.ExtractAsync(path);
        var session = Session.CreateNew("csv");
        var spans = await fx.Engine.DetectAsync(doc, FixtureFactory.Profile(), session: session);
        var pkg = fx.Engine.Export(doc, spans, session, FixtureFactory.Profile());
        Assert.Equal(".csv", pkg.NativeCopyExtension);
        string copy = System.Text.Encoding.UTF8.GetString(pkg.NativeCopy!);
        Assert.DoesNotContain("50000", copy);
        Assert.DoesNotContain(FixtureFactory.Person1, copy);
        Assert.StartsWith("Corrente;Vazão (kg/h);Responsável\n", copy);

        string copyPath = Path.Combine(fx.Folder, "correntes.anon.csv");
        await File.WriteAllBytesAsync(copyPath, pkg.NativeCopy!);
        string restored = Path.Combine(fx.Folder, "correntes.restaurado.csv");
        await FileReidentifier.RestoreAsync(copyPath, restored, session.Registry, session.Factors);
        string back = await File.ReadAllTextAsync(restored);
        Assert.Contains($"Carga;50000;{FixtureFactory.Person1}", back);
        Assert.Contains("\"42500\"", back);
    }

    [Fact]
    public async Task OfficeCopyDropsAuthorsAndDeletedText()
    {
        string path = Path.Combine(fx.Folder, "revisado.docx");
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(
                new W.Run(new W.Text($"O responsável é {FixtureFactory.Person1}.")),
                new W.DeletedRun(new W.Run(new W.DeletedText("texto apagado da Refinaria Secreta"))) { Author = FixtureFactory.Person2, Id = "1" })));
            doc.PackageProperties.Creator = FixtureFactory.Person2;
        }
        var extracted = await fx.Engine.ExtractAsync(path);
        var session = Session.CreateNew("autores");
        var spans = await fx.Engine.DetectAsync(extracted, FixtureFactory.Profile(), session: session);
        var pkg = fx.Engine.Export(extracted, spans, session, FixtureFactory.Profile());
        using var zip = new ZipArchive(new MemoryStream(pkg.NativeCopy!));
        string all = string.Concat(zip.Entries.Select(e => new StreamReader(e.Open()).ReadToEnd()));
        Assert.DoesNotContain(FixtureFactory.Person1, all);
        Assert.DoesNotContain(FixtureFactory.Person2, all);
        Assert.DoesNotContain("Refinaria Secreta", all);
    }
}
