using Billing.Bll.Dto;
using Billing.Bll.Services;

namespace Billing.Bll.Caching;

/// <summary>Декоратор над MerchantService. Довідник мерчантів — найвигідніший кандидат на кеш: читається часто, змінюється рідко.</summary>
public sealed class CachedMerchantService(IMerchantService inner, ITwoLevelCache cache) : IMerchantService
{
    public Task<MerchantDetailsDto> GetByIdAsync(long id, CancellationToken ct = default)
        => cache.GetOrCreateAsync(CacheKeys.Merchant(id), "merchant", CachePolicy.Entity,
            c => inner.GetByIdAsync(id, c), ct);

    public Task<IReadOnlyList<MerchantDto>> GetAllAsync(CancellationToken ct = default)
        => cache.GetOrCreateAsync(CacheKeys.MerchantsAll, "merchants-all", CachePolicy.Reference,
            c => inner.GetAllAsync(c), ct);

    public async Task<MerchantDto> CreateAsync(CreateMerchantDto input, CancellationToken ct = default)
    {
        var created = await inner.CreateAsync(input, ct);
        await cache.RemoveAsync(CacheKeys.MerchantsAll, "merchants-all", "merchant created", ct);   // новий мерчант має з'явитися в списку
        return created;
    }
}
