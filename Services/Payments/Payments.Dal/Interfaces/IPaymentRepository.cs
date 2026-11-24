using Payments.Domain.Models;

namespace Payments.Dal.Interfaces;

/// <summary>
/// Репозиторій платежів. Бізнес-операції виконують збережені процедури з Лаб. 01
/// (usp_CreatePayment, usp_AddPaymentAllocation, usp_ChangePaymentStatus,
/// usp_AuthorizePayment, usp_SoftDeletePayment).
/// AddAsync створює платіж разом з деталями (потрібен Payment.Details);
/// DeleteAsync виконує м'яке видалення.
/// </summary>
public interface IPaymentRepository : IGenericRepository<Payment>
{
    /// <summary>Платіж з деталями та розподілом на рахунки (один запит, multi-mapping).</summary>
    Task<Payment?> GetWithAllocationsAsync(long paymentId, CancellationToken ct = default);

    Task<IReadOnlyList<Payment>> GetByCustomerAsync(long customerId, int offset, int take, CancellationToken ct = default);

    /// <summary>usp_AddPaymentAllocation. amount = null означає «сплатити рахунок повністю».</summary>
    Task AddAllocationAsync(long paymentId, long invoiceId, decimal? amount, string? updatedBy, CancellationToken ct = default);

    /// <summary>usp_ChangePaymentStatus: перевіряє допустимість переходу та (опційно) RowVersion.</summary>
    Task ChangeStatusAsync(long paymentId, string newStatus, string? changedBy, string? note,
                           byte[]? expectedRowVersion, CancellationToken ct = default);

    /// <summary>usp_AuthorizePayment: запис операції авторизації + перехід у Authorized.</summary>
    Task AuthorizeAsync(long paymentId, decimal amount, string? providerRef, string? authorizedBy, CancellationToken ct = default);
}
