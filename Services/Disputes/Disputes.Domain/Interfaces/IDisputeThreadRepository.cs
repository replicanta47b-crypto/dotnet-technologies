using Disputes.Domain.Entities;
using MongoDB.Bson;

namespace Disputes.Domain.Interfaces;

public interface IDisputeThreadRepository
{
    Task AddAsync(DisputeThread thread, CancellationToken ct = default);

    Task DeleteByDisputeIdAsync(ObjectId disputeId, CancellationToken ct = default);
}
