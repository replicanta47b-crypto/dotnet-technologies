using Payments.Api.Infrastructure;
using Payments.Bll.Mapping;
using Payments.Bll.Services;
using Payments.Dal;
using Payments.Dal.Interfaces;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: рівні й приймачі — з конфігурації (appsettings.json, секція Serilog)
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

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
