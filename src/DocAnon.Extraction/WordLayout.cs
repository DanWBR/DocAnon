namespace DocAnon.Extraction;

/// <summary>Groups placed words into lines, then into paragraphs or into labels.</summary>
public static class WordLayout
{
    /// <summary>Words on the same visual line, left to right.</summary>
    public static List<List<PlacedWord>> Lines(IReadOnlyList<PlacedWord> words)
    {
        var lines = new List<List<PlacedWord>>();
        // OCR already knows its lines.
        if (words.All(w => w.Line >= 0))
        {
            foreach (var g in words.GroupBy(w => w.Line).OrderBy(g => g.Key))
                lines.Add(g.OrderBy(w => w.Box.X).ToList());
            return lines;
        }

        var sorted = words.OrderBy(w => w.Box.Y + w.Box.Height / 2).ToList();
        var bands = new List<(float Top, float Bottom, List<PlacedWord> Words)>();
        foreach (var w in sorted)
        {
            float cy = w.Box.Y + w.Box.Height / 2;
            int idx = bands.FindIndex(b => cy >= b.Top && cy <= b.Bottom);
            if (idx < 0) bands.Add((w.Box.Y, w.Box.Bottom, [w]));
            else
            {
                var b = bands[idx];
                b.Words.Add(w);
                bands[idx] = (Math.Min(b.Top, w.Box.Y), Math.Max(b.Bottom, w.Box.Bottom), b.Words);
            }
        }
        foreach (var b in bands.OrderBy(b => b.Top)) lines.Add(b.Words.OrderBy(w => w.Box.X).ToList());
        return lines;
    }

    /// <summary>Lines separated by a vertical gap larger than 1.2 line heights start a new paragraph.</summary>
    public static List<List<List<PlacedWord>>> Paragraphs(IReadOnlyList<PlacedWord> words)
    {
        var lines = Lines(words);
        var result = new List<List<List<PlacedWord>>>();
        if (lines.Count == 0) return result;
        var heights = lines.Select(l => l.Max(w => w.Box.Height)).OrderBy(h => h).ToList();
        float median = heights[heights.Count / 2];
        var current = new List<List<PlacedWord>> { lines[0] };
        for (int i = 1; i < lines.Count; i++)
        {
            float prevBottom = lines[i - 1].Max(w => w.Box.Bottom);
            float top = lines[i].Min(w => w.Box.Y);
            if (top - prevBottom > 1.2f * median)
            {
                result.Add(current);
                current = [];
            }
            current.Add(lines[i]);
        }
        result.Add(current);
        return result;
    }

    /// <summary>
    /// Splits each line where the horizontal gap is larger than 1.5 times the text height, and
    /// stacks clusters that sit right below each other with the same left edge. Each result is
    /// one label of a diagram.
    /// </summary>
    public static List<List<List<PlacedWord>>> Labels(IReadOnlyList<PlacedWord> words)
    {
        var clusters = new List<List<PlacedWord>>();
        foreach (var line in Lines(words))
        {
            var current = new List<PlacedWord> { line[0] };
            for (int i = 1; i < line.Count; i++)
            {
                float gap = line[i].Box.X - line[i - 1].Box.Right;
                float h = Math.Max(line[i].Box.Height, line[i - 1].Box.Height);
                if (gap > 1.5f * h)
                {
                    clusters.Add(current);
                    current = [];
                }
                current.Add(line[i]);
            }
            clusters.Add(current);
        }

        var labels = new List<List<List<PlacedWord>>>();
        var used = new bool[clusters.Count];
        for (int i = 0; i < clusters.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var label = new List<List<PlacedWord>> { clusters[i] };
            var last = clusters[i];
            for (int j = i + 1; j < clusters.Count; j++)
            {
                if (used[j]) continue;
                float lastLeft = last.Min(w => w.Box.X), lastBottom = last.Max(w => w.Box.Bottom), h = last.Max(w => w.Box.Height);
                float left = clusters[j].Min(w => w.Box.X), top = clusters[j].Min(w => w.Box.Y);
                if (Math.Abs(left - lastLeft) < 2 * h && top - lastBottom >= -h / 2 && top - lastBottom < 0.8f * h)
                {
                    used[j] = true;
                    label.Add(clusters[j]);
                    last = clusters[j];
                }
            }
            labels.Add(label);
        }
        return labels;
    }
}
