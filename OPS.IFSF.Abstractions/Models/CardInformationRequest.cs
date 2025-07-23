using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1100 — Card Information Request
/// </summary>
[IsoMessage("1100")]
public sealed partial class CardInformationRequest
{
    /// <summary>
    /// DE2 — PAN, LLVAR char ..19, C (Conditional)
    /// </summary>
    [IsoField(2, IsoFieldFormat.LLVar, 19)]
    public string? PAN { get; set; }

    /// <summary>
    /// DE3 — Processing Code, num(6), M (Mandatory), всегда “350000”
    /// </summary>
    [IsoField(3, IsoFieldFormat.NumPad, 6)]
    public int ProcessingCode { get; set; } = 350000;

    /// <summary>
    /// DE7 — Date and time, transmission (MMDDhhmmss), num(10), M
    /// </summary>
    [IsoField(7, IsoFieldFormat.DateTimeShort, 10)]
    public DateTime TransmissionDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// DE11 — System trace audit number (STAN), num(6), M
    /// </summary>
    [IsoField(11, IsoFieldFormat.NumPad, 6)]
    public int Stan { get; set; }

    /// <summary>
    /// DE12 — Date and time, local transaction (YYMMDDhhmmss), num(12), M
    /// </summary>
    [IsoField(12, IsoFieldFormat.DateTimeLong, 12)]
    public DateTime LocalDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// DE22 — Point of service data code, char(12), M
    /// </summary>
    [IsoField(22, IsoFieldFormat.CharPad, 12)]
    public string PointOfServiceDataCode { get; set; } = default!;

    /// <summary>
    /// DE24 — Function code, num(3), M, всегда “100”
    /// </summary>
    [IsoField(24, IsoFieldFormat.NumPad, 3)]
    public int FunctionCode { get; set; } = 100;

    /// <summary>
    /// DE26 — Card acceptor business code, num(4), M
    /// </summary>
    [IsoField(26, IsoFieldFormat.NumPad, 4)]
    public int CardAcceptorBusinessCode { get; set; } = 5541;

    /// <summary>
    /// DE32 — Acquiring institution identification code, LLVAR num(..99), M
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;

    /// <summary>
    /// DE41 — Card acceptor terminal identification, char(8), M
    /// </summary>
    [IsoField(41, IsoFieldFormat.CharPad, 8)]
    public string TerminalId { get; set; } = default!;

    ///// <summary>
    ///// DE42 — Card acceptor identification code, char(15), O
    ///// </summary>
    //[IsoField(42, IsoFieldFormat.CharPad, 15, IsoFieldPresence.Optional)]
    //public string? CardAcceptorIdCode { get; set; }

    /// <summary>
    /// DE48 — Message control data elements (nested)
    /// </summary>
    [IsoField(48, IsoFieldFormat.LLLVar, 999)]
    public De48CardInformationRequest Field48 { get; set; } = new();
}

/// <summary>S
/// Вложенный класс для DE48
/// </summary>
public sealed class De48CardInformationRequest
{
    /// <summary>
    /// DE48-3 — Language code, char(2), M
    /// </summary>
    [IsoField(3, IsoFieldFormat.CharPad, 2)]
    public string LanguageCode { get; set; } = "RU";
}