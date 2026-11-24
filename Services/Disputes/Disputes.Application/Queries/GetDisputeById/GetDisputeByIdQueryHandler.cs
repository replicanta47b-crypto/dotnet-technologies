using Disputes.Application.Common.Interfaces;
using Disputes.Application.Dtos;
using Disputes.Domain.Exceptions;
using MediatR;

namespace Disputes.Application.Queries.GetDisputeById;

/// <summary>Обробник запиту звертається до IDisputeQueries (інтерфейс в Application), а не до MongoDB напряму.</summary>
public class GetDisputeByIdQueryHandler(IDisputeQueries queries)
    : IRequestHandler<GetDisputeByIdQuery, DisputeDetailsDto>
{
    public async Task<DisputeDetailsDto> Handle(GetDisputeByIdQuery q, CancellationToken ct)
        => await queries.GetByIdAsync(q.Id, ct) ?? throw new NotFoundException("Dispute", q.Id);
}
