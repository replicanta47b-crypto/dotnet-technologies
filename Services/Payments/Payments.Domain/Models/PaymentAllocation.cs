namespace Payments.Domain.Models;

/// <summary>
/// Розподіл платежу на рахунок (M:N Payment ↔ Invoice). Таблиця dbo.PaymentAllocations.
/// InvoiceNumber і InvoiceAmount — ЗНІМОК на момент створення розподілу, не оновлюються.
/// </summary>
public class PaymentAllocation
{
    public long InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public decimal InvoiceAmount { get; set; }
    public decimal AllocatedAmount { get; set; }
}
