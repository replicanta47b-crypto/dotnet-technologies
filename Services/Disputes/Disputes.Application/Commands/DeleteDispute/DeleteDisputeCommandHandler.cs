using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using MediatR;
using MongoDB.Bson;

namespace Disputes.Application.Commands.DeleteDispute;

public class DeleteDisputeCommandHandler(IDisputeRepository disputes, IDisputeThreadRepository threads)
    : IRequestHandler<DeleteDisputeCommand>
{
    public async Task Handle(DeleteDisputeCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);

        var dispute = await disputes.GetByIdAsync(id, ct)
                      ?? throw new NotFoundException("Dispute", cmd.Id);

        dispute.EnsureCanBeDeleted();

        await disputes.DeleteAsync(id, ct);
        await threads.DeleteByDisputeIdAsync(id, ct);
    }
}
