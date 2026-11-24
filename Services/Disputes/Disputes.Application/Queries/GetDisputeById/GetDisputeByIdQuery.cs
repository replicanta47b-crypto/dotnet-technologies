using Disputes.Application.Common.Interfaces;
using Disputes.Application.Dtos;

namespace Disputes.Application.Queries.GetDisputeById;

public record GetDisputeByIdQuery(string Id) : IQuery<DisputeDetailsDto>;
