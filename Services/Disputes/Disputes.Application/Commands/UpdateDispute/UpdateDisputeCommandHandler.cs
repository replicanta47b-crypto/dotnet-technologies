using Disputes.Domain.Common;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using Disputes.Application.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Disputes.Application.Commands.UpdateDispute;

public class UpdateDisputeCommandHandler(IDisputeRepository disputes, ILogger<UpdateDisputeCommandHandler> logger)
    : IRequestHandler<UpdateDisputeCommand>
{
    public async Task Handle(UpdateDisputeCommand cmd, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("ChangeDisputeStatus");
        activity?.SetTag("dispute.id", cmd.Id);

        var dispute = await disputes.GetByIdAsync(ObjectId.Parse(cmd.Id), ct)
                      ?? throw new NotFoundException("Dispute", cmd.Id);

        if (cmd.ExpectedVersion is { } expected && expected != dispute.Version)
            throw new ConflictException(
                $"Dispute {cmd.Id} has version {dispute.Version}, but version {expected} was expected");

        EnumText.TryParse<DisputeStatus>(cmd.Status, out var status);   // коректність гарантує валідатор
        dispute.ChangeStatus(status);

        await disputes.UpdateAsync(dispute, ct);   // ReplaceOne лише для очікуваної версії
        logger.LogInformation("Dispute {DisputeId} moved to {Status}, version {Version}", cmd.Id, cmd.Status, dispute.Version);
    }
}
