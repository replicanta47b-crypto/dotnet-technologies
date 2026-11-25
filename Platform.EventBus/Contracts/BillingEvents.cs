namespace Platform.EventBus.Contracts;

// Контракти подій Білінгу (власник даних про рахунки й мерчантів). Видавець — Billing.Api,
// споживачі — Payments.Api (копія довідника Invoices) і Disputes.WebApi (денормалізована назва мерчанта).
// Події незмінні й самодостатні: споживач не ходить у Білінг по додаткові дані.

/// <summary>Рахунок випущено (Draft → Issued): споживач створює або оновлює локальну копію рахунку.</summary>
public sealed record InvoiceIssuedEvent(
    long InvoiceId,
    string Number,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency) : IntegrationEvent;

/// <summary>Сума рахунку змінилась (додано донарахування). Аналог ProductPriceChangedEvent з методички.</summary>
public sealed record InvoiceAmountChangedEvent(
    long InvoiceId,
    string Number,
    long MerchantId,
    string MerchantName,
    decimal OldAmount,
    decimal NewAmount,
    string Currency) : IntegrationEvent;

/// <summary>Мерчанта перейменовано: копії назви в інших сервісах треба оновити. Аналог ProductRenamedEvent.</summary>
public sealed record MerchantRenamedEvent(
    long MerchantId,
    string OldName,
    string NewName) : IntegrationEvent;

public static class BillingEventsTopology
{
    public const string Exchange = "billing-events";

    public const string InvoiceIssued = "invoice.issued";
    public const string InvoiceAmountChanged = "invoice.amount_changed";
    public const string MerchantRenamed = "merchant.renamed";

    /// <summary>Платежі зберігають локальну копію рахунків і назву мерчанта в ній: слухають invoice.* і merchant.*.</summary>
    public static readonly ConsumerQueue PaymentsQueue =
        new("payments.billing-sync", Exchange, ["invoice.*", "merchant.*"]);

    /// <summary>Спори зберігають лише назву мерчанта: слухають merchant.*.</summary>
    public static readonly ConsumerQueue DisputesQueue =
        new("disputes.billing-sync", Exchange, ["merchant.*"]);

    public static readonly ConsumerQueue[] AllQueues = [PaymentsQueue, DisputesQueue];
}
