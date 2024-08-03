using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDL.Models;

namespace FluentDL.Helpers;

internal class ApiHelper
{
    public static string GetUrl(SongSearchObject song)
    {
        var id = song.Id;
        // Get the url of the current object
        return song.Source switch
        {
            "deezer" => "https://www.deezer.com/track/" + id,
            "youtube" => "https://www.youtube.com/watch?v=" + id,
            "spotify" => "https://open.spotify.com/track/" + id,
            "qobuz" => "https://open.qobuz.com/track/" + id,
            "local" => id,
            _ => string.Empty
        };
    }


    public static int CalcLevenshteinDistance(string a, string b)
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

    public static async Task<Uri> GetRedirectedUrlAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, }, true);
        using var response = await client.GetAsync(uri, cancellationToken);

        return new Uri(response.Headers.GetValues("Location").First());
    }
}