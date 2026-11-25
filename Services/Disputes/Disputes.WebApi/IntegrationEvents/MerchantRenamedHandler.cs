using Disputes.Infrastructure.Persistence;
using MongoDB.Bson;
using MongoDB.Driver;
using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Disputes.WebApi.IntegrationEvents;

/// <summary>
/// Лаб. 08. Спори («Відгуки») зберігають денормалізовану назву мерчанта disputes.merchantName (політика з Лаб. 01:
/// «копія для читання, оновлюється подіями з Білінгу»). Подія MerchantRenamed оновлює її в УСІХ спорах мерчанта.
///
/// Ідемпотентність. MongoDB у контейнері працює без replica set, тож багатодокументних транзакцій немає. Тому порядок такий:
/// (1) перевірити колекцію processedEvents; (2) застосувати зміну — вона сама ідемпотентна ($set того самого значення);
/// (3) записати EventId (унікальний _id). Падіння між (2) і (3) призводить лише до безпечного повторного $set.
/// Out-of-order. Час останньої застосованої зміни назви мерчанта лежить у merchantNameVersions; запізніла подія,
/// що старіша за застосовану, пропускається. Документи спорів при цьому не отримують нових полів.
/// </summary>
public sealed class MerchantRenamedHandler(MongoDbContext db, ILogger<MerchantRenamedHandler> logger)
    : IIntegrationEventHandler<MerchantRenamedEvent>
{
    public const string ProcessedEventsCollection = "processedEvents";
    public const string VersionsCollection = "merchantNameVersions";

    public async Task HandleAsync(MerchantRenamedEvent e, CancellationToken ct)
    {
        var processed = db.Database.GetCollection<BsonDocument>(ProcessedEventsCollection);
        var versions = db.Database.GetCollection<BsonDocument>(VersionsCollection);
        var eventKey = e.EventId.ToString("D");

        // (1) дублікат доставки?
        if (await processed.Find(new BsonDocument("_id", eventKey)).AnyAsync(ct))
        {
            logger.LogInformation("Duplicate MerchantRenamed {EventId} skipped: already processed", e.EventId);
            return;
        }

        // out-of-order: уже застосовано новішу назву цього мерчанта?
        var current = await versions.Find(new BsonDocument("_id", e.MerchantId)).FirstOrDefaultAsync(ct);
        if (current is not null && current["at"].ToUniversalTime() > e.OccurredAt)
        {
            logger.LogWarning(
                "MerchantRenamed {EventId} for merchant {MerchantId} is stale (applied change from {AppliedAt:o} is newer than {EventAt:o}): skipped",
                e.EventId, e.MerchantId, current["at"].ToUniversalTime(), e.OccurredAt);
            await MarkProcessedAsync(processed, eventKey, ct);
            return;
        }

        // (2) зміна: усі спори мерчанта отримують нову назву (за merchantId, а не за назвою)
        var result = await db.Disputes.UpdateManyAsync(
            Builders<Disputes.Domain.Entities.Dispute>.Filter.Eq("merchantId", e.MerchantId),
            Builders<Disputes.Domain.Entities.Dispute>.Update.Set("merchantName", e.NewName),
            cancellationToken: ct);

        await versions.ReplaceOneAsync(
            new BsonDocument("_id", e.MerchantId),
            new BsonDocument { { "_id", e.MerchantId }, { "name", e.NewName }, { "at", e.OccurredAt } },
            new ReplaceOptions { IsUpsert = true }, ct);

        // (3) фіксуємо EventId
        await MarkProcessedAsync(processed, eventKey, ct);

        logger.LogInformation(
            "MerchantRenamed {EventId}: merchant {MerchantId} '{OldName}' -> '{NewName}', {Matched} disputes matched, {Modified} modified (CorrelationId {CorrelationId})",
            e.EventId, e.MerchantId, e.OldName, e.NewName, result.MatchedCount, result.ModifiedCount, e.CorrelationId);
    }

    private static async Task MarkProcessedAsync(IMongoCollection<BsonDocument> processed, string eventKey, CancellationToken ct)
    {
        try
        {
            await processed.InsertOneAsync(
                new BsonDocument { { "_id", eventKey }, { "processedAt", DateTime.UtcNow } }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // паралельна доставка тієї ж події вже записала ключ — це нормально
        }
    }
}
