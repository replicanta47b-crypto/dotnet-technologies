namespace Billing.Domain.Enums;

/// <summary>Життєвий цикл рахунку. У БД зберігається рядком (CHECK-обмеження CK_Invoices_Status).</summary>
public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Void
}
