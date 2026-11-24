using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Payments.Api.Infrastructure;

/// <summary>
/// Ідемпотентна ініціалізація PaymentsDb у контейнері SQL Server (його запускає AppHost, база стартує порожньою):
/// db/p1/schema.sql → procedures.sql → seed.sql. Скрипти з Лаб. 01 копіюються в вихідну папку (див. csproj),
/// діляться на пакети за рядком GO й виконуються по черзі. Вмикається налаштуванням Database:InitializeOnStartup.
/// </summary>
public static partial class DatabaseInitializer
{
    private static readonly string[] Scripts = ["schema.sql", "procedures.sql", "seed.sql"];

    public static async Task InitializeAsync(string connectionString, string scriptsDirectory, ILogger logger, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        foreach (var name in Scripts)
        {
            var path = Path.Combine(scriptsDirectory, name);
            var text = await File.ReadAllTextAsync(path, ct);

            var batches = GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            foreach (var batch in batches)
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
                await command.ExecuteNonQueryAsync(ct);
            }

            logger.LogInformation("Database script {Script} applied ({Batches} batches)", name, batches.Count);
        }
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
