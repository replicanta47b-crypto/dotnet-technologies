using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Payments.Api.HealthChecks;

/// <summary>Легка перевірка SQL Server: відкриває з'єднання й виконує SELECT 1. Тип збою задає реєстрація (failureStatus).</summary>
public sealed class SqlServerHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // коротке очікування з'єднання: недоступну БД видно швидко
            var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 3;
            await command.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("SQL Server responds (SELECT 1)");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SQL Server is unreachable", ex);
        }
    }
}
