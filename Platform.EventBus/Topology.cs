using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Черга споживача з DLQ. Основна черга durable, її dead-letter exchange (DLX) — direct; DLQ прив'язана до DLX
/// за ключем «ім'я DLQ», тому в кожного споживача своя DLQ і повідомлення різних споживачів не змішуються.
/// </summary>
public sealed record ConsumerQueue(string Queue, string Exchange, string[] RoutingKeys)
{
    public string DeadLetterExchange => $"{Exchange}.dlx";
    public string DeadLetterQueue => $"{Queue}.dlq";
}

/// <summary>
/// Декларативна топологія. Повторне оголошення з тими самими параметрами безпечне, тому кожен учасник (видавець
/// і споживач) оголошує все, що йому потрібно, під час старту. Параметри вже створеної черги вручну не змінювати —
/// RabbitMQ відхилить оголошення з іншими аргументами (PRECONDITION_FAILED).
/// </summary>
public static class Topology
{
    public static async Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken ct)
    {
        // topic: маршрутизація за шаблоном routing key (invoice.issued, merchant.renamed; * — одне слово, # — будь-яка кількість)
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false,
            arguments: null, cancellationToken: ct);
        await channel.ExchangeDeclareAsync($"{exchange}.dlx", ExchangeType.Direct, durable: true, autoDelete: false,
            arguments: null, cancellationToken: ct);
    }

    public static async Task DeclareQueueAsync(IChannel channel, ConsumerQueue q, CancellationToken ct)
    {
        await DeclareExchangeAsync(channel, q.Exchange, ct);

        // DLQ — звичайна durable-черга, куди RabbitMQ переносить відхилені (nack, requeue=false) повідомлення разом із заголовком x-death
        await channel.QueueDeclareAsync(q.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: ct);
        await channel.QueueBindAsync(q.DeadLetterQueue, q.DeadLetterExchange, routingKey: q.DeadLetterQueue,
            arguments: null, cancellationToken: ct);

        await channel.QueueDeclareAsync(q.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = q.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = q.DeadLetterQueue
            },
            cancellationToken: ct);

        foreach (var key in q.RoutingKeys)
            await channel.QueueBindAsync(q.Queue, q.Exchange, routingKey: key, arguments: null, cancellationToken: ct);
    }
}
