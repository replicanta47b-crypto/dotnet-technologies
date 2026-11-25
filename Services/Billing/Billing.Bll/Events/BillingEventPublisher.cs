using Billing.Domain.Entities;
using Microsoft.Extensions.Logging;
using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Billing.Bll.Events;

/// <summary>Публікація подій Білінгу. Викликається лише ПІСЛЯ успішного SaveChangesAsync.</summary>
public interface IBillingEventPublisher
{
    Task InvoiceIssuedAsync(Invoice invoice, string merchantName, CancellationToken ct = default);
    Task InvoiceAmountChangedAsync(Invoice invoice, string merchantName, decimal oldAmount, CancellationToken ct = default);
    Task MerchantRenamedAsync(long merchantId, string oldName, string newName, CancellationToken ct = default);
}

/// <summary>
/// Збій брокера НЕ відкочує вже збережену бізнес-зміну: він логується з EventId, а запит завершується успішно.
/// Ціна цього рішення — подія може бути втрачена, якщо застосунок упаде між збереженням і публікацією або брокер
/// недоступний. Повну гарантію доставки дає Transactional Outbox (подія пишеться в БД в одній транзакції зі змінами,
/// окремий процес її публікує) — див. розділ «Обмеження» в docs/lab-08/README.md.
/// </summary>
public sealed class BillingEventPublisher(IEventPublisher publisher, ILogger<BillingEventPublisher> logger) : IBillingEventPublisher
{
    public Task InvoiceIssuedAsync(Invoice invoice, string merchantName, CancellationToken ct = default)
        => PublishAsync(new InvoiceIssuedEvent(invoice.Id, invoice.Number, invoice.MerchantId, merchantName,
                invoice.TotalAmount, invoice.Currency) { CorrelationId = TracePropagation.CurrentCorrelationId() },
            BillingEventsTopology.InvoiceIssued, ct);

    public Task InvoiceAmountChangedAsync(Invoice invoice, string merchantName, decimal oldAmount, CancellationToken ct = default)
        => PublishAsync(new InvoiceAmountChangedEvent(invoice.Id, invoice.Number, invoice.MerchantId, merchantName,
                oldAmount, invoice.TotalAmount, invoice.Currency) { CorrelationId = TracePropagation.CurrentCorrelationId() },
            BillingEventsTopology.InvoiceAmountChanged, ct);

    public Task MerchantRenamedAsync(long merchantId, string oldName, string newName, CancellationToken ct = default)
        => PublishAsync(new MerchantRenamedEvent(merchantId, oldName, newName) { CorrelationId = TracePropagation.CurrentCorrelationId() },
            BillingEventsTopology.MerchantRenamed, ct);

    private async Task PublishAsync<TEvent>(TEvent @event, string routingKey, CancellationToken ct) where TEvent : IntegrationEvent
    {
        try
        {
            await publisher.PublishAsync(@event, BillingEventsTopology.Exchange, routingKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Failed to publish {EventType} {EventId} to {Exchange}/{RoutingKey}: the change is saved, but subscribers will not be notified (CorrelationId {CorrelationId})",
                typeof(TEvent).Name, @event.EventId, BillingEventsTopology.Exchange, routingKey, @event.CorrelationId);
        }
    }
}
