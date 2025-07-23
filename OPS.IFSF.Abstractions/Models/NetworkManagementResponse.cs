using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1810 — Network Management Response
/// </summary>
[IsoMessage("1810")]
public sealed partial class NetworkManagementResponse
{
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
    /// DE32 — Acquiring institution identification code, LLVAR num(..99), ME
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;

    /// <summary>
    /// DE39 — Action code, num(3), M — "800" means success
    /// </summary>
    [IsoField(39, IsoFieldFormat.NumPad, 3)]
    public int ActionCode { get; set; } = default!;
}
