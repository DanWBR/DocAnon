namespace DocAnon.Core.Model;

/// <summary>A piece of the document text that a detector flagged.</summary>
public sealed class DetectedSpan
{
    public DetectedSpan(int start, int length, EntityType type, string text, string detector, double score = 1.0)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        Start = start;
        Length = length;
        Type = type;
        Text = text;
        Detector = detector;
        Score = score;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;
    public EntityType Type { get; set; }
    public string Text { get; }

    /// <summary>Which detector produced the span: "regex", "ner", "perfil", "llm", "tag", "quantidade", "manual".</summary>
    public string Detector { get; }

    public double Score { get; set; }

    /// <summary>True when a check digit or checksum confirmed the value.</summary>
    public bool Validated { get; init; }

    /// <summary>Set for numbers with units.</summary>
    public QuantityInfo? Quantity { get; init; }

    /// <summary>Whether the anonymizer applies <see cref="Action"/> to this span. The review screen toggles it.</summary>
    public bool Include { get; set; } = true;

    public SpanAction Action { get; set; } = SpanAction.Pseudonymize;

    /// <summary>Short explanation shown in the review list, such as "dígito verificador ok".</summary>
    public string? Note { get; set; }

    public bool Overlaps(DetectedSpan other) => Start < other.End && other.Start < End;

    public bool Contains(int position) => position >= Start && position < End;

    public override string ToString() => $"{Type} [{Start},{End}) \"{Text}\" ({Detector}, {Score:0.00}{(Include ? "" : ", excluído")})";
}
