using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocAnon.Integration.Tests;

/// <summary>
/// Builds fictitious documents in every supported format. All names, numbers and companies are
/// invented. <see cref="Secrets"/> lists what must not survive anonymization.
/// </summary>
public static class FixtureFactory
{
    public const string Person1 = "Carlos Eduardo Mendes";
    public const string Person2 = "Fernanda Lima Rocha";
    public const string PersonEn = "Jonathan Pierce";
    public const string Company = "Petroquímica Horizonte";
    public const string Client = "Refinaria Atlântica";
    public const string Project = "Projeto Boreal";
    public const string Cpf = "529.982.247-25";
    public const string Cnpj = "11.222.333/0001-81";
    public const string Email = "carlos.mendes@horizonte.example";
    public const string Phone = "(21) 99876-5432";
    public const string Unit = "2400";

    /// <summary>Values that must not appear in any anonymized output.</summary>
    public static readonly string[] Secrets = [Person1, Person2, PersonEn, Company, Client, Project, Cpf, Cnpj, Email, Phone];

    public static Core.Profiles.CompanyProfile Profile() => new()
    {
        Name = "Fixtures",
        Terms =
        [
            new() { Text = Company, Type = Core.Model.EntityType.EMPRESA, Aliases = ["Petroquimica Horizonte"] },
            new() { Text = Client, Type = Core.Model.EntityType.CLIENTE },
            new() { Text = Project, Type = Core.Model.EntityType.PROJETO },
        ],
    };

    private static readonly string[] Paragraphs =
    [
        $"Contrato de prestação de serviços entre a {Company} S.A., CNPJ {Cnpj}, e a {Client}.",
        $"O responsável técnico é o engenheiro {Person1}, CPF {Cpf}, e-mail {Email}, telefone {Phone}.",
        $"No âmbito do {Project}, a unidade U-{Unit} processa 1.200 t/d de carga a 350 °C e 25 bar.",
        $"A bomba {Unit}-P-101A alimenta o vaso V-201. O investimento previsto é de R$ 45 milhões.",
        $"A revisão foi aprovada por {Person2} em reunião com {PersonEn}.",
    ];

    public static string Docx(string folder)
    {
        string path = Path.Combine(folder, "contrato.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new W.Body();
        foreach (var p in Paragraphs) body.Append(new W.Paragraph(new W.Run(new W.Text(p))));
        var table = new W.Table();
        foreach (var row in new[] { new[] { "Corrente", "Vazão (kg/h)" }, ["Carga", "50.000"], ["Produto", "42.500"] })
            table.Append(new W.TableRow(row.Select(c => new W.TableCell(new W.Paragraph(new W.Run(new W.Text(c)))))));
        body.Append(table);
        main.Document = new W.Document(body);
        return path;
    }

    public static string Xlsx(string folder)
    {
        string path = Path.Combine(folder, "avaliacao.xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new S.Workbook();
        var ws = wb.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        string[][] rows =
        [
            ["Item", "Responsável", "Custo (MM US$)", "Capacidade (t/d)"],
            ["Reator", Person1, "12.5", "1200"],
            ["Compressor", Person2, "8.75", "800"],
            [Project, Company, "3", "100"],
        ];
        uint r = 1;
        foreach (var row in rows)
        {
            var xr = new S.Row { RowIndex = r };
            int c = 0;
            foreach (var v in row)
            {
                string reference = $"{(char)('A' + c)}{r}";
                bool numeric = double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                xr.Append(numeric
                    ? new S.Cell { CellReference = reference, CellValue = new S.CellValue(v) }
                    : new S.Cell { CellReference = reference, DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(v)) });
                c++;
            }
            data.Append(xr);
            r++;
        }
        ws.Worksheet = new S.Worksheet(data);
        wb.Workbook.AppendChild(new S.Sheets(new S.Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Avaliação" }));
        return path;
    }

    public static string Pptx(string folder)
    {
        string path = Path.Combine(folder, "relatorio.pptx");
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var pres = doc.AddPresentationPart();
        pres.Presentation = new P.Presentation();
        var slide = pres.AddNewPart<SlidePart>();
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties());
        uint id = 2;
        foreach (var p in Paragraphs)
        {
            tree.Append(new P.Shape(
                new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = id++, Name = "t" }, new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(),
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(p))))));
        }
        slide.Slide = new P.Slide(new P.CommonSlideData(tree));
        pres.Presentation.SlideIdList = new P.SlideIdList(new P.SlideId { Id = 256, RelationshipId = pres.GetIdOfPart(slide) });
        return path;
    }

    private static void DrawLines(SKCanvas c, IEnumerable<string> lines, float x, float y, float size)
    {
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default, size);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var l in lines)
        {
            c.DrawText(l, x, y, font, ink);
            y += size * 1.6f;
        }
    }

    /// <summary>A PDF with a real text layer (Skia writes text as text).</summary>
    public static string NativePdf(string folder)
    {
        string path = Path.Combine(folder, "nativo.pdf");
        using var fs = File.Create(path);
        using var doc = SKDocument.CreatePdf(fs);
        var c = doc.BeginPage(595, 842);
        // Split long paragraphs so they fit the page.
        var lines = Paragraphs.SelectMany(p => Wrap(p, 85));
        DrawLines(c, lines, 40, 60, 10);
        doc.EndPage();
        doc.Close();
        return path;
    }

    /// <summary>A PDF that holds only an image of the text, as a scanner would produce.</summary>
    public static string ScannedPdf(string folder)
    {
        string path = Path.Combine(folder, "escaneado.pdf");
        using var bmp = new SKBitmap(1654, 2339);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            DrawLines(c, Paragraphs.SelectMany(p => Wrap(p, 70)), 120, 200, 34);
        }
        using var fs = File.Create(path);
        using var doc = SKDocument.CreatePdf(fs, new SKDocumentPdfMetadata { RasterDpi = 200 });
        var page = doc.BeginPage(595, 842);
        page.DrawBitmap(bmp, new SKRect(0, 0, 595, 842));
        doc.EndPage();
        doc.Close();
        return path;
    }

    /// <summary>A small process flow diagram with labels, lines and a title block, as vector PDF.</summary>
    public static string PfdPdf(string folder)
    {
        string path = Path.Combine(folder, "pfd.pdf");
        using var fs = File.Create(path);
        using var doc = SKDocument.CreatePdf(fs);
        var c = doc.BeginPage(1191, 842);
        using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true };
        var rnd = new Random(3);
        for (int i = 0; i < 60; i++) c.DrawLine(rnd.Next(60, 1100), rnd.Next(60, 650), rnd.Next(60, 1100), rnd.Next(60, 650), pen);
        foreach (var (x, y) in new[] { (150, 200), (450, 200), (750, 300), (450, 500) }) c.DrawCircle(x, y, 40, pen);
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default, 11);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        string[] labels = [$"{Unit}-P-101A", "V-201", "E-301", "FIC-2301", "1.200 t/d", "350 °C", $"U-{Unit}", "Carga fresca", "Produto", "Purga", "TK-501", "C-401"];
        int k = 0;
        foreach (var (x, y) in new[] { (110, 260), (420, 260), (720, 360), (430, 560), (200, 170), (500, 170), (800, 200), (60, 120), (900, 420), (300, 620), (950, 600), (650, 120) })
            c.DrawText(labels[k++], x, y, font, ink);
        c.DrawRect(800, 690, 360, 120, pen);
        c.DrawText($"CLIENTE: {Client}", 810, 715, font, ink);
        c.DrawText($"PROJETO: {Project}", 810, 740, font, ink);
        c.DrawText($"EXEC.: {Person1}", 810, 765, font, ink);
        c.DrawText($"{Company} S.A.", 810, 790, font, ink);
        doc.EndPage();
        doc.Close();
        return path;
    }

    public static string Svg(string folder)
    {
        string path = Path.Combine(folder, "fluxograma.svg");
        File.WriteAllText(path, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="800" height="400" viewBox="0 0 800 400">
              <rect width="800" height="400" fill="white"/>
              <line x1="100" y1="200" x2="700" y2="200" stroke="black"/>
              <circle cx="200" cy="200" r="40" fill="none" stroke="black"/>
              <text x="160" y="270" font-family="Arial" font-size="16">{Unit}-P-101A</text>
              <text x="400" y="180" font-family="Arial" font-size="16">1.200 t/d</text>
              <text x="40" y="380" font-family="Arial" font-size="16">Cliente: {Client}</text>
              <text x="420" y="380" font-family="Arial" font-size="16"><tspan>Resp.: </tspan><tspan>{Person1}</tspan></text>
            </svg>
            """);
        return path;
    }

    public static string Dxf(string folder)
    {
        string path = Path.Combine(folder, "desenho.dxf");
        var dxf = new netDxf.DxfDocument();
        dxf.Entities.Add(new netDxf.Entities.Line(new netDxf.Vector2(0, 0), new netDxf.Vector2(400, 0)));
        dxf.Entities.Add(new netDxf.Entities.Circle(new netDxf.Vector2(100, 50), 30));
        dxf.Entities.Add(new netDxf.Entities.Text($"{Unit}-P-101A", new netDxf.Vector2(70, 90), 8));
        dxf.Entities.Add(new netDxf.Entities.Text($"CLIENTE {Client}", new netDxf.Vector2(0, -40), 8));
        dxf.Entities.Add(new netDxf.Entities.MText($"RESP. {Person2}", new netDxf.Vector2(200, -20), 8));
        dxf.Save(path);
        return path;
    }

    /// <summary>A minimal DWSIM XML with two streams, one pump and an annotation.</summary>
    public static string Dwsim(string folder)
    {
        string path = Path.Combine(folder, "simulacao.dwxmz");
        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <DWSIM_Simulation_Data>
              <GeneralInfo><BuildVersion>9.0.0</BuildVersion></GeneralInfo>
              <Settings><SimulationName>{Project}</SimulationName><SimulationAuthor>{Person1}</SimulationAuthor><SimulationComments>Cliente {Client}</SimulationComments></Settings>
              <GraphicObjects>
                <GraphicObject><Name>MAT-1</Name><Tag>Carga</Tag><ObjectType>MaterialStream</ObjectType>
                  <OutputConnectors><Connector IsAttached="true" AttachedToObjID="PUMP-1" /></OutputConnectors></GraphicObject>
                <GraphicObject><Name>PUMP-1</Name><Tag>{Unit}-P-101A</Tag><ObjectType>Pump</ObjectType><Description>Bomba de carga</Description>
                  <OutputConnectors><Connector IsAttached="true" AttachedToObjID="MAT-2" /></OutputConnectors></GraphicObject>
                <GraphicObject><Name>MAT-2</Name><Tag>Descarga</Tag><ObjectType>MaterialStream</ObjectType></GraphicObject>
                <GraphicObject><Name>TXT-1</Name><ObjectType>GO_Text</ObjectType><Text>Revisado por {Person2}</Text></GraphicObject>
              </GraphicObjects>
              <SimulationObjects>
                <SimulationObject><Type>DWSIM.Thermodynamics.Streams.MaterialStream</Type><Name>MAT-1</Name>
                  <Phases><Phase><Properties /><Properties><temperature>298.15</temperature><pressure>101325</pressure><massflow>13.8889</massflow></Properties></Phase></Phases></SimulationObject>
                <SimulationObject><Type>DWSIM.UnitOperations.UnitOperations.Pump</Type><Name>PUMP-1</Name><Annotation /></SimulationObject>
              </SimulationObjects>
              <Compounds><Compound><Name>Water</Name></Compound></Compounds>
            </DWSIM_Simulation_Data>
            """;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("sim.xml");
        using var w = new StreamWriter(entry.Open());
        w.Write(xml);
        return path;
    }

    public static IEnumerable<string> Wrap(string s, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in s.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    public static List<string> All(string folder)
    {
        Directory.CreateDirectory(folder);
        return [Docx(folder), Xlsx(folder), Pptx(folder), NativePdf(folder), ScannedPdf(folder), PfdPdf(folder), Svg(folder), Dxf(folder), Dwsim(folder)];
    }
}
