using DocAnon.Core.Pseudonymization;
using DocAnon.Core.Quantities;
using DocAnon.Extraction.Native;

namespace DocAnon.Extraction;

/// <summary>What the restore did to one file.</summary>
public sealed record FileRestoreResult(string OutputPath, IReadOnlyList<RestoredItem> Items, IReadOnlyList<string> Warnings)
{
    public int Markers => Items.Count(i => i.Kind == RestoreKind.Marker);
    public int Quantities => Items.Count(i => i.Kind == RestoreKind.Quantity);
    public IReadOnlyList<string> UnknownMarkers => Items.Where(i => i.Kind == RestoreKind.UnknownMarker).Select(i => i.Found).Distinct().ToList();
}

/// <summary>
/// Restores the real values in a file that the AI wrote (a report, a deck, a spreadsheet, a PDF),
/// keeping its format: the markers become the real values and the scaled numbers go back to the
/// original scale.
/// </summary>
public static class FileReidentifier
{
    public static IReadOnlyCollection<string> Extensions { get; } =
        [".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".log", ".docx", ".docm", ".pptx", ".pptm", ".xlsx", ".xlsm", ".pdf"];

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// The name of the restored file in <paramref name="folder"/> (by default the folder of the
    /// input). Markers in the name are restored too, ".anon" is dropped and ".restaurado" is added.
    /// </summary>
    public static string OutputPathFor(string input, EntityRegistry registry, ScaleFactors factors, string? folder = null)
    {
        string stem = Path.GetFileNameWithoutExtension(input);
        stem = Reidentifier.Restore(stem, registry, factors, unscaleQuantities: false).Text;
        if (stem.EndsWith(".anon", StringComparison.OrdinalIgnoreCase)) stem = stem[..^5];
        foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        return Path.Combine(folder ?? Path.GetDirectoryName(Path.GetFullPath(input))!, stem + ".restaurado" + Path.GetExtension(input));
    }

    public static async Task<FileRestoreResult> RestoreAsync(string input, string output, EntityRegistry registry, ScaleFactors factors, CancellationToken ct = default)
    {
        byte[] bytes = await File.ReadAllBytesAsync(input, ct);
        var (result, items, warnings) = Restore(bytes, Path.GetExtension(input), registry, factors);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllBytesAsync(output, result, ct);
        return new FileRestoreResult(output, items, warnings);
    }

    public static (byte[] Bytes, IReadOnlyList<RestoredItem> Items, IReadOnlyList<string> Warnings) Restore(byte[] bytes, string extension, EntityRegistry registry, ScaleFactors factors)
    {
        string ext = extension.ToLowerInvariant();
        switch (ext)
        {
            case ".docx" or ".docm" or ".pptx" or ".pptm" or ".xlsx" or ".xlsm":
            {
                var (b, items) = OfficeCopies.Restore(bytes, registry, factors);
                return (b, items, []);
            }
            case ".pdf":
                return PdfRestorer.Restore(bytes, registry, factors);
            default:
            {
                if (!Extensions.Contains(ext)) throw new NotSupportedException($"O app não restaura arquivos {ext}.");
                var (b, items) = PlainCopies.Restore(bytes, ext, registry, factors);
                return (b, items, []);
            }
        }
    }
}
