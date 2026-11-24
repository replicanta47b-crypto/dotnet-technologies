using Disputes.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Disputes.Application;

public static class DependencyInjection
{
    /// <summary>MediatR (скан обробників + behaviors у заданому порядку) і валідатори FluentValidation.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            var licenseKey = config["LuckyPenny:LicenseKey"];
            if (!string.IsNullOrWhiteSpace(licenseKey))
                cfg.LicenseKey = licenseKey;

            cfg.RegisterServicesFromAssembly(assembly);

            // Порядок реєстрації = порядок виконання: Logging → ExceptionHandling → Validation → Performance → обробник
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));            // 1 — зовнішній
            cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>));  // 2
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));         // 3
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));        // 4 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);
        return services;
    }
}
