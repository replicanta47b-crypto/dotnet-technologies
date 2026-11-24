using FluentValidation;
using MongoDB.Bson;

namespace Disputes.Application.Commands.DeleteDispute;

public class DeleteDisputeCommandValidator : AbstractValidator<DeleteDisputeCommand>
{
    public DeleteDisputeCommandValidator()
        => RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _))
                             .WithMessage("Id must be a valid ObjectId");
}
