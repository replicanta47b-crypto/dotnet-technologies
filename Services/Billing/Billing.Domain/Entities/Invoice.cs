using Billing.Domain.Enums;
using Billing.Domain.Exceptions;

namespace Billing.Domain.Entities;

/// <summary>
/// Рахунок. Бізнес-правила інкапсульовані: приватні setter-и, валідація в конструкторі,
/// зміна стану — лише методами (за аналогією з CatalogItem.RemoveStock/AddStock з eShop).
/// Життєвий цикл: Draft → Issued → (Overdue) → Paid; Void — з будь-якого стану, крім Paid.
/// </summary>
public class Invoice
{
    public static readonly string[] AllowedCurrencies = { "UAH", "USD", "EUR" };

    public long Id { get; private set; }
    public string Number { get; private set; } = "";
    public long MerchantId { get; private set; }
    public Merchant Merchant { get; private set; } = null!;
    public string Currency { get; private set; } = "UAH";
    public decimal TotalAmount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();   // оптимістична конкурентність

    public InvoiceDetail? Detail { get; private set; }                       // 1:1
    public List<InvoiceLine> Lines { get; } = new();                         // 1:N
    public List<InvoiceCategory> InvoiceCategories { get; } = new();         // M:N

    private Invoice() { }                                                    // для EF Core

    public Invoice(string number, long merchantId, string currency, DateOnly dueDate, string? createdBy = null)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new DomainException("Invoice number is required");
        if (number.Length > 50) throw new DomainException("Invoice number is too long (max 50)");
        if (merchantId <= 0) throw new DomainException("Merchant is required");

        var normalized = (currency ?? "").Trim().ToUpperInvariant();
        if (!AllowedCurrencies.Contains(normalized))
            throw new DomainException($"Currency must be one of: {string.Join(", ", AllowedCurrencies)}");

        Number = number.Trim();
        MerchantId = merchantId;
        Currency = normalized;
        DueDate = dueDate;
        Status = InvoiceStatus.Draft;
        CreatedBy = createdBy;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Додає рядок і перераховує підсумок. Лише для чернетки.</summary>
    public InvoiceLine AddLine(string description, decimal quantity, decimal unitPrice)
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException("Lines can be changed only while the invoice is in Draft status");

        var line = new InvoiceLine(description, quantity, unitPrice, Lines.Count);
        Lines.Add(line);
        TotalAmount = Math.Round(Lines.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
        return line;
    }

    /// <summary>
    /// Донарахування до вже виставленого рахунку (Issued/Overdue): додає рядок і збільшує суму.
    /// Це бізнес-факт «сума рахунку змінилась» — Білінг публікує InvoiceAmountChangedEvent.
    /// </summary>
    public InvoiceLine AddSurcharge(string description, decimal amount)
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
            throw new DomainException($"A surcharge can be added only to an Issued or Overdue invoice (current status: {Status})");

        var line = new InvoiceLine(description, 1, amount, Lines.Count);
        Lines.Add(line);
        TotalAmount = Math.Round(Lines.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
        Touch();
        return line;
    }

    public void SetDetail(string? customerRef, DateOnly? periodStart, DateOnly? periodEnd, string? note)
        => Detail = new InvoiceDetail(customerRef, periodStart, periodEnd, note);

    /// <summary>Draft → Issued: рахунок має містити хоча б один рядок.</summary>
    public void Issue()
    {
        if (Status != InvoiceStatus.Draft)
            throw new DomainException($"Only a Draft invoice can be issued (current status: {Status})");
        if (Lines.Count == 0 || TotalAmount <= 0)
            throw new DomainException("An invoice without lines cannot be issued");
        ChangeStatus(InvoiceStatus.Issued);
    }

    /// <summary>Issued/Overdue → Paid.</summary>
    public void MarkPaid()
    {
        if (Status is not (InvoiceStatus.Issued or InvoiceStatus.Overdue))
            throw new DomainException($"Cannot mark an invoice as paid from status {Status}");
        ChangeStatus(InvoiceStatus.Paid);
    }

    /// <summary>Issued → Overdue (термін сплати минув).</summary>
    public void MarkOverdue()
    {
        if (Status != InvoiceStatus.Issued)
            throw new DomainException($"Only an Issued invoice can become overdue (current status: {Status})");
        ChangeStatus(InvoiceStatus.Overdue);
    }

    /// <summary>Анулювання: сплачений рахунок анулювати не можна.</summary>
    public void Void()
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Void)
            throw new DomainException($"Cannot void an invoice in status {Status}");
        ChangeStatus(InvoiceStatus.Void);
    }

    public void Reschedule(DateOnly newDueDate)
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Void)
            throw new DomainException($"Cannot change the due date of an invoice in status {Status}");
        DueDate = newDueDate;
        Touch();
    }

    /// <summary>Призначає категорію. Не більше однієї основної категорії на рахунок (інваріант UX_IC_Primary).</summary>
    public InvoiceCategory AssignCategory(long categoryId, bool isPrimary)
    {
        if (InvoiceCategories.Any(c => c.CategoryId == categoryId))
            throw new DomainException($"Category {categoryId} is already assigned to this invoice");
        if (isPrimary && InvoiceCategories.Any(c => c.IsPrimary))
            throw new DomainException("The invoice already has a primary category");

        var link = new InvoiceCategory(categoryId, isPrimary);
        InvoiceCategories.Add(link);
        Touch();
        return link;
    }

    /// <summary>М'яке видалення: лише чернетки й анульовані рахунки.</summary>
    public void MarkDeleted()
    {
        if (Status is not (InvoiceStatus.Draft or InvoiceStatus.Void))
            throw new DomainException($"Only Draft or Void invoices can be deleted (current status: {Status})");
        IsDeleted = true;
        Touch();
    }

    private void ChangeStatus(InvoiceStatus status)
    {
        Status = status;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
