using FluentDL.Contracts.Services;
using FluentDL.Core.Sources;
using FluentDL.ViewModels;
using FluentDL.Views;

namespace FluentDL.Helpers;

public enum SettingsSection { Spotify, Tidal, Deezer, Qobuz, Downloads, Lyrics, Backups, FFmpeg, Ytdlp, Updates }

internal static class SettingsNavigation
{
    internal static SettingsSection? ForSource(string? source) => source switch
    {
        SourceIds.Spotify => SettingsSection.Spotify,
        SourceIds.Tidal => SettingsSection.Tidal,
        SourceIds.Deezer => SettingsSection.Deezer,
        SourceIds.Qobuz => SettingsSection.Qobuz,
        _ => null,
    };

    internal static string Path(SettingsSection section) => section switch
    {
        SettingsSection.Spotify => "Settings > Authentication > Spotify",
        SettingsSection.Tidal => "Settings > Authentication > TIDAL",
        SettingsSection.Deezer => "Settings > Authentication > Deezer",
        SettingsSection.Qobuz => "Settings > Authentication > Qobuz",
        SettingsSection.Lyrics => "Settings > Downloads > Lyrics",
        SettingsSection.Downloads => "Settings > Downloads > Location",
        SettingsSection.Backups => "Settings > General > Backups",
        SettingsSection.FFmpeg => "Settings > General > FFmpeg",
        SettingsSection.Ytdlp => "Settings > General > yt-dlp",
        SettingsSection.Updates => "Settings > About > FluentDL > App updates",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    internal static string Link(SettingsSection section) => $"<a href='fluentdl://settings/{section.ToString().ToLowerInvariant()}'>{Path(section)}</a>";

    internal static string WithLink(string message, SettingsSection section)
    {
        if (message.Contains(Link(section), StringComparison.Ordinal)) return message;
        var path = Path(section);
        return message.Contains(path, StringComparison.Ordinal)
            ? message.Replace(path, Link(section), StringComparison.Ordinal)
            : message.TrimEnd() + " " + Link(section);
    }

    internal static bool TryParse(Uri uri, out SettingsSection section)
    {
        section = default;
        if (!uri.IsAbsoluteUri || uri.Scheme != "fluentdl" || uri.Host != "settings" || uri.Query.Length != 0
            || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return false;
        return Enum.TryParse(uri.AbsolutePath.Trim('/'), ignoreCase: true, out section) && Enum.IsDefined(section)
            && string.Equals(uri.AbsolutePath, "/" + section.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    internal static void Open(SettingsSection section)
    {
        var navigation = App.GetService<INavigationService>();
        if (navigation.Frame?.Content is SettingsPage page) page.RevealSection(section);
        else navigation.NavigateTo(typeof(SettingsViewModel).FullName!, section);
    }

}
