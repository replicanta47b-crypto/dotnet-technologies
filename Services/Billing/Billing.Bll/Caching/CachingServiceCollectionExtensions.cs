using Billing.Bll.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Billing.Bll.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє L1 (IMemoryCache з SizeLimit), дворівневий кеш і декоратори сервісів.
    /// IDistributedCache (Redis) реєструє хост через Aspire: builder.AddRedisDistributedCache("redis").
    /// </summary>
    public static IServiceCollection AddBillingCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.Section));
        var limit = configuration.GetSection(CacheOptions.Section).GetValue<long?>(nameof(CacheOptions.L1SizeLimit)) ?? 1024;

        // SizeLimit → кожен запис ОБОВ'ЯЗКОВО має Size (див. CachePolicy), інакше виняток при додаванні
        services.AddMemoryCache(o => o.SizeLimit = limit);
        services.AddSingleton<CacheMetrics>();
        services.AddSingleton<ITwoLevelCache, TwoLevelCache>();

        // Decorator: реальний сервіс реєструємо за конкретним типом, а інтерфейс віддаємо у вигляді обгортки
        services.AddScoped<InvoiceService>();
        services.AddScoped<MerchantService>();
        services.AddScoped<IInvoiceService>(sp => new CachedInvoiceService(
            sp.GetRequiredService<InvoiceService>(), sp.GetRequiredService<ITwoLevelCache>()));
        services.AddScoped<IMerchantService>(sp => new CachedMerchantService(
            sp.GetRequiredService<MerchantService>(), sp.GetRequiredService<ITwoLevelCache>()));
        return services;
    }
}
