using System.Diagnostics;

namespace Disputes.Application.Common;

/// <summary>Власні spans для бізнес-операцій. Ім'я джерела = ім'я збірки хоста (Disputes.WebApi): ServiceDefaults підписується на нього.</summary>
public static class Telemetry
{
    public const string SourceName = "Disputes.WebApi";
    public static readonly ActivitySource Source = new(SourceName);
}
