namespace Platform.EventBus;

internal sealed record HandlerRegistration(
    Type EventType,
    Type HandlerType,
    Func<object, object, CancellationToken, Task> Invoke);

/// <summary>Налаштування споживача: черга, bindings, обробники за типом події, QoS і політика повторів.</summary>
public sealed class EventConsumerOptions
{
    private readonly Dictionary<string, HandlerRegistration> _handlers = new(StringComparer.Ordinal);

    public required ConsumerQueue Queue { get; set; }

    /// <summary>Скільки непідтверджених повідомлень споживач може тримати одночасно (BasicQos).</summary>
    public ushort Prefetch { get; set; } = 10;

    /// <summary>Максимум повторних спроб тимчасового збою; далі повідомлення йде в DLQ.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Базова пауза перед повтором; кожна наступна спроба вдвічі довша (500 мс, 1 с, 2 с...).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    internal IReadOnlyDictionary<string, HandlerRegistration> Handlers => _handlers;

    /// <summary>Реєструє обробник: подія вибирається за BasicProperties.Type (= ім'я класу події).</summary>
    public EventConsumerOptions Handle<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        _handlers[typeof(TEvent).Name] = new HandlerRegistration(
            typeof(TEvent), typeof(THandler),
            static (handler, evt, ct) => ((THandler)handler).HandleAsync((TEvent)evt, ct));
        return this;
    }
}
