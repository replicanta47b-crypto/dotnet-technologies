using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>
/// Передача контексту трасування через брокер. Видавець кладе W3C traceparent у заголовки повідомлення,
/// споживач відновлює його й створює Activity типу Consumer — так HTTP-запит, публікація й асинхронна обробка
/// складаються в ОДИН trace. ActivitySource "Platform.Messaging" підписано в ServiceDefaults.
/// </summary>
public static class TracePropagation
{
    public const string SourceName = "Platform.Messaging";
    public const string CorrelationHeader = "x-correlation-id";

    public static readonly ActivitySource Source = new(SourceName);
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    /// <summary>Записує контекст поточного Activity у заголовки повідомлення (traceparent, tracestate, baggage).</summary>
    public static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        if (activity is null) return;
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), headers,
            static (h, key, value) => h[key] = value);
    }

    /// <summary>Producer-span публікації: дочірній до поточного (HTTP-запит), його контекст іде в заголовки.</summary>
    public static Activity? StartPublisherActivity(string exchange, string routingKey, string eventType, Guid eventId, string? correlationId)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer);
        if (activity is null) return null;
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation", "publish");
        activity.SetTag("messaging.destination.name", exchange);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity.SetTag("messaging.message.id", eventId.ToString());
        activity.SetTag("event.type", eventType);
        if (correlationId is not null) activity.SetTag("correlation.id", correlationId);
        return activity;
    }

    /// <summary>
    /// Відновлює контекст із заголовків (значення приходять як byte[]) і запускає Consumer-span як нащадка
    /// видавця. Теги messaging.* — за семантичними конвенціями OpenTelemetry.
    /// </summary>
    public static Activity? StartConsumerActivity(BasicDeliverEventArgs ea, string queue, string? eventType, string? correlationId)
    {
        var headers = ea.BasicProperties.Headers;
        var parent = Propagator.Extract(default, headers, ExtractHeader);

        Baggage.Current = parent.Baggage;
        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        if (activity is null) return null;
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation", "process");
        activity.SetTag("messaging.destination.name", queue);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", ea.RoutingKey);
        activity.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        activity.SetTag("event.type", eventType);
        if (correlationId is not null) activity.SetTag("correlation.id", correlationId);
        return activity;
    }

    /// <summary>
    /// CorrelationId поточного запиту: його ставить CorrelationIdMiddleware тегом "correlation.id" на span запиту,
    /// тож шукаємо його вгору по ланцюжку Activity (вкладені spans сервісів цей тег не успадковують).
    /// </summary>
    public static string? CurrentCorrelationId()
    {
        for (var a = Activity.Current; a is not null; a = a.Parent)
            if (a.GetTagItem("correlation.id") is string id && id.Length > 0)
                return id;
        return null;
    }

    private static IEnumerable<string>? ExtractHeader(IDictionary<string, object?>? headers, string key)
    {
        var value = HeaderString(headers, key);
        return value is null ? null : [value];
    }

    /// <summary>Рядкове значення заголовка (RabbitMQ повертає рядки як byte[]).</summary>
    public static string? HeaderString(IDictionary<string, object?>? headers, string key)
        => headers is not null && headers.TryGetValue(key, out var v)
            ? v switch { byte[] b => Encoding.UTF8.GetString(b), string s => s, null => null, _ => v.ToString() }
            : null;
}
