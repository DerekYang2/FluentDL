#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentDL.Core.Matching;

/// <summary>
/// String clean-up used to compare titles and artist names across music services.
/// Moved from ApiHelper. Letters and digits of every script are kept: dropping everything outside ASCII turned any
/// non-Latin title or artist into an empty string (bug 13).
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

    /// <summary>
    /// Removes featuring credits and accents, then keeps only letters and digits: "Don't Stop (feat. X)" becomes
    /// "DontStop", "Beyoncé" becomes "Beyonce", and "夜に駆ける" stays as it is.
    /// </summary>
    public static string PrunePunctuation(string str)
    {
        return Regex.Replace(RemoveDiacritics(PruneFeaturing(str)), @"[^\p{L}\p{Nd}]", string.Empty);
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

    /// <summary>
    /// Removes accents, then drops symbols outside ASCII, such as curly quotes and dashes. ASCII and the letters and
    /// digits of every script stay.
    /// </summary>
    public static string RemoveAccentsAndSymbols(string text)
    {
        var result = RemoveDiacritics(text);
        result = Regex.Replace(result, @"[^\u0000-\u007F\p{L}\p{Nd}]+", string.Empty);
        return result;
    }
}
