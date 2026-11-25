using Billing.Domain.Exceptions;

namespace Billing.Domain.Entities;

/// <summary>Мерчант (отримувач платежів). Власник даних — контекст «Білінг».</summary>
public class Merchant
{
    public long Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Country { get; private set; }
    public string? ContactEmail { get; private set; }
    public bool IsActive { get; private set; } = true;

    public List<Invoice> Invoices { get; } = new();           // 1:N

    private Merchant() { }                                     // для EF Core

    public Merchant(string name, string? country, string? contactEmail)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Merchant name is required");
        Name = name.Trim();
        Country = country?.Trim();
        ContactEmail = contactEmail?.Trim();
    }

    /// <summary>Перейменування мерчанта. Повертає false, якщо назва не змінилась (подія тоді не потрібна).</summary>
    public bool Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new DomainException("Merchant name is required");
        if (newName.Trim().Length > 200)
            throw new DomainException("Merchant name is too long (max 200)");
        var trimmed = newName.Trim();
        if (string.Equals(Name, trimmed, StringComparison.Ordinal)) return false;
        Name = trimmed;
        return true;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
