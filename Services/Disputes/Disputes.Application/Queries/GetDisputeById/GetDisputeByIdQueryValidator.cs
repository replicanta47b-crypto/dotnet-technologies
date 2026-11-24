using FluentValidation;
using MongoDB.Bson;

namespace Disputes.Application.Queries.GetDisputeById;

public class GetDisputeByIdQueryValidator : AbstractValidator<GetDisputeByIdQuery>
{
    public GetDisputeByIdQueryValidator()
        => RuleFor(x => x.Id).Must(id => ObjectId.TryParse(id, out _))
                             .WithMessage("Id must be a valid ObjectId");
}
