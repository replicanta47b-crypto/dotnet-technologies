using Disputes.Application;
using Disputes.Application.Common.Mappings;
using Disputes.Infrastructure;
using Disputes.Infrastructure.Persistence;
using Disputes.WebApi.Infrastructure;
using Disputes.WebApi.Rpc;
using Disputes.WebApi.Mapping;
using Platform.ServiceDefaults;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Лаб. 05: Serilog, OpenTelemetry, CorrelationId-handler, health checks, service discovery — з ServiceDefaults
// (LoggingBehavior / PerformanceBehavior пишуть через ILogger → Serilog → OpenTelemetry)
builder.AddServiceDefaults();

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

builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();   // X-Correlation-Id → LogContext, відповідь, span (ServiceDefaults)
app.UseSerilogRequestLogging();                  // зовнішній: бачить фінальний статус (400/404/409), а не 500
app.UseExceptionHandler();                       // → ApiExceptionHandler → ProblemDetails
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapGrpcService<DisputesGrpcService>();   // gRPC поряд із REST (HTTP/2 за ALPN на https-endpoint)
app.MapDefaultEndpoints();                       // /health (разом з MongoHealthCheck) і /alive

app.Run();
