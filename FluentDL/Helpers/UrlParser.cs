using System.Diagnostics;
using FluentDL.Core.Sources;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace FluentDL.Helpers;

internal static class UrlParser
{
    public static void ParseTextBlock(TextBlock textblock, string str, Action<SettingsSection>? onSettingsLink = null)
    {
        textblock.Inlines.Clear();
        foreach (var part in StatusMarkup.Parse(str))
        {
            if (part.Link is not { } uri)
            {
                textblock.Inlines.Add(new Run { Text = part.Text, FontWeight = part.Bold ? FontWeights.SemiBold : FontWeights.Normal });
                continue;
            }

            var link = new Hyperlink();
            link.Inlines.Add(new Run { Text = part.Text });
            if (SettingsNavigation.TryParse(uri, out var section))
                link.Click += (_, _) =>
                {
                    try
                    {
                        if (onSettingsLink != null) onSettingsLink(section);
                        else SettingsNavigation.Open(section);
                    }
                    catch (Exception error) { Serilog.Log.Warning(error, "Could not open the requested settings section"); }
                };
            else if (uri.IsFile)
                link.Click += (_, _) => OpenFile(uri.LocalPath);
            else if (uri.Scheme is "https" or "http")
                link.NavigateUri = uri;
            else
            {
                textblock.Inlines.Add(new Run { Text = part.Text });
                continue;
            }
            textblock.Inlines.Add(link);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            // Existence checks belong to the explicit click, never status rendering.
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            if (Directory.Exists(path)) start.ArgumentList.Add(path);
            else if (File.Exists(path))
            {
                start.ArgumentList.Add("/select,");
                start.ArgumentList.Add(path);
            }
            else return;
            Process.Start(start);
        }
        catch (Exception error) { Serilog.Log.Warning(error, "Could not open the selected status file link"); }
    }
}
