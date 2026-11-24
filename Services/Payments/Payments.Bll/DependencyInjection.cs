using Microsoft.Extensions.DependencyInjection;
using Payments.Bll.Mapping;
using Payments.Bll.Services;
using Payments.Dal;
using Payments.Dal.Interfaces;

namespace Payments.Bll;

public static class DependencyInjection
{
    /// <summary>
    /// Єдина точка реєстрації BLL/DAL: Unit of Work (Scoped — одне з'єднання на HTTP-запит),
    /// сервіси та AutoMapper-профілі.
    /// </summary>
    public static IServiceCollection AddPaymentsServices(this IServiceCollection services,
                                                         string connectionString,
                                                         string? autoMapperLicenseKey = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'PaymentsDb' is not configured (appsettings.json or ConnectionStrings__PaymentsDb).");

        services.AddScoped<IUnitOfWork>(_ => new UnitOfWork(connectionString));

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IPaymentService, PaymentService>();

        // AutoMapper 15: ліцензійний ключ береться з конфігурації (AutoMapper:LicenseKey)
        services.AddAutoMapper(cfg =>
        {
            if (!string.IsNullOrWhiteSpace(autoMapperLicenseKey))
                cfg.LicenseKey = autoMapperLicenseKey;
            cfg.AddProfile<MappingProfile>();
        });

        return services;
    }
}
