using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1200 — Purchase Request
/// </summary>
[IsoMessage("1200")]
public sealed partial class PurchaseRequest
{
    /// <summary>
    /// DE2 — PAN, LLVAR char ..19, C
    /// </summary>
    [IsoField(2, IsoFieldFormat.LLVar, 19)]
    public string? PAN { get; set; }

    /// <summary>
    /// DE3 — Processing Code, num(6), M (всегда “000000”)
    /// </summary>
    [IsoField(3, IsoFieldFormat.NumPad, 6)]
    public int ProcessingCode { get; set; } = 0;

    /// <summary>
    /// DE4 — Amount, transaction, num(12) with 2 decimals, M
    /// </summary>
    [IsoField(4, IsoFieldFormat.NumDecPad, 12)]
    public decimal Amount { get; set; }

    /// <summary>
    /// DE7 — Date and time, transmission (MMddHHmmss), num(10), M
    /// </summary>
    [IsoField(7, IsoFieldFormat.DateTimeShort, 10)]
    public DateTime TransmissionDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// DE11 — System trace audit number (STAN), num(6), M
    /// </summary>
    [IsoField(11, IsoFieldFormat.NumPad, 6)]
    public int Stan { get; set; }

    /// <summary>
    /// DE12 — Date and time, local transaction (yyMMddHHmmss), num(12), M
    /// </summary>
    [IsoField(12, IsoFieldFormat.DateTimeLong, 12)]
    public DateTime LocalDateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// DE22 — Point of service data code, char(12), M
    /// </summary>
    [IsoField(22, IsoFieldFormat.CharPad, 12)]
    public string PointOfServiceDataCode { get; set; } = default!;

    /// <summary>
    /// DE24 — Function code, num(3), M (всегда “200”)
    /// </summary>
    [IsoField(24, IsoFieldFormat.NumPad, 3)]
    public int FunctionCode { get; set; } = 200;

    /// <summary>
    /// DE26 — Card acceptor business code, num(4), M
    /// </summary>
    [IsoField(26, IsoFieldFormat.NumPad, 4)]
    public int CardAcceptorBusinessCode { get; set; }

    /// <summary>
    /// DE32 — Acquiring institution identification code, LLVAR num(..99), M
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;

    /// <summary>
    /// DE35 — Track 2 data, LLVAR char ..37, C
    /// </summary>
    //[IsoField(35, IsoFieldFormat.LLVar, 37)]
    //public string? Track2Data { get; set; }

    /// <summary>
    /// DE41 — Card acceptor terminal identification, char(8), M
    /// </summary>
    [IsoField(41, IsoFieldFormat.CharPad, 8)]
    public string TerminalId { get; set; } = default!;

    /// <summary>
    /// DE42 — Card acceptor identification code, char(15), O
    /// </summary>
    //[IsoField(42, IsoFieldFormat.CharPad, 15)]
    //public string? CardAcceptorIdCode { get; set; }

    /// <summary>
    /// DE45 — Track 1 data, LLVAR char ..76, C
    /// </summary>
    //[IsoField(45, IsoFieldFormat.LLVar, 76)]
    //public string? Track1Data { get; set; }

    /// <summary>
    /// DE48 — Message control data elements, LLLVAR ..999, M
    /// </summary>
    [IsoField(48, IsoFieldFormat.BitmapComposite, 999)]
    public De48PurchaseRequest Field48 { get; set; } = new();

    /// <summary>
    /// DE49 — Currency code, transaction, char(3), M
    /// </summary>
    [IsoField(49, IsoFieldFormat.NumPad, 3)]
    public int CurrencyCode { get; set; } = default!;

    /// <summary>
    /// DE52 — PIN Data, byte[8], C
    /// </summary>
    [IsoField(52, IsoFieldFormat.Byte, 8)]
    public byte[] PinData { get; set; }

    /// <summary>
    /// DE53 — Security Related Control Information, LLVAR ..48, M
    /// </summary>
    [IsoField(53, IsoFieldFormat.LLVar, 48)]
    public byte[] SecurityControlInfo { get; set; } 

    /// <summary>
    /// DE59 — Transport Data, LLLVAR ..999, O
    /// </summary>
    //[IsoField(59, IsoFieldFormat.LLLVar, 999)]
    //public string? TransportData { get; set; }

    /// <summary>
    /// DE63 — Product data, LLLVAR ..999, M
    /// </summary>
    [IsoField(63, IsoFieldFormat.LLLVar, 999)]
    public string ProductData { get; set; } = default!;

    //[IsoField(63, IsoFieldFormat.DelimitedComposite, 999)]
    //public De63 ProductData { get; set; }
}

/// <summary>
/// Одна строка списка товаров (элемент DE63)
/// </summary>
public class SaleItem
{
    [IsoField(1, IsoFieldFormat.CharPad, 1)]
    public char PaymentType { get; set; }

    [IsoField(2, IsoFieldFormat.CharPad, 1)]
    public char UnitOfMeasure { get; set; }

    [IsoField(3, IsoFieldFormat.NumPad, 1)]
    public int VatCode { get; set; }

    [IsoField(4, IsoFieldFormat.CharPad, 17)]
    public string ProductCode { get; set; }

    [IsoField(5, IsoFieldFormat.DecFrac3, 9)]
    public decimal Quantity { get; set; }

    [IsoField(6, IsoFieldFormat.DecFrac2, 9)]
    public decimal UnitPrice { get; set; }

    [IsoField(7, IsoFieldFormat.DecFrac2, 12)]
    public decimal Amount { get; set; }
}

public class De63
{
    /// <summary>
    /// 'F' = full service, 'S' = self-service, ' ' = none
    /// </summary>
    [IsoField(1, IsoFieldFormat.CharPad, 1)]
    public char ServiceLevel { get; set; }

    /// <summary>
    /// Количество строк в списке (Item count)
    /// </summary>
    [IsoField(2, IsoFieldFormat.NumPad, 2)]
    public int ItemCount { get; set; }

    /// <summary>
    /// Format-ID (навсегда '0')
    /// </summary>
    [IsoField(3, IsoFieldFormat.CharPad, 1)]
    public char FormatId { get; set; }

    /// <summary>
    /// Сам список товаров
    /// </summary>
    //[IsoField(64, IsoFieldFormat.Array, 0)]
    public List<SaleItem> Items { get; set; } = [];
}

/// <summary>
/// DE48 — Message control data elements for PurchaseRequest
/// </summary>
public sealed class De48PurchaseRequest
{
    /// <summary>
    /// DE48-0 — Bit map (8 bytes)
    /// </summary>
    //[IsoField(0, IsoFieldFormat.Byte, 8)]
    //public byte[] Bitmap { get; set; } = new byte[8];

    /// <summary>
    /// DE48-3 — Language code, char(2), M
    /// </summary>
    [IsoField(3, IsoFieldFormat.CharPad, 2)]
    public string LanguageCode { get; set; } = "RU";

    /// <summary>
    /// DE48-4 — Batch/Sequence number, num(10), M
    /// </summary>
    [IsoField(4, IsoFieldFormat.NumPad, 10)]
    public long BatchSequenceNumber { get; set; }

    /// <summary>
    /// DE48-8 — Customer data, char ..250, C
    /// </summary>
    //[IsoField(8, IsoFieldFormat.CharPad, 250)]
    //public string? CustomerData { get; set; }

    /// <summary>
    /// DE48-14 — PIN Encryption Methodology, num(2), O
    /// </summary>
    [IsoField(14, IsoFieldFormat.NumPad, 2)]
    public int? PinEncryptionMethodology { get; set; }

    /// <summary>
    /// DE48-32 — VAT Percentages, LLVAR ..99, M
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string VatPercentages { get; set; } = default!;
}

