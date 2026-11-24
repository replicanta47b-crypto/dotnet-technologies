namespace Payments.Domain.Models;

/// <summary>Деталі платежу (1:1 з Payment). Таблиця dbo.PaymentDetails.</summary>
public class PaymentDetails
{
    public string Method { get; set; } = "";
    public string PayerAccount { get; set; } = "";
    public string? Description { get; set; }
}
