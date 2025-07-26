using OPS.IFSF.Abstractions.Attributes;
using OPS.IFSF.Abstractions.Buffers;
using OPS.IFSF.Abstractions.Models;

namespace OPS.IFSF.Abstractions.Tests;

public class ChunkedPooledBufferWriterTests
{
    [Theory]
    [InlineData(IsoFieldFormat.LVar, 9)]
    [InlineData(IsoFieldFormat.LLVar, 99)]
    [InlineData(IsoFieldFormat.LLLVar, 999)]
    [InlineData(IsoFieldFormat.CharPad, 20)]
    public void WriteAndReadString_WorksCorrectly(IsoFieldFormat format, int expectedLength)
    {
        string value = new('a', expectedLength / 2);
        using ChunkedPooledBufferWriter writer = new();
        writer.Write(value, format, expectedLength);

        int pref = format switch
        {
            IsoFieldFormat.LVar => 1,
            IsoFieldFormat.LLVar => 2,
            IsoFieldFormat.LLLVar => 3,
            IsoFieldFormat.CharPad => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        int totalLen = format == IsoFieldFormat.CharPad ? expectedLength : value.Length + pref;
        Assert.Equal(totalLen, writer.TotalLength);

        writer.BeginRead();
        string act = writer.ReadString(format, expectedLength);

        Assert.Equal(value.Trim(), act);
    }

    [Theory]
    [InlineData(IsoFieldFormat.DateTimeShort, 10)]
    [InlineData(IsoFieldFormat.DateTimeLong, 12)]
    public void WriteAndReadDateTime_WorksCorrectly(IsoFieldFormat format, int expectedLength)
    {
        DateTime exp = new(2025, 06, 22, 12, 11, 44);

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(exp, format, expectedLength);

        Assert.Equal(expectedLength, writer.TotalLength);

        writer.BeginRead();
        DateTime act = writer.ReadDateTime(format, expectedLength);

        Assert.Equal(exp, act);
    }

    [Theory]
    [InlineData(IsoFieldFormat.NumPad, 10)]
    public void WriteAndReadInt_WorksCorrectly(IsoFieldFormat format, int expectedLength)
    {
        int exp = 2000;

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(exp, format, expectedLength);

        Assert.Equal(expectedLength, writer.TotalLength);

        writer.BeginRead();
        int act = writer.ReadInt(format, expectedLength);

        Assert.Equal(exp, act);
    }

    [Theory]
    [InlineData(IsoFieldFormat.NumPad, 10)]
    public void WriteAndReadLong_WorksCorrectly(IsoFieldFormat format, int expectedLength)
    {
        long exp = 2000;

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(exp, format, expectedLength);

        Assert.Equal(expectedLength, writer.TotalLength);

        writer.BeginRead();
        long act = writer.ReadLong(format, expectedLength);

        Assert.Equal(exp, act);
    }

    [Fact]
    public void NetworkManagementResponse_Test()
    {
        NetworkManagementResponse exp = new()
        {
            TransmissionDateTime = new DateTime(2025, 06, 09, 19, 45, 05),
            Stan = 1,
            AcquirerId = "280131",
            ActionCode = 800
        };
        byte[] data = [49, 56, 49, 48, 2, 32, 0, 1, 2, 0, 0, 0, 48, 54, 48, 57, 49, 57, 52, 53, 
            48, 53, 48, 48, 48, 48, 48, 49, 48, 54, 50, 56, 48, 49, 51, 49, 56, 48, 48];

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(data, IsoFieldFormat.Byte, data.Length);

        writer.BeginRead();
        var act = NetworkManagementResponse.Parse(writer);

        Assert.Equal(exp.AcquirerId, act.AcquirerId);
        Assert.Equal(exp.Stan, act.Stan);
        Assert.Equal(exp.TransmissionDateTime, act.TransmissionDateTime);
        Assert.Equal(exp.ActionCode, act.ActionCode);
    }
}
