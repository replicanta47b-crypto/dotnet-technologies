using Billing.Dal.Interfaces;
using Billing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Billing.Dal.Repositories;

/// <summary>Eager Loading: усе пов'язане завантажується одним запитом разом з рахунком.</summary>
public class InvoiceRepository : GenericRepository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(BillingDbContext context) : base(context) { }

    public Task<Invoice?> GetWithDetailsAsync(long id, CancellationToken ct = default)
        => Set.Include(i => i.Merchant)
              .Include(i => i.Detail)
              .Include(i => i.Lines.OrderBy(l => l.SortOrder))
              .Include(i => i.InvoiceCategories).ThenInclude(ic => ic.Category)
              .FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<bool> ExistsByNumberAsync(string number, CancellationToken ct = default)
        => Set.AnyAsync(i => i.Number == number, ct);
}

/// <summary>Explicit Loading: мерчант завантажується окремо, рахунки — окремим запитом, лише коли потрібні.</summary>
public class MerchantRepository : GenericRepository<Merchant>, IMerchantRepository
{
    public MerchantRepository(BillingDbContext context) : base(context) { }

    public async Task<Merchant?> GetWithInvoicesAsync(long id, CancellationToken ct = default)
    {
        var merchant = await Set.FirstOrDefaultAsync(m => m.Id == id, ct);             // 1-й запит
        if (merchant is null) return null;

        await Context.Entry(merchant).Collection(m => m.Invoices)
            .Query().OrderByDescending(i => i.DueDate)
            .LoadAsync(ct);                                                              // 2-й запит
        return merchant;
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        => Set.AnyAsync(m => m.Name == name, ct);
}

/// <summary>LINQ to Entities: M:N через проміжну сутність і її власне поле AssignedAt.</summary>
public class InvoiceCategoryRepository : GenericRepository<InvoiceCategory>, IInvoiceCategoryRepository
{
    public InvoiceCategoryRepository(BillingDbContext context) : base(context) { }

    // ключ складений (InvoiceId, CategoryId), тому пошук за одним Id не має сенсу
    public override Task<InvoiceCategory?> GetByIdAsync(long id, CancellationToken ct = default)
        => throw new NotSupportedException("InvoiceCategory has a composite key (InvoiceId, CategoryId).");

    public async Task<IReadOnlyList<Invoice>> GetRecentlyAssignedAsync(
        string categorySlug, DateTime since, CancellationToken ct = default)
        => await Set
            .Where(ic => ic.Category.Slug == categorySlug && ic.AssignedAt >= since)
            .OrderByDescending(ic => ic.AssignedAt)
            .Select(ic => ic.Invoice)
            .AsNoTracking()
            .ToListAsync(ct);
}
