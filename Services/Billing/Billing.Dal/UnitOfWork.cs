using Billing.Dal.Interfaces;
using Billing.Dal.Repositories;
using Billing.Domain.Entities;
using Billing.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Billing.Dal;

/// <summary>
/// Unit of Work: усі репозиторії працюють з одним DbContext; SaveChangesAsync викликається один раз
/// наприкінці бізнес-операції, EF Core виконує його в одній транзакції.
/// Технічні винятки БД перетворюються на доменні (конкурентність, унікальність).
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly BillingDbContext _context;

    public UnitOfWork(BillingDbContext context)
    {
        _context = context;
        Invoices = new InvoiceRepository(context);
        Merchants = new MerchantRepository(context);
        InvoiceCategories = new InvoiceCategoryRepository(context);
        Categories = new GenericRepository<ServiceCategory>(context);
    }

    public IInvoiceRepository Invoices { get; }
    public IMerchantRepository Merchants { get; }
    public IInvoiceCategoryRepository InvoiceCategories { get; }
    public IGenericRepository<ServiceCategory> Categories { get; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("The record was modified or deleted by another user. Reload it and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("A record with the same unique key already exists.", ex);
        }
    }
}
