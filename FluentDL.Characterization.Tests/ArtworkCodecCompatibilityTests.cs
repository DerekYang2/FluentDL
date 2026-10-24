using System.Runtime.InteropServices.WindowsRuntime;
using FluentDL.Helpers;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class ArtworkCodecCompatibilityTests
{
    // Trusted 320x160 solid-color WebP, encoded once for the decoder regression test.
    private const string Webp = "UklGRiYAAABXRUJQVlA4TBkAAAAvP8EnAAdQvKKUu/8BAUni//9URP/T/vcPAA==";
    private const string LossyWebp = "UklGRgYBAABXRUJQVlA4WAoAAAAIAAAAPwEAnwAAVlA4IJ4AAABQDgCdASpAAaAAPjEYjESiIaEQFAAgAwS0t3C7WI9uA/AAAArN2vFych77ZOQ99snIe+2TkPfbJyHvtk5D32ych77ZOQ99snIe+2TkPfbJyHvtk5D32ych77ZOQ99snIe+2TkPfbJyHvtk5D32ych77ZOQ99snIe+2TEAA/v9cZv//8Cd+BO/Anf+BO///s7v07v07v8bQAAAAAAAAAEVYSUZCAAAASUkqAAgAAAADABoBBQABAAAAMgAAABsBBQABAAAAOgAAACgBAwABAAAAAgAAAAAAAABgAAAAAQAAAGAAAAABAAAA";

    [TestMethod]
    [DataRow("jpeg", 76)]
    [DataRow("png", 76)]
    [DataRow("webp", 76)]
    [DataRow("webp-lossy", 76)]
    [DataRow("jpeg", 120)]
    [DataRow("png", 120)]
    [DataRow("webp", 120)]
    [DataRow("webp-lossy", 120)]
    public async Task CoverFormatsSupportTheQueueAndLocalArtworkResizePaths(string format, int size)
    {
        var encoded = format switch
        {
            "webp" => Convert.FromBase64String(Webp),
            "webp-lossy" => Convert.FromBase64String(LossyWebp),
            _ => await EncodeAsync(format == "jpeg" ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId, 320, 160),
        };
        using var input = new MemoryStream(encoded);
        var jpeg = await ArtworkThumbnail.CreateJpegAsync(input, (uint)size);
        using var output = new MemoryStream(jpeg);
        using var random = output.AsRandomAccessStream();
        var decoded = await BitmapDecoder.CreateAsync(random);

        Assert.AreEqual(BitmapDecoder.JpegDecoderId, decoded.DecoderInformation.CodecId);
        Assert.AreEqual((uint)size, Math.Min(decoded.PixelWidth, decoded.PixelHeight));
        Assert.AreEqual(2, (double)decoded.PixelWidth / decoded.PixelHeight, 0.01);
        var pixels = (await decoded.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
            new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)).DetachPixelData();
        Assert.IsTrue(Math.Abs(pixels[2] - 40) <= 8 && Math.Abs(pixels[1] - 120) <= 8 && Math.Abs(pixels[0] - 220) <= 8,
            "JPEG conversion should preserve the cover's representative color.");
        Assert.IsTrue(input.CanRead, "The helper must not dispose its caller's input stream.");
    }

    [TestMethod]
    public async Task SmallCoversAreNotUpscaled()
    {
        using var input = new MemoryStream(await EncodeAsync(BitmapEncoder.PngEncoderId, 40, 20));
        using var output = new MemoryStream(await ArtworkThumbnail.CreateJpegAsync(input, 120));
        using var random = output.AsRandomAccessStream();
        var decoded = await BitmapDecoder.CreateAsync(random);
        Assert.AreEqual(40u, decoded.PixelWidth);
        Assert.AreEqual(20u, decoded.PixelHeight);
    }

    [TestMethod]
    public async Task NonseekableCoverStreamsAreSupported()
    {
        using var input = new NonseekableStream(Convert.FromBase64String(Webp));
        var jpeg = await ArtworkThumbnail.CreateJpegAsync(input, 76);
        Assert.IsTrue(jpeg.Length > 0);
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task ExifOrientationIsAppliedOnceBeforeSavingTheThumbnail()
    {
        var jpeg = await EncodeAsync(BitmapEncoder.JpegEncoderId, 320, 160);
        // A little-endian EXIF IFD with Orientation=6, meaning rotate 90 degrees clockwise.
        byte[] orientation = [0xff, 0xe1, 0, 34, 69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        using var input = new MemoryStream(jpeg[..2].Concat(orientation).Concat(jpeg[2..]).ToArray());
        using var output = new MemoryStream(await ArtworkThumbnail.CreateJpegAsync(input, 76));
        using var random = output.AsRandomAccessStream();
        var decoded = await BitmapDecoder.CreateAsync(random);
        Assert.AreEqual(76u, decoded.PixelWidth);
        Assert.AreEqual(152u, decoded.PixelHeight);
        Assert.AreEqual(decoded.PixelWidth, decoded.OrientedPixelWidth, "Do not leave a second rotation in thumbnail metadata.");
    }

    [TestMethod]
    public async Task InvalidCoverBytesProduceADecoderError()
    {
        using var input = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6 });
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtworkThumbnail.CreateJpegAsync(input, 76));
    }

    [TestMethod]
    public async Task InvalidThumbnailSizesAreRejectedBeforeReadingTheStream()
    {
        using var input = new MemoryStream();
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => ArtworkThumbnail.CreateJpegAsync(input, 0));
        Assert.AreEqual(0, input.Position);
    }

    [TestMethod]
    public async Task OversizedEncodedArtworkIsRejectedWithoutClosingTheSource()
    {
        using var input = new MemoryStream(new byte[ArtworkThumbnail.MaximumEncodedBytes + 1]);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtworkThumbnail.CreateJpegAsync(input, 76));
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task ExcessiveImageDimensionsAreRejectedBeforeResizing()
    {
        using var input = new MemoryStream(await EncodeAsync(BitmapEncoder.PngEncoderId, 32769, 1));
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => ArtworkThumbnail.CreateJpegAsync(input, 76));
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task CancelledThumbnailRequestsLeaveTheSourceOpen()
    {
        using var input = new MemoryStream(Convert.FromBase64String(Webp));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => ArtworkThumbnail.CreateJpegAsync(input, 76, cancellation.Token));
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task SeekableArtworkIsDecodedWithoutAnAsyncInputCopy()
    {
        using var input = new DecodeOnlyStream(await EncodeAsync(BitmapEncoder.PngEncoderId, 320, 160));
        var jpeg = await ArtworkThumbnail.CreateJpegAsync(input, 76);
        Assert.IsTrue(jpeg.Length > 0);
        Assert.IsTrue(input.CanRead, "Decoding in place must not close the caller's stream.");
    }

    [TestMethod]
    public async Task SeekableArtworkAtANonzeroPositionUsesOnlyTheRemainingBytes()
    {
        var bytes = Convert.FromBase64String(Webp);
        using var input = new MemoryStream(new byte[23].Concat(bytes).ToArray());
        input.Position = 23;
        using var output = new MemoryStream(await ArtworkThumbnail.CreateJpegAsync(input, 76));
        using var random = output.AsRandomAccessStream();
        var decoded = await BitmapDecoder.CreateAsync(random);
        Assert.AreEqual(152u, decoded.PixelWidth);
        Assert.AreEqual(76u, decoded.PixelHeight);
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task ConcurrentWebpThumbnailsDoNotShareStreams()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var input = new MemoryStream(Convert.FromBase64String(Webp));
            var edge = index % 2 == 0 ? 76u : 120u;
            using var output = new MemoryStream(await ArtworkThumbnail.CreateJpegAsync(input, edge));
            using var random = output.AsRandomAccessStream();
            var decoded = await BitmapDecoder.CreateAsync(random);
            Assert.AreEqual(edge, decoded.PixelHeight);
            Assert.AreEqual(edge * 2, decoded.PixelWidth);
            Assert.IsTrue(input.CanRead);
        }));
    }

    private static async Task<byte[]> EncodeAsync(Guid format, uint width, uint height)
    {
        using var encoded = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(format, encoded);
        var pixels = new byte[checked((int)(width * height * 4))];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 220; pixels[i + 1] = 120; pixels[i + 2] = 40; pixels[i + 3] = 255;
        }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
        encoder.IsThumbnailGenerated = false;
        await encoder.FlushAsync();
        encoded.Seek(0);
        using var reader = encoded.AsStreamForRead();
        using var bytes = new MemoryStream();
        await reader.CopyToAsync(bytes);
        return bytes.ToArray();
    }

    private sealed class NonseekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream source = new(bytes);
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => source.ReadAsync(buffer, token);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }

    private sealed class DecodeOnlyStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
            => throw new AssertFailedException("Seekable artwork must not be copied into a second input buffer.");
    }
}
