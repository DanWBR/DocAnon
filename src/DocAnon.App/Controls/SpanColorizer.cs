using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using DocAnon.Core.Model;

namespace DocAnon.App.Controls;

/// <summary>Paints detected spans in the editor: strong colors for what will change, a faint underline for what stays.</summary>
public sealed class SpanColorizer : DocumentColorizingTransformer
{
    public IReadOnlyList<(int Start, int Length, EntityType Type, bool Active, bool Scaled)> Spans { get; set; } = [];

    private static readonly Dictionary<string, IBrush> Brushes = new()
    {
        ["pessoal"] = new SolidColorBrush(Color.FromArgb(110, 230, 80, 80)),
        ["empresa"] = new SolidColorBrush(Color.FromArgb(110, 80, 120, 230)),
        ["local"] = new SolidColorBrush(Color.FromArgb(110, 60, 170, 110)),
        ["tag"] = new SolidColorBrush(Color.FromArgb(110, 170, 110, 220)),
        ["numero"] = new SolidColorBrush(Color.FromArgb(120, 240, 190, 40)),
        ["outro"] = new SolidColorBrush(Color.FromArgb(110, 230, 140, 60)),
    };

    private static readonly IBrush Faint = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));

    public static IBrush BrushFor(EntityType t, bool scaled) => Brushes[Category(t, scaled)];

    private static string Category(EntityType t, bool scaled) => t switch
    {
        _ when scaled => "numero",
        EntityType.QUANTIDADE or EntityType.VALOR => "numero",
        EntityType.EMPRESA or EntityType.CLIENTE or EntityType.FORNECEDOR or EntityType.PROJETO or EntityType.CONTRATO or EntityType.PRODUTO or EntityType.CATALISADOR => "empresa",
        EntityType.LOCAL or EntityType.PLANTA or EntityType.UNIDADE or EntityType.ENDERECO or EntityType.CEP => "local",
        EntityType.TAG_EQUIP or EntityType.TAG_INSTR => "tag",
        _ when EntityTypeInfo.IsPersonal(t) => "pessoal",
        _ => "outro",
    };

    protected override void ColorizeLine(DocumentLine line)
    {
        int ls = line.Offset, le = line.EndOffset;
        foreach (var (start, length, type, active, scaled) in Spans)
        {
            int a = Math.Max(start, ls), b = Math.Min(start + length, le);
            if (a >= b) continue;
            var brush = active ? BrushFor(type, scaled) : Faint;
            ChangeLinePart(a, b, el => el.TextRunProperties.SetBackgroundBrush(brush));
        }
    }
}
