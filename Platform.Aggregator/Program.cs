using System.Text.Json.Serialization;
using Platform.Aggregator.Caching;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.Contracts.Billing;
using Platform.Contracts.Disputes;
using Platform.Contracts.Payments;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, CorrelationIdHandler для всіх HttpClient, service discovery, resilience

// ---- REST-шлях (Лаб. 05): typed HttpClients. "http://" — явно HTTP/1.1-endpoint сервісу ("https+http" обрав би https,
//      який тепер потрібен лише для gRPC); ім'я — ресурс з AppHost (service discovery) ----
builder.Services.AddHttpClient<PaymentsClient>(c => c.BaseAddress = new("http://payments-api"));
builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new("http://billing-api"));
builder.Services.AddHttpClient<DisputesClient>(c => c.BaseAddress = new("http://disputes-api"));

// ---- gRPC-шлях (Лаб. 06): згенеровані клієнти. Адреса "https://<ресурс>" → service discovery бере https-endpoint,
//      де за ALPN узгоджується HTTP/2. GrpcClientInterceptor передає CorrelationId у metadata. ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<GrpcClientInterceptor>();
AddGrpcClient<PaymentsService.PaymentsServiceClient>("https://payments-api");
AddGrpcClient<BillingService.BillingServiceClient>("https://billing-api");
AddGrpcClient<DisputesService.DisputesServiceClient>("https://disputes-api");

void AddGrpcClient<TClient>(string address) where TClient : class
{
    var clientBuilder = builder.Services
        .AddGrpcClient<TClient>(o => o.Address = new Uri(address))
        .AddInterceptor<GrpcClientInterceptor>();

    // Лише в Development: сертифікат ASP.NET Core dev-cert може бути не довіреним у системі — приймаємо його,
    // щоб лабораторна запускалась без `dotnet dev-certs https --trust`. У Production перевірка сертифіката лишається повною.
    if (builder.Environment.IsDevelopment())
        clientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        });
}

// ---- Лаб. 06: Redis (ресурс "redis" з AppHost) як IDistributedCache для зібраних карток ----
builder.AddRedisDistributedCache("redis");
builder.Services.AddSingleton<AggregatorCache>();

builder.Services.AddScoped<HttpPaymentCardSource>();
builder.Services.AddScoped<GrpcPaymentCardSource>();
builder.Services.AddScoped<PaymentCardService>();

// JSON: camelCase (за замовчуванням), null-значення не серіалізуються
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // для запитів у обхід Gateway CorrelationId генерується тут
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapDefaultEndpoints();   // /health і /alive

app.Run();
