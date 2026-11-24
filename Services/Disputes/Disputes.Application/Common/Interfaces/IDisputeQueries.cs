using Disputes.Application.Common.Models;
using Disputes.Application.Dtos;

namespace Disputes.Application.Common.Interfaces;

/// <summary>
/// Інтерфейс читання (сторона Query в CQRS). Оголошений в Application, реалізований в Infrastructure
/// (проєкція й aggregation pipeline на MongoDB) — обробники запитів не знають про IMongoCollection.
/// </summary>
public interface IDisputeQueries
{
    Task<DisputeDetailsDto?> GetByIdAsync(string id, CancellationToken ct);

    Task<PagedResult<DisputeDto>> GetListAsync(DisputeListFilter filter, CancellationToken ct);
}
