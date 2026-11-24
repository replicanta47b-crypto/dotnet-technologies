namespace Disputes.Domain.Enums;

/// <summary>Причина спору. У БД — snake_case-рядок (fraud, service_not_provided, ...).</summary>
public enum DisputeReason
{
    Fraud,
    ServiceNotProvided,
    Duplicate,
    AmountMismatch,
    Other
}

/// <summary>Життєвий цикл спору. У БД — snake_case-рядок (open, under_review, ...).</summary>
public enum DisputeStatus
{
    Open,
    UnderReview,
    Won,
    Lost,
    Closed
}

/// <summary>Статус теми обговорення.</summary>
public enum ThreadStatus
{
    Open,
    Closed
}
