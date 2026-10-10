namespace FluentDL.Core.Sources;

/// <summary>The audio conversions the sources need after a download. The app's FFmpegRunner implements it.</summary>
public interface IAudioConverter
{
    bool IsInitialized { get; }

    /// <summary>Converts the file to FLAC in place.</summary>
    Task ConvertToFlacAsync(string initialPath, int samplingRate = 48000);

    /// <summary>Converts <paramref name="inputPath"/> to FLAC at <paramref name="outputPath"/>.</summary>
    Task ConvertToFlacAsync(string inputPath, string outputPath);

    /// <summary>Converts an MP4 audio download to M4A and deletes the MP4.</summary>
    Task ConvertMp4ToM4aAsync(string inputPath, string outputPath);

    /// <summary>Remuxes a WebM audio download to opus and deletes the WebM.</summary>
    Task RemuxOpusAsync(string inputPath, string outputPath);

    /// <summary>Creates an MP3 next to the file, or in <paramref name="outputDirectory"/>, and returns its path.</summary>
    Task<string?> CreateMp3Async(string initialPath, string? outputDirectory = null);
}
