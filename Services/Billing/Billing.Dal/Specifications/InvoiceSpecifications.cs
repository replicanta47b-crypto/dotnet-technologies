using Ardalis.Specification;
using Billing.Domain.Entities;
using Billing.Domain.Enums;

namespace Billing.Dal.Specifications;

/// <summary>Параметри вибірки списку рахунків (не залежить від API-контракту й EF).</summary>
public sealed record InvoiceListCriteria(
    int Page,
    int PageSize,
    string? Search,
    InvoiceStatus? Status,
    long? MerchantId,
    decimal? MinAmount,
    decimal? MaxAmount,
    string SortBy,
    string SortDir);

/// <summary>Лише критерії фільтрації (без включень і пагінації) — використовується і для підрахунку totalCount.</summary>
public class InvoicesFilterSpec : Specification<Invoice>
{
    public InvoicesFilterSpec(InvoiceListCriteria c)
    {
        var search = c.Search;
        var status = c.Status;
        var merchantId = c.MerchantId;
        var min = c.MinAmount;
        var max = c.MaxAmount;

        if (!string.IsNullOrWhiteSpace(search))
            Query.Where(i => i.Number.Contains(search));
        if (status is not null)
            Query.Where(i => i.Status == status);
        if (merchantId is not null)
            Query.Where(i => i.MerchantId == merchantId);
        if (min is not null)
            Query.Where(i => i.TotalAmount >= min);
        if (max is not null)
            Query.Where(i => i.TotalAmount <= max);
    }
}

/// <summary>Сторінка рахунків: ті самі критерії + Include, сортування (лише дозволені поля) та Skip/Take.</summary>
public class InvoicesPageSpec : InvoicesFilterSpec
{
    public static readonly string[] SortFields = { "number", "amount", "duedate" };

    public InvoicesPageSpec(InvoiceListCriteria c) : base(c)
    {
        Query.Include(i => i.Merchant);

        var desc = string.Equals(c.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (c.SortBy.ToLowerInvariant())
        {
            case "amount" when desc: Query.OrderByDescending(i => i.TotalAmount).ThenBy(i => i.Id); break;
            case "amount":           Query.OrderBy(i => i.TotalAmount).ThenBy(i => i.Id); break;
            case "number" when desc: Query.OrderByDescending(i => i.Number).ThenBy(i => i.Id); break;
            case "number":           Query.OrderBy(i => i.Number).ThenBy(i => i.Id); break;
            case "duedate" when desc: Query.OrderByDescending(i => i.DueDate).ThenBy(i => i.Id); break;
            default:                 Query.OrderBy(i => i.DueDate).ThenBy(i => i.Id); break;
        }

        Query.Skip((c.Page - 1) * c.PageSize).Take(c.PageSize);   // OFFSET ... FETCH у SQL
    }
}

/// <summary>Друга специфікація з іншими критеріями: виставлені рахунки, термін сплати яких минув.</summary>
public class OverdueInvoicesSpec : Specification<Invoice>
{
    public OverdueInvoicesSpec(DateOnly today)
    {
        Query.Where(i => i.Status == InvoiceStatus.Issued && i.DueDate < today)
             .Include(i => i.Merchant)
             .OrderBy(i => i.DueDate).ThenBy(i => i.Id);
    }
}
