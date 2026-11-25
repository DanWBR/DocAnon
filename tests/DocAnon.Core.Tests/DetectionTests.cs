using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;

namespace DocAnon.Core.Tests;

public class DetectionTests
{
    private static List<DetectedSpan> Run(string text, CompanyProfile? profile = null) =>
        DetectionPipeline.CreateDefault().Run(new DetectionContext(text, profile));

    [Fact]
    public void FindsPersonalIdentifiers()
    {
        string cpf = Fake.Cpf("123456789");
        string text = $"O contratante, CPF {cpf}, e-mail joao.silva@example.com, telefone (21) 99876-5432, CEP 20040-020, " +
                      $"CNPJ 11.222.333/0001-81, cartão 4111 1111 1111 1111.";
        var spans = Run(text);
        Assert.Contains(spans, s => s.Type == EntityType.CPF && s.Text == cpf && s.Validated);
        Assert.Contains(spans, s => s.Type == EntityType.EMAIL && s.Text == "joao.silva@example.com");
        Assert.Contains(spans, s => s.Type == EntityType.TELEFONE && s.Text == "(21) 99876-5432");
        Assert.Contains(spans, s => s.Type == EntityType.CEP && s.Text == "20040-020");
        Assert.Contains(spans, s => s.Type == EntityType.CNPJ && s.Validated);
        Assert.Contains(spans, s => s.Type == EntityType.CARTAO && s.Validated);
    }

    [Fact]
    public void ContextRequiredRules()
    {
        var spans = Run("RG 12.345.678-9 e conta corrente 12345-6, nascido em 12/03/1980.");
        Assert.Contains(spans, s => s.Type == EntityType.RG);
        Assert.Contains(spans, s => s.Type == EntityType.CONTA_BANCARIA && s.Text == "12345-6");
        Assert.Contains(spans, s => s.Type == EntityType.DATA_NASCIMENTO && s.Text == "12/03/1980");

        var none = Run("A tabela 12.345.678 mostra 12/03/1980 como data do teste.");
        Assert.DoesNotContain(none, s => s.Type is EntityType.RG or EntityType.DATA_NASCIMENTO);
    }

    [Fact]
    public void AlphanumericCnpj()
    {
        var spans = Run("Fornecedor inscrito no CNPJ 12.ABC.345/01DE-35.");
        Assert.Contains(spans, s => s.Type == EntityType.CNPJ && s.Validated && s.Text == "12.ABC.345/01DE-35");
    }

    [Fact]
    public void TagsAreKeptAndUnitPrefixesAnonymized()
    {
        var spans = Run("A bomba 2100-P-101A alimenta o vaso V-201 controlado pelo FIC-2301 da U-2100. Ver API 650 e NR-13.");
        var unit = spans.Where(s => s.Type == EntityType.UNIDADE).ToList();
        Assert.Equal(2, unit.Count);
        Assert.All(unit, u => Assert.Equal("2100", u.Text));
        Assert.All(unit, u => Assert.True(u.Include));

        var pump = Assert.Single(spans, s => s.Text == "P-101A");
        Assert.Equal(EntityType.TAG_EQUIP, pump.Type);
        Assert.False(pump.Include);
        Assert.Contains(spans, s => s.Text == "V-201" && s.Type == EntityType.TAG_EQUIP);
        Assert.Contains(spans, s => s.Text == "FIC-2301" && s.Type == EntityType.TAG_INSTR);
        Assert.DoesNotContain(spans, s => s.Text.Contains("NR") || s.Text.Contains("API"));
    }

    [Fact]
    public void ProfileTermsAndExceptions()
    {
        var profile = new CompanyProfile
        {
            Terms =
            [
                new ProfileTerm { Text = "Petroquímica Exemplo", Type = EntityType.EMPRESA },
                new ProfileTerm { Text = "Projeto Aurora", Type = EntityType.PROJETO, Aliases = ["Aurora"] },
            ],
            Exceptions = ["metano"],
        };
        var spans = Run("A PETROQUIMICA EXEMPLO lançou o projeto aurora; a fase Aurora II usa metano.", profile);
        Assert.Contains(spans, s => s.Type == EntityType.EMPRESA && s.Text == "PETROQUIMICA EXEMPLO");
        Assert.Contains(spans, s => s.Type == EntityType.PROJETO && s.Text == "projeto aurora");
        Assert.Contains(spans, s => s.Type == EntityType.PROJETO && s.Text == "Aurora");
    }

    [Fact]
    public void OverlapPrefersValidatedThenLonger()
    {
        var a = new DetectedSpan(0, 10, EntityType.EMPRESA, "0123456789", "ner", 0.99);
        var b = new DetectedSpan(2, 5, EntityType.CPF, "23456", "regex", 0.5) { Validated = true };
        var c = new DetectedSpan(12, 4, EntityType.LOCAL, "Rio ", "ner", 0.9);
        var d = new DetectedSpan(12, 8, EntityType.LOCAL, "Rio Bran", "ner", 0.6);
        var r = DetectionPipeline.Resolve([a, b, c, d]);
        Assert.Equal([b, d], r);
    }

    [Fact]
    public void FirstNamesInTitleBlocks()
    {
        var spans = Run("EXEC.: RENATO T.  VERIF.: SERGIO  APROV.: HÉLIO\nResponsável: Otavio B Moreira. Fica em São Paulo.");
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text == "RENATO T.");
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text == "SERGIO");
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text == "HÉLIO");
        Assert.Contains(spans, s => s.Type == EntityType.PESSOA && s.Text == "Otavio B Moreira");
        Assert.DoesNotContain(spans, s => s.Text == "Paulo");
    }
}
