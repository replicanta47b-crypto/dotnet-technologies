using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Оголошує топологію при старті ВИДАВЦЯ: exchange, DLX і черги всіх відомих споживачів. Завдяки цьому події,
/// опубліковані, поки споживач вимкнений, не губляться: черга вже існує й накопичує їх (сценарій «зупинили
/// orders-api, змінили ціни, запустили знову»). Недоступність брокера не валить застосунок — лише логується.
/// </summary>
public sealed class TopologyInitializer(IConnection connection, IReadOnlyList<ConsumerQueue> queues,
    ILogger<TopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                foreach (var q in queues)
                    await Topology.DeclareQueueAsync(channel, q, cancellationToken);

                logger.LogInformation("Messaging topology declared: {Exchange}, queues [{Queues}]",
                    queues[0].Exchange, string.Join(", ", queues.Select(q => q.Queue)));
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Declaring messaging topology failed (attempt {Attempt}/5)", attempt);
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        logger.LogError("Messaging topology was not declared: events may be dropped until a consumer declares its queue");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
