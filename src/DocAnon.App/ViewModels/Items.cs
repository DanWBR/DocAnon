using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Services;

namespace DocAnon.App.ViewModels;

/// <summary>All occurrences of one value in a document, toggled together in the review list.</summary>
public sealed partial class EntityGroup : ObservableObject
{
    public EntityGroup(EntityType type, string text, List<DetectedSpan> spans)
    {
        Type = type;
        Text = text;
        Spans = spans;
        include = spans.Any(s => s.Include);
        var first = spans[0];
        IsQuantity = first.Quantity is not null;
        actionIndex = first.Action switch
        {
            SpanAction.Scale => 0,
            SpanAction.Keep => 1,
            _ => 2,
        };
    }

    public EntityType Type { get; }
    public string Text { get; }
    public List<DetectedSpan> Spans { get; }
    public string TypeLabel => EntityTypeInfo.Label(Type);
    public int Count => Spans.Count;
    public bool IsQuantity { get; }
    public string Detectors => string.Join(", ", Spans.Select(s => s.Detector).Distinct());
    public string Note => Spans.Select(s => s.Note).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "";

    /// <summary>Called after any change, so the window can refresh highlights and the preview.</summary>
    public Action? Changed { get; set; }

    [ObservableProperty]
    private bool include;

    /// <summary>For quantities: 0 scale, 1 keep, 2 marker.</summary>
    [ObservableProperty]
    private int actionIndex;

    [ObservableProperty]
    private string preview = "";

    partial void OnIncludeChanged(bool value)
    {
        foreach (var s in Spans) s.Include = value;
        Changed?.Invoke();
    }

    partial void OnActionIndexChanged(int value)
    {
        var action = value switch
        {
            0 => SpanAction.Scale,
            1 => SpanAction.Keep,
            _ => SpanAction.Pseudonymize,
        };
        foreach (var s in Spans)
        {
            // A quantity without a scalable unit cannot be scaled; it is kept instead.
            s.Action = action == SpanAction.Scale && s.Quantity is not { Dimension.IsScalable: true } ? SpanAction.Keep : action;
        }
        Changed?.Invoke();
    }
}

public sealed partial class PageItem(VisualPage page) : ObservableObject
{
    public VisualPage Page { get; } = page;
    public string Label => Page.Source + (Page.TextVerified ? "" : " (texto não verificado)");

    [ObservableProperty]
    private bool export = page.Export;

    partial void OnExportChanged(bool value) => Page.Export = value;
}

public sealed partial class DocumentItem : ObservableObject
{
    public DocumentItem(ExtractedDocument doc, string? path)
    {
        Doc = doc;
        Path = path;
        foreach (var p in doc.VisualPages) Pages.Add(new PageItem(p));
    }

    public ExtractedDocument Doc { get; }
    public string? Path { get; }
    public string Name => Doc.FileName;
    public List<DetectedSpan> Spans { get; set; } = [];
    public ObservableCollection<EntityGroup> Groups { get; } = [];
    public ObservableCollection<PageItem> Pages { get; } = [];
    public ExportPackage? Package { get; set; }
    public string Warnings => string.Join("\n", Doc.Warnings);
    public bool HasWarnings => Doc.Warnings.Count > 0;

    [ObservableProperty]
    private string status = "extraído";
}

public sealed partial class ProfileTermItem : ObservableObject
{
    [ObservableProperty]
    private string text = "";

    [ObservableProperty]
    private EntityType type = EntityType.EMPRESA;

    [ObservableProperty]
    private string aliases = "";

    public static ProfileTermItem From(ProfileTerm t) => new() { Text = t.Text, Type = t.Type, Aliases = string.Join("; ", t.Aliases) };

    public ProfileTerm ToTerm() => new()
    {
        Text = Text.Trim(),
        Type = Type,
        Aliases = Aliases.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
    };
}

public sealed record LeakItem(string Where, string Text, string Reason);

public sealed record RestoredView(string Found, string Restored, string Kind);

/// <summary>A file of the AI restored on disk: its name, where the restored copy went and what changed.</summary>
public sealed record RestoredFileView(string Name, string Output, string Detail);
