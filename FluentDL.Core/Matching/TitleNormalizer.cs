#nullable enable
using System.Text.RegularExpressions;

namespace FluentDL.Core.Matching;

/// <summary>
/// The title normalizers used when matching a song on another service. They differ in small ways, and changing
/// which one a source uses changes its matches, so each keeps the exact behavior of the method it came from.
/// </summary>
public static class TitleNormalizer
{
    /// <summary>
    /// Lowercase, remove every bracketed featuring credit, remove spaces and common punctuation, keep ASCII only.
    /// "Get Lucky (feat. Pharrell Williams)" becomes "getlucky". Formerly DeezerApi.PruneTitle; Deezer and YouTube use it.
    /// </summary>
    public static string Compact(string title)
    {
        var titlePruned = title.ToLower().Trim();

        titlePruned = TextNormalization.PruneFeaturing(titlePruned);
        titlePruned = StripPunctuation(titlePruned);

        titlePruned = TextNormalization.EnforceAscii(titlePruned);
        return titlePruned.Trim();
    }

    /// <summary>
    /// The older variant of <see cref="Compact"/>: removes only the first "(feat.", "(ft." and "(with " group, found by
    /// index. A group with no closing bracket runs to the end of the title. Formerly ApiHelper.PruneTitle; Spotify
    /// uses it.
    /// </summary>
    public static string CompactLegacy(string title)
    {
        var titlePruned = title.ToLower().Trim();

        titlePruned = RemoveFirstGroup(titlePruned, "(feat.");
        titlePruned = RemoveFirstGroup(titlePruned, "(ft.");
        titlePruned = RemoveFirstGroup(titlePruned, "(with "); // the space keeps "(without" out

        titlePruned = StripPunctuation(titlePruned);

        titlePruned = TextNormalization.EnforceAscii(titlePruned);
        return titlePruned.Trim();
    }

    /// <summary>
    /// Cleans a title for use in a search query: lowercase, hyphens to spaces, first "(feat." and "(ft." groups and
    /// "radio edit" removed, repeated whitespace collapsed, ASCII only. A group with no closing bracket runs to the end
    /// of the title. Formerly DeezerApi.PruneTitleSearch.
    /// </summary>
    public static string ForSearchQuery(string title)
    {
        var titlePruned = title.ToLower().Trim();
        titlePruned = titlePruned.Replace("-", " ").Replace("\u2014", "").Replace("[", "").Replace("]", "");

        titlePruned = RemoveFirstGroup(titlePruned, "(feat.");
        titlePruned = RemoveFirstGroup(titlePruned, "(ft.");

        titlePruned = titlePruned.Replace("(radio edit)", "").Replace("radio edit", "");
        titlePruned = Regex.Replace(titlePruned, @"\s+", " ");
        return TextNormalization.EnforceAscii(titlePruned).Trim();
    }

    private static string StripPunctuation(string title)
    {
        return title.Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "").Replace("[", "").Replace("]", "").Replace("\u2014", "").Replace("'", "").Replace("\"", "");
    }

    // Removes from the first occurrence of `opening` through the next ")", or to the end when there is none.
    private static string RemoveFirstGroup(string title, string opening)
    {
        var index = title.IndexOf(opening);
        if (index == -1)
        {
            return title;
        }

        var closingIndex = title.IndexOf(")", index);
        return closingIndex == -1 ? title[..index] : title.Remove(index, closingIndex - index + 1);
    }
}
