using Billing.Domain.Exceptions;

namespace Billing.Domain.Entities;

/// <summary>Додаткові відомості про рахунок. 1:1 з Invoice — первинний ключ є водночас зовнішнім.</summary>
public class InvoiceDetail
{
    public long InvoiceId { get; private set; }
    public string? CustomerRef { get; private set; }
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }
    public string? Note { get; private set; }

    private InvoiceDetail() { }

    internal InvoiceDetail(string? customerRef, DateOnly? periodStart, DateOnly? periodEnd, string? note)
    {
        if (periodStart is not null && periodEnd is not null && periodEnd < periodStart)
            throw new DomainException("Period end cannot be earlier than period start");
        CustomerRef = customerRef?.Trim();
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Note = note?.Trim();
    }
}
