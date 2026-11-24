using System.Text.Json.Serialization;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Serilog, OpenTelemetry, CorrelationIdHandler для всіх HttpClient, service discovery, resilience

// ---- typed HttpClients: "https+http://" = HTTPS, якщо доступний, інакше HTTP; ім'я — ресурс з AppHost ----
builder.Services.AddHttpClient<PaymentsClient>(c => c.BaseAddress = new("https+http://payments-api"));
builder.Services.AddHttpClient<BillingClient>(c => c.BaseAddress = new("https+http://billing-api"));
builder.Services.AddHttpClient<DisputesClient>(c => c.BaseAddress = new("https+http://disputes-api"));
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
