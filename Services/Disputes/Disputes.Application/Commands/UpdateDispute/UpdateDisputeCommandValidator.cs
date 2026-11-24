using Disputes.Domain.Common;
using Disputes.Domain.Enums;
using FluentValidation;
using MongoDB.Bson;

namespace Disputes.Application.Commands.UpdateDispute;

public class UpdateDisputeCommandValidator : AbstractValidator<UpdateDisputeCommand>
{
    public UpdateDisputeCommandValidator()
    {
        RuleFor(x => x.Id)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("Id must be a valid ObjectId");
        RuleFor(x => x.Status)
            .Must(s => EnumText.TryParse<DisputeStatus>(s, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", EnumText.Names<DisputeStatus>())}");
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion.HasValue);
    }
}
