using System.ComponentModel.DataAnnotations;

namespace Billing.Bll.Dto;

// ---------- відповіді ----------

public record InvoiceDto
{
    public long Id { get; init; }
    public string Number { get; init; } = "";
    public long MerchantId { get; init; }
    public string? MerchantName { get; init; }
    public string Currency { get; init; } = "";
    public decimal TotalAmount { get; init; }
    public DateOnly DueDate { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public record InvoiceLineDto
{
    public long Id { get; init; }
    public string Description { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    public int SortOrder { get; init; }
}

public record InvoiceDetailDto
{
    public string? CustomerRef { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
    public string? Note { get; init; }
}

public record InvoiceCategoryDto
{
    public long CategoryId { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public bool IsPrimary { get; init; }
    public DateTime AssignedAt { get; init; }
}

/// <summary>Картка рахунку: усе пов'язане (Eager Loading — один запит).</summary>
public record InvoiceDetailsDto : InvoiceDto
{
    public InvoiceDetailDto? Detail { get; init; }
    public List<InvoiceLineDto> Lines { get; init; } = new();
    public List<InvoiceCategoryDto> Categories { get; init; } = new();
}

public record MerchantDto
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string? Country { get; init; }
    public string? ContactEmail { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Мерчант з рахунками (Explicit Loading — окремий запит).</summary>
public record MerchantDetailsDto : MerchantDto
{
    public List<InvoiceDto> Invoices { get; init; } = new();
}

// ---------- запити (перший рівень валідації — DataAnnotations; складніші правила — FluentValidation) ----------

public record InvoiceLineInput
{
    [Required, StringLength(300)]
    public string Description { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public record CreateInvoiceDto
{
    [Required, StringLength(50)]
    public string Number { get; init; } = "";

    public long MerchantId { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = "UAH";

    public DateOnly DueDate { get; init; }

    [StringLength(100)]
    public string? CustomerRef { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }

    [StringLength(1000)]
    public string? Note { get; init; }

    [Required]
    public List<InvoiceLineInput> Lines { get; init; } = new();
}

public record RescheduleInvoiceDto
{
    public DateOnly DueDate { get; init; }
}

public record AssignCategoryDto
{
    public long CategoryId { get; init; }
    public bool IsPrimary { get; init; }
}

public record CreateMerchantDto
{
    [Required, StringLength(200)]
    public string Name { get; init; } = "";

    [StringLength(100)]
    public string? Country { get; init; }

    [StringLength(256)]
    public string? ContactEmail { get; init; }
}

// ---------- пагінація, фільтрація, сортування ----------

public record InvoiceQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public string? Status { get; init; }
    public long? MerchantId { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string SortBy { get; init; } = "dueDate";
    public string SortDir { get; init; } = "asc";
}

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
