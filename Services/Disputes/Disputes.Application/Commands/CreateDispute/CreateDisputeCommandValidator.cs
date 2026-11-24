using Disputes.Domain.Common;
using Disputes.Domain.Entities;
using Disputes.Domain.Enums;
using Disputes.Domain.Interfaces;
using FluentValidation;

namespace Disputes.Application.Commands.CreateDispute;

public class CreateDisputeCommandValidator : AbstractValidator<CreateDisputeCommand>
{
    public CreateDisputeCommandValidator(IDisputeRepository disputes)
    {
        RuleFor(x => x.PaymentId).GreaterThan(0);
        RuleFor(x => x.InvoiceId).GreaterThan(0);
        RuleFor(x => x.MerchantId).GreaterThan(0);
        RuleFor(x => x.MerchantName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Currency)
            .Must(c => Dispute.AllowedCurrencies.Contains(c))
            .WithMessage($"Currency must be one of: {string.Join(", ", Dispute.AllowedCurrencies)}");
        RuleFor(x => x.Reason)
            .Must(r => EnumText.TryParse<DisputeReason>(r, out _))
            .WithMessage($"Reason must be one of: {string.Join(", ", EnumText.Names<DisputeReason>())}");
        RuleFor(x => x.OpenedByUserId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OpenedByName).NotEmpty().MaximumLength(100);

        // Асинхронне правило із запитом до MongoDB (через репозиторій у Domain):
        // один користувач — один спір за платежем із тієї самої причини
        RuleFor(x => x.PaymentId)
            .MustAsync(async (cmd, _, ct) =>
                !EnumText.TryParse<DisputeReason>(cmd.Reason, out var reason)
                || string.IsNullOrWhiteSpace(cmd.OpenedByUserId)
                || !await disputes.ExistsAsync(cmd.PaymentId, cmd.OpenedByUserId.Trim(), reason, ct))
            .WithMessage("You have already opened a dispute for this payment with the same reason");
    }
}
