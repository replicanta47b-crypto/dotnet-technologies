using System.ComponentModel.DataAnnotations;

namespace Payments.Bll.Dto;

public class PaymentDetailsDto
{
    public string Method { get; set; } = string.Empty;
    public string PayerAccount { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Позиція розподілу платежу на рахунок; номер і сума рахунку — знімок на момент розподілу.</summary>
public class PaymentAllocationDto
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal InvoiceAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public class PaymentDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime InitiatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>RowVersion у Base64 — для оптимістичної конкурентності у ChangeStatus.</summary>
    public string? RowVersion { get; set; }

    public PaymentDetailsDto? Details { get; set; }
    public List<PaymentAllocationDto> Allocations { get; set; } = new();
}

/// <summary>Позиція розподілу у запиті. Amount = null — сплатити рахунок повністю.</summary>
public class AllocationRequestDto
{
    [Range(1, long.MaxValue)]
    public long InvoiceId { get; init; }

    [Range(0.01, 1_000_000_000)]
    public decimal? Amount { get; init; }
}

/// <summary>Створення платежу разом із розподілом на рахунки (одна транзакція).</summary>
public class CreatePaymentDto
{
    [Range(1, long.MaxValue)]
    public long CustomerId { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string Method { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string PayerAccount { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    [Required, MinLength(1)]
    public List<AllocationRequestDto> Allocations { get; init; } = new();
}

public class ChangePaymentStatusDto
{
    [Required, StringLength(20)]
    public string NewStatus { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; init; }

    /// <summary>Необов'язковий RowVersion (Base64) для перевірки конкурентних змін.</summary>
    public string? RowVersion { get; init; }
}

public class AuthorizePaymentDto
{
    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; init; }

    [StringLength(100)]
    public string? ProviderRef { get; init; }
}
