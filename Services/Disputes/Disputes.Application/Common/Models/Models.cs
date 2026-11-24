namespace Disputes.Application.Common.Models;

public record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Критерії вибірки списку спорів. Фільтрація, сортування й пагінація виконуються в MongoDB.</summary>
public record DisputeListFilter(
    long? MerchantId,
    long? PaymentId,
    string? Status,
    string? Search,
    string SortBy,
    bool Descending,
    int Page,
    int PageSize);
