using Microsoft.Extensions.Caching.Memory;

namespace Billing.Bll.Caching;

/// <summary>Налаштування кешування для одного виду даних: різні дані — різні TTL, пріоритет і розмір запису.</summary>
/// <param name="L1Absolute">Максимальний вік запису в пам'яті процесу (absolute expiration).</param>
/// <param name="L1Sliding">Якщо запис довго не читали — вилучити раніше (sliding expiration, не довше за absolute).</param>
/// <param name="L2Absolute">TTL у Redis (розподілений кеш, спільний для всіх екземплярів сервісу).</param>
/// <param name="Priority">Пріоритет під час витіснення з L1 при досягненні SizeLimit.</param>
/// <param name="Size">«Вага» запису в одиницях SizeLimit (обов'язкова, коли в IMemoryCache задано SizeLimit).</param>
public sealed record CachePolicy(
    TimeSpan L1Absolute,
    TimeSpan L1Sliding,
    TimeSpan L2Absolute,
    CacheItemPriority Priority = CacheItemPriority.Normal,
    long Size = 1)
{
    /// <summary>Довідник, що рідко змінюється: довго живе в обох рівнях, витісняється останнім.</summary>
    public static readonly CachePolicy Reference = new(
        L1Absolute: TimeSpan.FromMinutes(5), L1Sliding: TimeSpan.FromMinutes(2),
        L2Absolute: TimeSpan.FromMinutes(30), Priority: CacheItemPriority.High, Size: 1);

    /// <summary>Картка сутності: середній TTL; інвалідується при зміні (див. CachedInvoiceService).</summary>
    public static readonly CachePolicy Entity = new(
        L1Absolute: TimeSpan.FromMinutes(2), L1Sliding: TimeSpan.FromSeconds(45),
        L2Absolute: TimeSpan.FromMinutes(10), Priority: CacheItemPriority.Normal, Size: 1);

    /// <summary>Похідні списки (прострочені рахунки): короткий TTL, можуть бути витіснені першими.</summary>
    public static readonly CachePolicy List = new(
        L1Absolute: TimeSpan.FromSeconds(30), L1Sliding: TimeSpan.FromSeconds(15),
        L2Absolute: TimeSpan.FromMinutes(2), Priority: CacheItemPriority.Low, Size: 5);
}
