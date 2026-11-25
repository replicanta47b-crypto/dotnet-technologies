using Billing.Bll.Dto;
using Billing.Bll.Services;

namespace Billing.Bll.Caching;

/// <summary>
/// Декоратор над InvoiceService: читання — через дворівневий кеш, запис — виконує справжній сервіс і ІНВАЛІДУЄ кеш.
/// Контролер не знає про кеш: він працює з IInvoiceService (патерн Decorator, DI реєструє обгортку).
/// </summary>
public sealed class CachedInvoiceService(IInvoiceService inner, ITwoLevelCache cache) : IInvoiceService
{
    // ---------- читання ----------

    public Task<InvoiceDetailsDto> GetByIdAsync(long id, CancellationToken ct = default)
        => cache.GetOrCreateAsync(CacheKeys.Invoice(id), "invoice", CachePolicy.Entity,
            c => inner.GetByIdAsync(id, c), ct);

    public Task<IReadOnlyList<InvoiceDto>> GetOverdueAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(CacheKeys.Overdue, "invoices-overdue", CachePolicy.List,
            c => inner.GetOverdueAsync(c), ct);

    // списки з довільними фільтрами мають надто багато комбінацій ключів — не кешуємо (свідоме рішення)
    public Task<PagedResult<InvoiceDto>> GetListAsync(InvoiceQuery query, CancellationToken ct = default)
        => inner.GetListAsync(query, ct);

    public Task<IReadOnlyList<InvoiceDto>> GetByCategoryAsync(string categorySlug, DateTime? since, CancellationToken ct = default)
        => inner.GetByCategoryAsync(categorySlug, since, ct);

    // ---------- запис + інвалідація ----------

    public async Task<InvoiceDetailsDto> CreateAsync(CreateInvoiceDto input, CancellationToken ct = default)
        => await Invalidate(await inner.CreateAsync(input, ct), ct);

    public async Task<InvoiceDetailsDto> MarkPaidAsync(long id, CancellationToken ct = default)
        => await Invalidate(await inner.MarkPaidAsync(id, ct), ct);

    public async Task<InvoiceDetailsDto> VoidAsync(long id, CancellationToken ct = default)
        => await Invalidate(await inner.VoidAsync(id, ct), ct);

    public async Task<InvoiceDetailsDto> RescheduleAsync(long id, RescheduleInvoiceDto input, CancellationToken ct = default)
        => await Invalidate(await inner.RescheduleAsync(id, input, ct), ct);

    public async Task<InvoiceDetailsDto> AddSurchargeAsync(long id, AddSurchargeDto input, CancellationToken ct = default)
        => await Invalidate(await inner.AddSurchargeAsync(id, input, ct), ct);

    public async Task<InvoiceDetailsDto> AssignCategoryAsync(long id, AssignCategoryDto input, CancellationToken ct = default)
        => await Invalidate(await inner.AssignCategoryAsync(id, input, ct), ct);

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        // мерчант потрібен, щоб скинути й його картку (там вкладений список рахунків) — читаємо ДО видалення, повз кеш
        long? merchantId = null;
        try { merchantId = (await inner.GetByIdAsync(id, ct)).MerchantId; } catch { /* не знайдено — Delete сам кине 404 */ }

        await inner.DeleteAsync(id, ct);

        const string reason = "invoice deleted";
        await cache.RemoveAsync(CacheKeys.Invoice(id), "invoice", reason, ct);
        await cache.RemoveAsync(CacheKeys.Overdue, "invoices-overdue", reason, ct);
        if (merchantId is { } m) await cache.RemoveAsync(CacheKeys.Merchant(m), "merchant", reason, ct);
    }

    /// <summary>Після зміни рахунку скидаємо: його картку, список прострочених і картку мерчанта (вона містить рахунки).</summary>
    private async Task<InvoiceDetailsDto> Invalidate(InvoiceDetailsDto changed, CancellationToken ct)
    {
        var reason = $"invoice {changed.Id} changed (status {changed.Status})";
        await cache.RemoveAsync(CacheKeys.Invoice(changed.Id), "invoice", reason, ct);
        await cache.RemoveAsync(CacheKeys.Overdue, "invoices-overdue", reason, ct);
        await cache.RemoveAsync(CacheKeys.Merchant(changed.MerchantId), "merchant", reason, ct);
        return changed;
    }
}
