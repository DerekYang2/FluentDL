using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libwebp;

namespace FluentDL.Helpers;

internal static class ArtworkThumbnail
{
    internal const int MaximumEncodedBytes = 32 * 1024 * 1024;
    private const ulong MaximumPixels = 64UL * 1024 * 1024;
    private const uint MaximumDimension = 32768;

    // Static initialization serializes registration before either thumbnail caller starts decoding.
    static ArtworkThumbnail() => CodecManager.Configure(codecs => codecs.UseLibwebp());

    internal static async Task<byte[]> CreateJpegAsync(Stream source, uint minimumEdge, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (minimumEdge is 0 or > 512) throw new ArgumentOutOfRangeException(nameof(minimumEdge));

        // HTTP response streams need not seek. Keep a bounded copy without taking ownership of the source.
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) != 0)
        {
            total = checked(total + read);
            if (total > MaximumEncodedBytes) throw new InvalidDataException("Album artwork exceeds the supported file size.");
            await input.WriteAsync(buffer.AsMemory(0, read), token);
        }
        input.Position = 0;

        // MagicScaler is synchronous. Keep native decoding and resizing off the UI thread.
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var frame = ImageFileInfo.Load(input).Frames[0];
            var width = frame.Width;
            var height = frame.Height;
            if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension
                || (ulong)width * (ulong)height > MaximumPixels)
                throw new InvalidDataException("Album artwork exceeds the supported image dimensions.");

            // Frame dimensions already account for EXIF orientation. Do not upscale or crop.
            var scale = Math.Min(1d, (double)minimumEdge / Math.Min(width, height));
            var settings = new ProcessImageSettings
            {
                Width = (int)Math.Max(1, Math.Round(width * scale)),
                Height = (int)Math.Max(1, Math.Round(height * scale)),
                ResizeMode = CropScaleMode.Max,
                Interpolation = InterpolationSettings.Lanczos,
                Sharpen = false,
                OrientationMode = OrientationMode.Normalize,
                ColorProfileMode = ColorProfileMode.ConvertToSrgb,
                EncoderOptions = new JpegEncoderOptions(80, ChromaSubsampleMode.Default, false),
            };
            if (!settings.TrySetEncoderFormat(ImageMimeTypes.Jpeg))
                throw new NotSupportedException("A JPEG artwork encoder is unavailable.");

            input.Position = 0;
            using var output = new MemoryStream();
            MagicImageProcessor.ProcessImage(input, output, settings);
            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }, token);
    }
}
