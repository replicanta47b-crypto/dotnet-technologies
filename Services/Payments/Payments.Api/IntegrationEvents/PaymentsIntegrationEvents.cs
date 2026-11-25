using Microsoft.Data.SqlClient;
using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Payments.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08. Платежі — СПОЖИВАЧ подій Білінгу. Черга payments.billing-sync прив'язана до exchange billing-events
/// за ключами invoice.* і merchant.*; кожен тип події має власний обробник (scope на повідомлення).
/// </summary>
public static class PaymentsIntegrationEvents
{
    public static WebApplicationBuilder AddPaymentsIntegrationEvents(this WebApplicationBuilder builder)
    {
        builder.AddEventBus();                                     // AddRabbitMQClient("messaging")
        builder.Services.AddScoped<IdempotentEventProcessor>();

        builder.Services.AddEventConsumer(BillingEventsTopology.PaymentsQueue, o => o
            .Handle<InvoiceIssuedEvent, InvoiceIssuedHandler>()
            .Handle<InvoiceAmountChangedEvent, InvoiceAmountChangedHandler>()
            .Handle<MerchantRenamedEvent, MerchantRenamedHandler>());
        return builder;
    }

    /// <summary>
    /// Створює dbo.ProcessedEvents, якщо її ще немає (база розгорнута скриптами Лаб. 01 до появи цієї таблиці).
    /// Той самий ідемпотентний оператор, що й у db/p1/schema.sql.
    /// </summary>
    public static async Task EnsureProcessedEventsTableAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        const string sql = @"
            IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
            CREATE TABLE dbo.ProcessedEvents (
                EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
                EventType   NVARCHAR(100)    NOT NULL,
                ProcessedAt DATETIME2        NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME()
            );";
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            // БД недоступна під час старту: не валимо застосунок, health check /health/ready покаже збій
            logger.LogError(ex, "Could not ensure dbo.ProcessedEvents exists");
        }
    }
}
