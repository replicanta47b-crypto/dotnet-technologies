namespace Billing.Bll.Caching;

/// <summary>Усі ключі кешу в одному місці: префікс сервісу + тип + ідентифікатор. Так простіше інвалідувати й шукати в Redis.</summary>
public static class CacheKeys
{
    public const string Prefix = "billing";

    public static string Invoice(long id) => $"{Prefix}:invoice:{id}";
    public static string Merchant(long id) => $"{Prefix}:merchant:{id}";
    public static string MerchantsAll => $"{Prefix}:merchants:all";
    public static string Overdue => $"{Prefix}:invoices:overdue";
}
