using Payments.Api.Infrastructure;
using Payments.Bll;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: рівні й приймачі — з конфігурації (appsettings.json, секція Serilog)
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Рядок підключення: appsettings.json, але змінна середовища ConnectionStrings__PaymentsDb (Aspire) має пріоритет
var connectionString = builder.Configuration.GetConnectionString("PaymentsDb") ?? string.Empty;

builder.Services.AddPaymentsServices(connectionString, builder.Configuration["AutoMapper:LicenseKey"]);

builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();      // → DomainExceptionHandler → ProblemDetails
app.UseStatusCodePages();       // 404 для невідомих маршрутів теж у форматі ProblemDetails
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();

app.Run();
