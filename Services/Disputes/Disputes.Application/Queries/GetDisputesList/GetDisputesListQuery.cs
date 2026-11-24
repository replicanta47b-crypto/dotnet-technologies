using Disputes.Application.Common.Interfaces;
using Disputes.Application.Common.Models;
using Disputes.Application.Dtos;

namespace Disputes.Application.Queries.GetDisputesList;

/// <summary>Список спорів: фільтри (мерчант, платіж, статус), повнотекстовий пошук, сортування, пагінація.</summary>
public record GetDisputesListQuery(
    long? MerchantId = null,
    long? PaymentId = null,
    string? Status = null,
    string? Search = null,
    string? SortBy = null,
    bool Descending = true,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<DisputeDto>>;
