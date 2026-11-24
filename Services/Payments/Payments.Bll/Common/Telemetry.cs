using System.Diagnostics;

namespace Payments.Bll.Common;

/// <summary>Власні spans для бізнес-операцій. Ім'я джерела = ім'я збірки хоста (Payments.Api): ServiceDefaults підписується на нього.</summary>
public static class Telemetry
{
    public const string SourceName = "Payments.Api";
    public static readonly ActivitySource Source = new(SourceName);
}
