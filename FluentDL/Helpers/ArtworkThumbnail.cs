using System.Buffers;
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

    internal static async Task<MemoryStream> DownloadAsync(HttpClient client, string uri, CancellationToken token = default)
    {
        // GetByteArrayAsync buffers once under the client's size limit and whole-response timeout.
        // Unlike GetAsync followed by ReadAsByteArrayAsync, it does not copy an already buffered response.
        return new MemoryStream(await client.GetByteArrayAsync(uri, token), writable: false);
    }

    internal static async Task<byte[]> CreateJpegAsync(Stream source, uint minimumEdge, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (minimumEdge is 0 or > 512) throw new ArgumentOutOfRangeException(nameof(minimumEdge));

        token.ThrowIfCancellationRequested();
        var remaining = source.CanSeek ? source.Length - source.Position : (long?)null;
        if (remaining > MaximumEncodedBytes)
            throw new InvalidDataException("Album artwork exceeds the supported file size.");

        // Downloaded and embedded artwork already have seekable buffers. Decode them in place.
        // Only copy streams that cannot seek or that start partway through another stream.
        using var buffered = source.CanSeek && source.Position == 0 ? null
            : new MemoryStream(remaining is >= 0 ? checked((int)remaining.Value) : 0);
        var input = (Stream?)buffered ?? source;
        if (buffered != null)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), token)) != 0)
                {
                    if (buffered.Length + read > MaximumEncodedBytes)
                        throw new InvalidDataException("Album artwork exceeds the supported file size.");
                    buffered.Write(buffer, 0, read);
                }
                buffered.Position = 0;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

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
