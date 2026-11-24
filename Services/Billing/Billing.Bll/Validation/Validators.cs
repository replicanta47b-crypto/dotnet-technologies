using Billing.Bll.Dto;
using Billing.Domain.Entities;
using FluentValidation;

namespace Billing.Bll.Validation;

/// <summary>
/// Другий рівень валідації вхідних даних: форматні й міжполя правила. Прості обмеження довжини/обов'язковості
/// вже задані DataAnnotations на DTO і тут не дублюються. Правила, що потребують БД (унікальність номера), — у сервісі.
/// </summary>
public class CreateInvoiceDtoValidator : AbstractValidator<CreateInvoiceDto>
{
    public CreateInvoiceDtoValidator()
    {
        RuleFor(x => x.Number).Matches("^[A-Z0-9-]+$")
            .WithMessage("Number may contain only capital letters, digits and '-'.");
        RuleFor(x => x.MerchantId).GreaterThan(0);
        RuleFor(x => x.Currency).Must(c => Invoice.AllowedCurrencies.Contains(c?.ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", Invoice.AllowedCurrencies)}.");
        RuleFor(x => x.DueDate).NotEqual(default(DateOnly)).WithMessage("DueDate is required.");
        RuleFor(x => x.PeriodEnd).GreaterThanOrEqualTo(x => x.PeriodStart)
            .When(x => x.PeriodStart is not null && x.PeriodEnd is not null)
            .WithMessage("PeriodEnd cannot be earlier than PeriodStart.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("An invoice must contain at least one line.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitPrice).GreaterThan(0);
        });
    }
}

public class RescheduleInvoiceDtoValidator : AbstractValidator<RescheduleInvoiceDto>
{
    public RescheduleInvoiceDtoValidator()
    {
        RuleFor(x => x.DueDate).NotEqual(default(DateOnly)).WithMessage("DueDate is required.");
    }
}

public class AssignCategoryDtoValidator : AbstractValidator<AssignCategoryDto>
{
    public AssignCategoryDtoValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}

public class CreateMerchantDtoValidator : AbstractValidator<CreateMerchantDto>
{
    public CreateMerchantDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
        RuleFor(x => x.ContactEmail).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}
