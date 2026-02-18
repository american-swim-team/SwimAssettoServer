namespace SwimGatePlugin;

public static class TextNormalizer
{
    private static readonly Dictionary<char, char> LeetMap = new()
    {
        { '@', 'a' },
        { '4', 'a' },
        { '3', 'e' },
        { '1', 'i' },
        { '!', 'i' },
        { '0', 'o' },
        { '$', 's' },
        { '5', 's' },
        { '7', 't' },
        { '8', 'b' },
    };

    private static readonly HashSet<char> Separators = ['.', '-', '_', ' ', '\u200B', '\u200C', '\u200D', '\uFEFF'];

    public static (string Normalized, int[] IndexMap) Normalize(string input)
    {
        var normalized = new List<char>(input.Length);
        var indexMap = new List<int>(input.Length);

        // Step 1+2: Lowercase + leetspeak substitution
        for (int i = 0; i < input.Length; i++)
        {
            char c = char.ToLowerInvariant(input[i]);

            if (LeetMap.TryGetValue(c, out char mapped))
                c = mapped;

            normalized.Add(c);
            indexMap.Add(i);
        }

        // Step 3: Strip separators between letters
        var stripped = new List<char>(normalized.Count);
        var strippedMap = new List<int>(normalized.Count);

        for (int i = 0; i < normalized.Count; i++)
        {
            if (Separators.Contains(normalized[i]))
            {
                // Only strip if between letters
                bool prevLetter = i > 0 && char.IsLetter(normalized[i - 1]);
                bool nextLetter = i < normalized.Count - 1 && char.IsLetter(normalized[i + 1]);
                if (prevLetter && nextLetter)
                    continue;
            }

            stripped.Add(normalized[i]);
            strippedMap.Add(indexMap[i]);
        }

        // Step 4: Collapse repeats (3+ of same char → 1)
        var collapsed = new List<char>(stripped.Count);
        var collapsedMap = new List<int>(stripped.Count);

        for (int i = 0; i < stripped.Count; i++)
        {
            if (i >= 2 && stripped[i] == stripped[i - 1] && stripped[i] == stripped[i - 2])
                continue;

            collapsed.Add(stripped[i]);
            collapsedMap.Add(strippedMap[i]);
        }

        return (new string(collapsed.ToArray()), collapsedMap.ToArray());
    }
}
