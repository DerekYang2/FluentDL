using System.Buffers.Binary;
using System.Text;
using FluentDL.Core.ReplayGain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class WavFormatTests
{
    // Builds a WAV header the way ffmpeg writes one to a pipe: placeholder sizes, an odd-sized chunk before the
    // format chunk to check the padding byte is skipped, and an extensible format chunk.
    private static byte[] Header(ushort formatTag, ushort bits, ushort channels, uint sampleRate, uint channelMask)
    {
        var bytes = new List<byte>();
        void Text(string text) => bytes.AddRange(Encoding.ASCII.GetBytes(text));
        void UInt16(ushort value) { var buffer = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(buffer, value); bytes.AddRange(buffer); }
        void UInt32(uint value) { var buffer = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(buffer, value); bytes.AddRange(buffer); }

        Text("RIFF"); UInt32(uint.MaxValue); Text("WAVE");
        Text("LIST"); UInt32(5); Text("INFOx"); bytes.Add(0);
        Text("fmt "); UInt32(40);
        UInt16(0xFFFE); UInt16(channels); UInt32(sampleRate); UInt32(sampleRate * channels * bits / 8u);
        UInt16((ushort)(channels * bits / 8)); UInt16(bits); UInt16(22); UInt16(bits); UInt32(channelMask);
        UInt16(formatTag); bytes.AddRange(new byte[14]);
        Text("data"); UInt32(uint.MaxValue);
        return bytes.ToArray();
    }

    [TestMethod]
    public void Read_ReturnsTheFormatAndStopsWhereTheAudioStarts()
    {
        // Arrange
        var audio = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        using var stream = new MemoryStream([.. Header(3, 32, 6, 44100, 0x3F), .. audio]);

        // Act
        var format = WavFormat.Read(stream);

        // Assert
        Assert.AreEqual(new WavFormat(6, 44100, 0x3F), format);
        CollectionAssert.AreEqual(audio, stream.ToArray()[(int)stream.Position..]);
    }

    [TestMethod]
    public void Read_ReturnsNullWhenTheStreamEndsBeforeTheAudio()
    {
        // Arrange: ffmpeg wrote part of the header, then failed.
        using var stream = new MemoryStream(Header(3, 32, 2, 48000, 0x3)[..20]);

        // Act
        var format = WavFormat.Read(stream);

        // Assert
        Assert.IsNull(format);
    }

    [TestMethod]
    public void Read_RejectsAudioThatIsNot32BitFloat()
    {
        // Arrange: 16-bit integer PCM.
        using var stream = new MemoryStream(Header(1, 16, 2, 48000, 0x3));

        // Act and assert
        Assert.ThrowsException<InvalidDataException>(() => WavFormat.Read(stream));
    }
}
