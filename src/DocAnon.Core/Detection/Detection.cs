using DocAnon.Core.Model;
using DocAnon.Core.Profiles;
using DocAnon.Core.Text;

namespace DocAnon.Core.Detection;

/// <summary>Everything a detector may read about the document.</summary>
public sealed class DetectionContext
{
    public DetectionContext(string text, CompanyProfile? profile = null, bool? commaIsDecimal = null)
    {
        Text = text;
        Folded = TextFold.FoldPreservingLength(text);
        Profile = profile ?? new CompanyProfile();
        Language = LanguageGuess.Language(text);
        CommaIsDecimal = commaIsDecimal ?? LanguageGuess.CommaIsDecimal(text);
    }

    public string Text { get; }

    /// <summary>Lowercase, accent-free copy of <see cref="Text"/> with the same offsets.</summary>
    public string Folded { get; }

    public CompanyProfile Profile { get; }
    public DocLanguage Language { get; }
    public bool CommaIsDecimal { get; }

    /// <summary>Folded text in the window just before <paramref name="start"/>.</summary>
    public ReadOnlySpan<char> Before(int start, int window = 40)
    {
        int s = Math.Max(0, start - window);
        return Folded.AsSpan(s, start - s);
    }

    public bool HasContextBefore(int start, IEnumerable<string> foldedKeywords, int window = 40)
    {
        var before = Before(start, window);
        foreach (string k in foldedKeywords)
        {
            // The keyword must start a word, so "ag " does not match inside "tag ".
            int from = 0;
            while (from < before.Length)
            {
                int i = before[from..].IndexOf(k.AsSpan(), StringComparison.Ordinal);
                if (i < 0) break;
                int at = from + i;
                if (at == 0 || !char.IsLetterOrDigit(before[at - 1])) return true;
                from = at + 1;
            }
        }
        return false;
    }
}

public interface IDetector
{
    string Id { get; }

    IEnumerable<DetectedSpan> Detect(DetectionContext context);
}

/// <summary>Detectors that need time (NER, LLM) implement this and report progress.</summary>
public interface IAsyncDetector
{
    string Id { get; }

    Task<IReadOnlyList<DetectedSpan>> DetectAsync(DetectionContext context, IProgress<double>? progress, CancellationToken ct);
}
