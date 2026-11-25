namespace Platform.EventBus;

public interface IEventPublisher
{
    /// <summary>Серіалізує подію в JSON і публікує її в exchange з routing key (persistent-повідомлення).</summary>
    Task PublishAsync<TEvent>(TEvent @event, string exchange, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent;
}

/// <summary>Обробник одного типу події. Створюється в scope повідомлення (DI), тож може брати Scoped-залежності.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}
