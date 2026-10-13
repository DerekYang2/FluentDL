using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FluentDL.Services.Tidal;

// The two formats used by the spike: a BTS URL list or a DASH initialization segment and numbered audio segments.
internal sealed record TidalDownloadManifest(bool Full, string Quality, string Extension, List<Uri> Parts)
{
    public static TidalDownloadManifest Parse(JsonElement response)
    {
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(response.GetProperty("manifest").GetString()!));
        var mime = response.GetProperty("manifestMimeType").GetString();
        var parts = new List<Uri>();
        string codec;
        if (mime == "application/vnd.tidal.bts")
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.GetProperty("encryptionType").GetString() != "NONE")
                throw new NotSupportedException("TIDAL returned encrypted audio.");
            codec = root.GetProperty("codecs").GetString()!;
            parts.AddRange(root.GetProperty("urls").EnumerateArray().Select(p => MediaUri(p.GetString()!)));
        }
        else if (mime == "application/dash+xml")
        {
            XNamespace ns = "urn:mpeg:dash:schema:mpd:2011";
            var xml = XDocument.Parse(text);
            if (xml.Descendants(ns + "ContentProtection").Any())
                throw new NotSupportedException("TIDAL returned encrypted audio.");
            var representations = xml.Descendants(ns + "Representation").ToList();
            if (representations.Count != 1) throw new NotSupportedException("Expected one TIDAL audio representation.");
            var rep = representations[0];
            codec = (string?)rep.Attribute("codecs") ?? "";
            var template = rep.Element(ns + "SegmentTemplate") ?? rep.Parent?.Element(ns + "SegmentTemplate")
                ?? throw new NotSupportedException("Missing TIDAL segment template.");
            var number = (int?)template.Attribute("startNumber") ?? 1;
            var media = (string?)template.Attribute("media") ?? throw new NotSupportedException("Missing segment URL.");
            parts.Add(MediaUri((string?)template.Attribute("initialization") ?? throw new NotSupportedException("Missing initialization URL.")));
            var timeline = template.Element(ns + "SegmentTimeline") ?? throw new NotSupportedException("Missing segment timeline.");
            foreach (var segment in timeline.Elements(ns + "S"))
            {
                var repeat = (int?)segment.Attribute("r") ?? 0;
                if (repeat < 0 || repeat > 10000 || parts.Count + repeat > 10000)
                    throw new NotSupportedException("Unsupported TIDAL segment count.");
                for (var i = 0; i <= repeat; i++)
                    parts.Add(MediaUri(media.Replace("$Number$", (number++).ToString(CultureInfo.InvariantCulture))));
            }
            if (parts.Count < 2) throw new NotSupportedException("TIDAL returned no audio segments.");
        }
        else throw new NotSupportedException("Unsupported TIDAL manifest format.");

        if (parts.Count == 0) throw new NotSupportedException("TIDAL returned no audio URLs.");
        var extension = codec == "flac" ? ".flac" : codec.StartsWith("mp4a", StringComparison.Ordinal) ? ".m4a"
            : throw new NotSupportedException("TIDAL returned an unsupported audio codec.");
        return new(response.GetProperty("assetPresentation").GetString() == "FULL",
            response.GetProperty("audioQuality").GetString() ?? "unknown", extension, parts);
    }

    private static Uri MediaUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? uri
            : throw new NotSupportedException("TIDAL returned an invalid media URL.");
}
