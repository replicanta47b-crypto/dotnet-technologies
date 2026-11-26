using System.Text.Json.Serialization;
using Platform.Aggregator.Caching;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Security;
using Platform.Security;
using Platform.Aggregator.Services;
using Platform.Contracts.Billing;
using Platform.Contracts.Disputes;
using Platform.Contracts.Payments;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, CorrelationIdHandler для всіх HttpClient, service discovery, resilience

// ---- Лаб. 09: вхід — JWT (будь-який емітент платформи), усі endpoints агрегатора лише для автентифікованих;
//      вихід — Client Credentials: Aggregator звертається до мікросервісів від власного імені (клієнт aggregator_service) ----
builder.AddPlatformAuthentication(service: null);
builder.Services.Configure<AggregatorClientOptions>(builder.Configuration.GetSection(AggregatorClientOptions.Section));
builder.Services.AddHttpClient<ClientCredentialsTokenProvider>();
builder.Services.AddTransient<BearerTokenHandler>();

// ---- Resilience (Лаб. 07). ServiceDefaults через ConfigureHttpClientDefaults вже додає Standard Resilience Handler
//      до КОЖНОГО клієнта. Для клієнтів із власними вимогами спершу RemoveAllResilienceHandlers(), потім один власний
//      AddStandardResilienceHandler — інакше обробники нашаровувались би. Значення — у Resilience/ClientPolicies.cs.
//      Критичні (payments, billing): більше повторів, чутливіший breaker. Некритичний (disputes): швидко здається. ----

// ---- REST-шлях (Лаб. 05): typed HttpClients. "http://" — явно HTTP/1.1-endpoint сервісу ("https+http" обрав би https,
//      який тепер потрібен лише для gRPC); ім'я — ресурс з AppHost (service discovery) ----
builder.Services.AddHttpClient<PaymentsClient>(c => c.BaseAddress = new("http://payments-api"))
    .AddHttpMessageHandler<BearerTokenHandler>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(o => ClientPolicies.Apply(o, ClientPolicies.Payments, grpc: false));
builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new("http://billing-api"))
    .AddHttpMessageHandler<BearerTokenHandler>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(o => ClientPolicies.Apply(o, ClientPolicies.Billing, grpc: false));
builder.Services.AddHttpClient<DisputesClient>(c => c.BaseAddress = new("http://disputes-api"))
    .AddHttpMessageHandler<BearerTokenHandler>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(o => ClientPolicies.Apply(o, ClientPolicies.Disputes, grpc: false));

// ---- gRPC-шлях (Лаб. 06): згенеровані клієнти. Адреса "https://<ресурс>" → service discovery бере https-endpoint,
//      де за ALPN узгоджується HTTP/2. GrpcClientInterceptor передає CorrelationId у metadata.
//      Лаб. 07: retry за gRPC-статусами (ServiceConfig каналу) + HTTP-політика того ж рівня критичності. ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<GrpcClientInterceptor>();
AddGrpcClient<PaymentsService.PaymentsServiceClient>("https://payments-api", ClientPolicies.Payments);
AddGrpcClient<BillingService.BillingServiceClient>("https://billing-api", ClientPolicies.Billing);
AddGrpcClient<DisputesService.DisputesServiceClient>("https://disputes-api", ClientPolicies.Disputes);

// клієнти стандартного gRPC Health Checking Protocol — для health checks залежностей (іменовані: по одному на сервіс)
AddGrpcHealthClient("payments-health", "https://payments-api");
AddGrpcHealthClient("billing-health", "https://billing-api");
AddGrpcHealthClient("disputes-health", "https://disputes-api");

void AddGrpcClient<TClient>(string address, ClientPolicy policy) where TClient : class
{
    var clientBuilder = builder.Services
        .AddGrpcClient<TClient>(o => o.Address = new Uri(address))
        .ConfigureChannel(channel => channel.ServiceConfig = ClientPolicies.GrpcServiceConfig(policy))   // retry за Unavailable / ResourceExhausted
        .AddInterceptor<GrpcClientInterceptor>()
        .AddHttpMessageHandler<BearerTokenHandler>();   // Лаб. 09: service-токен у кожному gRPC-виклику
    AllowDevCertificate(clientBuilder);

    // спершу прибираємо стандартний обробник із ConfigureHttpClientDefaults, потім додаємо один власний
    clientBuilder
        .RemoveAllResilienceHandlers()
        .AddStandardResilienceHandler(o => ClientPolicies.Apply(o, policy, grpc: true));
}

void AddGrpcHealthClient(string name, string address)
{
    var clientBuilder = builder.Services
        .AddGrpcClient<Health.HealthClient>(name, o => o.Address = new Uri(address))
        .AddInterceptor<GrpcClientInterceptor>();
    AllowDevCertificate(clientBuilder);
}

// Лише в Development: сертифікат ASP.NET Core dev-cert може бути не довіреним у системі — приймаємо його,
// щоб лабораторна запускалась без `dotnet dev-certs https --trust`. У Production перевірка сертифіката лишається повною.
void AllowDevCertificate(IHttpClientBuilder clientBuilder)
{
    if (builder.Environment.IsDevelopment())
        clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        });
}

// ---- Лаб. 06: Redis (ресурс "redis" з AppHost) як IDistributedCache для зібраних карток ----
// Вбудовану перевірку Aspire вимкнено: вона б давала Unhealthy; власна (RedisCacheHealthCheck) дає Degraded — кеш необов'язковий
builder.AddRedisDistributedCache("redis", settings => settings.DisableHealthChecks = true);
builder.Services.AddSingleton<AggregatorCache>();

builder.Services.AddScoped<HttpPaymentCardSource>();
builder.Services.AddScoped<GrpcPaymentCardSource>();
builder.Services.AddScoped<PaymentCardService>();

// ---- Health checks (Лаб. 07): усі з тегом "ready" → /health/ready. Redis — Degraded (необов'язковий);
//      payments — критичний (Unhealthy), billing і disputes — Degraded: Aggregator і без них віддасть часткову відповідь ----
builder.Services.AddHealthChecks()
    .AddCheck<RedisCacheHealthCheck>("redis-cache", HealthStatus.Degraded, ["cache", "redis", "ready"], TimeSpan.FromSeconds(3))
    .Add(GrpcCheck("payments-api", "payments-health", HealthStatus.Unhealthy))
    .Add(GrpcCheck("billing-api", "billing-health", HealthStatus.Degraded))
    .Add(GrpcCheck("disputes-api", "disputes-health", HealthStatus.Degraded));

static HealthCheckRegistration GrpcCheck(string name, string clientName, HealthStatus failureStatus) =>
    new(name, sp => ActivatorUtilities.CreateInstance<GrpcDependencyHealthCheck>(sp, clientName, name),
        failureStatus, ["grpc", "downstream", "ready"], TimeSpan.FromSeconds(3));

// JSON: camelCase (за замовчуванням), null-значення не серіалізуються
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// колбеки circuit breaker логують через цей logger
ClientPolicies.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Resilience");

app.UseMiddleware<CorrelationIdMiddleware>();   // для запитів у обхід Gateway CorrelationId генерується тут
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.UsePlatformAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();   // /health, /health/live, /health/ready, /health/startup

app.Run();
