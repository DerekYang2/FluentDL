namespace FluentDL.Core.Helpers;

/// <summary>The audio file extensions the app reads, and removing one from a path. Moved from LocalExplorerViewModel and ApiHelper.</summary>
public static class AudioFiles
{
    public static readonly HashSet<string> SupportedExtensions =
    [
        ".aac",
        ".mp4",
        ".m4a",
        ".m4b",
        ".caf",
        ".aax",
        ".aa",
        ".aif",
        ".aiff",
        ".aifc",
        ".dts",
        ".dsd",
        ".dsf",
        ".ac3",
        ".xm",
        ".flac",
        ".gym",
        ".it",
        ".mid",
        ".midi",
        ".ape",
        ".mp1",
        ".mp2",
        ".mp3",
        ".mpc",
        ".mod",
        ".ogg",
        ".oga",
        ".opus",
        ".ofr",
        ".ofs",
        ".psf",
        ".psf1",
        ".psf2",
        ".minipsf",
        ".minipsf1",
        ".minipsf2",
        ".ssf",
        ".minissf",
        ".dsf",
        ".minidsf",
        ".gsf",
        ".minigsf",
        ".qsf",
        ".miniqsf",
        ".s3m",
        ".spc",
        ".tak",
        ".tta",
        ".vqf",
        ".wav",
        ".bwav",
        ".bwf",
        ".vgm",
        ".vgz",
        ".wv",
        ".wma",
        ".asf",
        ".webm",
    ];

    /// <summary>Removes the extension when it is a supported audio extension; otherwise returns the name unchanged.</summary>
    public static string RemoveExtension(string filename)
    {
        var idx = filename.LastIndexOf('.');
        if (idx == -1) return filename;
        var ext = filename[idx..].ToLower();

        if (!SupportedExtensions.Contains(ext))
        {
            return filename; // Not a valid extension
        }

        return filename[..idx];
    }
}
