using OPS.IFSF.Abstractions.Attributes;

namespace OPS.IFSF.Abstractions.Models;

/// <summary>
/// Сообщение 1800 — Запрос сетевого управления (Network Management Request)
/// </summary>
[IsoMessage("1800")]
public sealed partial class NetworkManagementRequest
{
    /// <summary>
    /// DE7 — Дата и время передачи (формат MMddHHmmss), num(10)
    /// </summary>
    [IsoField(7, IsoFieldFormat.DateTimeShort, 10)]
    public DateTime TransmissionDateTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// DE11 — Уникальный идентификатор транзакции (STAN), num(6)
    /// </summary>
    [IsoField(11, IsoFieldFormat.NumPad, 6)]
    public int Stan { get; set; }

    /// <summary>
    /// DE24 — Код функции сообщения, всегда 800, num(3)
    /// </summary>
    [IsoField(24, IsoFieldFormat.NumPad, 3)]
    public int FunctionCode { get; set; } = 800;

    /// <summary>
    /// DE32 — Код идентификации системы-эквайера, LLVAR num(..99)
    /// </summary>
    [IsoField(32, IsoFieldFormat.LLVar, 99)]
    public string AcquirerId { get; set; } = default!;
}
