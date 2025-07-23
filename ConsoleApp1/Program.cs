using OPS.IFSF.Abstractions.Attributes;
using OPS.IFSF.Abstractions.Buffers;
using OPS.IFSF.Abstractions.Models;
using System;
using System.Net.Sockets;
using System.Text;

//var str = "1810 060919004200000106280131800";
//byte[] data = [49, 56, 49, 48, 2, 32, 0, 1, 2, 0, 0, 0, 48, 54, 48, 57, 49, 57, 52, 53, 48, 53, 48, 48, 48, 48, 48, 49, 48, 54, 50, 56, 48, 49, 51, 49, 56, 48, 48];
//using ChunkedPooledBufferWriter writer = new();
//writer.Write(data, IsoFieldFormat.Byte, data.Length);
//writer.BeginRead();
//var response = NetworkManagementResponse.Parse(writer);

//using ChunkedPooledBufferWriter writer = new();
//byte[] data = [49, 49, 49, 48, 34, 48, 0, 1, 14, 144, 0, 0, 51, 53, 48, 48, 48, 48, 48, 54, 49, 52, 49, 51, 52, 51, 51, 55,
//    48, 48, 48, 48, 48, 49, 50, 53, 48, 54, 49, 52, 49, 54, 52, 51, 51, 55, 48, 54, 50, 56, 48, 49, 51, 49, 48, 48, 48, 48,
//    48, 49, 53, 54, 56, 55, 53, 57, 53, 54, 56, 55, 53, 57, 48, 48, 48, 50, 52, 48, 48, 49, 32, 32, 32, 48, 49, 52, 112,
//    0, 0, 0, 0, 0, 0, 0, 49, 48, 50, 48, 51, 50];
//writer.Write(data, IsoFieldFormat.Byte, data.Length);
//writer.BeginRead();
//var response = CardInformationResponse.Parse(writer);

//CardInformationResponse r = new()
//{
//    AcquirerId = "123",
//    ApprovalCode = "123",
//    TerminalId = "123",
//    CardAcceptorIdCode = "123",
//    TransportData = "123",
//    AdditionalResponseData = new De44
//    {

//    }
//};
//r.WriteTo(writer);

//await Ping();
//await CardInformation();

await Purchase();

return;

static async Task CardInformation()
{
    using ChunkedPooledBufferWriter writer = new();
    CardInformationRequest request = new()
    {
        PAN = "7801310000009999490",
        Stan = 51,
        PointOfServiceDataCode = "B0010160014C",
        AcquirerId = "280131",
        TerminalId = "24001",
    };
    request.WriteTo(writer);

    var res = writer.ToArray();
    Console.WriteLine(string.Join("-", Convert.ToHexString(res).Chunk(2).Select(c => new string(c))));
    Console.WriteLine(Encoding.ASCII.GetString(res));

    using var client = new TcpClient();
    await client.ConnectAsync("127.0.0.1", 8525);
    var stream = client.GetStream();
    await writer.ToStreamAsync(stream);
    writer.Clear();

    var leng = writer.GetMemory(4);
    await stream.ReadExactlyAsync(leng);

    writer.BeginRead();
    int len = writer.ReadInt(IsoFieldFormat.NumPad, 4);

    var data = writer.GetMemory(len);
    var val = await stream.ReadAsync(data);
    Console.WriteLine(Encoding.ASCII.GetString(data.Span));
    Console.WriteLine(string.Join(", ", data.ToArray()));

    var response = CardInformationResponse.Parse(writer);
    //Console.WriteLine(string.Join("-", Convert.ToHexString(data.Span).Chunk(2).Select(c => new string(c))));
}

static async Task Ping()
{
    using ChunkedPooledBufferWriter writer = new();
    NetworkManagementRequest request = new()
    {
        TransmissionDateTime = DateTime.Now,
        Stan = 1,
        AcquirerId = "280131"
    };
    request.WriteTo(writer);

    using var client = new TcpClient();
    //await client.ConnectAsync("127.0.0.1", 8525);
    await client.ConnectAsync("193.169.178.252", 8525);
    var stream = client.GetStream();
    await writer.ToStreamAsync(stream);
    writer.Clear();

    var leng = writer.GetMemory(4);
    await stream.ReadExactlyAsync(leng);

    writer.BeginRead();
    var len = writer.ReadInt(IsoFieldFormat.NumPad, 4);

    var data = writer.GetMemory(len);
    var val = await stream.ReadAsync(data);
    Console.WriteLine(Encoding.ASCII.GetString(data.Span));
    Console.WriteLine(string.Join(", ", data.ToArray()));

    var response = NetworkManagementResponse.Parse(writer);
    //Console.WriteLine(string.Join("-", Convert.ToHexString(data.Span).Chunk(2).Select(c => new string(c))));
}

static async Task Purchase()
{
    //var pinData = Convert.FromHexString("1111111111111111");
    var pinData = Convert.FromHexString("753c77c4893c2378");
    using ChunkedPooledBufferWriter writer = new();
    PurchaseRequest request = new()
    {
        PAN = "7801310000009999490",
        ProcessingCode = 0,
        Amount = 413.57m,
        TransmissionDateTime = new DateTime(2025, 07, 23, 22, 13, 00), //DateTime.UtcNow,
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
        ProductData = "F0105L012\\20.125\\20.55\\413.57",
        //ProductData = new De63
        //{
        //    ServiceLevel = 'F',
        //    FormatId = '0',
        //    ItemCount = 1,
        //    Items = [
        //         new SaleItem
        //         {
        //             PaymentType = '5',
        //             UnitOfMeasure = 'L',
        //             VatCode = 0,
        //             ProductCode = "12",
        //             Quantity = 20.125m,
        //             UnitPrice = 20.55m,
        //             Amount = 413.57m
        //         }
        //    ]
        //}
    };
    request.WriteTo(writer);

    var res = writer.ToArray();
    Console.WriteLine(string.Join(", ", res));
    Console.WriteLine(Encoding.ASCII.GetString(res));

    using var client = new TcpClient();
    //await client.ConnectAsync("127.0.0.1", 8525);
    await client.ConnectAsync("193.169.178.252", 8525);
    var stream = client.GetStream();
    await writer.ToStreamAsync(stream);
    writer.Clear();

    var leng = writer.GetMemory(4);
    await stream.ReadExactlyAsync(leng);

    writer.BeginRead();
    var len = writer.ReadInt(IsoFieldFormat.NumPad, 4);

    var data = writer.GetMemory(len);
    var val = await stream.ReadAsync(data);
    Console.WriteLine(Encoding.ASCII.GetString(data.Span));
    Console.WriteLine(string.Join(", ", data.ToArray()));

    var response = PurchaseResponse.Parse(writer);
    //Console.WriteLine(string.Join("-", Convert.ToHexString(data.Span).Chunk(2).Select(c => new string(c))));
}