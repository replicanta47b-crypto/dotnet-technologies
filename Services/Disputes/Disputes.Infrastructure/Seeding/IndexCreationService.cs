using Disputes.Domain.Entities;
using Disputes.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Seeding;

/// <summary>
/// Створює індекси при старті застосунку. Повторне створення наявного індексу з тими самими ключами й назвою
/// безпечне — MongoDB його не дублює. Назви збігаються з db/p3/collections.js (Лаб. 01).
/// </summary>
public class IndexCreationService(MongoDbContext context, ILogger<IndexCreationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var disputeKeys = Builders<Dispute>.IndexKeys;

            await context.Disputes.Indexes.CreateManyAsync(new[]
            {
                // «останні спори за платежем» — складений (compound) індекс
                new CreateIndexModel<Dispute>(
                    disputeKeys.Ascending("paymentId").Descending("openedAt"),
                    new CreateIndexOptions { Name = "ix_disputes_payment_opened" }),

                // «спори мерчанта за статусом» — черга розгляду
                new CreateIndexModel<Dispute>(
                    disputeKeys.Ascending("merchantId").Ascending("status"),
                    new CreateIndexOptions { Name = "ix_disputes_merchant_status" }),

                // повнотекстовий пошук: текст коментарів і назва мерчанта (text-індекс)
                new CreateIndexModel<Dispute>(
                    disputeKeys.Text("comments.text").Text("merchantName"),
                    new CreateIndexOptions { Name = "ix_disputes_text" }),

                // цілісність: один користувач — один спір за платежем із тієї самої причини (unique-індекс)
                new CreateIndexModel<Dispute>(
                    disputeKeys.Ascending("paymentId").Ascending("openedBy.userId").Ascending("reason"),
                    new CreateIndexOptions { Name = "ux_disputes_payment_user_reason", Unique = true })
            }, ct);

            await context.Threads.Indexes.CreateOneAsync(
                new CreateIndexModel<DisputeThread>(
                    Builders<DisputeThread>.IndexKeys.Ascending("disputeId"),
                    new CreateIndexOptions { Name = "ix_threads_dispute", Unique = true }),
                cancellationToken: ct);

            logger.LogInformation("MongoDB indexes are ready");
        }
        catch (Exception ex)
        {
            // недоступність БД чи конфлікт індексу не повинні валити старт застосунку
            logger.LogError(ex, "Failed to create MongoDB indexes");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
