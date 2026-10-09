namespace FluentDL.Core.Sources;

/// <summary>The audio conversions the sources need after a download. The app's FFmpegRunner implements it.</summary>
public interface IAudioConverter
{
    bool IsInitialized { get; }

    /// <summary>Converts the file to FLAC in place.</summary>
    Task ConvertToFlacAsync(string initialPath, int samplingRate = 48000);

    /// <summary>Creates an MP3 next to the file, or in <paramref name="outputDirectory"/>, and returns its path.</summary>
    Task<string?> CreateMp3Async(string initialPath, string? outputDirectory = null);
}
