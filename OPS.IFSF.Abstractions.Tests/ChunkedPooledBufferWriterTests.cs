using OPS.IFSF.Abstractions.Attributes;
using OPS.IFSF.Abstractions.Buffers;
using OPS.IFSF.Abstractions.Models;
using System.Text;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

    private readonly byte[] pinData = Convert.FromHexString("753c77c4893c2378");

    [Fact]
    public void PurchaseRequest_WriteTo_Test()
    {
        byte[] expected = [ 48, 49, 57, 51, 49, 50, 48, 48, 114, 48, 5, 65, 0, 129, 152, 2, 49, 
            57, 55, 56, 48, 49, 51, 49, 48, 48, 48, 48, 48, 48, 57, 57, 57, 57, 52, 57, 48, 
            48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 52, 49, 51, 53, 55, 48, 55, 
            50, 51, 50, 50, 49, 51, 48, 48, 48, 48, 48, 48, 50, 50, 50, 53, 48, 55, 50, 51, 
            50, 50, 49, 51, 48, 48, 66, 48, 48, 49, 48, 49, 54, 48, 48, 49, 52, 67, 50, 48, 
            48, 53, 53, 52, 49, 48, 54, 50, 56, 48, 49, 51, 49, 50, 52, 48, 48, 49, 32, 32, 
            32, 48, 51, 48, 48, 4, 0, 1, 0, 0, 0, 0, 82, 85, 48, 48, 48, 48, 49, 50, 51, 52, 
            53, 54, 49, 51, 48, 54, 48, 92, 48, 46, 48, 48, 54, 52, 51, 117, 60, 119, 196, 
            137, 60, 35, 120, 48, 49, 1, 48, 50, 57, 70, 48, 49, 48, 53, 76, 48, 49, 50, 92, 
            50, 48, 46, 49, 50, 53, 92, 50, 48, 46, 53, 53, 92, 52, 49, 51, 46, 53, 55];
        
        using ChunkedPooledBufferWriter writer = new();
        PurchaseRequest request = new()
        {
            PAN = "7801310000009999490",
            ProcessingCode = 0,
            Amount = 413.57m,
            TransmissionDateTime = new DateTime(2025, 07, 23, 22, 13, 00),
            Stan = 22,
            LocalDateTime = new DateTime(2025, 07, 23, 22, 13, 00),
            PointOfServiceDataCode = "B0010160014C",
            FunctionCode = 200,
            CardAcceptorBusinessCode = 5541,
            AcquirerId = "280131",
            TerminalId = "24001",
            Field48 = new De48PurchaseRequest
            {
                LanguageCode = "RU",
                BatchSequenceNumber = 123456,
                PinEncryptionMethodology = 13,
                VatPercentages = "0\\0.00"
            },
            CurrencyCode = 643,
            PinData = pinData,
            SecurityControlInfo = [0x01],
            /// TODO: Вот это сейчас работает, но нужно перейти на объект
            /// Аналогичный пример ниже в виде класса
            /// ProductData = "F0105L012\\20.125\\20.55\\413.57",
            //ProductData = new De63
            //{
            //    ServiceLevel = 'S',
            //    ItemCount = 1,
            //    FormatId = '0',
            //    //Items = [
            //    //     new SaleItem
            //    //     {
            //    //         PaymentType = '5',
            //    //         UnitOfMeasure = 'L',
            //    //         VatCode = 0,
            //    //         ProductCode = "12",
            //    //         Quantity = 10,
            //    //         UnitPrice = 10,
            //    //         Amount = 100
            //    //     },
            //    //    ]
            //}
        };
        request.WriteTo(writer);
        var actual = writer.ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PurchaseRequest_Parse_Test()
    {
        /// TODO: первые 4 байта скипаем
        byte[] data = [ /*48, 49, 57, 51,*/ 49, 50, 48, 48, 114, 48, 5, 65, 0, 129, 152, 2, 49,
            57, 55, 56, 48, 49, 51, 49, 48, 48, 48, 48, 48, 48, 57, 57, 57, 57, 52, 57, 48,
            48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 52, 49, 51, 53, 55, 48, 55,
            50, 51, 50, 50, 49, 51, 48, 48, 48, 48, 48, 48, 50, 50, 50, 53, 48, 55, 50, 51,
            50, 50, 49, 51, 48, 48, 66, 48, 48, 49, 48, 49, 54, 48, 48, 49, 52, 67, 50, 48,
            48, 53, 53, 52, 49, 48, 54, 50, 56, 48, 49, 51, 49, 50, 52, 48, 48, 49, 32, 32,
            32, 48, 51, 48, 48, 4, 0, 1, 0, 0, 0, 0, 82, 85, 48, 48, 48, 48, 49, 50, 51, 52,
            53, 54, 49, 51, 48, 54, 48, 92, 48, 46, 48, 48, 54, 52, 51, 117, 60, 119, 196,
            137, 60, 35, 120, 48, 49, 1, 48, 50, 57, 70, 48, 49, 48, 53, 76, 48, 49, 50, 92,
            50, 48, 46, 49, 50, 53, 92, 50, 48, 46, 53, 53, 92, 52, 49, 51, 46, 53, 55];

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(data, IsoFieldFormat.Byte, data.Length);

        writer.BeginRead();
        var exp = PurchaseRequest.Parse(writer);

        PurchaseRequest request = new()
        {
            PAN = "7801310000009999490",
            ProcessingCode = 0,
            Amount = 413.57m,
            TransmissionDateTime = new DateTime(2025, 07, 23, 22, 13, 00),
            Stan = 22,
            LocalDateTime = new DateTime(2025, 07, 23, 22, 13, 00),
            PointOfServiceDataCode = "B0010160014C",
            FunctionCode = 200,
            CardAcceptorBusinessCode = 5541,
            AcquirerId = "280131",
            TerminalId = "24001",
            Field48 = new De48PurchaseRequest
            {
                LanguageCode = "RU",
                BatchSequenceNumber = 123456,
                PinEncryptionMethodology = 13,
                VatPercentages = "0\\0.00"
            },
            CurrencyCode = 643,
            PinData = pinData,
            SecurityControlInfo = [0x01],
            /// TODO: Вот это сейчас работает, но нужно перейти на объект
            /// Аналогичный пример ниже в виде класса
            /// ProductData = "F0105L012\\20.125\\20.55\\413.57",
            //ProductData = new De63
            //{
            //    ServiceLevel = 'S',
            //    ItemCount = 1,
            //    FormatId = '0',
            //    //Items = [
            //    //     new SaleItem
            //    //     {
            //    //         PaymentType = '5',
            //    //         UnitOfMeasure = 'L',
            //    //         VatCode = 0,
            //    //         ProductCode = "12",
            //    //         Quantity = 10,
            //    //         UnitPrice = 10,
            //    //         Amount = 100
            //    //     },
            //    //    ]
            //}
        };

        string expected = JsonSerializer.Serialize(request);
        string actual = JsonSerializer.Serialize(exp);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PurchaseResponse_WriteTo_Test()
    {
        byte[] expected = [48, 49, 49, 57, 49, 50, 49, 48, 50, 48, 0, 1, 14, 129, 128, 0, 48, 48, 48, 48, 
            48, 48, 48, 48, 48, 48, 48, 48, 48, 52, 49, 51, 53, 55, 48, 55, 50, 51, 
            50, 50, 49, 51, 48, 48, 48, 48, 48, 48, 50, 50, 50, 53, 48, 55, 50, 51, 
            50, 50, 49, 51, 48, 48, 48, 54, 50, 56, 48, 49, 51, 49, 48, 48, 48, 48, 
            48, 49, 53, 56, 48, 48, 57, 51, 53, 56, 48, 48, 57, 51, 48, 48, 48, 50, 
            52, 48, 48, 49, 32, 32, 32, 48, 49, 56, 16, 0, 0, 0, 0, 0, 0, 0, 48, 48, 
            48, 48, 49, 50, 51, 52, 53, 54, 54, 52, 51];

        using ChunkedPooledBufferWriter writer = new();
        var response = new PurchaseResponse
        {
            ProcessingCode = 0,                                             // DE3
            Amount = 413.57m,                                               // DE4
            TransmissionDateTime = new DateTime(2025, 07, 23, 22, 13, 00),  // DE7
            Stan = 22,                                                      // DE11
            LocalDateTime = new DateTime(2025, 07, 23, 22, 13, 00),         // DE12
            OriginalAmounts = null,                                         // DE30
            AcquirerId = "280131",                                          // DE32
            RetrievalReferenceNumber = 1580093,                             // DE37
            ApprovalCode = "580093",                                        // DE38
            ActionCode = 0,                                                 // DE39
            TerminalId = "24001",                                           // DE41
            CardAcceptorIdCode = null,                                      // DE42
            Field48 = new De48PurchaseResponse                              // DE48
            {
                BatchSequenceNumber = 123456                                // DE48‑4
            },
            CurrencyCode = "643",                                           // DE49
            TransportData = null                                            // DE59
        };

        response.WriteTo(writer);
        var actual = writer.ToArray();
        var expectedStr = Encoding.ASCII.GetString(expected);
        var actualStr = Encoding.ASCII.GetString(actual);


        Assert.Equal(expectedStr, actualStr);
    }

    [Fact]
    public void PurchaseResponse_Parse_Test()
    {
        /// TODO: первые 4 байта скипаем
        byte[] data = [/*48, 49, 49, 51,*/ 49, 50, 49, 48, 50, 48, 0, 1, 14, 129, 128, 0, 48, 48, 48, 48,
            48, 48, 48, 48, 48, 48, 48, 48, 48, 52, 49, 51, 53, 55, 48, 55, 50, 51,
            50, 50, 49, 51, 48, 48, 48, 48, 48, 48, 50, 50, 50, 53, 48, 55, 50, 51,
            50, 50, 49, 51, 48, 48, 48, 54, 50, 56, 48, 49, 51, 49, 48, 48, 48, 48,
            48, 49, 53, 56, 48, 48, 57, 51, 53, 56, 48, 48, 57, 51, 48, 48, 48, 50,
            52, 48, 48, 49, 32, 32, 32, 48, 49, 56, 16, 0, 0, 0, 0, 0, 0, 0, 48, 48,
            48, 48, 49, 50, 51, 52, 53, 54, 54, 52, 51];

        using ChunkedPooledBufferWriter writer = new();
        writer.Write(data, IsoFieldFormat.Byte, data.Length);

        writer.BeginRead();
        var exp = PurchaseResponse.Parse(writer);

        var response = new PurchaseResponse
        {
            ProcessingCode = 0,                                             // DE3
            Amount = 413.57m,                                               // DE4
            TransmissionDateTime = new DateTime(2025, 07, 23, 22, 13, 00),  // DE7
            Stan = 22,                                                      // DE11
            LocalDateTime = new DateTime(2025, 07, 23, 22, 13, 00),         // DE12
            OriginalAmounts = null,                                         // DE30
            AcquirerId = "280131",                                          // DE32
            RetrievalReferenceNumber = 1580093,                             // DE37
            ApprovalCode = "580093",                                        // DE38
            ActionCode = 0,                                                 // DE39
            TerminalId = "24001",                                           // DE41
            /// TODO: подумать об скипе nullble string (сейчас просто закоментил атрибут)
            CardAcceptorIdCode = null,                                      // DE42
            Field48 = new De48PurchaseResponse                              // DE48
            {
                BatchSequenceNumber = 123456                                // DE48‑4
            },
            CurrencyCode = "643",                                           // DE49
            /// TODO: подумать об скипе nullble string (сейчас просто закоментил атрибут)
            TransportData = null                                            // DE59
        };

        string expected = JsonSerializer.Serialize(response);
        string actual = JsonSerializer.Serialize(exp);

        Assert.Equal(expected, actual);
    }
}
