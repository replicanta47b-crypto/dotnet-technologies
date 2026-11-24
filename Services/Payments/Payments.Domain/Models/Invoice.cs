namespace Payments.Domain.Models;

/// <summary>
/// Локальна копія довідника рахунків з контексту «Білінг» (dbo.Invoices у PaymentsDb).
/// Власник даних — BillingDb; Id збігається з Id у Білінгу, без FOREIGN KEY між базами.
/// Поля Number, MerchantName, Amount, Currency оновлюються подіями з Білінгу.
/// </summary>
public class Invoice
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public string MerchantName { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public DateTime SyncedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
