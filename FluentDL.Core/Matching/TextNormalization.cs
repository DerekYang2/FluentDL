#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentDL.Core.Matching;

/// <summary>
/// String clean-up used to compare titles and artist names across music services.
/// Moved unchanged from ApiHelper; the characterization baselines pin the output, including the known bugs.
/// </summary>
public static class TextNormalization
{
    /// <summary>Removes bracketed featuring credits such as " (feat. X)", " (ft. X)", " (featuring X)" and " (with X)".</summary>
    public static string PruneFeaturing(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Pattern: Matches a space, then brackets containing feat/ft/featuring, any text, and closing bracket.
        // The \s* at the start ensures we don't leave a trailing space behind.
        string pattern = @"\s*\((?:feat|ft|featuring|with)\.?\s+.*?\)";

        return Regex.Replace(title, pattern, string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled).Trim();
    }

    /// <summary>Removes featuring credits, then keeps only ASCII letters and digits. Non-Latin text becomes empty (bug 13).</summary>
    public static string PrunePunctuation(string str)
    {
        return Regex.Replace(PruneFeaturing(str), @"[^a-zA-Z0-9]", string.Empty);
    }

    /// <summary>Replaces accented letters with their base letter, for example "é" with "e".</summary>
    public static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    /// <summary>Removes accents, then drops every remaining non-ASCII character.</summary>
    public static string EnforceAscii(string text)
    {
        var result = RemoveDiacritics(text);
        result = Regex.Replace(result, @"[^\u0000-\u007F]+", string.Empty);
        return result;
    }
}
