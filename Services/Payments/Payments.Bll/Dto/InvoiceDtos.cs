using System.ComponentModel.DataAnnotations;

namespace Payments.Bll.Dto;

public class InvoiceDto
{
    public long Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime SyncedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Рахунок надходить із Білінгу (його Id призначає Білінг), тому Id передається явно.</summary>
public class CreateInvoiceDto
{
    [Range(1, long.MaxValue)]
    public long Id { get; init; }

    [Required, StringLength(50)]
    public string Number { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string MerchantName { get; init; } = string.Empty;

    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = string.Empty;
}

public class UpdateInvoiceDto
{
    [Required, StringLength(50)]
    public string Number { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string MerchantName { get; init; } = string.Empty;

    [Range(0.01, 1_000_000_000)]
    public decimal Amount { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = string.Empty;
}
