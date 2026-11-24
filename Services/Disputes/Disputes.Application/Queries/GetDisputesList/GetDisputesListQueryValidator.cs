using Disputes.Domain.Common;
using Disputes.Domain.Enums;
using FluentValidation;

namespace Disputes.Application.Queries.GetDisputesList;

public class GetDisputesListQueryValidator : AbstractValidator<GetDisputesListQuery>
{
    public const string DefaultSort = "openedAt";

    /// <summary>Поля, за якими дозволено сортувати (мають підтримку індексами).</summary>
    public static readonly IReadOnlyList<string> SortFields = new[] { "openedAt", "amount", "status", "merchantName" };

    public GetDisputesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.MerchantId).GreaterThan(0).When(x => x.MerchantId.HasValue);
        RuleFor(x => x.PaymentId).GreaterThan(0).When(x => x.PaymentId.HasValue);
        RuleFor(x => x.Status)
            .Must(s => EnumText.TryParse<DisputeStatus>(s, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage($"Status must be one of: {string.Join(", ", EnumText.Names<DisputeStatus>())}");
        RuleFor(x => x.SortBy)
            .Must(s => SortFields.Contains(s!, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.SortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}");
        RuleFor(x => x.Search).MaximumLength(200);
    }
}
