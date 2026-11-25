using Payments.Domain.Models;
using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Payments.Api.IntegrationEvents;

// Лаб. 08. Платежі («Замовлення») тримають ЛОКАЛЬНУ КОПІЮ довідника рахунків Білінгу (dbo.Invoices; політика з Лаб. 01:
// Number, MerchantName, Amount, Currency «оновлюються подіями з Білінгу»). Знімки в PaymentAllocations
// (InvoiceNumber, InvoiceAmount) НЕ чіпаємо: це фіксація на момент розподілу, вони за політикою не оновлюються —
// так само, як OrderItems.UnitPrice в оформлених замовленнях.

public sealed class InvoiceIssuedHandler(IdempotentEventProcessor processor, ILogger<InvoiceIssuedHandler> logger)
    : IIntegrationEventHandler<InvoiceIssuedEvent>
{
    public Task HandleAsync(InvoiceIssuedEvent e, CancellationToken ct) => processor.ProcessAsync(e, async uow =>
    {
        var result = await uow.Invoices.UpsertSnapshotAsync(
            new Invoice { Id = e.InvoiceId, Number = e.Number, MerchantName = e.MerchantName, Amount = e.Amount, Currency = e.Currency },
            e.OccurredAt, ct);
        logger.LogInformation(
            "InvoiceIssued {EventId}: invoice {InvoiceId} {InvoiceNumber} copy {Result} (merchant {MerchantName}, {Amount} {Currency}, CorrelationId {CorrelationId})",
            e.EventId, e.InvoiceId, e.Number, result, e.MerchantName, e.Amount, e.Currency, e.CorrelationId);
    }, ct);
}

public sealed class InvoiceAmountChangedHandler(IdempotentEventProcessor processor, ILogger<InvoiceAmountChangedHandler> logger)
    : IIntegrationEventHandler<InvoiceAmountChangedEvent>
{
    public Task HandleAsync(InvoiceAmountChangedEvent e, CancellationToken ct) => processor.ProcessAsync(e, async uow =>
    {
        // подія самодостатня: якщо копії ще немає (InvoiceIssued запізнився), вона створюється з даних цієї події
        var result = await uow.Invoices.UpsertSnapshotAsync(
            new Invoice { Id = e.InvoiceId, Number = e.Number, MerchantName = e.MerchantName, Amount = e.NewAmount, Currency = e.Currency },
            e.OccurredAt, ct);
        logger.LogInformation(
            "InvoiceAmountChanged {EventId}: invoice {InvoiceId} amount {OldAmount} -> {NewAmount} {Currency}, copy {Result} (CorrelationId {CorrelationId})",
            e.EventId, e.InvoiceId, e.OldAmount, e.NewAmount, e.Currency, result, e.CorrelationId);
    }, ct);
}

public sealed class MerchantRenamedHandler(IdempotentEventProcessor processor, ILogger<MerchantRenamedHandler> logger)
    : IIntegrationEventHandler<MerchantRenamedEvent>
{
    public Task HandleAsync(MerchantRenamedEvent e, CancellationToken ct) => processor.ProcessAsync(e, async uow =>
    {
        var rows = await uow.Invoices.RenameMerchantAsync(e.OldName, e.NewName, e.OccurredAt, ct);
        logger.LogInformation(
            "MerchantRenamed {EventId}: merchant {MerchantId} '{OldName}' -> '{NewName}', {Rows} invoice copies updated (CorrelationId {CorrelationId})",
            e.EventId, e.MerchantId, e.OldName, e.NewName, rows, e.CorrelationId);
    }, ct);
}
