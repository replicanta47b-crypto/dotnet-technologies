using Billing.Domain.Exceptions;

namespace Billing.Domain.Entities;

/// <summary>Рядок рахунку (1:N). Сума рядка рахується в БД (обчислювана стовпчик LineTotal).</summary>
public class InvoiceLine
{
    public long Id { get; private set; }
    public long InvoiceId { get; private set; }
    public string Description { get; private set; } = "";
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }
    public int SortOrder { get; private set; }

    private InvoiceLine() { }

    internal InvoiceLine(string description, decimal quantity, decimal unitPrice, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("Line description is required");
        if (description.Length > 300) throw new DomainException("Line description is too long (max 300)");
        if (quantity <= 0) throw new DomainException("Quantity must be positive");
        if (unitPrice <= 0) throw new DomainException("Unit price must be positive");
        Description = description.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = quantity * unitPrice;
        SortOrder = sortOrder;
    }
}
