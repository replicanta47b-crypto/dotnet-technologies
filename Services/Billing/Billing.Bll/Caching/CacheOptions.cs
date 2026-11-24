namespace Billing.Bll.Caching;

/// <summary>Секція конфігурації "Cache" (appsettings.json).</summary>
public sealed class CacheOptions
{
    public const string Section = "Cache";

    /// <summary>Загальний «бюджет» L1 в умовних одиницях (сума Size усіх записів).</summary>
    public long L1SizeLimit { get; set; } = 1024;

    /// <summary>Скільки чекати відповіді Redis, перш ніж вважати його недоступним і піти в БД.</summary>
    public int L2TimeoutMs { get; set; } = 400;

    /// <summary>Скільки поспіль помилок L2 відкривають «запобіжник» (circuit breaker).</summary>
    public int L2BreakerThreshold { get; set; } = 3;

    /// <summary>На скільки секунд запобіжник виключає L2 з роботи (потім одна пробна спроба).</summary>
    public int L2BreakerSeconds { get; set; } = 20;
}
