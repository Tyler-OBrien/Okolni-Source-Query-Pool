using System.Text;
using Okolni.Source.Common;
using Okolni.Source.Common.ByteHelper;

namespace Okolni.Source.Query.Test;

public class ByteReaderTests
{
    // Plain arrays (not rented) are fine here as long as the reader isn't disposed, which would return them to the pool
    private static ByteReader Reader(params byte[] data) => new(data);

    [Fact]
    public void ReadsLittleEndianNumbers()
    {
        var reader = Reader(
            0x01,
            0x02, 0x01,
            0xFE, 0xFF,
            0x78, 0x56, 0x34, 0x12,
            0xFF, 0xFF, 0xFF, 0xFF,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0x00, 0x00, 0x80, 0x3F);

        Assert.Equal(1, reader.GetByte());
        Assert.Equal((ushort)0x0102, reader.GetUShort());
        Assert.Equal((short)-2, reader.GetShort());
        Assert.Equal(0x12345678, reader.GetInt());
        Assert.Equal(uint.MaxValue, reader.GetUInt());
        Assert.Equal(0x0102030405060708UL, reader.GetULong());
        Assert.Equal(1.0f, reader.GetFloat());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadsNullTerminatedUtf8Strings()
    {
        var reader = Reader([.. Encoding.UTF8.GetBytes("héllo"), 0x00, 0x00, (byte)'x', 0x00]);

        Assert.Equal("héllo", reader.GetString());
        Assert.Equal("", reader.GetString());
        Assert.Equal("x", reader.GetString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void StringWithoutTerminator_Throws()
    {
        var reader = Reader((byte)'a', (byte)'b');

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetString());
    }

    [Fact]
    public void ReadingPastTheEnd_Throws()
    {
        var reader = Reader(0x01, 0x02, 0x03);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetInt());
        Assert.Equal((ushort)0x0201, reader.GetUShort());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetShort());
        reader.GetByte();
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetByte());
    }

    [Fact]
    public void GetBytesAndGetRemaining_SliceWithoutCopying()
    {
        var reader = Reader(1, 2, 3, 4, 5);

        Assert.Equal([1, 2], reader.GetBytes(2).ToArray());
        Assert.Equal(3, reader.Remaining);
        Assert.Equal([3, 4, 5], reader.GetRemaining().ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(4));
    }

    [Fact]
    public void ReaderOverMemorySlice_OnlySeesThatSlice()
    {
        byte[] underlying = [9, 9, 0x2A, 0x00, 0x00, 0x00, 9];
        var reader = new ByteReader(underlying, underlying.AsMemory(2, 4));

        Assert.Equal(42, reader.GetInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0x64, Enums.ServerType.Dedicated)]
    [InlineData(0x6C, Enums.ServerType.NonDedicated)]
    [InlineData(0x70, Enums.ServerType.SourceTvRelay)]
    [InlineData(0x00, Enums.ServerType.NonDedicated)]
    public void ToServerType_MapsKnownBytes(byte value, Enums.ServerType expected)
    {
        Assert.Equal(expected, value.ToServerType());
    }

    [Theory]
    [InlineData(0x6C, Enums.Environment.Linux)]
    [InlineData(0x77, Enums.Environment.Windows)]
    [InlineData(0x6D, Enums.Environment.Mac)]
    [InlineData(0x6F, Enums.Environment.Mac)]
    public void ToEnvironment_MapsKnownBytes(byte value, Enums.Environment expected)
    {
        Assert.Equal(expected, value.ToEnvironment());
    }

    [Fact]
    public void UnknownEnumBytes_Throw()
    {
        Assert.Throws<ArgumentException>(() => ((byte)0x01).ToServerType());
        Assert.Throws<ArgumentException>(() => ((byte)0x01).ToEnvironment());
        Assert.Throws<ArgumentException>(() => ((byte)0x05).ToVisibility());
        Assert.Equal(Enums.Visibility.Private, ((byte)0x01).ToVisibility());
    }

    [Fact]
    public void GetNextNullCharPosition_FindsTerminatorFromStartIndex()
    {
        var data = new byte[] { 1, 0, 2, 3, 0 }.AsMemory();

        Assert.Equal(1, data.GetNextNullCharPosition(0));
        Assert.Equal(4, data.GetNextNullCharPosition(2));
        Assert.Equal(-1, new byte[] { 1, 2 }.AsMemory().GetNextNullCharPosition(0));
    }
}
