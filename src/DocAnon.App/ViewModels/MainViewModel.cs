using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocAnon.Core.Detection;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;
using DocAnon.Core.Text;
using DocAnon.Core.Vault;
using DocAnon.Extraction;
using DocAnon.Llm;
using DocAnon.Services;

namespace DocAnon.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly string _root;
    private readonly SessionStore _sessions;
    private readonly ProfileStore _profiles;
    private Engine? _engine;
    private CancellationTokenSource? _cts;

    public AppSettings Settings { get; }

    public MainViewModel(string? root = null)
    {
        _root = root ?? EncryptedStore.DefaultRoot();
        Settings = AppSettings.Load(_root);
        _sessions = new SessionStore(_root);
        _profiles = new ProfileStore(_root);
        RefreshStoredLists();
        Status = "Crie ou abra uma sessão para começar. Tudo roda neste computador.";
    }

    public static IReadOnlyList<EntityType> EntityTypes { get; } = Enum.GetValues<EntityType>().Where(t => t is not (EntityType.QUANTIDADE or EntityType.VALOR)).ToList();
    public static IReadOnlyList<string> QuantityActions { get; } = ["escalar", "manter", "marcador"];

    // ---- status -------------------------------------------------------------------------

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand), nameof(AddPastedTextCommand), nameof(ExportCommand))]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private int selectedTab;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task RunBusy(string message, Func<CancellationToken, Task> work)
    {
        IsBusy = true;
        Status = message;
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelado.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or InvalidOperationException or CryptographicException or HttpRequestException or UnauthorizedAccessException)
        {
            Status = "Erro: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private Engine EnsureEngine()
    {
        if (_engine is not null) return _engine;
        _engine = new Engine(new EngineOptions { UseNer = Settings.UseNer, Dpi = Settings.Dpi });
        foreach (var w in _engine.StartupWarnings) Status = w;
        return _engine;
    }

    // ---- vault key ----------------------------------------------------------------------

    [ObservableProperty]
    private string vaultPassword = "";

    public bool UsesPassword => Settings.Protection == "senha";

    private VaultKey Key()
    {
        if (Settings.Protection == "windows" && OperatingSystem.IsWindows()) return new VaultKey.WindowsUser();
        if (string.IsNullOrEmpty(VaultPassword)) throw new InvalidOperationException("Digite a senha do cofre.");
        return new VaultKey.Password(VaultPassword);
    }

    // ---- sessions -----------------------------------------------------------------------

    public ObservableCollection<StoredItem> StoredSessions { get; } = [];

    [ObservableProperty]
    private StoredItem? selectedStoredSession;

    [ObservableProperty]
    private string newSessionLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionTitle), nameof(HasSession))]
    [NotifyCanExecuteChangedFor(nameof(AddFilesCommand), nameof(AddPastedTextCommand))]
    private Session? session;

    public bool HasSession => Session is not null;
    public string SessionTitle => Session is null ? "Sem sessão" : $"Sessão: {Session.Label} ({Session.Registry.Records.Count} entidades, {Session.Documents.Count} documentos)";

    private void RefreshStoredLists()
    {
        StoredSessions.Clear();
        foreach (var s in _sessions.List()) StoredSessions.Add(s);
        StoredProfiles.Clear();
        foreach (var p in _profiles.List()) StoredProfiles.Add(p);
    }

    [RelayCommand]
    private void NewSession()
    {
        string label = string.IsNullOrWhiteSpace(NewSessionLabel) ? $"sessao-{DateTime.Now:yyyyMMdd-HHmm}" : NewSessionLabel.Trim();
        Session = Session.CreateNew(label, Settings.ScaleMin, Settings.ScaleMax);
        Documents.Clear();
        SaveSession();
        Status = $"Sessão \"{label}\" criada. O rótulo fica no nome do arquivo, sem cifra; não use nomes sigilosos nele.";
    }

    [RelayCommand]
    private void OpenSession()
    {
        if (SelectedStoredSession is null) return;
        try
        {
            Session = _sessions.Load(SelectedStoredSession.Path, Key());
            Documents.Clear();
            Status = $"Sessão aberta. Os documentos anteriores ({Session.Documents.Count}) não ficam guardados; abra-os de novo se precisar.";
        }
        catch (CryptographicException)
        {
            Status = "Não foi possível abrir: senha errada ou cofre de outro usuário.";
        }
    }

    [RelayCommand]
    private void DeleteSession()
    {
        if (SelectedStoredSession is null) return;
        EncryptedStore.SecureDelete(SelectedStoredSession.Path);
        RefreshStoredLists();
        Status = "Sessão apagada.";
    }

    private void SaveSession()
    {
        if (Session is null) return;
        _sessions.Save(Session, Key());
        RefreshStoredLists();
        OnPropertyChanged(nameof(SessionTitle));
    }

    // ---- profiles -----------------------------------------------------------------------

    public ObservableCollection<StoredItem> StoredProfiles { get; } = [];

    [ObservableProperty]
    private StoredItem? selectedStoredProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileTitle))]
    private CompanyProfile? profile;

    public string ProfileTitle => Profile is null ? "Sem perfil de empresa" : $"Perfil: {Profile.Name}";

    public ObservableCollection<ProfileTermItem> ProfileTerms { get; } = [];

    [ObservableProperty]
    private string profileName = "";

    [ObservableProperty]
    private string profileExceptions = "";

    [ObservableProperty]
    private string profileUnitPatterns = "";

    [ObservableProperty]
    private bool profileAnonymizeAllTags;

    [ObservableProperty]
    private bool profileCoverSmallImages = true;

    public ObservableCollection<LogoTemplate> ProfileLogos { get; } = [];

    public ObservableCollection<RelativeRegion> ProfileRegions { get; } = [];

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedStoredProfile is null) return;
        try
        {
            Profile = _profiles.Load(SelectedStoredProfile.Path, Key());
            Settings.LastProfileId = Profile.Id;
            Settings.Save(_root);
            FillProfileEditor();
            Status = $"Perfil \"{Profile.Name}\" carregado ({Profile.Terms.Count} termos, {Profile.Logos.Count} logotipos).";
        }
        catch (CryptographicException)
        {
            Status = "Não foi possível abrir o perfil: senha errada ou cofre de outro usuário.";
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        Profile = new CompanyProfile { Name = "Novo perfil" };
        FillProfileEditor();
        SelectedTab = 4;
    }

    [RelayCommand]
    private void ClearProfile()
    {
        Profile = null;
        FillProfileEditor();
    }

    private void FillProfileEditor()
    {
        ProfileTerms.Clear();
        ProfileLogos.Clear();
        ProfileRegions.Clear();
        ProfileName = Profile?.Name ?? "";
        ProfileExceptions = string.Join(Environment.NewLine, Profile?.Exceptions ?? []);
        ProfileUnitPatterns = string.Join(Environment.NewLine, Profile?.UnitPrefixPatterns ?? []);
        ProfileAnonymizeAllTags = Profile?.AnonymizeAllTags ?? false;
        ProfileCoverSmallImages = Profile?.CoverSmallImages ?? true;
        if (Profile is null) return;
        foreach (var t in Profile.Terms) ProfileTerms.Add(ProfileTermItem.From(t));
        foreach (var l in Profile.Logos) ProfileLogos.Add(l);
        foreach (var r in Profile.FixedRegions) ProfileRegions.Add(r);
    }

    [RelayCommand]
    private void AddProfileTerm() => ProfileTerms.Add(new ProfileTermItem());

    [RelayCommand]
    private void RemoveProfileTerm(ProfileTermItem? item)
    {
        if (item is not null) ProfileTerms.Remove(item);
    }

    [RelayCommand]
    private void RemoveLogo(LogoTemplate? logo)
    {
        if (logo is not null) ProfileLogos.Remove(logo);
    }

    public void AddLogoFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        ProfileLogos.Add(new LogoTemplate { Name = System.IO.Path.GetFileNameWithoutExtension(path), PngBase64 = Convert.ToBase64String(bytes) });
    }

    [RelayCommand]
    private void AddRegion() => ProfileRegions.Add(new RelativeRegion { Label = "carimbo", X = 0.8, Y = 0.8, Width = 0.2, Height = 0.2, Orientation = PageOrientation.Landscape });

    [RelayCommand]
    private void RemoveRegion(RelativeRegion? r)
    {
        if (r is not null) ProfileRegions.Remove(r);
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Profile ??= new CompanyProfile();
        Profile.Name = string.IsNullOrWhiteSpace(ProfileName) ? "Perfil" : ProfileName.Trim();
        Profile.Terms = ProfileTerms.Where(t => !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.ToTerm()).ToList();
        Profile.Exceptions = Lines(ProfileExceptions);
        Profile.UnitPrefixPatterns = Lines(ProfileUnitPatterns);
        Profile.AnonymizeAllTags = ProfileAnonymizeAllTags;
        Profile.CoverSmallImages = ProfileCoverSmallImages;
        Profile.Logos = ProfileLogos.ToList();
        Profile.FixedRegions = ProfileRegions.ToList();
        _profiles.Save(Profile, Key());
        Settings.LastProfileId = Profile.Id;
        Settings.Save(_root);
        RefreshStoredLists();
        OnPropertyChanged(nameof(ProfileTitle));
        Status = "Perfil salvo e cifrado.";
    }

    private static List<string> Lines(string s) => s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---- documents and detection ---------------------------------------------------------

    public ObservableCollection<DocumentItem> Documents { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    private DocumentItem? selectedDocument;

    public bool HasDocument => SelectedDocument is not null;

    [ObservableProperty]
    private string pastedText = "";

    public static string FileFilterDescription => string.Join(", ", DocumentExtractor.SupportedExtensions);

    /// <summary>Raised after spans change, so the editor redraws its highlights.</summary>
    public event Action? SpansChanged;

    private bool CanAddDocuments() => !IsBusy && Session is not null;

    [RelayCommand(CanExecute = nameof(CanAddDocuments))]
    private Task AddFiles(IReadOnlyList<string> paths) => RunBusy("Lendo documentos...", async ct =>
    {
        var engine = EnsureEngine();
        await ConfigureLlmAsync(engine, ct);
        foreach (var path in paths)
        {
            if (!engine.Extractor.CanRead(path))
            {
                Status = $"Formato não suportado: {System.IO.Path.GetFileName(path)}";
                continue;
            }
            Status = $"Lendo {System.IO.Path.GetFileName(path)} (OCR pode demorar em desenhos grandes)...";
            var doc = await engine.ExtractAsync(path, ct);
            var item = new DocumentItem(doc, path);
            Documents.Add(item);
            SelectedDocument = item;
            await DetectAsync(item, ct);
        }
        SelectedTab = 1;
    });

    [RelayCommand(CanExecute = nameof(CanAddDocuments))]
    private Task AddPastedText() => RunBusy("Analisando o texto colado...", async ct =>
    {
        if (string.IsNullOrWhiteSpace(PastedText)) return;
        var engine = EnsureEngine();
        await ConfigureLlmAsync(engine, ct);
        var item = new DocumentItem(DocumentExtractor.FromText(PastedText, $"texto colado {Documents.Count + 1}"), null);
        Documents.Add(item);
        SelectedDocument = item;
        PastedText = "";
        await DetectAsync(item, ct);
        SelectedTab = 1;
    });

    private async Task ConfigureLlmAsync(Engine engine, CancellationToken ct)
    {
        if (!Settings.UseLlm || engine.Llm is not null) return;
        bool ok = await engine.EnableLlmAsync(new LlmOptions
        {
            Address = Settings.OllamaAddress,
            TextModel = Settings.LlmTextModel,
            VisionModel = string.IsNullOrWhiteSpace(Settings.LlmVisionModel) ? null : Settings.LlmVisionModel,
        }, ct);
        if (!ok) Status = engine.StartupWarnings.LastOrDefault() ?? "Ollama indisponível.";
    }

    private async Task DetectAsync(DocumentItem item, CancellationToken ct)
    {
        var engine = EnsureEngine();
        Status = $"Procurando dados sensíveis em {item.Name}...";
        var progress = new Progress<(string Stage, double Fraction)>(p =>
        {
            Progress = p.Fraction * 100;
            Status = $"{item.Name}: {(p.Stage == "ner" ? "reconhecimento de nomes" : p.Stage == "llm" ? "sugestões do LLM local" : p.Stage)} {p.Fraction:P0}";
        });
        var spans = await engine.DetectAsync(item.Doc, Profile, Settings.UseNer, Settings.UseLlm, progress, ct, Session);
        if (!Settings.UseFirstNames) spans.RemoveAll(s => s.Detector == "nomes");
        if (engine.Llm is { HasTextModel: true } llm)
        {
            Status = $"{item.Name}: o LLM local está revisando os casos duvidosos...";
            await llm.DisambiguateAsync(item.Doc.Text, spans, ct);
        }
        item.Spans = spans;
        RebuildGroups(item);
        item.Status = $"{spans.Count(s => s.Include && s.Action != SpanAction.Keep)} trechos a anonimizar";
        Status = $"{item.Name}: revise a lista. Itens desmarcados não serão alterados.";
    }

    public void RebuildGroups(DocumentItem item)
    {
        item.Groups.Clear();
        var groups = item.Spans
            .GroupBy(s => (s.Type, Key: s.Quantity is null ? TextFold.Key(s.Text) : s.Start.ToString()))
            .Select(g => new EntityGroup(g.Key.Type, g.First().Text, g.ToList()))
            .OrderBy(g => g.IsQuantity)
            .ThenBy(g => g.Type)
            .ThenBy(g => g.Text, StringComparer.CurrentCultureIgnoreCase);
        foreach (var g in groups)
        {
            g.Changed = () =>
            {
                UpdatePreview(g);
                SpansChanged?.Invoke();
            };
            UpdatePreview(g);
            item.Groups.Add(g);
        }
        SpansChanged?.Invoke();
    }

    private void UpdatePreview(EntityGroup g)
    {
        if (Session is null) return;
        var s = g.Spans[0];
        if (!g.Include || s.Action == SpanAction.Keep) g.Preview = "mantido";
        else if (s.Action == SpanAction.Scale && s.Quantity is { } q && SelectedDocument is { } d)
            g.Preview = Session.Factors.ScaleSpanText(d.Doc.Text, s.Start, q) + d.Doc.Text[q.Numbers.Max(n => n.End)..s.End];
        else
        {
            var type = s.Type == EntityType.QUANTIDADE ? EntityType.VALOR : s.Type;
            g.Preview = Session.Registry.FindByText(type, s.Text)?.Token ?? $"[{type}_novo]";
        }
    }

    /// <summary>Marks the selected text by hand. The mark wins over every detector and is searched in the whole document.</summary>
    public void MarkSelection(int start, int length, EntityType type)
    {
        if (SelectedDocument is not { } d || length <= 0) return;
        string text = d.Doc.Text.Substring(start, length).Trim();
        if (text.Length == 0) return;
        int lead = d.Doc.Text.Substring(start, length).IndexOf(text, StringComparison.Ordinal);
        var manual = new DetectedSpan(start + lead, text.Length, type, text, "manual", 1) { Note = "marcado à mão" };
        var ctx = new DetectionContext(d.Doc.Text, Profile);
        d.Spans = DetectionPipeline.Propagate(ctx, DetectionPipeline.Resolve(d.Spans.Append(manual), Profile));
        RebuildGroups(d);
        Status = $"\"{text}\" marcado como {EntityTypeInfo.Label(type)} em todo o documento.";
    }

    public void AddSelectionToProfile(string text, EntityType type)
    {
        text = text.Trim();
        if (text.Length < 2) return;
        Profile ??= new CompanyProfile { Name = "Perfil" };
        if (ProfileTerms.All(t => !TextFold.Key(t.Text).Equals(TextFold.Key(text), StringComparison.Ordinal)))
            ProfileTerms.Add(new ProfileTermItem { Text = text, Type = type });
        SaveProfile();
        if (SelectedDocument is { } d)
        {
            var start = d.Doc.Text.IndexOf(text, StringComparison.Ordinal);
            if (start >= 0) MarkSelection(start, text.Length, type);
        }
    }

    public void NeverAnonymize(string text)
    {
        text = text.Trim();
        if (text.Length < 2) return;
        Profile ??= new CompanyProfile { Name = "Perfil" };
        var lines = Lines(ProfileExceptions);
        if (!lines.Contains(text)) lines.Add(text);
        ProfileExceptions = string.Join(Environment.NewLine, lines);
        SaveProfile();
        if (SelectedDocument is { } d)
        {
            string k = TextFold.Key(text);
            foreach (var s in d.Spans.Where(s => TextFold.Key(s.Text) == k)) s.Include = false;
            RebuildGroups(d);
        }
        Status = $"\"{text}\" não será mais anonimizado (exceção do perfil).";
    }

    // ---- export ---------------------------------------------------------------------------

    [ObservableProperty]
    private string exportText = "";

    public ObservableCollection<LeakItem> Leaks { get; } = [];

    public ObservableCollection<RedactedImage> ExportImages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private bool leaksAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopy))]
    private bool exportReady;

    public bool CanCopy => ExportReady && (Leaks.Count == 0 || LeaksAcknowledged);

    public string ExportSummary { get; private set; } = "";

    private bool CanExport() => !IsBusy && SelectedDocument is not null && Session is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task Export() => RunBusy("Anonimizando e verificando vazamentos...", async ct =>
    {
        var d = SelectedDocument!;
        var engine = EnsureEngine();
        var pkg = await Task.Run(() => engine.ExportAsync(d.Doc, d.Spans, Session!, Profile, images: true, ct), ct);
        d.Package = pkg;
        ExportText = pkg.Anonymized.Text;
        Leaks.Clear();
        foreach (var l in pkg.TextLeaks) Leaks.Add(new LeakItem("texto", l.Text, l.Reason));
        foreach (var l in pkg.VisualLeaks) Leaks.Add(new LeakItem(l.Source, l.Text, "valor original ainda legível na imagem"));
        foreach (var s in pkg.SkippedPages) Leaks.Add(new LeakItem("imagem", "", "não exportada: " + s));
        ExportImages.Clear();
        foreach (var p in pkg.Pages) ExportImages.Add(new RedactedImage(p.Page.Source, p.Png));
        LeaksAcknowledged = false;
        ExportReady = true;
        OnPropertyChanged(nameof(CanCopy));
        SaveSession();
        int markers = pkg.Anonymized.Replacements.Count(r => r.Action == SpanAction.Pseudonymize);
        int scaled = pkg.Anonymized.Replacements.Count(r => r.Action == SpanAction.Scale);
        ExportSummary = $"{markers} marcadores, {scaled} números escalados, {pkg.Pages.Count} imagens.";
        OnPropertyChanged(nameof(ExportSummary));
        d.Status = "exportado";
        Status = pkg.HasLeaks || pkg.SkippedPages.Count > 0
            ? $"{ExportSummary} A checagem encontrou {Leaks.Count} ponto(s) para revisar antes de enviar."
            : $"{ExportSummary} A checagem de vazamento não encontrou problemas.";
        SelectedTab = 2;
    });

    public async Task SavePackageAsync(string folder)
    {
        if (SelectedDocument?.Package is not { } pkg) return;
        var written = await Engine.SaveAsync(pkg, SelectedDocument.Doc, folder);
        Status = $"{written.Count} arquivos gravados em {folder}.";
    }

    // ---- reidentification ----------------------------------------------------------------

    [ObservableProperty]
    private string answerText = "";

    [ObservableProperty]
    private string restoredText = "";

    public ObservableCollection<RestoredView> RestoredItems { get; } = [];

    [ObservableProperty]
    private string restoreSummary = "";

    [RelayCommand]
    private void Restore()
    {
        if (Session is null)
        {
            Status = "Abra a sessão em que o documento foi anonimizado.";
            return;
        }
        var r = Reidentifier.Restore(AnswerText, Session.Registry, Session.Factors);
        RestoredText = r.Text;
        RestoredItems.Clear();
        foreach (var i in r.Items)
            RestoredItems.Add(new RestoredView(i.Found, i.Restored, i.Kind switch
            {
                RestoreKind.Marker => "marcador",
                RestoreKind.Quantity => "número convertido",
                _ => "MARCADOR DESCONHECIDO",
            }));
        RestoreSummary = $"{r.Items.Count(i => i.Kind == RestoreKind.Marker)} marcadores restaurados, {r.Items.Count(i => i.Kind == RestoreKind.Quantity)} números convertidos de volta"
                         + (r.UnknownMarkers > 0 ? $", {r.UnknownMarkers} marcadores que não existem nesta sessão." : ".");
        Status = RestoreSummary;
    }

    // ---- settings -------------------------------------------------------------------------

    public ObservableCollection<string> OllamaModels { get; } = [];

    [RelayCommand]
    private Task TestOllama() => RunBusy("Conectando ao Ollama local...", async ct =>
    {
        using var client = new OllamaClient(Settings.OllamaAddress);
        if (!await client.IsAvailableAsync(ct))
        {
            Status = "O Ollama não respondeu. Verifique se ele está rodando neste computador.";
            return;
        }
        OllamaModels.Clear();
        foreach (var m in await client.ListModelsAsync(ct)) OllamaModels.Add(m);
        Status = $"Ollama respondeu com {OllamaModels.Count} modelos.";
    });

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            OllamaClient.ValidateAddress(Settings.OllamaAddress);
        }
        catch (LoopbackOnlyException ex)
        {
            Status = ex.Message;
            return;
        }
        Settings.Save(_root);
        _engine?.Dispose();
        _engine = null;
        OnPropertyChanged(nameof(UsesPassword));
        Status = "Configurações salvas. Os modelos serão recarregados no próximo documento.";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _engine?.Dispose();
    }
}

public sealed class RedactedImage(string source, byte[] png)
{
    public string Source { get; } = source;
    public byte[] Png { get; } = png;
    public Avalonia.Media.Imaging.Bitmap Bitmap { get; } = new(new MemoryStream(png));
}
