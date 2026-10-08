#nullable enable
namespace FluentDL.Core.Matching;

/// <summary>
/// Loose comparisons used to decide whether two titles or artist names refer to the same thing.
/// Moved unchanged from ApiHelper.
/// </summary>
public static class FuzzyMatch
{
    private static readonly string[] VersionKeywords = { "remix", "edit", "live", "version", "remaster", "instrumental", "acoustic" };

    /// <summary>
    /// True when one string contains the other after <see cref="TextNormalization.PrunePunctuation"/> and lowercasing.
    /// Always false when either side is empty after pruning, so two non-Latin names never match here.
    /// </summary>
    public static bool IsSubstring(string str, string str2)
    {
        str = TextNormalization.PrunePunctuation(str).ToLower();
        str2 = TextNormalization.PrunePunctuation(str2).ToLower();
        if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(str2))
        {
            return false;
        }

        return str.Contains(str2) || str2.Contains(str);
    }

    /// <summary>
    /// <see cref="IsSubstring"/>, but false when exactly one side mentions a version keyword such as "remix" or "live",
    /// so "Levels" does not match "Levels (Skrillex Remix)".
    /// </summary>
    public static bool CloseMatch(string str1, string str2)
    {
        str1 = TextNormalization.PruneFeaturing(str1).ToLower();
        str2 = TextNormalization.PruneFeaturing(str2).ToLower();
        if (VersionKeywords.Any(s => str1.Contains(s) ^ str2.Contains(s)))
        {
            return false;
        }
        return IsSubstring(str1.ToLower(), str2.ToLower());
    }

    /// <summary>Edit distance between two strings. An empty or null string counts as length 0.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (string.IsNullOrEmpty(a))
        {
            return b.Length;
        }

        if (string.IsNullOrEmpty(b))
        {
            return a.Length;
        }

        int lengthA = a.Length;
        int lengthB = b.Length;
        var distances = new int[lengthA + 1, lengthB + 1];

        for (int i = 0; i <= lengthA; distances[i, 0] = i++) ;
        for (int j = 0; j <= lengthB; distances[0, j] = j++) ;

        for (int i = 1; i <= lengthA; i++)
        {
            for (int j = 1; j <= lengthB; j++)
            {
                int cost = b[j - 1] == a[i - 1] ? 0 : 1;

                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost
                );
            }
        }

        return distances[lengthA, lengthB];
    }
}
