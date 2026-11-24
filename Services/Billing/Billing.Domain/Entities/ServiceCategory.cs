using Billing.Domain.Exceptions;

namespace Billing.Domain.Entities;

/// <summary>Довідник категорій послуг (M:N з рахунками через InvoiceCategory).</summary>
public class ServiceCategory
{
    public long Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";

    public List<InvoiceCategory> InvoiceCategories { get; } = new();   // M:N

    private ServiceCategory() { }

    public ServiceCategory(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Category name is required");
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("Category slug is required");
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
    }
}
