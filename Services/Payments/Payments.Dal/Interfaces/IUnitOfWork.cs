using System.Data;

namespace Payments.Dal.Interfaces;

/// <summary>
/// Unit of Work: одне з'єднання і одна транзакція для всіх репозиторіїв однієї бізнес-операції.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    ICustomerRepository Customers { get; }
    IInvoiceRepository Invoices { get; }
    IPaymentRepository Payments { get; }

    Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
