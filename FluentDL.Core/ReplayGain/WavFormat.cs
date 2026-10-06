#nullable enable
using System.Buffers.Binary;

namespace FluentDL.Core.ReplayGain;

// The header of the 32-bit float WAV stream the scanner asks ffmpeg to write to a pipe. ffmpeg decodes to the
// stream's own channel count and sample rate and reports them here, so nothing is remixed or resampled.
// ChannelMask is the WAV speaker mask, or 0 when ffmpeg writes none.
public sealed record WavFormat(int Channels, int SampleRate, uint ChannelMask)
{
    private const ushort IeeeFloat = 3;
    private const ushort Extensible = 0xFFFE;

    // Reads up to the start of the audio and leaves the stream there. Returns null if the stream ends first, which
    // happens when ffmpeg fails. Chunk sizes on a pipe are placeholders, so the audio runs to the end of the stream.
    public static WavFormat? Read(Stream stream)
    {
        var header = new byte[12];
        if (!TryFill(stream, header)) return null;
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("ffmpeg's output isn't a WAV stream.");

        WavFormat? format = null;
        var chunk = new byte[8];
        while (TryFill(stream, chunk))
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
                return format ?? throw new InvalidDataException("ffmpeg's WAV stream has no format chunk.");

            // Chunks are padded to an even length.
            if (size > 1 << 24) throw new InvalidDataException("ffmpeg's WAV stream has an oversized header chunk.");
            var body = new byte[size + (size & 1)];
            if (!TryFill(stream, body)) return null;
            if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8)) format = Parse(body);
        }

        return null;
    }

    private static WavFormat Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length < 16) throw new InvalidDataException("ffmpeg's WAV format chunk is too short.");
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
        uint channelMask = 0;
        if (tag == Extensible && body.Length >= 40)
        {
            channelMask = BinaryPrimitives.ReadUInt32LittleEndian(body[20..]);
            // The sub-format GUID starts with the real format tag.
            tag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
        }

        if (tag != IeeeFloat || bits != 32 || channels == 0 || sampleRate == 0)
            throw new InvalidDataException($"ffmpeg wrote format {tag} with {bits}-bit samples instead of 32-bit float.");
        return new WavFormat(channels, (int)sampleRate, channelMask);
    }

    private static bool TryFill(Stream stream, Span<byte> buffer)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = stream.Read(buffer[filled..]);
            if (read == 0) return false;
            filled += read;
        }
        return true;
    }
}
