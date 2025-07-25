using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1210 — Purchase Response
/// </summary>
[IsoMessage("1210")]
public sealed partial class PurchaseResponse
{
    /// <summary>
    /// DE3 — Processing Code, num(6), ME (Mandatory Echo)
    /// </summary>
    [IsoField(3, IsoFieldFormat.NumPad, 6)]
    public int ProcessingCode { get; set; }

    /// <summary>
    /// DE4 — Amount, transaction, num(12) with 2 decimals, M  
    /// (финальная сумма, включая скидки)
    /// </summary>
    [IsoField(4, IsoFieldFormat.NumDecPad, 12)]
    public decimal Amount { get; set; }

    /// <summary>
    /// DE7 — Date and time, transmission (MMddHHmmss), num(10), M
    /// </summary>
    [IsoField(7, IsoFieldFormat.DateTimeShort, 10)]
    public DateTime TransmissionDateTime { get; set; }

    /// <summary>
    /// DE11 — System trace audit number (STAN), num(6), ME
    /// </summary>
    [IsoField(11, IsoFieldFormat.NumPad, 6)]
    public int Stan { get; set; }

    /// <summary>
    /// DE12 — Date and time, local transaction (yyMMddHHmmss), num(12), ME
    /// </summary>
    [IsoField(12, IsoFieldFormat.DateTimeLong, 12)]
    public DateTime LocalDateTime { get; set; }

    /// <summary>
    /// DE30 — Original amounts, num(24) with 2 decimals, C  
    /// (если сумма отличается от запрошенной)
    /// </summary>
    [IsoField(30, IsoFieldFormat.NumDecPad, 24)]
    public decimal? OriginalAmounts { get; set; }

    /// <summary>
    /// DE32 — Acquiring institution identification code, LLVAR num(..99), ME
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;

    /// <summary>
    /// DE37 — Retrieval reference number, num(12), C  
    /// (только для успешных транзакций)
    /// </summary>
    [IsoField(37, IsoFieldFormat.NumPad, 12)]
    public long? RetrievalReferenceNumber { get; set; }

    /// <summary>
    /// DE38 — Approval code, char(6), C  
    /// (только для успешных транзакций)
    /// </summary>
    [IsoField(38, IsoFieldFormat.CharPad, 6)]
    public string? ApprovalCode { get; set; }

    /// <summary>
    /// DE39 — Action code, num(3), M (“000” или “002” = успех)
    /// </summary>
    [IsoField(39, IsoFieldFormat.NumPad, 3)]
    public int ActionCode { get; set; }

    /// <summary>
    /// DE41 — Card acceptor terminal identification, char(8), ME
    /// </summary>
    [IsoField(41, IsoFieldFormat.CharPad, 8)]
    public string TerminalId { get; set; } = default!;

    /// <summary>
    /// DE42 — Card acceptor identification code, char(15), OE
    /// </summary>
    [IsoField(42, IsoFieldFormat.CharPad, 15)]
    public string? CardAcceptorIdCode { get; set; }

    /// <summary>
    /// DE48 — Message control data elements, LLLVAR ..999, M
    /// </summary>
    [IsoField(48, IsoFieldFormat.LLLVar, 999)]
    public De48PurchaseResponse Field48 { get; set; } = new();

    /// <summary>
    /// DE49 — Currency code, transaction, char(3), ME
    /// </summary>
    [IsoField(49, IsoFieldFormat.CharPad, 3)]
    public string CurrencyCode { get; set; } = default!;

    /// <summary>
    /// DE59 — Transport Data, LLLVAR ..999, OE
    /// </summary>
    [IsoField(59, IsoFieldFormat.LLLVar, 999)]
    public string? TransportData { get; set; }
}

/// <summary>
/// DE48 — Message Control Data Elements for Purchase Response
/// </summary>
public sealed class De48PurchaseResponse
{
    /// <summary>
    /// DE48-4 — Batch/sequence number, num(10), ME
    /// </summary>
    [IsoField(4, IsoFieldFormat.NumPad, 10)]
    public long BatchSequenceNumber { get; set; }
}
