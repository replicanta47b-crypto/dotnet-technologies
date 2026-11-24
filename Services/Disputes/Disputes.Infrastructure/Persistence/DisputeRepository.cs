using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Persistence;

public class DisputeRepository(MongoDbContext context) : IDisputeRepository
{
    private readonly IMongoCollection<Dispute> _disputes = context.Disputes;

    public async Task<Dispute?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await _disputes.Find(d => d.Id == id).FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(long paymentId, string authorUserId, DisputeReason reason,
                                  CancellationToken ct = default)
        => _disputes.Find(d => d.PaymentId == paymentId
                               && d.OpenedBy.UserId == authorUserId
                               && d.Reason == reason)
                    .AnyAsync(ct);

    public Task AddAsync(Dispute dispute, CancellationToken ct = default)
        => _disputes.InsertOneAsync(dispute, cancellationToken: ct);

    /// <summary>
    /// Optimistic concurrency: документ замінюється лише якщо його version у БД дорівнює очікуваному.
    /// Документи, створені mongosh-скриптом Лаб. 01, поля version не мають — вважаються версією 0.
    /// </summary>
    public async Task UpdateAsync(Dispute dispute, CancellationToken ct = default)
    {
        var expectedVersion = dispute.Version;
        dispute.BumpVersion();

        var filters = Builders<Dispute>.Filter;
        var versionFilter = expectedVersion == 0
            ? filters.Or(filters.Eq(d => d.Version, 0), filters.Exists(d => d.Version, false))
            : filters.Eq(d => d.Version, expectedVersion);

        var result = await _disputes.ReplaceOneAsync(
            filters.Eq(d => d.Id, dispute.Id) & versionFilter,
            dispute,
            cancellationToken: ct);

        if (result.MatchedCount == 0)   // хтось змінив (або видалив) документ раніше
            throw new ConflictException(
                $"Dispute {dispute.Id} was modified or deleted by someone else (expected version {expectedVersion})");
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default)
    {
        var result = await _disputes.DeleteOneAsync(d => d.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
