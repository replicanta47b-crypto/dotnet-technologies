using Payments.Domain.Models;

namespace Payments.Dal.Interfaces;

/// <summary>Локальна копія довідника рахунків (власник даних — Білінг).</summary>
public interface IInvoiceRepository : IGenericRepository<Invoice>
{
    Task<Invoice?> GetByNumberAsync(string number, CancellationToken ct = default);
    Task<bool> UpdateAsync(Invoice invoice, CancellationToken ct = default);
}
