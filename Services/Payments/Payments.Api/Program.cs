using Payments.Api.Infrastructure;
using Payments.Api.Rpc;
using Payments.Bll.Mapping;
using Payments.Bll.Services;
using Payments.Dal;
using Payments.Dal.Interfaces;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Лаб. 05: Serilog, OpenTelemetry, CorrelationId-handler, health checks, service discovery — з ServiceDefaults
builder.AddServiceDefaults();

// Рядок підключення: appsettings.json, але змінна середовища ConnectionStrings__PaymentsDb (Aspire) має пріоритет
var connectionString = builder.Configuration.GetConnectionString("PaymentsDb") ?? string.Empty;

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "Connection string 'PaymentsDb' is not configured (appsettings.json or ConnectionStrings__PaymentsDb).");

// Unit of Work — Scoped: одне з'єднання й одна транзакція на HTTP-запит
builder.Services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));

// Сервіси BLL
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// AutoMapper 15: профілі реєструються централізовано, ліцензійний ключ — AutoMapper:LicenseKey
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey))
        cfg.LicenseKey = licenseKey;
    cfg.AddProfile<MappingProfile>();
});

// ---- Лаб. 06: gRPC-сервер (контракт Protos/payments.proto) + interceptor (CorrelationId, логування, помилки) ----
builder.Services.AddGrpc(o => o.Interceptors.Add<GrpcServerInterceptor>());

builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // X-Correlation-Id → LogContext, відповідь, span
app.UseExceptionHandler();      // → DomainExceptionHandler → ProblemDetails
app.UseStatusCodePages();       // 404 для невідомих маршрутів теж у форматі ProblemDetails
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapGrpcService<PaymentsGrpcService>();   // gRPC поряд із REST (HTTP/2 за ALPN на https-endpoint)
app.MapDefaultEndpoints();   // /health і /alive

// Лаб. 05: у контейнері SQL Server база порожня — AppHost вмикає ініціалізацію (db/p1 з Лаб. 01, ідемпотентно)
if (app.Configuration.GetValue("Database:InitializeOnStartup", false))
    await DatabaseInitializer.InitializeAsync(connectionString, Path.Combine(AppContext.BaseDirectory, "db", "p1"), app.Logger);

app.Run();
