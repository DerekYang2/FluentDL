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
    /// Removes featuring credits and accents, writes Cyrillic in Latin, then keeps only letters and digits:
    /// "Don't Stop (feat. X)" becomes "DontStop", "Beyoncé" becomes "Beyonce", "Кино" becomes "Kino", and "夜に駆ける"
    /// stays as it is.
    /// </summary>
    public static string PrunePunctuation(string str)
    {
        return Regex.Replace(RemoveDiacritics(TransliterateCyrillic(PruneFeaturing(str))), @"[^\p{L}\p{Nd}]", string.Empty);
    }

    // Russian, Ukrainian and Serbian letters, in a simple scheme close to what the services use ("Кино" is "Kino" on
    // Spotify). Spellings still vary between services (Tsoi, Tsoy), so this only helps when they agree.
    private static readonly Dictionary<char, string> CyrillicLetters = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya", ['і'] = "i", ['ї'] = "yi", ['є'] = "ye", ['ґ'] = "g", ['ђ'] = "dj", ['ј'] = "j", ['љ'] = "lj",
        ['њ'] = "nj", ['ћ'] = "c", ['џ'] = "dz",
    };

    /// <summary>
    /// Writes Cyrillic letters in Latin, so a name one service lists as "Кино" and another as "Kino" compares equal.
    /// Other characters are unchanged. Used only to compare names, never to build a search.
    /// </summary>
    public static string TransliterateCyrillic(string text)
    {
        if (!text.Any(c => c is >= '\u0400' and <= '\u04FF'))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (CyrillicLetters.TryGetValue(char.ToLowerInvariant(c), out var latin))
            {
                builder.Append(char.IsUpper(c) && latin.Length > 0 ? char.ToUpperInvariant(latin[0]) + latin[1..] : latin);
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
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
