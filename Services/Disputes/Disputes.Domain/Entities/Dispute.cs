using Disputes.Domain.Common;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.ValueObjects;
using MongoDB.Bson;

namespace Disputes.Domain.Entities;

/// <summary>
/// Агрегат «Спір» (колекція disputes з Лаб. 01). Вкладені: openedBy (Author), evidence[] та comments[];
/// threadId — посилання на disputeThreads (гібридна модель). Гнучка схема: додаткові поля документа
/// (fraudDetails, serviceProof, tags, ...) зберігаються в ExtraElements і не губляться при оновленні.
/// </summary>
public class Dispute : BaseEntity
{
    public static readonly IReadOnlyList<string> AllowedCurrencies = new[] { "UAH", "USD", "EUR" };

    // приватні поля колекцій: BSON-мапінг налаштовується в Infrastructure (BsonClassMap), а не атрибутами
    private List<EvidenceItem> _evidence = new();
    private List<Comment> _comments = new();

    public long PaymentId { get; private set; }
    public long InvoiceId { get; private set; }
    public long MerchantId { get; private set; }
    public string MerchantName { get; private set; } = "";      // денормалізовано з Білінгу
    public decimal Amount { get; private set; }                 // знімок суми на момент відкриття спору
    public string Currency { get; private set; } = "";
    public DisputeReason Reason { get; private set; }
    public DisputeStatus Status { get; private set; }
    public Author OpenedBy { get; private set; } = null!;
    public DateTime OpenedAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public ObjectId? ThreadId { get; private set; }

    public IReadOnlyCollection<EvidenceItem> Evidence => _evidence.AsReadOnly();
    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();

    /// <summary>Поля документа, яких немає в моделі (flexible schema). Мапиться як extra elements.</summary>
    public BsonDocument ExtraElements { get; private set; } = new();

    private Dispute() { }   // для десеріалізації

    public Dispute(long paymentId, long invoiceId, long merchantId, string merchantName,
                   decimal amount, string currency, DisputeReason reason, Author openedBy)
    {
        if (paymentId <= 0) throw new DomainException("PaymentId must be positive");
        if (invoiceId <= 0) throw new DomainException("InvoiceId must be positive");
        if (merchantId <= 0) throw new DomainException("MerchantId must be positive");
        if (string.IsNullOrWhiteSpace(merchantName)) throw new DomainException("MerchantName is required");
        if (merchantName.Length > 200) throw new DomainException("MerchantName is too long (max 200)");
        if (amount <= 0) throw new DomainException("Amount must be positive");
        if (!AllowedCurrencies.Contains(currency)) throw new DomainException($"Currency must be one of: {string.Join(", ", AllowedCurrencies)}");
        ArgumentNullException.ThrowIfNull(openedBy);

        PaymentId = paymentId;
        InvoiceId = invoiceId;
        MerchantId = merchantId;
        MerchantName = merchantName.Trim();
        Amount = amount;
        Currency = currency;
        Reason = reason;
        Status = DisputeStatus.Open;
        OpenedBy = openedBy;
        OpenedAt = DateTime.UtcNow;
    }

    public void AddComment(string authorName, string text)
    {
        if (Status == DisputeStatus.Closed)
            throw new DomainException("Cannot comment on a closed dispute");

        _comments.Add(new Comment(authorName, text, DateTime.UtcNow));   // вкладений документ
        Touch();
    }

    public void AddEvidence(string type, string? url)
    {
        if (Status is DisputeStatus.Closed or DisputeStatus.Won or DisputeStatus.Lost)
            throw new DomainException("Evidence cannot be added after the dispute is resolved");

        _evidence.Add(new EvidenceItem(type, url, DateTime.UtcNow));
        Touch();
    }

    /// <summary>Дозволені переходи: Open → UnderReview | Closed; UnderReview → Won | Lost; Won | Lost → Closed.</summary>
    public void ChangeStatus(DisputeStatus next)
    {
        if (next == Status)
            throw new DomainException($"Dispute is already in status '{EnumText.ToText(Status)}'");

        var allowed = Status switch
        {
            DisputeStatus.Open => next is DisputeStatus.UnderReview or DisputeStatus.Closed,
            DisputeStatus.UnderReview => next is DisputeStatus.Won or DisputeStatus.Lost,
            DisputeStatus.Won or DisputeStatus.Lost => next == DisputeStatus.Closed,
            _ => false
        };

        if (!allowed)
            throw new DomainException(
                $"Transition '{EnumText.ToText(Status)}' → '{EnumText.ToText(next)}' is not allowed");

        Status = next;
        if (next is DisputeStatus.Won or DisputeStatus.Lost or DisputeStatus.Closed)
            ResolvedAt ??= DateTime.UtcNow;
        Touch();
    }

    public void AttachThread(ObjectId threadId)
    {
        ThreadId = threadId;
        Touch();
    }

    /// <summary>Спір, який зараз розглядається, видаляти не можна.</summary>
    public void EnsureCanBeDeleted()
    {
        if (Status == DisputeStatus.UnderReview)
            throw new DomainException("A dispute under review cannot be deleted");
    }
}
