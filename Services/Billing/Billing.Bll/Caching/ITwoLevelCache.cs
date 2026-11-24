namespace Billing.Bll.Caching;

/// <summary>Дворівневий кеш: L1 (пам'ять процесу, найшвидший) → L2 (Redis, спільний) → джерело (БД).</summary>
public interface ITwoLevelCache
{
    /// <summary>Cache-aside: шукає в L1, потім у L2; якщо ніде немає — викликає factory й кладе результат в обидва рівні.</summary>
    Task<T> GetOrCreateAsync<T>(string key, string name, CachePolicy policy,
        Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);

    /// <summary>Інвалідація: видаляє запис з обох рівнів (L1 і L2) і логує причину (reason).</summary>
    Task RemoveAsync(string key, string name, string reason, CancellationToken ct = default);
}
