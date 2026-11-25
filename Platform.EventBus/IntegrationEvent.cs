namespace Platform.EventBus;

/// <summary>
/// Базова незмінна подія для обміну між сервісами. Назви похідних подій — у минулому часі (факт уже стався),
/// дані самодостатні: споживач не звертається до сервісу-власника по додаткову інформацію.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Ідентифікатор події: MessageId повідомлення й ключ ідемпотентності в споживачів.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Коли відбувся бізнес-факт (UTC). За цим часом споживач відкидає застарілі події (out-of-order).</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Наскрізний CorrelationId запиту, що спричинив подію (Лаб. 05).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Версія схеми: нові поля додаються, наявні не перейменовуються й не видаляються.</summary>
    public int SchemaVersion { get; init; } = 1;
}
