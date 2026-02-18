namespace SwimGatePlugin;

public class ProfanityFilter
{
    private readonly List<string> _words;
    private readonly string _replacement;

    public ProfanityFilter(ProfanityFilterConfig config)
    {
        var allWords = new HashSet<string>(ProfanityWordList.Words, StringComparer.OrdinalIgnoreCase);
        foreach (var w in config.CustomWords)
        {
            if (!string.IsNullOrWhiteSpace(w))
                allWords.Add(w.ToLowerInvariant());
        }

        // Sort by length descending so longer matches take priority
        _words = allWords.OrderByDescending(w => w.Length).ToList();
        _replacement = config.Replacement;
    }

    public string Filter(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var (normalized, indexMap) = TextNormalizer.Normalize(input);

        // Track which original positions have been replaced
        var result = input.ToCharArray();
        var replaced = new bool[result.Length];

        foreach (var word in _words)
        {
            int searchStart = 0;
            while (searchStart <= normalized.Length - word.Length)
            {
                int pos = normalized.IndexOf(word, searchStart, StringComparison.Ordinal);
                if (pos < 0)
                    break;

                // Map normalized range back to original positions
                int origStart = indexMap[pos];
                int origEnd = indexMap[pos + word.Length - 1];

                // Check if any part of this range was already replaced
                bool alreadyReplaced = false;
                for (int i = origStart; i <= origEnd; i++)
                {
                    if (replaced[i])
                    {
                        alreadyReplaced = true;
                        break;
                    }
                }

                if (!alreadyReplaced)
                {
                    // Replace the span in the original with replacement chars
                    for (int i = origStart; i <= origEnd; i++)
                    {
                        replaced[i] = true;
                    }
                }

                searchStart = pos + 1;
            }
        }

        // Build result: replaced spans become _replacement, unreplaced chars kept
        var output = new List<char>(result.Length);
        bool inReplacement = false;

        for (int i = 0; i < result.Length; i++)
        {
            if (replaced[i])
            {
                if (!inReplacement)
                {
                    foreach (char c in _replacement)
                        output.Add(c);
                    inReplacement = true;
                }
            }
            else
            {
                inReplacement = false;
                output.Add(result[i]);
            }
        }

        return new string(output.ToArray());
    }
}
