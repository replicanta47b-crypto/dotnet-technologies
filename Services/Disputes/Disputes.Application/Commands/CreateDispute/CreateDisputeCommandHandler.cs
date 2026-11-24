using Disputes.Domain.Common;
using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using Disputes.Domain.ValueObjects;
using Disputes.Application.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Disputes.Application.Commands.CreateDispute;

public class CreateDisputeCommandHandler(
    IDisputeRepository disputes, IDisputeThreadRepository threads, ILogger<CreateDisputeCommandHandler> logger)
    : IRequestHandler<CreateDisputeCommand, string>
{
    public async Task<string> Handle(CreateDisputeCommand cmd, CancellationToken ct)
    {
        using var activity = Telemetry.Source.StartActivity("CreateDispute");
        activity?.SetTag("payment.id", cmd.PaymentId);

        if (!EnumText.TryParse<DisputeReason>(cmd.Reason, out var reason))
            throw new DomainException($"Unknown reason '{cmd.Reason}'");

        var dispute = new Dispute(
            cmd.PaymentId, cmd.InvoiceId, cmd.MerchantId, cmd.MerchantName,
            cmd.Amount, cmd.Currency, reason,
            new Author(cmd.OpenedByUserId, cmd.OpenedByName));

        // тема обговорення — окрема колекція (reference threadId); без транзакції — eventual consistency
        var thread = new DisputeThread(dispute.Id, $"Спір за платежем {cmd.PaymentId}");
        dispute.AttachThread(thread.Id);

        await disputes.AddAsync(dispute, ct);
        await threads.AddAsync(thread, ct);

        activity?.SetTag("dispute.id", dispute.Id.ToString());
        logger.LogInformation(
            "Dispute {DisputeId} opened for payment {PaymentId}: {Amount} {Currency}, reason {Reason}, merchant {MerchantId}",
            dispute.Id, cmd.PaymentId, cmd.Amount, cmd.Currency, cmd.Reason, cmd.MerchantId);

        return dispute.Id.ToString();
    }
}
