using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Vault;
using DocAnon.Services;

Console.OutputEncoding = Encoding.UTF8;

var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

var fileArg = new Argument<string[]>("arquivos") { Description = "Documentos de entrada.", Arity = ArgumentArity.OneOrMore };
var noNer = new Option<bool>("--sem-ner") { Description = "Não usa o modelo de NER." };
var profileOpt = new Option<string?>("--perfil") { Description = "Perfil da empresa em JSON (texto puro) ou arquivo .docprofile cifrado." };
var jsonOpt = new Option<bool>("--json") { Description = "Saída em JSON." };
var sessionOpt = new Option<string>("--sessao") { Description = "Rótulo da sessão (cria se não existir) ou caminho de um arquivo .docanon.", Required = true };
var passwordOpt = new Option<string?>("--senha") { Description = "Senha do cofre. Sem senha, o cofre usa a proteção do usuário do Windows. Também lida de DOCANON_SENHA." };
var outOpt = new Option<string>("--saida") { Description = "Pasta de saída.", DefaultValueFactory = _ => "anonimizado" };
var noImages = new Option<bool>("--sem-imagens") { Description = "Exporta só o texto." };
var diagramsOpt = new Option<string>("--diagramas") { Description = "auto, todas ou nunca: quais páginas de PDF tratar como diagrama.", DefaultValueFactory = _ => "auto" };
var inOpt = new Option<string?>("--entrada") { Description = "Arquivo com a resposta da IA. Sem ele, lê a entrada padrão." };
var outFileOpt = new Option<string?>("--arquivo-saida") { Description = "Arquivo para gravar o texto reidentificado." };
var llmOpt = new Option<string?>("--llm") { Description = "Modelo do Ollama local para sugerir entidades e auditar o texto (por exemplo gemma4:latest)." };
var llmVisionOpt = new Option<string?>("--llm-visao") { Description = "Modelo de visão do Ollama local para conferir as imagens tarjadas." };
var acceptLlmOpt = new Option<bool>("--aceitar-llm") { Description = "Aplica as sugestões do LLM sem revisão (na interface elas começam desmarcadas)." };
var rootOpt = new Option<string?>("--raiz") { Description = "Pasta dos cofres (padrão: %LOCALAPPDATA%\\DocAnon)." };

VaultKey Key(ParseResult r)
{
    string? pw = r.GetValue(passwordOpt) ?? Environment.GetEnvironmentVariable("DOCANON_SENHA");
    if (!string.IsNullOrEmpty(pw)) return new VaultKey.Password(pw);
    if (OperatingSystem.IsWindows()) return new VaultKey.WindowsUser();
    throw new InvalidOperationException("Informe --senha fora do Windows.");
}

CompanyProfile? LoadProfile(ParseResult r)
{
    string? p = r.GetValue(profileOpt);
    if (p is null) return null;
    if (p.EndsWith(".docprofile", StringComparison.OrdinalIgnoreCase)) return new ProfileStore(r.GetValue(rootOpt)).Load(p, Key(r));
    return JsonSerializer.Deserialize<CompanyProfile>(File.ReadAllText(p), json);
}

(Session Session, string? Path) OpenSession(ParseResult r, bool create)
{
    var store = new SessionStore(r.GetValue(rootOpt));
    string s = r.GetValue(sessionOpt)!;
    if (File.Exists(s)) return (store.Load(s, Key(r)), s);
    var found = store.List().FirstOrDefault(i => i.Label.Equals(EncryptedStore.Slug(s), StringComparison.OrdinalIgnoreCase));
    if (found is not null) return (store.Load(found.Path, Key(r)), found.Path);
    if (!create) throw new FileNotFoundException($"Sessão \"{s}\" não encontrada.");
    return (Session.CreateNew(s), null);
}

async Task<Engine> MakeEngineAsync(ParseResult r)
{
    var e = MakeEngine(r);
    if (r.GetValue(llmOpt) is not null || r.GetValue(llmVisionOpt) is not null)
        await e.EnableLlmAsync(new DocAnon.Llm.LlmOptions { TextModel = r.GetValue(llmOpt) ?? "", VisionModel = r.GetValue(llmVisionOpt), AcceptSuggestions = r.GetValue(acceptLlmOpt) });
    return e;
}

Engine MakeEngine(ParseResult r) => new(new EngineOptions
{
    UseNer = !r.GetValue(noNer),
    Diagrams = r.GetValue(diagramsOpt) switch
    {
        "todas" => DocAnon.Extraction.DiagramDetection.AllPages,
        "nunca" => DocAnon.Extraction.DiagramDetection.Never,
        _ => DocAnon.Extraction.DiagramDetection.Auto,
    },
});

// docanon detect
var detect = new Command("detect", "Lista os dados sensíveis encontrados, sem alterar nada.") { fileArg, noNer, profileOpt, jsonOpt, diagramsOpt, passwordOpt, rootOpt, llmOpt, acceptLlmOpt };
detect.SetAction(async (r, ct) =>
{
    using var engine = await MakeEngineAsync(r);
    foreach (var w in engine.StartupWarnings) Console.Error.WriteLine("aviso: " + w);
    var profile = LoadProfile(r);
    foreach (var f in r.GetValue(fileArg)!)
    {
        var doc = await engine.ExtractAsync(f, ct);
        var spans = await engine.DetectAsync(doc, profile, ct: ct);
        if (r.GetValue(jsonOpt))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                file = doc.FileName,
                chars = doc.Text.Length,
                visualPages = doc.VisualPages.Count,
                warnings = doc.Warnings,
                spans = spans.Select(s => new { s.Start, s.Length, Type = s.Type.ToString(), s.Text, s.Detector, s.Score, s.Include, Action = s.Action.ToString(), s.Note }),
            }, json));
            continue;
        }
        Console.WriteLine($"== {doc.FileName}: {doc.Text.Length} caracteres, {doc.VisualPages.Count} páginas visuais, {spans.Count} trechos");
        foreach (var w in doc.Warnings) Console.WriteLine("  aviso: " + w);
        foreach (var g in spans.GroupBy(s => s.Type).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-16} {g.Count(),5}  ({g.Count(s => s.Include && s.Action != SpanAction.Keep)} a anonimizar)");
    }
    return 0;
});

// docanon anonymize
var anonymize = new Command("anonymize", "Anonimiza os documentos e grava texto, imagens tarjadas e a sessão cifrada.") { fileArg, sessionOpt, passwordOpt, profileOpt, outOpt, noNer, noImages, diagramsOpt, rootOpt, llmOpt, llmVisionOpt, acceptLlmOpt };
anonymize.SetAction(async (r, ct) =>
{
    using var engine = await MakeEngineAsync(r);
    foreach (var w in engine.StartupWarnings) Console.Error.WriteLine("aviso: " + w);
    var profile = LoadProfile(r);
    var (session, _) = OpenSession(r, create: true);
    session.ProfileId = profile?.Id;
    int exit = 0;
    foreach (var f in r.GetValue(fileArg)!)
    {
        var doc = await engine.ExtractAsync(f, ct);
        var spans = await engine.DetectAsync(doc, profile, ct: ct, session: session);
        var pkg = await engine.ExportAsync(doc, spans, session, profile, images: !r.GetValue(noImages), ct: ct);
        var written = await Engine.SaveAsync(pkg, doc, r.GetValue(outOpt)!);
        Console.WriteLine($"== {doc.FileName}");
        Console.WriteLine($"  {pkg.Anonymized.Replacements.Count(x => x.Action == SpanAction.Pseudonymize)} marcadores, {pkg.Anonymized.Replacements.Count(x => x.Action == SpanAction.Scale)} números escalados, {pkg.Pages.Count} imagens");
        foreach (var w in doc.Warnings) Console.WriteLine("  aviso: " + w);
        foreach (var s in pkg.SkippedPages) Console.WriteLine("  imagem não exportada: " + s);
        foreach (var p in pkg.Pages)
            foreach (var g in p.Page.AutoCovers.Where(c => c.Include).GroupBy(c => c.Reason))
                Console.WriteLine($"  {p.Page.Source}: {g.Count()} tarja(s) sem texto ({g.Key})");
        foreach (var g in pkg.TextLeaks.GroupBy(l => l.Reason)) Console.WriteLine($"  VAZAMENTO no texto ({g.Count()}x): {g.Key}");
        foreach (var g in pkg.VisualLeaks.GroupBy(l => l.Source)) Console.WriteLine($"  VAZAMENTO na imagem {g.Key}: ainda legível: {string.Join(", ", g.Select(l => $"\"{l.Text}\" em ({l.Box.X:0},{l.Box.Y:0})"))}");
        foreach (var w in written) Console.WriteLine("  -> " + w);
        if (pkg.HasLeaks) exit = 2;
    }
    string saved = new SessionStore(r.GetValue(rootOpt)).Save(session, Key(r));
    Console.WriteLine($"Sessão salva em {saved} ({session.Registry.Records.Count} entidades).");
    if (exit != 0) Console.WriteLine("A checagem de vazamento encontrou problemas. Revise antes de enviar.");
    return exit;
});

// docanon reidentify
var reidentify = new Command("reidentify", "Restaura os valores reais na resposta da IA.") { sessionOpt, passwordOpt, inOpt, outFileOpt, rootOpt };
reidentify.SetAction(async (r, ct) =>
{
    var (session, _) = OpenSession(r, create: false);
    string answer = r.GetValue(inOpt) is { } inPath ? await File.ReadAllTextAsync(inPath, ct) : await Console.In.ReadToEndAsync(ct);
    var result = Reidentifier.Restore(answer, session.Registry, session.Factors);
    if (r.GetValue(outFileOpt) is { } outPath) await File.WriteAllTextAsync(outPath, result.Text, ct);
    else Console.WriteLine(result.Text);
    Console.Error.WriteLine($"{result.Items.Count(i => i.Kind == RestoreKind.Marker)} marcadores restaurados, {result.Items.Count(i => i.Kind == RestoreKind.Quantity)} números convertidos, {result.UnknownMarkers} marcadores desconhecidos.");
    return result.UnknownMarkers > 0 ? 3 : 0;
});

// docanon sessions
var sessions = new Command("sessions", "Lista as sessões salvas.") { rootOpt };
sessions.SetAction(r =>
{
    foreach (var s in new SessionStore(r.GetValue(rootOpt)).List())
        Console.WriteLine($"{s.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {s.Label,-30} {(s.PasswordProtected ? "senha" : "usuário Windows")}  {s.Path}");
    return 0;
});

// docanon extract
var extract = new Command("extract", "Mostra o texto extraído (Markdown), sem detectar nada.") { fileArg, diagramsOpt };
extract.SetAction(async (r, ct) =>
{
    using var engine = new Engine(new EngineOptions { UseNer = false, Diagrams = MakeEngine(r).Options.Diagrams });
    foreach (var f in r.GetValue(fileArg)!)
    {
        var doc = await engine.ExtractAsync(f, ct);
        Console.WriteLine(doc.Text);
    }
    return 0;
});

// docanon render
var pageOpt = new Option<int>("--pagina") { Description = "Página (começa em 1).", DefaultValueFactory = _ => 1 };
var pngOpt = new Option<string>("--png") { Description = "Arquivo PNG de saída.", Required = true };
var render = new Command("render", "Desenha uma página de PDF em PNG, por exemplo para recortar o logotipo da empresa.") { fileArg, pageOpt, pngOpt };
render.SetAction(r =>
{
    var raster = new DocAnon.Ocr.PdfiumRasterizer();
    var (png, _, _) = raster.Render(File.ReadAllBytes(r.GetValue(fileArg)![0]), r.GetValue(pageOpt) - 1, 200);
    File.WriteAllBytes(r.GetValue(pngOpt)!, png);
    return 0;
});

// docanon words (diagnóstico)
var filterOpt = new Option<string>("--filtro") { Description = "Mostra só palavras que contêm este texto.", DefaultValueFactory = _ => "" };
var words = new Command("words", "Diagnóstico: palavras da camada de texto de um PDF com as caixas.") { fileArg, pageOpt, filterOpt };
words.Hidden = true;
words.SetAction(r =>
{
    using var pdf = UglyToad.PdfPig.PdfDocument.Open(r.GetValue(fileArg)![0]);
    var page = pdf.GetPage(r.GetValue(pageOpt));
    Console.WriteLine($"page {page.Width}x{page.Height} rot {page.Rotation.Value} crop {page.CropBox.Bounds}");
    foreach (var w in page.GetWords(UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor.NearestNeighbourWordExtractor.Instance))
        if (w.Text.Contains(r.GetValue(filterOpt)!))
            Console.WriteLine($"{w.Text} | {w.TextOrientation} | TL {w.BoundingBox.TopLeft} BR {w.BoundingBox.BottomRight} | L{w.BoundingBox.Left:0} B{w.BoundingBox.Bottom:0} R{w.BoundingBox.Right:0} T{w.BoundingBox.Top:0}");
    return 0;
});

// docanon inspect
var inspect = new Command("inspect", "Mostra as entidades e os fatores de uma sessão.") { sessionOpt, passwordOpt, rootOpt };
inspect.SetAction(r =>
{
    var (session, path) = OpenSession(r, create: false);
    Console.WriteLine(path);
    Console.WriteLine($"fatores: k_ext={session.KExt:0.####} k_moeda={session.KMoney:0.####}");
    Console.WriteLine($"documentos: {string.Join(", ", session.Documents)}");
    foreach (var e in session.Registry.Records.OrderBy(e => e.Type).ThenBy(e => e.Number))
        Console.WriteLine($"{e.Token,-22} {string.Join(" | ", e.Variants)}");
    Console.WriteLine($"{session.Registry.ScaledNumbers.Count} números escalados registrados.");
    return 0;
});

var root = new RootCommand("DocAnon: anonimiza documentos localmente antes de enviá-los a modelos de IA na nuvem.") { extract, detect, anonymize, reidentify, sessions, inspect, render, words };
return await root.Parse(args).InvokeAsync();
