using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>
/// Довготривалий споживач черги (BackgroundService). Ручне підтвердження (autoAck: false):
///  • успіх                       → BasicAck;
///  • постійний збій (битий JSON, невідомий тип/версія, PermanentEventException) → BasicNack(requeue: false) → DLX → DLQ;
///  • тимчасовий збій (БД недоступна, таймаут) → повтор: повідомлення ПЕРЕПУБЛІКОВУЄТЬСЯ в ту саму чергу зі збільшеним
///    заголовком x-retry-count, оригінал підтверджується; після MaxRetries → DLQ.
/// Чому не requeue: повернене в чергу повідомлення приходить без змін, лічильник у заголовку збільшити не можна.
/// Чому в чергу напряму (default exchange, ключ = ім'я черги), а не в основний exchange: повтор отримав би лише цей споживач,
/// а не всі черги, прив'язані до exchange.
/// Обробка — у scope повідомлення (DI), тож обробники можуть мати Scoped-залежності (UnitOfWork, DbContext).
/// </summary>
public sealed class EventConsumerService(
    IConnection connection,
    IServiceScopeFactory scopes,
    EventConsumerOptions options,
    ILogger<EventConsumerService> logger) : BackgroundService
{
    public const string RetryHeader = "x-retry-count";
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(5);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Старт споживача не повинен валити застосунок, якщо брокер ще піднімається: пробуємо, доки не вдасться.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await Topology.DeclareQueueAsync(_channel, options.Queue, stoppingToken);
                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.Prefetch, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += (_, ea) => OnMessageAsync(ea, stoppingToken);
                await _channel.BasicConsumeAsync(options.Queue.Queue, autoAck: false, consumer, stoppingToken);

                logger.LogInformation(
                    "Consumer started: queue {Queue} bound to {Exchange} by [{RoutingKeys}], prefetch {Prefetch}, handlers [{Handlers}]",
                    options.Queue.Queue, options.Queue.Exchange, string.Join(", ", options.Queue.RoutingKeys),
                    options.Prefetch, string.Join(", ", options.Handlers.Keys));

                // Автовідновлення з'єднання (RabbitMQ.Client) само відновлює канал і споживача — тут лише чекаємо зупинки.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumer for queue {Queue} could not start, retrying in {Delay}s",
                    options.Queue.Queue, ConnectRetryDelay.TotalSeconds);
                await SafeCloseChannelAsync();
                try { await Task.Delay(ConnectRetryDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);   // скасовує stoppingToken: нові повідомлення не беруться
        await SafeCloseChannelAsync();             // непідтверджені повідомлення повертаються в чергу брокером
        logger.LogInformation("Consumer for queue {Queue} stopped", options.Queue.Queue);
    }

    private async Task SafeCloseChannelAsync()
    {
        try
        {
            if (_channel is { IsOpen: true }) await _channel.CloseAsync();
            _channel?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Channel close failed");
        }
        _channel = null;
    }

    private async Task OnMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var channel = _channel!;
        var body = ea.Body.ToArray();   // копія: пам'ять буфера належить бібліотеці лише на час обробника
        var props = ea.BasicProperties;
        var eventType = props.Type;
        var messageId = props.MessageId;
        var correlationId = TracePropagation.HeaderString(props.Headers, TracePropagation.CorrelationHeader);
        var queue = options.Queue.Queue;

        using var activity = TracePropagation.StartConsumerActivity(ea, queue, eventType, correlationId);
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = messageId,
            ["EventType"] = eventType,
            ["CorrelationId"] = correlationId,
            ["Queue"] = queue
        });

        try
        {
            if (eventType is null || !options.Handlers.TryGetValue(eventType, out var registration))
                throw new PermanentEventException($"No handler registered for event type '{eventType ?? "<none>"}' (incompatible version or foreign message).");

            var evt = JsonSerializer.Deserialize(body, registration.EventType, JsonDefaults.Options)
                      ?? throw new JsonException("Message body is empty or null.");

            await using (var scope = scopes.CreateAsyncScope())
            {
                var handler = scope.ServiceProvider.GetRequiredService(registration.HandlerType);
                await registration.Invoke(handler, evt, ct);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            logger.LogInformation("Handled {EventType} {EventId} from {Queue}: ack (delivery {DeliveryTag}, redelivered {Redelivered})",
                eventType, messageId, queue, ea.DeliveryTag, ea.Redelivered);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // застосунок зупиняється посеред обробки: повертаємо повідомлення в чергу без штрафу для лічильника
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            logger.LogWarning("Shutdown during {EventType} {EventId}: nack with requeue", eventType, messageId);
        }
        catch (Exception ex) when (ex is JsonException or PermanentEventException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex,
                "Permanent failure for {EventType} {EventId} from {Queue}: nack without requeue → {DeadLetterQueue}",
                eventType, messageId, queue, options.Queue.DeadLetterQueue);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await RetryOrDeadLetterAsync(channel, ea, body, ex, ct);
        }
    }

    private async Task RetryOrDeadLetterAsync(IChannel channel, BasicDeliverEventArgs ea, byte[] body, Exception ex, CancellationToken ct)
    {
        var props = ea.BasicProperties;
        var attempt = ReadRetryCount(props.Headers);

        if (attempt >= options.MaxRetries)
        {
            logger.LogError(ex,
                "{EventType} {EventId} failed after {Attempts} retries: nack without requeue → {DeadLetterQueue}",
                props.Type, props.MessageId, attempt, options.Queue.DeadLetterQueue);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        var delay = TimeSpan.FromMilliseconds(options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt));
        logger.LogWarning(ex,
            "{EventType} {EventId} failed (transient), retry {Attempt}/{Max} in {DelayMs} ms",
            props.Type, props.MessageId, attempt + 1, options.MaxRetries, (int)delay.TotalMilliseconds);

        try
        {
            await Task.Delay(delay, ct);

            var headers = props.Headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(props.Headers);
            headers[RetryHeader] = attempt + 1;

            var retryProps = new BasicProperties
            {
                Persistent = true,
                ContentType = props.ContentType,
                MessageId = props.MessageId,
                Type = props.Type,
                Headers = headers
            };

            // default exchange: routing key = ім'я черги → повтор отримує лише ця черга
            await channel.BasicPublishAsync(exchange: "", routingKey: options.Queue.Queue, mandatory: false, retryProps, body, ct);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    private static int ReadRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(RetryHeader, out var value)) return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var n) => n,
            _ => 0
        };
    }
}
