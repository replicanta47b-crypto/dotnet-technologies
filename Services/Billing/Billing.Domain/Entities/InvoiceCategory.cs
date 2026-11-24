namespace Billing.Domain.Entities;

/// <summary>
/// Проміжна сутність M:N «рахунок ↔ категорія» з власними полями AssignedAt та IsPrimary.
/// Автоматичне M:N в EF Core такі поля зберегти не дозволило б.
/// </summary>
public class InvoiceCategory
{
    public long InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;
    public long CategoryId { get; private set; }
    public ServiceCategory Category { get; private set; } = null!;
    public DateTime AssignedAt { get; private set; }
    public bool IsPrimary { get; private set; }

    private InvoiceCategory() { }

    internal InvoiceCategory(long categoryId, bool isPrimary)
    {
        CategoryId = categoryId;
        IsPrimary = isPrimary;
        AssignedAt = DateTime.UtcNow;
    }
}
