namespace FluentDL.Services;

internal interface ILocalFormatConverter
{
    string? Convert(string path, string outputDirectory, int format, int quality);
}

internal sealed class LocalFormatConverter : ILocalFormatConverter
{
    // Keep the existing local format/quality choices. These encoders finish their current file on cancellation.
    public string? Convert(string path, string outputDirectory, int format, int quality) => format switch
    {
        0 => FFmpegRunner.CreateFlac(path, outputDirectory),
        1 => quality switch
        {
            0 => FFmpegRunner.CreateMp3(path, outputDirectory),
            1 => FFmpegRunner.CreateMp3(path, 128, outputDirectory),
            2 => FFmpegRunner.CreateMp3(path, 192, outputDirectory),
            3 => FFmpegRunner.CreateMp3(path, 256, outputDirectory),
            4 => FFmpegRunner.CreateMp3(path, 320, outputDirectory),
            _ => null,
        },
        2 => quality switch
        {
            0 => FFmpegRunner.CreateAac(path, 128, outputDirectory),
            1 => FFmpegRunner.CreateAac(path, 192, outputDirectory),
            2 => FFmpegRunner.CreateAac(path, 256, outputDirectory),
            3 => FFmpegRunner.CreateAac(path, 320, outputDirectory),
            _ => null,
        },
        3 => FFmpegRunner.CreateAlac(path, outputDirectory),
        4 => quality switch
        {
            0 => FFmpegRunner.CreateVorbisVBR(path, 5, outputDirectory),
            1 => FFmpegRunner.CreateVorbisVBR(path, 8, outputDirectory),
            2 => FFmpegRunner.CreateVorbisVBR(path, 10, outputDirectory),
            3 => FFmpegRunner.CreateVorbis(path, 128, outputDirectory),
            4 => FFmpegRunner.CreateVorbis(path, 192, outputDirectory),
            5 => FFmpegRunner.CreateVorbis(path, 256, outputDirectory),
            _ => null,
        },
        5 => quality switch
        {
            0 => FFmpegRunner.CreateOpus(path, 96, outputDirectory),
            1 => FFmpegRunner.CreateOpus(path, 128, outputDirectory),
            2 => FFmpegRunner.CreateOpus(path, 192, outputDirectory),
            _ => null,
        },
        _ => null,
    };
}
