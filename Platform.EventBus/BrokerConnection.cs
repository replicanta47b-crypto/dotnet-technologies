using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Одне спільне з'єднання з RabbitMQ на застосунок (RabbitMQ.Client 7.x створює його асинхронно, тому lazy + семафор).
/// Рядок підключення (amqp://user:pass@host:port) AppHost передає через WithReference(rabbit) → ConnectionStrings:messaging.
/// Автовідновлення з'єднання увімкнене: після рестарту брокера клієнт перепідключається сам.
/// </summary>
public sealed class BrokerConnection(IConfiguration configuration) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection is { IsOpen: true };

    public async Task<IConnection> GetAsync(CancellationToken ct = default)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            var cs = configuration.GetConnectionString(EventBusExtensions.ConnectionName)
                     ?? throw new InvalidOperationException(
                         $"ConnectionStrings:{EventBusExtensions.ConnectionName} не задано (AppHost: WithReference(rabbit)).");

            var factory = new ConnectionFactory
            {
                Uri = new Uri(cs),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = AppDomain.CurrentDomain.FriendlyName
            };

            if (_connection is not null) await _connection.DisposeAsync();
            _connection = await factory.CreateConnectionAsync(ct);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
