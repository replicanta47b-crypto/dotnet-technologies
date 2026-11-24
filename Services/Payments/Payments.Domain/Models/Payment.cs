namespace Payments.Domain.Models;

/// <summary>
/// Корінь агрегату «Платіж»: Payment + PaymentDetails (1:1) + PaymentAllocations (1:N).
/// Властивості без атрибутів збігаються зі стовпцями dbo.Payments.
/// </summary>
public class Payment
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string Currency { get; set; } = "UAH";
    public string Status { get; set; } = "Created";
    public DateTime InitiatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[]? RowVersion { get; set; }

    // навігація (не стовпці dbo.Payments)
    public PaymentDetails? Details { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = new();
}
