using System.Net;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// Строго типізовані клієнти: кожен інкапсулює взаємодію з одним сервісом. Адреса — логічне ім'я (service discovery),
// CorrelationId додає CorrelationIdHandler із ServiceDefaults, resilience — стандартний обробник ServiceDefaults.

public class PaymentsClient(HttpClient http)
{
    /// <summary>null — платежу немає (404). Інші помилки (сервіс недоступний тощо) — виняток.</summary>
    public async Task<PaymentDto?> GetPaymentAsync(long id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/payments/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentDto>(ct);
    }
}

public class BillingClient(HttpClient http)
{
    public async Task<BillingInvoiceDto?> GetInvoiceAsync(long id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"/api/invoices/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BillingInvoiceDto>(ct);
    }
}

public class DisputesClient(HttpClient http)
{
    public async Task<IReadOnlyList<DisputeDto>> GetByPaymentAsync(long paymentId, CancellationToken ct)
    {
        var page = await http.GetFromJsonAsync<PagedDisputes>($"/api/disputes?paymentId={paymentId}&pageSize=50", ct);
        return page?.Items ?? [];
    }
}
