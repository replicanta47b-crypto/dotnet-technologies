using System.Diagnostics;

namespace Billing.Bll.Common;

/// <summary>Власні spans для бізнес-операцій. Ім'я джерела = ім'я збірки хоста (Billing.Api): ServiceDefaults підписується на нього.</summary>
public static class Telemetry
{
    public const string SourceName = "Billing.Api";
    public static readonly ActivitySource Source = new(SourceName);
}
