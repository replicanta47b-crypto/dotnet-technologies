using Disputes.Domain.Entities;
using Disputes.Domain.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Disputes.Infrastructure.Persistence;

public class DisputeThreadRepository(MongoDbContext context) : IDisputeThreadRepository
{
    private readonly IMongoCollection<DisputeThread> _threads = context.Threads;

    public Task AddAsync(DisputeThread thread, CancellationToken ct = default)
        => _threads.InsertOneAsync(thread, cancellationToken: ct);

    public Task DeleteByDisputeIdAsync(ObjectId disputeId, CancellationToken ct = default)
        => _threads.DeleteManyAsync(t => t.DisputeId == disputeId, ct);
}
