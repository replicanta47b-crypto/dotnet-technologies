using Disputes.Application;
using Disputes.Application.Common.Mappings;
using Disputes.Infrastructure;
using Disputes.Infrastructure.Persistence;
using Disputes.WebApi.Infrastructure;
using Disputes.WebApi.Mapping;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog: структуровані логи (LoggingBehavior / PerformanceBehavior пишуть через ILogger → Serilog) ----
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

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

app.UseMiddleware<CorrelationIdMiddleware>();   // CorrelationId, RequestPath, UserId → кожен лог
app.UseSerilogRequestLogging();                  // зовнішній: бачить фінальний статус (400/404/409), а не 500
app.UseExceptionHandler();                       // → ApiExceptionHandler → ProblemDetails
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
