namespace Payments.Domain.Constants;

/// <summary>Статуси платежу (збігаються з CHECK-обмеженням dbo.Payments.Status з Лаб. 01).</summary>
public static class PaymentStatuses
{
    public const string Created = "Created";
    public const string Validating = "Validating";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Settled = "Settled";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
        new[] { Created, Validating, Authorized, Captured, Settled, Cancelled };
}

/// <summary>Допустимі валюти (CHECK у dbo.Payments / dbo.Invoices).</summary>
public static class Currencies
{
    public static readonly IReadOnlyList<string> All = new[] { "UAH", "USD", "EUR" };
}

/// <summary>Способи оплати (CHECK у dbo.PaymentDetails.Method).</summary>
public static class PaymentMethods
{
    public static readonly IReadOnlyList<string> All = new[] { "Card", "BankTransfer", "Wallet" };
}
