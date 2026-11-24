using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using MongoDB.Bson;

namespace Disputes.Domain.Interfaces;

/// <summary>Контракт запису/читання агрегату Dispute. Реалізація — в Infrastructure (MongoDB Driver).</summary>
public interface IDisputeRepository
{
    Task<Dispute?> GetByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Чи вже відкривав цей користувач спір за цим платежем із цієї причини.</summary>
    Task<bool> ExistsAsync(long paymentId, string authorUserId, DisputeReason reason, CancellationToken ct = default);

    Task AddAsync(Dispute dispute, CancellationToken ct = default);

    /// <summary>Оновлення з перевіркою Version (optimistic concurrency); конфлікт → ConflictException.</summary>
    Task UpdateAsync(Dispute dispute, CancellationToken ct = default);

    Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default);
}
