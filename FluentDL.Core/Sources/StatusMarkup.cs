#nullable enable
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FluentDL.Core.Sources;

public sealed record StatusTextPart(string Text, bool Bold = false, Uri? Link = null);

/// <summary>Formats the app's small status markup language. Interpolated values are always text.</summary>
public static class StatusMarkup
{
    private static readonly Regex Tags = new("<a\\s+href=(?:\"(?<href>[^\"]*)\"|'(?<href>[^']*)')>(?<value>.*?)</a>|<b>(?<bold>.*?)</b>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string Text(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string Format(FormattableString message) => string.Format(EscapedValues.Instance, message.Format, message.GetArguments());

    public static IReadOnlyList<StatusTextPart> Parse(string markup)
    {
        var parts = new List<StatusTextPart>();
        var previous = 0;
        foreach (Match match in Tags.Matches(markup))
        {
            if (match.Index > previous) parts.Add(new(WebUtility.HtmlDecode(markup[previous..match.Index])));
            if (match.Groups["bold"].Success)
                parts.Add(new(WebUtility.HtmlDecode(match.Groups["bold"].Value), Bold: true));
            else
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                var text = WebUtility.HtmlDecode(match.Groups["value"].Value);
                parts.Add(new(text.Length == 0 ? href : text, Link: TryLink(href)));
            }
            previous = match.Index + match.Length;
        }
        if (previous < markup.Length) parts.Add(new(WebUtility.HtmlDecode(markup[previous..])));
        return parts;
    }

    public static Uri? TryLink(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal) || href.IndexOfAny(['\0', '\r', '\n', '"']) >= 0 || !Uri.TryCreate(href, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0) return null;
        return uri.Scheme switch
        {
            "http" or "https" when uri.Host.Length > 0 => uri,
            "file" when uri.Query.Length == 0 && uri.Fragment.Length == 0 => uri,
            "fluentdl" when uri.Host == "settings" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.IsDefaultPort => uri,
            _ => null,
        };
    }

    private sealed class EscapedValues : IFormatProvider, ICustomFormatter
    {
        internal static readonly EscapedValues Instance = new();
        public object? GetFormat(Type? type) => type == typeof(ICustomFormatter) ? this : CultureInfo.CurrentCulture.GetFormat(type!);
        public string Format(string? format, object? value, IFormatProvider? provider) =>
            Text(value is IFormattable formattable ? formattable.ToString(format, CultureInfo.CurrentCulture) : value?.ToString());
    }
}
