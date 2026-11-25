using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;

namespace DocAnon.Core.Tests;

public class QuantityTests
{
    [Theory]
    [InlineData("1.234,5", true, 1234.5)]
    [InlineData("1,234.5", true, 1234.5)]
    [InlineData("1.234", true, 1234)]
    [InlineData("1.234", false, 1.234)]
    [InlineData("1,234", false, 1234)]
    [InlineData("1,234", true, 1.234)]
    [InlineData("12,5", false, 12.5)]
    [InlineData("0.05", true, 0.05)]
    [InlineData("1.500.000", false, 1500000)]
    [InlineData("-40", true, -40)]
    [InlineData("12 345", true, 12345)]
    public void ParsesNumbers(string token, bool commaIsDecimal, double expected)
    {
        Assert.True(NumberText.TryParse(token, commaIsDecimal, out double v, out _));
        Assert.Equal(expected, v, 9);
    }

    [Theory]
    [InlineData("1.500", true, 1.37, "2.055")]
    [InlineData("12,5", true, 2, "25,0")]
    [InlineData("1,250.75", false, 2, "2,501.50")]
    [InlineData("100", true, 1.37, "137")]
    [InlineData("0,05", true, 1.37, "0,0685")]
    public void FormatsWithSameStyle(string token, bool commaIsDecimal, double factor, string expected)
    {
        Assert.True(NumberText.TryParse(token, commaIsDecimal, out double v, out var style));
        Assert.Equal(expected, NumberText.Format(v * factor, style));
    }

    [Theory]
    [InlineData("kg/h", 1, 0, QuantityKind.Extensive)]
    [InlineData("t/d", 1, 0, QuantityKind.Extensive)]
    [InlineData("Nm³/h", 1, 0, QuantityKind.Extensive)]
    [InlineData("MW", 1, 0, QuantityKind.Extensive)]
    [InlineData("kWh/t", 0, 0, QuantityKind.Intensive)]
    [InlineData("kg/m³", 0, 0, QuantityKind.Intensive)]
    [InlineData("kJ/kg.K", 0, 0, QuantityKind.Intensive)]
    [InlineData("°C", 0, 0, QuantityKind.Intensive)]
    [InlineData("bar", 0, 0, QuantityKind.Intensive)]
    [InlineData("W/m².K", 0, 0, QuantityKind.Intensive)]
    [InlineData("mm", 0, 0, QuantityKind.Geometric)]
    [InlineData("tpd", 1, 0, QuantityKind.Extensive)]
    public void UnitDimensions(string unit, int ext, int money, QuantityKind kind)
    {
        var p = UnitCatalog.Parse(unit);
        Assert.NotNull(p);
        Assert.Equal(new ScaleDimension(ext, money), p.Dimension);
        Assert.Equal(kind, p.Kind);
    }

    [Theory]
    [InlineData("US$/t", 0, 1)]
    [InlineData("R$", 0, 1)]
    [InlineData("MM US$", 0, 1)]
    public void MoneyHeaders(string unit, int ext, int money)
    {
        var p = QuantityDetector.ParseHeaderUnit(unit);
        Assert.NotNull(p);
        var expected = unit.Contains('/') ? new ScaleDimension(-1, 1) : new ScaleDimension(ext, money);
        Assert.Equal(expected, p.Dimension);
    }

    private static List<DetectedSpan> Quantities(string text) =>
        new QuantityDetector().Detect(new DetectionContext(text)).OrderBy(s => s.Start).ToList();

    [Fact]
    public void FindsInlineQuantities()
    {
        var q = Quantities("A planta processa 1.200 t/d de carga a 350 °C e 25 bar, com custo de R$ 12,5 milhões e preço de US$ 450/t. A faixa é 80-120 m³/h.");
        Assert.Contains(q, s => s.Text == "1.200 t/d" && s.Action == SpanAction.Scale && s.Quantity!.Numbers[0].Value == 1200);
        Assert.Contains(q, s => s.Text == "350 °C" && s.Action == SpanAction.Keep);
        Assert.Contains(q, s => s.Text == "25 bar" && s.Action == SpanAction.Keep);
        Assert.Contains(q, s => s.Text == "R$ 12,5 milhões" && s.Quantity!.Dimension == new ScaleDimension(0, 1));
        Assert.Contains(q, s => s.Text == "US$ 450/t" && s.Quantity!.Dimension == new ScaleDimension(-1, 1));
        var range = Assert.Single(q, s => s.Text == "80-120 m³/h");
        Assert.Equal(2, range.Quantity!.Numbers.Count);
    }

    [Fact]
    public void IgnoresIdentifiersAndTags()
    {
        var q = Quantities("Tag 2100-P-101, CPF 529.982.247-25, data 12/03/2020, telefone (21) 99876-5432.");
        Assert.Empty(q);
    }

    [Fact]
    public void TableColumnsAndRowUnits()
    {
        string table = """
            | Corrente | Vazão (kg/h) | Temperatura (°C) | Custo (MM US$) |
            |---|---|---|---|
            | S1 | 1.000 | 25 | 3,5 |
            | S2 | 250,5 | 80 | 1,2 |

            | Propriedade | Unidade | S1 | S2 |
            |---|---|---|---|
            | Vazão molar | kmol/h | 10,5 | 20 |
            | Pressão | bar | 5 | 6 |
            """;
        var q = Quantities(table);
        Assert.Contains(q, s => s.Text == "1.000" && s.Action == SpanAction.Scale && s.Quantity!.UnitFromColumnHeader);
        Assert.Contains(q, s => s.Text == "25" && s.Action == SpanAction.Keep);
        Assert.Contains(q, s => s.Text == "3,5" && s.Quantity!.Kind == QuantityKind.Monetary);
        Assert.Contains(q, s => s.Text == "10,5" && s.Quantity!.Unit == "kmol/h");
        Assert.Contains(q, s => s.Text == "6" && s.Action == SpanAction.Keep);
    }

    [Fact]
    public void ScalingKeepsMassBalance()
    {
        // Feed = product + purge, in kg/h.
        string text = """
            | Corrente | Vazão (kg/h) |
            |---|---|
            | Carga | 1.000 |
            | Produto | 850 |
            | Purga | 150 |
            """;
        var ctx = new DetectionContext(text);
        var spans = DetectionPipeline.CreateDefault().Run(ctx);
        var f = new ScaleFactors(1.7, 0.6);
        var result = Anonymizer.Apply(text, spans, new EntityRegistry(), f);

        var after = new DetectionContext(result.Text);
        var values = new QuantityDetector().Detect(after).OrderBy(s => s.Start).Select(s => s.Quantity!.Numbers[0].Value).ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(1700, values[0], 6);
        Assert.Equal(values[0], values[1] + values[2], 6);
    }

    [Fact]
    public void PriceScalesWithMoneyOverExtension()
    {
        var f = new ScaleFactors(2, 3);
        Assert.Equal(1.5, f.Factor(new ScaleDimension(-1, 1)), 9);
        Assert.Equal(2, f.Factor(new ScaleDimension(1, 0)), 9);
        Assert.Equal(1, f.Factor(ScaleDimension.None), 9);
    }

    [Fact]
    public void RandomFactorsStayAwayFromOne()
    {
        for (int i = 0; i < 200; i++)
        {
            var f = ScaleFactors.Random();
            Assert.InRange(f.KExt, 0.4, 2.5);
            Assert.True(f.KExt < 0.8 || f.KExt > 1.25);
            Assert.True(f.KMoney < 0.8 || f.KMoney > 1.25);
        }
    }
}
