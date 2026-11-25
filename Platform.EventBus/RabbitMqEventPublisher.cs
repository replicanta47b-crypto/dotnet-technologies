using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Публікує інтеграційні події в RabbitMQ. BrokerConnection — одне з'єднання на застосунок,
/// канал створюється на кожну публікацію й одразу закривається (IChannel не призначений для спільного використання потоками).
/// Повідомлення persistent (переживає перезапуск брокера, якщо черга durable), MessageId = EventId, Type = ім'я події.
/// </summary>
public sealed class RabbitMqEventPublisher(BrokerConnection connection, ILogger<RabbitMqEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent @event, string exchange, string routingKey, CancellationToken ct = default)
        where TEvent : IntegrationEvent
    {
        var eventType = typeof(TEvent).Name;
        var correlationId = @event.CorrelationId ?? TracePropagation.CurrentCorrelationId();

        using var activity = TracePropagation.StartPublisherActivity(exchange, routingKey, eventType, @event.EventId, correlationId);

        var headers = new Dictionary<string, object?>
        {
            [TracePropagation.CorrelationHeader] = correlationId,
            ["x-event-version"] = @event.SchemaVersion
        };
        TracePropagation.Inject(activity, headers);   // W3C traceparent → заголовки

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = @event.EventId.ToString(),
            Type = eventType,
            Headers = headers
        };

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, typeof(TEvent), JsonDefaults.Options);

        try
        {
            await using var channel = await (await connection.GetAsync(ct)).CreateChannelAsync(cancellationToken: ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, props, body, ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }

        logger.LogInformation(
            "Published {EventType} {EventId} to {Exchange}/{RoutingKey} (CorrelationId {CorrelationId}, {Bytes} bytes)",
            eventType, @event.EventId, exchange, routingKey, correlationId, body.Length);
    }
}
