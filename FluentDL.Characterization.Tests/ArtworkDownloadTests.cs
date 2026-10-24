using FluentDL.Helpers;
using System.Net;

namespace FluentDL.Characterization.Tests;

[TestClass]
[DoNotParallelize]
public class ArtworkDownloadTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DownloadReturnsARewoundIndependentStreamAndClosesTheBody(bool knownLength)
    {
        using var body = new GeneratedBody(2048);
        using var client = Client(body, knownLength, 4096);
        using var downloaded = await ArtworkThumbnail.DownloadAsync(client, "https://fixture.invalid/cover");
        Assert.IsTrue(body.Disposed);
        Assert.AreEqual(0L, downloaded.Position);
        Assert.AreEqual(2048L, downloaded.Length);
        Assert.AreEqual(40, downloaded.ReadByte());
        Assert.IsFalse(downloaded.CanWrite);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DownloadEnforcesTheBufferLimitWithOrWithoutContentLength(bool knownLength)
    {
        using var body = new GeneratedBody(2048);
        using var client = Client(body, knownLength, 1024);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => ArtworkThumbnail.DownloadAsync(client, "https://fixture.invalid/cover"));
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task DownloadRejectsHttpErrorsAndClosesTheBody()
    {
        using var body = new GeneratedBody(32);
        using var client = Client(body, true, 1024, HttpStatusCode.NotFound);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => ArtworkThumbnail.DownloadAsync(client, "https://fixture.invalid/cover"));
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task HttpTimeoutStillCoversReadingTheBody()
    {
        using var body = new GeneratedBody(32, blockReads: true);
        using var client = Client(body, false, 1024);
        client.Timeout = TimeSpan.FromMilliseconds(250);
        var error = await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => ArtworkThumbnail.DownloadAsync(client, "https://fixture.invalid/cover"));
        Assert.IsInstanceOfType<TimeoutException>(error.InnerException);
        Assert.IsTrue(body.ReadStarted, "The headers must arrive before the timeout during body reading.");
        Assert.IsTrue(body.Disposed);
    }

    [TestMethod]
    public async Task KnownLengthDownloadDoesNotAllocateASecondFullResponseBuffer()
    {
        // Warm up the same production path before measuring managed allocations, not process RAM.
        using (var warmBody = new GeneratedBody(1024))
        using (var warmClient = Client(warmBody, true, 4096))
        using (await ArtworkThumbnail.DownloadAsync(warmClient, "https://fixture.invalid/cover")) { }

        const int size = 8 * 1024 * 1024;
        using var body = new GeneratedBody(size);
        using var client = Client(body, true, ArtworkThumbnail.MaximumEncodedBytes);
        var before = GC.GetTotalAllocatedBytes(precise: true);
        using var downloaded = await ArtworkThumbnail.DownloadAsync(client, "https://fixture.invalid/cover");
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.AreEqual((long)size, downloaded.Length);
        Assert.IsTrue(allocated < size + 1024 * 1024,
            $"An 8 MiB response should need one full managed input buffer, not two. Allocated {allocated} bytes.");
    }

    private static HttpClient Client(GeneratedBody body, bool knownLength, int limit, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new StreamContent(body);
        if (knownLength) content.Headers.ContentLength = body.Size;
        return new HttpClient(new Handler(new HttpResponseMessage(status) { Content = content }))
        {
            MaxResponseContentBufferSize = limit,
        };
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(response);
    }

    private sealed class GeneratedBody(int size, bool blockReads = false) : Stream
    {
        private int read;
        internal int Size => size;
        internal bool Disposed { get; private set; }
        internal bool ReadStarted { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var length = Math.Min(count, size - read);
            buffer.AsSpan(offset, length).Fill(40);
            read += length;
            return length;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            ReadStarted = true;
            if (blockReads) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
            var length = Math.Min(buffer.Length, size - read);
            buffer.Span[..length].Fill(40);
            read += length;
            return length;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
