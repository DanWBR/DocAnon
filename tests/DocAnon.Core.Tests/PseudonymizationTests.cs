using System.Security.Cryptography;
using DocAnon.Core.Detection;
using DocAnon.Core.Export;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;
using DocAnon.Core.Vault;

namespace DocAnon.Core.Tests;

public class PseudonymizationTests
{
    [Fact]
    public void NumericIdentifiersShareMarkerAcrossFormats()
    {
        var reg = new EntityRegistry();
        var a = reg.GetOrCreate(EntityType.CPF, "529.982.247-25");
        var b = reg.GetOrCreate(EntityType.CPF, "52998224725");
        Assert.Same(a, b);
        Assert.Equal("[CPF_1]", a.Token);
    }

    [Fact]
    public void PersonVariantsLinkWhenUnique()
    {
        var reg = new EntityRegistry();
        var full = reg.GetOrCreate(EntityType.PESSOA, "João Carlos da Silva");
        Assert.Same(full, reg.GetOrCreate(EntityType.PESSOA, "João Silva"));
        Assert.Same(full, reg.GetOrCreate(EntityType.PESSOA, "Sr. Silva"));
        var other = reg.GetOrCreate(EntityType.PESSOA, "Maria Souza");
        Assert.NotSame(full, other);
        Assert.Equal("[PESSOA_2]", other.Token);
        Assert.Equal("João Carlos da Silva", full.Canonical);
    }

    [Fact]
    public void AmbiguousFirstNameGetsItsOwnMarker()
    {
        var reg = new EntityRegistry();
        reg.GetOrCreate(EntityType.PESSOA, "Ana Lima");
        reg.GetOrCreate(EntityType.PESSOA, "Ana Costa");
        var ana = reg.GetOrCreate(EntityType.PESSOA, "Ana");
        Assert.Equal(3, ana.Number);
    }

    [Fact]
    public void CompanySuffixesAndPrefixesLink()
    {
        var reg = new EntityRegistry();
        var a = reg.GetOrCreate(EntityType.EMPRESA, "Petroquímica Exemplo S.A.");
        Assert.Same(a, reg.GetOrCreate(EntityType.EMPRESA, "PETROQUIMICA EXEMPLO"));
        Assert.Same(a, reg.GetOrCreate(EntityType.CLIENTE, "Petroquímica Exemplo do Sul"));
    }

    [Fact]
    public void MergeAndSplit()
    {
        var reg = new EntityRegistry();
        var a = reg.GetOrCreate(EntityType.PESSOA, "Carlos Pereira");
        var b = reg.GetOrCreate(EntityType.PESSOA, "Cadu");
        reg.Merge(a, b);
        Assert.Same(a, reg.GetOrCreate(EntityType.PESSOA, "Cadu"));
        var c = reg.Split(a, "Cadu");
        Assert.NotSame(a, c);
        Assert.Same(c, reg.FindByText(EntityType.PESSOA, "Cadu"));
    }

    private const string Doc = """
        Contrato entre Petroquímica Exemplo S.A. e João Carlos da Silva, CPF 529.982.247-25.
        A unidade U-2100 produz 1.200 t/d a 350 °C, com receita de R$ 45 milhões por ano.
        O Sr. Silva responde pela bomba 2100-P-101A.
        """;

    private static (AnonymizationResult Result, EntityRegistry Registry, ScaleFactors Factors, List<DetectedSpan> Spans) Anonymize()
    {
        var profile = new CompanyProfile { Terms = [new ProfileTerm { Text = "Petroquímica Exemplo S.A.", Type = EntityType.EMPRESA }] };
        var spans = DetectionPipeline.CreateDefault().Run(new DetectionContext(Doc, profile));
        // Names come from NER in the app. The test marks them by hand.
        int i = Doc.IndexOf("João Carlos da Silva", StringComparison.Ordinal);
        spans.Add(new DetectedSpan(i, "João Carlos da Silva".Length, EntityType.PESSOA, "João Carlos da Silva", "manual"));
        int j = Doc.IndexOf("Sr. Silva", StringComparison.Ordinal) + 4;
        spans.Add(new DetectedSpan(j, 5, EntityType.PESSOA, "Silva", "manual"));
        var reg = new EntityRegistry();
        var f = new ScaleFactors(1.5, 0.5);
        return (Anonymizer.Apply(Doc, spans, reg, f), reg, f, spans);
    }

    [Fact]
    public void AnonymizedTextHasMarkersAndScaledNumbers()
    {
        var (r, _, _, _) = Anonymize();
        Assert.Contains("[EMPRESA_1]", r.Text);
        Assert.Contains("[PESSOA_1]", r.Text);
        Assert.Contains("[CPF_1]", r.Text);
        Assert.Contains("U-[UNIDADE_1]", r.Text);
        Assert.Contains("[UNIDADE_1]-P-101A", r.Text);
        Assert.Contains("1.800 t/d", r.Text);
        Assert.Contains("350 °C", r.Text);
        Assert.Contains("R$ 22,5 milhões", r.Text);
        Assert.Contains("O Sr. [PESSOA_1]", r.Text);
        Assert.DoesNotContain("Silva", r.Text);
        Assert.DoesNotContain("529.982", r.Text);
    }

    [Fact]
    public void RoundTripRestoresOriginal()
    {
        var (r, reg, f, _) = Anonymize();
        var back = Reidentifier.Restore(r.Text, reg, f);
        // "Silva" restores to the canonical full name, which is the only intended difference.
        string expected = Doc.Replace("O Sr. Silva", "O Sr. João Carlos da Silva");
        Assert.Equal(expected, back.Text);
        Assert.Equal(0, back.UnknownMarkers);
    }

    [Fact]
    public void TolerantMarkersInAnswer()
    {
        var (_, reg, f, _) = Anonymize();
        string answer = "Recomendo que **[PESSOA_1]** revise; [Pessoa 1] e PESSOA_1 concordam. The [PERSON_1] at [COMPANY_1] ... [PESSOA_9] não existe. A planta poderia chegar a 2.400 t/d.";
        var back = Reidentifier.Restore(answer, reg, f);
        Assert.Equal(4, back.Items.Count(i => i.Kind == RestoreKind.Marker && i.Restored == "João Carlos da Silva"));
        Assert.Contains("**João Carlos da Silva**", back.Text);
        Assert.Contains("Petroquímica Exemplo S.A.", back.Text);
        Assert.Equal(1, back.UnknownMarkers);
        Assert.Contains("1.600 t/d", back.Text);
    }

    [Fact]
    public void LeakCheckFindsLeftovers()
    {
        var (r, reg, _, _) = Anonymize();
        Assert.Empty(LeakChecker.Check(r.Text, reg));

        string leaked = r.Text + "\nContato: joao@example.com, Silva, CPF 52998224725.";
        var findings = LeakChecker.Check(leaked, reg);
        Assert.Contains(findings, x => x.Text == "Silva");
        Assert.Contains(findings, x => x.Text == "52998224725");
        Assert.Contains(findings, x => x.Type == EntityType.EMAIL);
    }

    [Fact]
    public void ExcludedSpansAreLeftAlone()
    {
        var spans = DetectionPipeline.CreateDefault().Run(new DetectionContext("CPF 529.982.247-25"));
        foreach (var s in spans) s.Include = false;
        var r = Anonymizer.Apply("CPF 529.982.247-25", spans, new EntityRegistry(), ScaleFactors.Identity);
        Assert.Equal("CPF 529.982.247-25", r.Text);
    }

    [Fact]
    public void QuantityTurnedIntoMarker()
    {
        string text = "Temperatura do reator: 412 °C.";
        var spans = DetectionPipeline.CreateDefault().Run(new DetectionContext(text));
        var t = Assert.Single(spans, s => s.Type == EntityType.QUANTIDADE);
        t.Action = SpanAction.Pseudonymize;
        var reg = new EntityRegistry();
        var r = Anonymizer.Apply(text, spans, reg, ScaleFactors.Identity);
        Assert.Equal("Temperatura do reator: [VALOR_1].", r.Text);
        Assert.Equal("Temperatura do reator: 412 °C.", Reidentifier.Restore(r.Text, reg, ScaleFactors.Identity).Text);
    }
}

public class VaultTests
{
    [Fact]
    public void PasswordRoundTripAndWrongPassword()
    {
        byte[] plain = "segredo industrial"u8.ToArray();
        byte[] enc = VaultCrypto.Encrypt(plain, new VaultKey.Password("correta"));
        Assert.Equal(plain, VaultCrypto.Decrypt(enc, new VaultKey.Password("correta")));
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(enc, new VaultKey.Password("errada")));
    }

    [Fact]
    public void TamperedHeaderFails()
    {
        byte[] enc = VaultCrypto.Encrypt("x"u8, new VaultKey.Password("p"));
        enc[10] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => VaultCrypto.Decrypt(enc, new VaultKey.Password("p")));
    }

    [Fact]
    public void WindowsUserKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        byte[] enc = VaultCrypto.Encrypt("abc"u8, new VaultKey.WindowsUser());
        Assert.Equal("abc"u8.ToArray(), VaultCrypto.Decrypt(enc, new VaultKey.WindowsUser()));
    }

    [Fact]
    public void SessionStoreRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), "docanon-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SessionStore(root);
            var s = Session.CreateNew("teste");
            s.Registry.GetOrCreate(EntityType.PESSOA, "Maria Souza");
            s.Documents.Add("contrato.pdf");
            string path = store.Save(s, new VaultKey.Password("pw"));
            Assert.DoesNotContain("Maria", File.ReadAllText(path));

            var loaded = store.Load(path, new VaultKey.Password("pw"));
            Assert.Equal(s.KExt, loaded.KExt);
            Assert.Equal("[PESSOA_1]", loaded.Registry.FindByText(EntityType.PESSOA, "maria souza")!.Token);
            Assert.Equal("[PESSOA_2]", loaded.Registry.GetOrCreate(EntityType.PESSOA, "Paulo Lima").Token);

            var listed = Assert.Single(store.List());
            Assert.True(listed.PasswordProtected);
            EncryptedStore.SecureDelete(path);
            Assert.Empty(store.List());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProfileStoreRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), "docanon-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(root);
            var p = new CompanyProfile { Name = "Cliente A", Terms = [new ProfileTerm { Text = "Refinaria Exemplo", Type = EntityType.PLANTA }] };
            string path = store.Save(p, new VaultKey.Password("pw"));
            var loaded = store.Load(path, new VaultKey.Password("pw"));
            Assert.Equal(EntityType.PLANTA, loaded.Terms[0].Type);
            p.Name = "Cliente B";
            store.Save(p, new VaultKey.Password("pw"));
            Assert.Single(store.List());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
