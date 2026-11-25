using DocAnon.Core.Model;

namespace DocAnon.Core.Profiles;

/// <summary>A term that must always be anonymized, with optional spelling variants.</summary>
public sealed class ProfileTerm
{
    public string Text { get; set; } = "";
    public EntityType Type { get; set; } = EntityType.PERSONALIZADO;
    public List<string> Aliases { get; set; } = [];
}

/// <summary>
/// A regular expression for values that follow a format instead of a fixed list, such as contract
/// numbers or the area code inside document numbers. The whole match becomes a span of the type.
/// </summary>
public sealed class ProfilePattern
{
    public string Pattern { get; set; } = "";
    public EntityType Type { get; set; } = EntityType.PERSONALIZADO;

    /// <summary>Short description shown in the review list.</summary>
    public string? Label { get; set; }
}

/// <summary>An image of the company logo, searched on every visual page.</summary>
public sealed class LogoTemplate
{
    public string Name { get; set; } = "logotipo";

    /// <summary>PNG bytes in base64. The file stays inside the encrypted profile.</summary>
    public string PngBase64 { get; set; } = "";

    /// <summary>Used by the CLI's plain JSON profiles: a PNG file to load instead of <see cref="PngBase64"/>.</summary>
    public string? Path { get; set; }

    public byte[] Bytes() => Path is { Length: > 0 } p && File.Exists(p) ? File.ReadAllBytes(p) : Convert.FromBase64String(PngBase64);
}

public enum PageOrientation
{
    Any,
    Landscape,
    Portrait,
}

/// <summary>A rectangle in page fractions (0 to 1), such as the corner of a title block that holds the logo.</summary>
public sealed class RelativeRegion
{
    public string Label { get; set; } = "região";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public PageOrientation Orientation { get; set; } = PageOrientation.Any;
}

/// <summary>
/// Reusable glossary of a company's confidential names. It is stored encrypted, because the
/// list itself is sensitive.
/// </summary>
public sealed class CompanyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Sem perfil";
    public List<ProfileTerm> Terms { get; set; } = [];

    /// <summary>Regular expressions for formatted values, such as contract and document numbers.</summary>
    public List<ProfilePattern> Patterns { get; set; } = [];

    /// <summary>Terms that must never be anonymized, such as compound names or standards (API 650).</summary>
    public List<string> Exceptions { get; set; } = [];

    /// <summary>
    /// Regular expressions for the unit or plant part of a tag. When a tag starts with a prefix that
    /// matches one of them, the prefix becomes an UNIDADE marker. The default treats numeric area
    /// codes such as "2100-" and "U-2100" as unit prefixes.
    /// </summary>
    public List<string> UnitPrefixPatterns { get; set; } = [];

    /// <summary>Whether all equipment and instrument tags are anonymized, not only their unit prefixes.</summary>
    public bool AnonymizeAllTags { get; set; }

    public List<LogoTemplate> Logos { get; set; } = [];

    public List<RelativeRegion> FixedRegions { get; set; } = [];

    /// <summary>Whether small images embedded in diagram pages (logos, stamps, signatures) are blacked out.</summary>
    public bool CoverSmallImages { get; set; } = true;
}
