using Disputes.Application.Common.Interfaces;
using Disputes.Application.Common.Models;
using Disputes.Application.Dtos;
using MediatR;

namespace Disputes.Application.Queries.GetDisputesList;

public class GetDisputesListQueryHandler(IDisputeQueries queries)
    : IRequestHandler<GetDisputesListQuery, PagedResult<DisputeDto>>
{
    public Task<PagedResult<DisputeDto>> Handle(GetDisputesListQuery q, CancellationToken ct)
        => queries.GetListAsync(
            new DisputeListFilter(
                q.MerchantId, q.PaymentId, q.Status, q.Search,
                string.IsNullOrWhiteSpace(q.SortBy) ? GetDisputesListQueryValidator.DefaultSort : q.SortBy.Trim(),
                q.Descending, q.Page, Math.Clamp(q.PageSize, 1, 100)),
            ct);
}
