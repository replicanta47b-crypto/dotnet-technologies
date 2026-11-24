using Payments.Domain.Models;

namespace Payments.Dal.Interfaces;

/// <summary>
/// Репозиторій платників. Реалізований на чистому ADO.NET і СВІДОМО не успадковує IGenericRepository.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetPageAsync(int offset, int take, CancellationToken ct = default);
    Task<long> AddAsync(Customer customer, CancellationToken ct = default);
    Task<bool> UpdateAsync(Customer customer, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(long id, string? deletedBy, CancellationToken ct = default);
}
