using Billing.Api.HealthChecks;
using Billing.Api.IntegrationEvents;
using Billing.Api.Infrastructure;
using Billing.Api.Metrics;
using Billing.Api.Rpc;
using Billing.Bll.Caching;
using Billing.Bll.Mapping;
using Billing.Bll.Services;
using Billing.Bll.Validation;
using Billing.Dal;
using Billing.Dal.Interfaces;
using Billing.Dal.Seeding;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Security;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Лаб. 05: Serilog, OpenTelemetry, CorrelationId-handler, health checks, service discovery — з ServiceDefaults
builder.AddServiceDefaults();

// ---- Лаб. 09: захист. JWT приймаємо від Auth Service, IdentityServer і Keycloak (схема за issuer); дозвіл на кожен endpoint —
// [RequirePermission("billing:...")] перевіряється ЛОКАЛЬНО за claims токена, без звернення до IdP ----
builder.AddPlatformAuthentication("billing");

// ---- EF Core: рядок підключення з appsettings.json; ConnectionStrings__BillingDb (env) має пріоритет ----
var connectionString = builder.Configuration.GetConnectionString("BillingDb")
    ?? throw new InvalidOperationException("Connection string 'BillingDb' is not configured.");

builder.Services.AddDbContext<BillingDbContext>(o => o
    .UseSqlServer(connectionString)
    // знімок міграції може відрізнятися від моделі після ручних змін — не блокуємо запуск
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ---- Лаб. 06: дворівневий кеш. L2 = Redis (ресурс "redis" з AppHost, рядок ConnectionStrings:redis), L1 = IMemoryCache.
//      IInvoiceService / IMerchantService реєструються як декоратори з кешем (див. AddBillingCaching) ----
// Вбудовану перевірку Aspire вимкнено: вона б давала Unhealthy; власна (RedisCacheHealthCheck) дає Degraded — кеш необов'язковий
builder.AddRedisDistributedCache("redis", settings => settings.DisableHealthChecks = true);
builder.Services.AddBillingCaching(builder.Configuration);

// ---- Лаб. 07: health checks (теги: "database"/"cache" — категорія, "ready" — входить у /health/ready) ----
builder.Services.AddHealthChecks()
    .AddCheck("billing-db", new SqlServerHealthCheck(connectionString), HealthStatus.Unhealthy,
        ["database", "sql", "ready"], TimeSpan.FromSeconds(5))
    .AddCheck<RedisCacheHealthCheck>("redis-cache", HealthStatus.Degraded,        // кеш необов'язковий → Degraded, а не Unhealthy
        ["cache", "redis", "ready"], TimeSpan.FromSeconds(3));
builder.Services.AddGrpcHealthChecks();                                           // gRPC Health Checking Protocol

// ---- Лаб. 08: RabbitMQ — Білінг публікує події про рахунки й мерчантів (видавець) ----
builder.AddBillingIntegrationEvents();

// ---- Лаб. 07: бізнес-метрики (Meter "Billing.Api") ----
builder.Services.AddSingleton<BillingMetrics>();

// ---- Лаб. 06: gRPC-сервер (контракт Protos/billing.proto) + interceptor логування й мапінгу помилок ----
builder.Services.AddGrpc(o => o.Interceptors.Add<GrpcServerInterceptor>());

// ---- AutoMapper 15: профілі централізовано, ліцензійний ключ — AutoMapper:LicenseKey ----
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey))
        cfg.LicenseKey = licenseKey;
    cfg.AddProfile<BillingProfile>();
});

// ---- FluentValidation: валідатори реєструються й викликаються явно (AddFluentValidation застарів) ----
builder.Services.AddValidatorsFromAssemblyContaining<CreateInvoiceDtoValidator>();

builder.Services.AddControllers(o => o.Filters.Add<BillingMetricsFilter>());
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.AddPlatformSecurity(builder.Configuration, "billing"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // X-Correlation-Id → LogContext, відповідь, span (ServiceDefaults)
app.UseExceptionHandler();                       // → GlobalExceptionHandler → ProblemDetails
app.UseStatusCodePages();
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.OAuthClientId("swagger");   // публічний client IdentityServer: Authorization Code + PKCE (Лаб. 09)
    o.OAuthUsePkce();
});

app.UsePlatformAuthorization();   // UseAuthentication + UseAuthorization; /health і Swagger лишаються відкритими

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapGrpcService<BillingGrpcService>();
app.MapGrpcHealthChecksService();            // grpc.health.v1.Health/Check — його викликає Aggregator   // gRPC поряд із REST на тому самому Kestrel (HTTP/2 за ALPN)
app.MapDefaultEndpoints();   // /health і /alive

// ---- міграції та програмний ідемпотентний сідінг (у Production сідінг вимкнено) ----
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await db.Database.MigrateAsync();
    if (!app.Environment.IsProduction() && app.Configuration.GetValue("Database:Seed", true))
        await DbInitializer.SeedAsync(db);
}

app.Run();
