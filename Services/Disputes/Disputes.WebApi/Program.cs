using Disputes.Application;
using Disputes.Application.Common.Mappings;
using Disputes.Infrastructure;
using Disputes.Infrastructure.Persistence;
using Disputes.WebApi.HealthChecks;
using Disputes.WebApi.IntegrationEvents;
using Disputes.WebApi.Infrastructure;
using Disputes.WebApi.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Disputes.WebApi.Rpc;
using Disputes.WebApi.Mapping;
using Platform.Security;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Лаб. 05: Serilog, OpenTelemetry, CorrelationId-handler, health checks, service discovery — з ServiceDefaults
// (LoggingBehavior / PerformanceBehavior пишуть через ILogger → Serilog → OpenTelemetry)
builder.AddServiceDefaults();

// ---- Лаб. 09: захист. JWT приймаємо від Auth Service, IdentityServer і Keycloak (схема за issuer); дозвіл на кожен endpoint —
// [RequirePermission("disputes:...")] перевіряється ЛОКАЛЬНО за claims токена, без звернення до IdP ----
builder.AddPlatformAuthentication("disputes");

// ---- Кожен шар надає власний метод розширення ----
builder.Services
    .AddApplication(builder.Configuration)      // MediatR, behaviors, валідатори FluentValidation
    .AddInfrastructure(builder.Configuration);  // MongoDB: мапінг, контекст, репозиторії, індекси, сідінг

// ---- AutoMapper 15: профілі Application (сутність → DTO) та WebApi (запит → команда) ----
builder.Services.AddAutoMapper(cfg =>
{
    var licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey))
        cfg.LicenseKey = licenseKey;
    cfg.AddProfile<DisputeProfile>();
    cfg.AddProfile<ApiMappingProfile>();
});

// ---- Лаб. 06: gRPC-сервер (контракт Protos/disputes.proto) + interceptor (CorrelationId, логування, помилки) ----
builder.Services.AddGrpc(o => o.Interceptors.Add<GrpcServerInterceptor>());

// ---- Лаб. 08: RabbitMQ — Спори споживають події Білінгу й оновлюють merchantName (споживач) ----
builder.AddDisputesIntegrationEvents();

// ---- Лаб. 07: бізнес-метрики (Meter "Disputes.Api") ----
builder.Services.AddSingleton<DisputeMetrics>();

builder.Services.AddControllers(o => o.Filters.Add<DisputeMetricsFilter>());
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// ---- Лаб. 07: health checks (теги: "database"/"mongodb" — категорія, "ready" — входить у /health/ready) ----
builder.Services.AddHealthChecks()
    .AddCheck<MongoPingHealthCheck>("disputes-db", HealthStatus.Unhealthy,
        ["database", "mongodb", "ready"], TimeSpan.FromSeconds(5));
builder.Services.AddGrpcHealthChecks();   // gRPC Health Checking Protocol

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => c.AddPlatformSecurity(builder.Configuration, "disputes"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // X-Correlation-Id → LogContext, відповідь, span (ServiceDefaults)
app.UseSerilogRequestLogging();                  // зовнішній: бачить фінальний статус (400/404/409), а не 500
app.UseExceptionHandler();                       // → ApiExceptionHandler → ProblemDetails
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.OAuthClientId("swagger");   // публічний client IdentityServer: Authorization Code + PKCE (Лаб. 09)
    o.OAuthUsePkce();
});

app.UsePlatformAuthorization();   // UseAuthentication + UseAuthorization; /health і Swagger лишаються відкритими

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapGrpcService<DisputesGrpcService>();
app.MapGrpcHealthChecksService();            // grpc.health.v1.Health/Check — його викликає Aggregator   // gRPC поряд із REST (HTTP/2 за ALPN на https-endpoint)
app.MapDefaultEndpoints();                       // /health, /health/live, /health/ready (з MongoPingHealthCheck), /health/startup

app.Run();
