using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocAnon.Core.Model;

namespace DocAnon.Extraction;

/// <summary>
/// DWSIM simulations (.dwxmz is a zip with one XML, .dwxml is the XML itself). The XML follows
/// what FlowsheetBase.LoadFromXML reads: GraphicObjects (tags and connectors), SimulationObjects
/// (stream conditions), Compounds and Settings. The reader writes a summary with the equipment
/// list, the connectivity and a stream table in engineering units. It does not need DWSIM.
/// </summary>
public sealed partial class DwsimReader : IDocumentReader
{
    public IReadOnlyCollection<string> Extensions { get; } = [".dwxmz", ".dwxml"];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Leaf elements whose text is shown to the user inside unit operations (names, tags, comments).</summary>
    private static readonly HashSet<string> TextLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Tag", "Description", "Comments", "Comment", "Annotation", "Notes", "Label", "Title", "Text", "ScenarioName", "NodeName", "SegmentName", "PipeName", "DisplayName",
    };

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-|^(MAT|EN|PIPE|NODE)-[0-9a-fA-F]{8}", RegexOptions.CultureInvariant)]
    private static partial Regex IdLikeRx();

    public Task<ExtractedDocument> ReadAsync(string path, ExtractionOptions o, CancellationToken ct)
    {
        XDocument xdoc = Load(path);
        var root = xdoc.Element("DWSIM_Simulation_Data") ?? throw new InvalidDataException("O arquivo não é uma simulação do DWSIM.");
        var b = new DocumentBuilder(Path.GetFileName(path), "dwsim");

        var settings = root.Element("Settings");
        b.Heading("Simulação DWSIM", 1, "simulação");
        var info = new List<string>();
        void AddInfo(string label, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) info.Add($"{label}: {StripRtf(v!)}");
        }
        AddInfo("Nome da simulação", settings?.Element("SimulationName")?.Value);
        AddInfo("Autor", settings?.Element("SimulationAuthor")?.Value);
        AddInfo("Comentários", settings?.Element("SimulationComments")?.Value);
        if (info.Count > 0) b.ListItems(info, "configurações");

        // Graphic objects: id -> (tag, type, description) and connections.
        var graphics = root.Element("GraphicObjects")?.Elements("GraphicObject").ToList() ?? [];
        var tagById = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeById = new Dictionary<string, string>(StringComparer.Ordinal);
        var annotations = new List<string>();
        foreach (var g in graphics)
        {
            string id = g.Element("Name")?.Value ?? "";
            string tag = g.Element("Tag")?.Value ?? id;
            string type = g.Element("ObjectType")?.Value ?? "?";
            if (type.StartsWith("GO_", StringComparison.Ordinal) || type is "GO_Text" or "GO_FloatingTable")
            {
                string? text = g.Element("Text")?.Value ?? g.Element("Description")?.Value;
                if (!string.IsNullOrWhiteSpace(text)) annotations.Add(StripRtf(text));
                continue;
            }
            if (id.Length == 0) continue;
            tagById[id] = tag;
            typeById[id] = type;
        }

        var equipment = graphics
            .Where(g => (g.Element("Name")?.Value ?? "") is { Length: > 0 } id && typeById.ContainsKey(id) && typeById[id] is not ("MaterialStream" or "EnergyStream"))
            .Select(g => $"{g.Element("Tag")?.Value} ({FriendlyType(g.Element("ObjectType")?.Value, g.Element("Type")?.Value)})" + Desc(g))
            .ToList();
        if (equipment.Count > 0)
        {
            b.Heading("Equipamentos", 2, "equipamentos");
            b.ListItems(equipment, "equipamentos");
        }

        // Connectivity: stream -> unit and unit -> stream, from the output connectors.
        var links = new List<string>();
        foreach (var g in graphics)
        {
            string id = g.Element("Name")?.Value ?? "";
            if (!tagById.TryGetValue(id, out var fromTag)) continue;
            foreach (var c in g.Element("OutputConnectors")?.Elements("Connector") ?? [])
            {
                if ((string?)c.Attribute("IsAttached") != "true") continue;
                string to = (string?)c.Attribute("AttachedToObjID") ?? "";
                if (tagById.TryGetValue(to, out var toTag)) links.Add($"{fromTag} → {toTag}");
            }
            var ec = g.Element("EnergyConnector")?.Element("Connector");
            if (ec is not null && (string?)ec.Attribute("IsAttached") == "true" && tagById.TryGetValue((string?)ec.Attribute("AttachedToObjID") ?? "", out var et))
                links.Add($"{fromTag} → {et} (energia)");
        }
        if (links.Count > 0)
        {
            b.Heading("Conectividade", 2, "conectividade");
            b.ListItems(links.Distinct(), "conectividade");
        }

        // Stream table, one column per material stream.
        var simObjects = root.Element("SimulationObjects")?.Elements("SimulationObject").ToList() ?? [];
        var streams = new List<(string Tag, Dictionary<string, double> Props)>();
        foreach (var so in simObjects)
        {
            string type = so.Element("Type")?.Value ?? "";
            if (!type.EndsWith("MaterialStream", StringComparison.Ordinal)) continue;
            string id = so.Element("Name")?.Value ?? "";
            var mix = so.Element("Phases")?.Elements("Phase").FirstOrDefault();
            var props = mix?.Elements("Properties").LastOrDefault();
            if (props is null) continue;
            var d = new Dictionary<string, double>();
            foreach (var p in props.Elements())
                if (double.TryParse(p.Value, NumberStyles.Float, Inv, out double v) && double.IsFinite(v)) d[p.Name.LocalName] = v;
            streams.Add((tagById.GetValueOrDefault(id, id), d));
        }
        if (streams.Count > 0)
        {
            var rows = new List<IReadOnlyList<string>>();
            var header = new List<string> { "Propriedade", "Unidade" };
            header.AddRange(streams.Select(s => s.Tag));
            rows.Add(header);
            void Row(string label, string unit, string key, Func<double, double> conv)
            {
                if (!streams.Any(s => s.Props.ContainsKey(key))) return;
                var r = new List<string> { label, unit };
                r.AddRange(streams.Select(s => s.Props.TryGetValue(key, out var v) ? Fmt(conv(v)) : ""));
                rows.Add(r);
            }
            Row("Temperatura", "°C", "temperature", v => v - 273.15);
            Row("Pressão", "kPa", "pressure", v => v / 1000);
            Row("Vazão mássica", "kg/h", "massflow", v => v * 3600);
            Row("Vazão molar", "kmol/h", "molarflow", v => v * 3.6);
            Row("Vazão volumétrica", "m³/h", "volumetric_flow", v => v * 3600);
            Row("Massa específica", "kg/m³", "density", v => v);
            Row("Massa molar", "kg/kmol", "molecularWeight", v => v);
            b.Heading("Tabela de correntes", 2, "correntes");
            // Wide tables are split so each chunk stays readable.
            const int perTable = 8;
            for (int start = 0; start < streams.Count; start += perTable)
            {
                int n = Math.Min(perTable, streams.Count - start);
                var chunk = rows.Select(r => (IReadOnlyList<string>)r.Take(2).Concat(r.Skip(2 + start).Take(n)).ToList()).ToList();
                b.Table(chunk, "correntes");
            }
        }

        var compounds = root.Element("Compounds")?.Elements().Select(c => c.Element("Name")?.Value ?? c.Element("ConstantProperties")?.Element("Name")?.Value).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList() ?? [];
        if (compounds.Count > 0)
        {
            b.Heading("Componentes", 2, "componentes");
            b.Paragraph(string.Join(", ", compounds!), "componentes");
        }

        // Names, tags and comments inside unit operations, such as the segments of a pipe network.
        foreach (var so in simObjects)
        {
            ct.ThrowIfCancellationRequested();
            string type = so.Element("Type")?.Value ?? "";
            if (type.EndsWith("MaterialStream", StringComparison.Ordinal)) continue;
            string id = so.Element("Name")?.Value ?? "";
            var texts = so.Descendants()
                .Where(e => !e.HasElements && TextLeafNames.Contains(e.Name.LocalName))
                .Select(e => StripRtf(e.Value).Trim())
                .Where(v => v.Length > 1 && !IdLikeRx().IsMatch(v) && !double.TryParse(v, NumberStyles.Float, Inv, out _) && v is not ("True" or "False"))
                .Distinct()
                .Take(500)
                .ToList();
            string? annotation = so.Element("Annotation")?.Value;
            if (texts.Count == 0 && string.IsNullOrWhiteSpace(annotation)) continue;
            b.Heading($"Textos de {tagById.GetValueOrDefault(id, id)}", 3, "objeto " + id);
            b.ListItems(texts, "objeto " + id);
        }

        if (annotations.Count > 0)
        {
            b.Heading("Anotações do fluxograma", 2, "anotações");
            b.ListItems(annotations, "anotações");
        }

        if (root.Element("Spreadsheet")?.Element("RGFData")?.Value is { Length: > 2 })
            b.Warnings.Add("A planilha interna da simulação não foi incluída no resumo.");
        if (root.Element("ScriptItems")?.HasElements == true)
            b.Warnings.Add("Os scripts da simulação não foram incluídos no resumo.");

        return Task.FromResult(b.Build());
    }

    private static XDocument Load(string path)
    {
        if (path.EndsWith(".dwxml", StringComparison.OrdinalIgnoreCase)) return XDocument.Load(path);
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("O arquivo .dwxmz não contém XML.");
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static string Desc(XElement g)
    {
        string? d = g.Element("Description")?.Value;
        return string.IsNullOrWhiteSpace(d) ? "" : ": " + StripRtf(d);
    }

    private static string FriendlyType(string? objectType, string? cls)
    {
        if (objectType is "External" && cls is not null)
        {
            string n = cls.Split('.').Last().Replace("Graphic", "");
            return n.Replace("ExternalUnitOperation", "operação externa");
        }
        return objectType ?? "?";
    }

    private static string Fmt(double v) => Math.Abs(v) >= 1000 ? v.ToString("0.#", Inv) : Math.Abs(v) >= 1 ? v.ToString("0.###", Inv) : v.ToString("0.#####", Inv);

    /// <summary>Removes RTF control words, which DWSIM uses for annotations.</summary>
    internal static string StripRtf(string s)
    {
        if (!s.TrimStart().StartsWith(@"{\rtf", StringComparison.Ordinal)) return s.Trim();
        string t = Regex.Replace(s, @"\{\\\*[^{}]*\}|\\'[0-9a-f]{2}|\\[a-z]+-?\d* ?|[{}]", m => m.Value.StartsWith(@"\'") ? ((char)Convert.ToInt32(m.Value[2..], 16)).ToString() : "", RegexOptions.IgnoreCase);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
}
