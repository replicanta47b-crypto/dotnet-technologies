using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Billing.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08. Білінг — ВЛАСНИК даних про рахунки й мерчантів, тож він видавець подій:
///   invoice.issued · invoice.amount_changed · merchant.renamed  → exchange «billing-events» (topic).
/// Контракти подій — Platform.EventBus/Contracts/BillingEvents.cs (спільні для видавця й споживачів).
/// Публікують бізнес-сервіси Billing.Bll (IBillingEventPublisher) після успішного SaveChangesAsync.
/// </summary>
public static class BillingIntegrationEvents
{
    public static WebApplicationBuilder AddBillingIntegrationEvents(this WebApplicationBuilder builder)
    {
        builder.AddEventBus();                       // AddRabbitMQClient("messaging"): IConnection + health check + readiness
        builder.Services.AddEventPublisher();        // IEventPublisher (JSON, persistent, traceparent у заголовках)

        // Оголошуємо exchange і черги всіх споживачів: події не губляться, поки Payments/Disputes ще не стартували.
        builder.Services.AddTopologyInitializer(BillingEventsTopology.AllQueues);
        return builder;
    }
}
