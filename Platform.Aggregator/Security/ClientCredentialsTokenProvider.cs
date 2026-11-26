using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Platform.Security;

namespace Platform.Aggregator.Security;

/// <summary>Налаштування клієнта-сервісу Aggregator у IdentityServer (секція «Aggregator:Client»).</summary>
public sealed class AggregatorClientOptions
{
    public const string Section = "Aggregator:Client";
    public string ClientId { get; set; } = "aggregator_service";
    // Лише для локальної розробки (збігається із seed IdentityServer). У Production — секрет із менеджера секретів.
    public string ClientSecret { get; set; } = "aggregator-dev-secret";
    public string Scope { get; set; } = "payments.read billing.read disputes.read";
}

/// <summary>
/// OAuth 2.0 Client Credentials: Aggregator (сервіс, а не користувач) сам отримує access token в IdentityServer
/// (grant_type=client_credentials). У такому токені НЕМАЄ claim «sub» — немає користувача, лише client_id.
/// Токен кешується до закінчення (мінус запас 30 с), одночасні запити не плодять зайвих звернень до IdentityServer.
/// </summary>
public sealed class ClientCredentialsTokenProvider(
    HttpClient http, IOptions<PlatformAuthOptions> auth, IOptions<AggregatorClientOptions> client,
    ILogger<ClientCredentialsTokenProvider> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expires) return _token;

            var o = client.Value;
            var endpoint = auth.Value.IdentityServer.Authority.TrimEnd('/') + "/connect/token";
            using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.ClientId,
                ["client_secret"] = o.ClientSecret,
                ["scope"] = o.Scope
            }), ct);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _token = json.RootElement.GetProperty("access_token").GetString()!;
            var seconds = json.RootElement.GetProperty("expires_in").GetInt32();
            _expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(seconds - 30, 5));
            logger.LogInformation("Aggregator отримав service-токен (client_credentials) на {Seconds} с", seconds);
            return _token;
        }
        finally { _lock.Release(); }
    }
}

/// <summary>Додає service-токен до кожного вихідного запиту (REST і gRPC) до мікросервісів.</summary>
public sealed class BearerTokenHandler(ClientCredentialsTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }
}
