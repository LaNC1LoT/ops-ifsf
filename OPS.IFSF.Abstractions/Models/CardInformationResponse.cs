using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1110 — Card Information Response
/// </summary>
[IsoMessage("1110")]
public sealed partial class CardInformationResponse
{
    /// <summary>
    /// DE3 — Processing Code, num(6), ME (Mandatory Echo)
    /// </summary>
    [IsoField(3, IsoFieldFormat.NumPad, 6)]
    public int ProcessingCode { get; set; }

    /// <summary>
    /// DE7 — Date and time, transmission (MMDDhhmmss), num(10), M
    /// </summary>
    [IsoField(7, IsoFieldFormat.DateTimeShort, 10)]
    public DateTime TransmissionDateTime { get; set; }

    /// <summary>
    /// DE11 — System trace audit number (STAN), num(6), ME
    /// </summary>
    [IsoField(11, IsoFieldFormat.NumPad, 6)]
    public int Stan { get; set; }

    /// <summary>
    /// DE12 — Date and time, local transaction (YYMMDDhhmmss), num(12), ME
    /// </summary>
    [IsoField(12, IsoFieldFormat.DateTimeLong, 12)]
    public DateTime LocalDateTime { get; set; }

    /// <summary>
    /// DE32 — Acquiring institution identification code, LLVAR num(..99), ME
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;

    /// <summary>
    /// DE37 — Retrieval reference number, num(12), C
    /// </summary>
    [IsoField(37, IsoFieldFormat.NumPad, 12)]
    public long RetrievalReferenceNumber { get; set; }

    /// <summary>
    /// DE38 — Approval code, char(6), C
    /// </summary>
    [IsoField(38, IsoFieldFormat.CharPad, 6)]
    public string ApprovalCode { get; set; } = default!;

    /// <summary>
    /// DE39 — Action code, num(3), M (“000” = success)
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
    /// DE44 — Additional response data, LLLVAR ..999, M
    /// </summary>
    [IsoField(44, IsoFieldFormat.LLLVar, 999)]
    public De44 AdditionalResponseData { get; set; } = default!;

    /// <summary>
    /// DE59 — Transport Data, LLLVAR ..999, OE
    /// </summary>
    [IsoField(59, IsoFieldFormat.LLLVar, 999)]
    public string? TransportData { get; set; }
}

/// <summary>
/// DE44 — Additional Response Data (LLLVAR ..999)
/// </summary>
public sealed class De44
{
    /// <summary>
    /// DE44-2 — PIN protection (num 1): “0” = PIN not required, “1” = PIN must be entered
    /// </summary>
    [IsoField(2, IsoFieldFormat.NumPad, 1)]
    public int PinProtection { get; set; }

    /// <summary>
    /// DE44-3 — Card type (num 3):
    ///   003 = Gift card  
    ///   004 = Bonus card  
    ///   006 = Bank card  
    ///   011 = Discount card  
    ///   020 = Fleet/fuel card  
    ///   021 = Fleet/fuel one-off ticket  
    /// </summary>
    [IsoField(3, IsoFieldFormat.NumPad, 3)]
    public int CardType { get; set; }

    /// <summary>
    /// DE44-4 — Allowed payment types (num 2), bit-map:
    ///   bit 1 = Cash  
    ///   bit 2 = Bank card  
    ///   bit 3 = Bonus account  
    ///   bit 5 = Customer’s account  
    /// </summary>
    [IsoField(4, IsoFieldFormat.NumPad, 2)]
    public int AllowedPaymentTypes { get; set; }

    /// <summary>
    /// DE44-5 — Card expiration date (num 12, YYMMDDhhmmss)
    /// </summary>
    [IsoField(5, IsoFieldFormat.DateTimeLong, 12)]
    public DateTime? ExpirationDate { get; set; }
}