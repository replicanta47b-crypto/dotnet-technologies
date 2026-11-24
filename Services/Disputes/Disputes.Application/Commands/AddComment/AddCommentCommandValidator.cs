using FluentValidation;
using MongoDB.Bson;

namespace Disputes.Application.Commands.AddComment;

public class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.DisputeId).Must(id => ObjectId.TryParse(id, out _))
                                 .WithMessage("DisputeId must be a valid ObjectId");
        RuleFor(x => x.Author).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0).When(x => x.ExpectedVersion.HasValue);
    }
}
