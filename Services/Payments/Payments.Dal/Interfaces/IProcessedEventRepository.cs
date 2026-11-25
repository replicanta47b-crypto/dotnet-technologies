namespace Payments.Dal.Interfaces;

/// <summary>Журнал оброблених подій (dbo.ProcessedEvents): основа ідемпотентності споживача (Лаб. 08).</summary>
public interface IProcessedEventRepository
{
    /// <summary>
    /// Фіксує EventId у поточній транзакції. true — подію бачимо вперше; false — уже оброблена (дублікат доставки).
    /// Унікальність гарантує PRIMARY KEY, тож гонка двох споживачів неможлива.
    /// </summary>
    Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default);
}
