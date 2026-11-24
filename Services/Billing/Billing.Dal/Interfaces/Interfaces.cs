using Ardalis.Specification;
using Billing.Domain.Entities;

namespace Billing.Dal.Interfaces;

/// <summary>
/// Async Generic Repository на DbSet. Репозиторії лише додають/змінюють/видаляють сутності в контексті;
/// зберігає зміни Unit of Work (єдиний SaveChangesAsync). Update і Delete синхронні — вони міняють лише стан у контексті.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);

    /// <summary>Вибірка за специфікацією (Ardalis.Specification) — будь-яка специфікація без змін у репозиторії.</summary>
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default);
}

public interface IInvoiceRepository : IGenericRepository<Invoice>
{
    /// <summary>Eager Loading: картка рахунку з усім пов'язаним — ОДИН запит (Include/ThenInclude).</summary>
    Task<Invoice?> GetWithDetailsAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsByNumberAsync(string number, CancellationToken ct = default);
}

public interface IMerchantRepository : IGenericRepository<Merchant>
{
    /// <summary>Explicit Loading: мерчант — 1-й запит, його рахунки довантажуються окремим (2-м) запитом.</summary>
    Task<Merchant?> GetWithInvoicesAsync(long id, CancellationToken ct = default);

    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
}

public interface IInvoiceCategoryRepository : IGenericRepository<InvoiceCategory>
{
    /// <summary>LINQ to Entities: вибірка M:N через проміжну сутність та її власне поле AssignedAt.</summary>
    Task<IReadOnlyList<Invoice>> GetRecentlyAssignedAsync(string categorySlug, DateTime since, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    IInvoiceRepository Invoices { get; }
    IMerchantRepository Merchants { get; }
    IInvoiceCategoryRepository InvoiceCategories { get; }
    IGenericRepository<ServiceCategory> Categories { get; }

    /// <summary>Єдиний SaveChangesAsync: всі зміни бізнес-операції потрапляють у БД в одній транзакції.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
