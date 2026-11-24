using Disputes.Domain.Common;
using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.Exceptions;
using Disputes.Domain.Interfaces;
using Disputes.Domain.ValueObjects;
using MediatR;

namespace Disputes.Application.Commands.CreateDispute;

public class CreateDisputeCommandHandler(IDisputeRepository disputes, IDisputeThreadRepository threads)
    : IRequestHandler<CreateDisputeCommand, string>
{
    public async Task<string> Handle(CreateDisputeCommand cmd, CancellationToken ct)
    {
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

        return dispute.Id.ToString();
    }
}
