using Platform.EventBus;
using Platform.EventBus.Contracts;

namespace Disputes.WebApi.IntegrationEvents;

/// <summary>
/// Лаб. 08. Спори — СПОЖИВАЧ подій Білінгу. Черга disputes.billing-sync прив'язана до exchange billing-events
/// за ключем merchant.* (рахунки спорам не потрібні: зберігається лише назва мерчанта).
/// </summary>
public static class DisputesIntegrationEvents
{
    public static WebApplicationBuilder AddDisputesIntegrationEvents(this WebApplicationBuilder builder)
    {
        builder.AddEventBus();                                     // AddRabbitMQClient("messaging")

        builder.Services.AddEventConsumer(BillingEventsTopology.DisputesQueue, o => o
            .Handle<MerchantRenamedEvent, MerchantRenamedHandler>());
        return builder;
    }
}
